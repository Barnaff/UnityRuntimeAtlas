using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// High-performance runtime texture atlas with dynamic update support.
    /// Allows adding/removing sprites at runtime without breaking existing references.
    /// </summary>
    public sealed class RuntimeAtlas : IDisposable
    {
        // Global atlas registry for debugging/tools
        private static readonly List<RuntimeAtlas> _allAtlases = new List<RuntimeAtlas>();
        private static int _nextRegistryId = 1;
        
        /// <summary>Get all active runtime atlases (for debugging/tools).</summary>
        public static IReadOnlyList<RuntimeAtlas> GetAllAtlases()
        {
            // Clean up disposed atlases
            _allAtlases.RemoveAll(a => a == null || a._isDisposed);
            return _allAtlases.AsReadOnly();
        }
        
        // Multi-page texture support
        private List<Texture2D> _textures;
        private List<IPackingAlgorithm> _packers;
        private int _currentPageIndex;
        
        private readonly AtlasSettings _settings;
        private readonly Dictionary<int, AtlasEntry> _entries;
        private readonly Dictionary<string, AtlasEntry> _entriesByName;
        private readonly Queue<int> _recycledIds;
        private int _nextId;
        private bool _isDisposed;
        private bool _isDirty;
        private int _version;
        private int _registryId; // Unique ID for this atlas instance
        private string _debugName; // Name for debugging

        /// <summary>The primary atlas texture (page 0).</summary>
        public Texture2D Texture => _textures != null && _textures.Count > 0 ? _textures[0] : null;

        /// <summary>Get a specific texture page by index.</summary>
        public Texture2D GetTexture(int pageIndex)
        {
            if (_textures == null || pageIndex < 0 || pageIndex >= _textures.Count)
            {
                return null;
            }
            return _textures[pageIndex];
        }

        /// <summary>Number of texture pages in this atlas.</summary>
        public int PageCount => _textures?.Count ?? 0;

        /// <summary>Current atlas width (all pages have same size).</summary>
        public int Width => _textures != null && _textures.Count > 0 && _textures[0] != null ? _textures[0].width : 0;

        /// <summary>Current atlas height (all pages have same size).</summary>
        public int Height => _textures != null && _textures.Count > 0 && _textures[0] != null ? _textures[0].height : 0;

        /// <summary>Number of entries in the atlas.</summary>
        public int EntryCount => _entries.Count;

        /// <summary>Atlas version, incremented on each modification.</summary>
        public int Version => _version;

        /// <summary>Current fill ratio (0-1) - average across all pages.</summary>
        public float FillRatio
        {
            get
            {
                if (_packers == null || _packers.Count == 0)
                {
                    return 0f;
                }
                var sum = 0f;
                foreach (var packer in _packers)
                {
                    sum += packer?.GetFillRatio() ?? 0f;
                }
                return sum / _packers.Count;
            }
        }

        /// <summary>Settings used for this atlas.</summary>
        public AtlasSettings Settings => _settings;
        
        /// <summary>Debug name for this atlas (for tools/debugging).</summary>
        public string DebugName
        {
            get => _debugName;
            set => _debugName = string.IsNullOrEmpty(value) ? $"Atlas_{_registryId}" : value;
        }

        /// <summary>Source file path if this atlas was loaded from disk.</summary>
        public string SourceFilePath { get; internal set; }

        /// <summary>Event fired when the atlas texture is resized or recreated.</summary>
        public event Action<RuntimeAtlas> OnAtlasResized;

        /// <summary>Event fired when any entry's UV coordinates change.</summary>
        public event Action<RuntimeAtlas, AtlasEntry> OnEntryUpdated;

        /// <summary>
        /// Create a new runtime atlas with default settings.
        /// </summary>
        public RuntimeAtlas() : this(AtlasSettings.Default) { }

        /// <summary>
        /// Create a new runtime atlas with the specified settings.
        /// </summary>
        public RuntimeAtlas(AtlasSettings settings)
        {
            // Validate and fix settings if needed
            if (settings.Format == 0 || !SystemInfo.SupportsTextureFormat(settings.Format))
            {
                Debug.LogWarning($"[RuntimeAtlas] Invalid or unsupported texture format {settings.Format}. Using RGBA32 instead.");
                settings.Format = TextureFormat.RGBA32;
            }

            // Ensure valid dimensions
            if (settings.InitialSize <= 0) 
            {
                settings.InitialSize = 256;
#if UNITY_EDITOR
                Debug.LogWarning("[RuntimeAtlas] InitialSize was invalid (<= 0), setting to 256");
#endif
            }
            
            if (settings.MaxSize <= 0) 
            {
                settings.MaxSize = 2048;
#if UNITY_EDITOR
                Debug.LogWarning("[RuntimeAtlas] MaxSize was invalid (<= 0), setting to 2048");
#endif
            }
            
            _settings = settings;
            _entries = new Dictionary<int, AtlasEntry>(64);
            _entriesByName = new Dictionary<string, AtlasEntry>(64);
            _recycledIds = new Queue<int>(16);
            _textures = new List<Texture2D>();
            _packers = new List<IPackingAlgorithm>();
            _currentPageIndex = 0;
            
            // Register in global registry for debugging
            _registryId = _nextRegistryId++;
            _debugName = $"Atlas_{_registryId}";
            _allAtlases.Add(this);
            
            // Create first page
            CreateNewPage();
            
#if UNITY_EDITOR
            Debug.Log($"[RuntimeAtlas] Created new atlas '{_debugName}' with ID {_registryId}");
#endif
        }

        /// <summary>
        /// Internal constructor for deserialization. Does not create initial page.
        /// Pages will be added via AddLoadedPage() during deserialization.
        /// </summary>
        /// <param name="settings">Atlas settings</param>
        /// <param name="skipInitialPage">If true, skips creating the initial page</param>
        internal RuntimeAtlas(AtlasSettings settings, bool skipInitialPage)
        {
            // Format validation
            if (settings.Format == 0 || !SystemInfo.SupportsTextureFormat(settings.Format))
            {
                settings.Format = TextureFormat.RGBA32;
#if UNITY_EDITOR
                Debug.LogWarning("[RuntimeAtlas] TextureFormat not supported, using RGBA32");
#endif
            }

            // MaxSize validation
            if (settings.MaxSize <= 0)
            {
                settings.MaxSize = 2048;
#if UNITY_EDITOR
                Debug.LogWarning("[RuntimeAtlas] MaxSize was invalid (<= 0), setting to 2048");
#endif
            }
            
            _settings = settings;
            _entries = new Dictionary<int, AtlasEntry>(64);
            _entriesByName = new Dictionary<string, AtlasEntry>(64);
            _recycledIds = new Queue<int>(16);
            _textures = new List<Texture2D>();
            _packers = new List<IPackingAlgorithm>();
            _currentPageIndex = 0;
            
            // Register in global registry for debugging
            _registryId = _nextRegistryId++;
            _debugName = $"Atlas_{_registryId}";
            _allAtlases.Add(this);
            
            // Skip creating initial page for deserialization
            if (!skipInitialPage)
            {
                CreateNewPage();
            }
            
#if UNITY_EDITOR
            Debug.Log($"[RuntimeAtlas] Created new atlas '{_debugName}' with ID {_registryId} (skipInitialPage: {skipInitialPage})");
#endif
        }

        private void CreateNewPage()
        {
            var pageIndex = _textures.Count;
            
#if UNITY_EDITOR || UNITY_IOS
            Debug.Log($"[RuntimeAtlas.CreateNewPage] Creating page {pageIndex}. Size: {_settings.InitialSize}x{_settings.InitialSize}, Format: {_settings.Format}, Readable: {_settings.Readable}. Memory before: {System.GC.GetTotalMemory(false) / 1024 / 1024} MB");
#endif
            
            // Create packer for this page
            IPackingAlgorithm packer = _settings.Algorithm switch
            {
#if PACKING_BURST_ENABLED
                PackingAlgorithm.MaxRects => new MaxRectsAlgorithm(),
                PackingAlgorithm.Skyline => new SkylineAlgorithm(),
#else
                PackingAlgorithm.MaxRects => new MaxRectsAlgorithmNoBurst(),
                // Fallback for others if they rely on burst/native collections
                PackingAlgorithm.Skyline => new MaxRectsAlgorithmNoBurst(), 
#endif
                
#if PACKING_BURST_ENABLED
                _ => new MaxRectsAlgorithm()
#else
                _ => new MaxRectsAlgorithmNoBurst()
#endif
            };
            
            // Initialize packer
            packer.Initialize(_settings.InitialSize, _settings.InitialSize);
            _packers.Add(packer);
            
#if UNITY_EDITOR || UNITY_IOS
            Debug.Log($"[RuntimeAtlas.CreateNewPage] Creating Texture2D object...");
#endif
            
            // Create texture
            var texture = new Texture2D(_settings.InitialSize, _settings.InitialSize, _settings.Format, _settings.GenerateMipMaps);
            texture.filterMode = _settings.FilterMode;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.name = $"RuntimeAtlas_Page{pageIndex}";
            
#if UNITY_EDITOR || UNITY_IOS
            Debug.Log($"[RuntimeAtlas.CreateNewPage] Texture2D created. Clearing with RenderTexture...");
#endif
            
            // ✅ MEMORY OPTIMIZATION: Use RenderTexture to clear instead of allocating large Color32 array
            // This avoids a large CPU memory allocation (e.g., 4096x4096x4 = 64MB)
            if (_settings.InitialSize > 0) // Ensure valid size
            {
                RenderTexture rt = null;
                try
                {
                    rt = RenderTexture.GetTemporary(_settings.InitialSize, _settings.InitialSize, 0, RenderTextureFormat.ARGB32);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    GL.Clear(true, true, Color.clear);
                    RenderTexture.active = prev;
                    
                    if (texture.isReadable)
                    {
#if UNITY_EDITOR || UNITY_IOS
                        Debug.Log($"[RuntimeAtlas.CreateNewPage] Using ReadPixels for readable texture...");
#endif
                        // For readable textures, use ReadPixels
                        RenderTexture.active = rt;
                        texture.ReadPixels(new Rect(0, 0, _settings.InitialSize, _settings.InitialSize), 0, 0, false);
                        RenderTexture.active = prev;
                        texture.Apply(false, false);
                    }
                    else
                    {
#if UNITY_EDITOR || UNITY_IOS
                        Debug.Log($"[RuntimeAtlas.CreateNewPage] Using Graphics.CopyTexture for non-readable texture...");
#endif
                        // For non-readable textures, use Graphics.CopyTexture
                        Graphics.CopyTexture(rt, texture);
                    }
                }
                catch (Exception ex)
                {
#if UNITY_EDITOR || UNITY_IOS
                    Debug.LogError($"[RuntimeAtlas.CreateNewPage] ✗ CRASH during texture clearing: {ex.Message}\nStack: {ex.StackTrace}");
#endif
                    throw;
                }
                finally
                {
                    if (rt != null)
                    {
                        RenderTexture.ReleaseTemporary(rt);
                    }
                }
            }
            
            _textures.Add(texture);
            _currentPageIndex = pageIndex;
            
#if UNITY_EDITOR || UNITY_IOS
            Debug.Log($"[RuntimeAtlas.CreateNewPage] ✓ Page {pageIndex} created successfully. Total pages: {_textures.Count}, Memory after: {System.GC.GetTotalMemory(false) / 1024 / 1024} MB");
#endif

#if UNITY_IOS
            // ✅ iOS MEMORY FIX: Force garbage collection after creating large texture
            // iOS has strict memory limits - aggressively free unused memory
            if (pageIndex > 0) // Only for pages after the first one
            {
                var memBefore = System.GC.GetTotalMemory(false) / 1024 / 1024;
                System.GC.Collect();
                System.GC.WaitForPendingFinalizers();
                System.GC.Collect();
                var memAfter = System.GC.GetTotalMemory(false) / 1024 / 1024;
                Debug.Log($"[RuntimeAtlas.CreateNewPage] iOS: Forced GC. Memory: {memBefore}MB → {memAfter}MB (freed {memBefore - memAfter}MB)");
            }
#endif
        }

        /// <summary>
        /// Add a texture to the atlas.
        /// <para>⚠️ <b>IMPORTANT</b>: This method COPIES the texture data into the atlas.</para>
        /// <para>If you created or downloaded the input texture, you MUST destroy it after calling this method to avoid memory leaks.</para>
        /// <para>Textures loaded from Resources or assigned in the Inspector should NOT be destroyed.</para>
        /// </summary>
        /// <param name="texture">The texture to add to the atlas</param>
        /// <param name="spriteVersion">Optional sprite version number (default is 0). Used to track content changes.</param>
        /// <returns>A tuple containing the result status and an AtlasEntry reference (null if not successful).</returns>
        /// <example>
        /// <code>
        /// // Example 1: Downloaded texture (MUST destroy after adding)
        /// var downloadedTexture = await DownloadTextureAsync(url);
        /// var (result, entry) = atlas.Add(downloadedTexture);
        /// Object.Destroy(downloadedTexture); // ← REQUIRED to prevent memory leak!
        /// 
        /// // Example 2: Resources texture (DO NOT destroy)
        /// var resourceTexture = Resources.Load&lt;Texture2D&gt;("MyTexture");
        /// var (result, entry) = atlas.Add(resourceTexture);
        /// // Do NOT destroy resourceTexture - Unity manages it
        /// 
        /// // Example 3: With version tracking
        /// var (result, entry) = atlas.Add(texture, spriteVersion: 2);
        /// </code>
        /// </example>
        public (AddResult result, AtlasEntry entry) Add(Texture2D texture, int spriteVersion = 0)
        {
            if (_isDisposed)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[RuntimeAtlas.Add] Atlas is disposed");
#endif
                return (AddResult.Failed, null);
            }
            
            if (texture == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[RuntimeAtlas.Add] Null texture provided");
#endif
                return (AddResult.InvalidTexture, null);
            }

            // ✅ FIX: Check if texture is readable before proceeding
            if (!EnsureTextureReadable(texture))
            {
                return (AddResult.InvalidTexture, null);
            }

#if UNITY_EDITOR
            var profiler = RuntimeAtlasProfiler.Begin("Add", GetAtlasName(), $"{texture.name} ({texture.width}x{texture.height})");
#endif

#if UNITY_EDITOR
            Debug.Log($"[RuntimeAtlas] Add: Packing '{texture.name}': {texture.width}x{texture.height}");
#endif

            // Use internal method for packing
            var (result, entry) = AddInternal(texture, spriteVersion);
            
            if (result != AddResult.Success)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[RuntimeAtlas] Add: Failed with result: {result}");
                RuntimeAtlasProfiler.End(profiler);
#endif
                return (result, null);
            }
            
            // Apply texture changes immediately for single adds to the page that was modified
            if (entry != null)
            {
                try
                {
                    var pageTexture = _textures[entry.TextureIndex];
                    // ✅ MEMORY FIX: Use makeNoLongerReadable to free CPU memory immediately
                    bool makeNoLongerReadable = !_settings.Readable;
#if UNITY_EDITOR
                    Debug.Log($"[RuntimeAtlas] Applying texture changes: Readable={_settings.Readable}, makeNoLongerReadable={makeNoLongerReadable}, TextureReadable={pageTexture.isReadable}");
#endif
                    pageTexture.Apply(false, makeNoLongerReadable);
                    _isDirty = false;
#if UNITY_EDITOR
                    Debug.Log($"[RuntimeAtlas] After Apply: TextureReadable={pageTexture.isReadable}");
#endif
                }
                catch (UnityException ex)
                {
#if UNITY_EDITOR
                    Debug.LogWarning($"[RuntimeAtlas] Could not apply texture changes: {ex.Message}");
#endif
                }
            }

            // Validate no overlaps
            ValidateNoOverlaps();
            
#if UNITY_EDITOR
            Debug.Log($"[RuntimeAtlas] Add: Complete. Entry ID: {entry.Id}, Page: {entry.TextureIndex}, Total entries: {_entries.Count}");
            RuntimeAtlasProfiler.End(profiler);
#endif
            return (AddResult.Success, entry);
        }

        /// <summary>
        /// Add a texture to the atlas with sprite properties for proper recreation.
        /// <para>⚠️ <b>IMPORTANT</b>: This method COPIES the texture data into the atlas.</para>
        /// <para>If you created or downloaded the input texture, you MUST destroy it after calling this method to avoid memory leaks.</para>
        /// </summary>
        /// <param name="texture">The texture to add</param>
        /// <param name="border">Border values for 9-slicing (left, bottom, right, top)</param>
        /// <param name="pivot">Pivot point (0-1 normalized)</param>
        /// <param name="pixelsPerUnit">Pixels per unit value</param>
        /// <param name="spriteVersion">Optional sprite version number (default is 0)</param>
        /// <returns>A tuple containing the result status and an AtlasEntry reference (null if not successful).</returns>
        public (AddResult result, AtlasEntry entry) Add(Texture2D texture, Vector4 border, Vector2 pivot, float pixelsPerUnit = 100f, int spriteVersion = 0)
        {
            if (_isDisposed)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[RuntimeAtlas.Add] Atlas is disposed");
#endif
                return (AddResult.Failed, null);
            }
            
            if (texture == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[RuntimeAtlas.Add] Null texture provided");
#endif
                return (AddResult.InvalidTexture, null);
            }

            // Validate pivot is in normalized range (0-1)
            if (pivot.x < 0f || pivot.x > 1f || pivot.y < 0f || pivot.y > 1f)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[RuntimeAtlas.Add] Pivot should be normalized (0-1 range). Got: {pivot}. " +
                    "Clamping to valid range. If you intended to use pixel coordinates, convert them: " +
                    "normalizedPivot = pixelPivot / textureSize");
#endif
                pivot.x = Mathf.Clamp01(pivot.x);
                pivot.y = Mathf.Clamp01(pivot.y);
            }

            // ✅ FIX: Check if texture is readable before proceeding
            if (!EnsureTextureReadable(texture))
            {
                return (AddResult.InvalidTexture, null);
            }

#if UNITY_EDITOR
            var profiler = RuntimeAtlasProfiler.Begin("Add", GetAtlasName(), $"{texture.name} ({texture.width}x{texture.height})");
#endif

            // Use internal method for packing
            var (result, entry) = AddInternal(texture, border, pivot, pixelsPerUnit, spriteVersion);
            
            if (result != AddResult.Success)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[RuntimeAtlas] Add: Failed with result: {result}");
                RuntimeAtlasProfiler.End(profiler);
#endif
                return (result, null);
            }
            
            // Apply texture changes
            if (entry != null)
            {
                try
                {
                    var pageTexture = _textures[entry.TextureIndex];
                    // ✅ MEMORY FIX: Use makeNoLongerReadable to free CPU memory immediately
                    bool makeNoLongerReadable = !_settings.Readable;
                    pageTexture.Apply(false, makeNoLongerReadable);
                    _isDirty = false;
                }
                catch (UnityException ex)
                {
#if UNITY_EDITOR
                    Debug.LogWarning($"[RuntimeAtlas] Could not apply texture changes: {ex.Message}");
#endif
                }
            }

            ValidateNoOverlaps();

#if UNITY_EDITOR
            RuntimeAtlasProfiler.End(profiler);
#endif
            return (AddResult.Success, entry);
        }

        /// <summary>
        /// Add a texture to the atlas with a specific name for later retrieval.
        /// If a texture with the same name already exists, it will be replaced.
        /// <para>⚠️ <b>IMPORTANT</b>: This method COPIES the texture data into the atlas.</para>
        /// <para>If you created or downloaded the input texture, you MUST destroy it after calling this method to avoid memory leaks.</para>
        /// </summary>
        /// <param name="name">The unique name to associate with this texture</param>
        /// <param name="texture">The texture to add</param>
        /// <param name="spriteVersion">Optional sprite version number (default is 0). If the same version already exists, the existing entry is returned.</param>
        /// <returns>A tuple containing the result status and an AtlasEntry reference (null if not successful).</returns>
        public (AddResult result, AtlasEntry entry) Add(string name, Texture2D texture, int spriteVersion = 0)
        {
            if (string.IsNullOrEmpty(name))
            {
#if UNITY_EDITOR
                Debug.LogWarning("[RuntimeAtlas.Add] Null or empty name provided");
#endif
                return (AddResult.InvalidTexture, null);
            }

            // Check if entry with same name and version already exists
            if (_entriesByName.TryGetValue(name, out var existingEntry))
            {
                if (existingEntry.SpriteVersion == spriteVersion)
                {
#if UNITY_EDITOR
                    Debug.Log($"[RuntimeAtlas.Add] Entry '{name}' with version {spriteVersion} already exists, returning existing entry");
#endif
                    return (AddResult.Success, existingEntry);
                }
                
                // Different version - remove old and add new
#if UNITY_EDITOR
                Debug.Log($"[RuntimeAtlas.Add] Replacing '{name}' version {existingEntry.SpriteVersion} with version {spriteVersion}");
#endif
                RemoveById(existingEntry.Id);
            }

            // Add the texture with version
            var (result, entry) = Add(texture, spriteVersion);
            
            if (result == AddResult.Success && entry != null)
            {
                // Store in name dictionary
                _entriesByName[name] = entry;
            }

            return (result, entry);
        }

        /// <summary>
        /// Check if a texture with the given name exists in the atlas.
        /// </summary>
        /// <param name="name">The name to check</param>
        /// <returns>True if a texture with this name exists in the atlas</returns>
        public bool ContainsName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            
            return _entriesByName.ContainsKey(name);
        }

        /// <summary>
        /// Replace an existing entry with a new texture.
        /// If the entry doesn't exist, it will be added instead.
        /// </summary>
        /// <param name="name">The name of the entry to replace</param>
        /// <param name="texture">The new texture</param>
        /// <returns>A tuple containing the result status and an AtlasEntry reference</returns>
        public (AddResult result, AtlasEntry entry) Replace(string name, Texture2D texture)
        {
            if (string.IsNullOrEmpty(name))
            {
#if UNITY_EDITOR
                Debug.LogWarning("[RuntimeAtlas.Replace] Null or empty name provided");
#endif
                return (AddResult.InvalidTexture, null);
            }

            bool hadExisting = ContainsName(name);
            
            // Use Add with name - it already handles replacement
            var (result, entry) = Add(name, texture);

#if UNITY_EDITOR
            if (result == AddResult.Success)
            {
                if (hadExisting)
                {
                    Debug.Log($"[RuntimeAtlas.Replace] Replaced existing entry '{name}'");
                }
                else
                {
                    Debug.Log($"[RuntimeAtlas.Replace] Added new entry '{name}' (didn't exist before)");
                }
            }
#endif

            return (result, entry);
        }

        /// <summary>
        /// Replace an existing entry with a new texture using sprite properties.
        /// If the entry doesn't exist, it will be added instead.
        /// </summary>
        /// <param name="name">The name of the entry to replace</param>
        /// <param name="texture">The new texture</param>
        /// <param name="border">Border values for 9-slicing</param>
        /// <param name="pivot">Pivot point of the sprite</param>
        /// <param name="pixelsPerUnit">Pixels per unit value</param>
        /// <returns>A tuple containing the result status and an AtlasEntry reference</returns>
        public (AddResult result, AtlasEntry entry) Replace(string name, Texture2D texture, Vector4 border, Vector2 pivot, float pixelsPerUnit = 100f)
        {
            if (string.IsNullOrEmpty(name))
            {
#if UNITY_EDITOR
                Debug.LogWarning("[RuntimeAtlas.Replace] Null or empty name provided");
#endif
                return (AddResult.InvalidTexture, null);
            }

            // Remove existing if present
            if (_entriesByName.TryGetValue(name, out var existingEntry))
            {
                RemoveById(existingEntry.Id);
            }

            // Add with sprite properties
            var (result, entry) = Add(texture, border, pivot, pixelsPerUnit);

            if (result == AddResult.Success && entry != null)
            {
                _entriesByName[name] = entry;
            }

            return (result, entry);
        }

        /// <summary>
        /// Remove multiple entries by their names.
        /// </summary>
        /// <param name="names">Collection of names to remove</param>
        /// <returns>Number of entries successfully removed</returns>
        public int RemoveByNames(IEnumerable<string> names)
        {
            if (names == null)
            {
                return 0;
            }

            int removedCount = 0;
            foreach (var name in names)
            {
                if (RemoveByName(name))
                {
                    removedCount++;
                }
            }

            return removedCount;
        }

        /// <summary>
        /// Clear all entries from the atlas.
        /// </summary>
        public void Clear()
        {
            // Dispose all entries
            foreach (var entry in _entries.Values)
            {
                entry?.Dispose();
            }

            _entries.Clear();
            _entriesByName.Clear();
            _recycledIds.Clear();

            // Reset all packers
            for (int i = 0; i < _packers.Count; i++)
            {
                if (_packers[i] != null)
                {
                    // Reinitialize packers with current size
                    var size = _textures[i] != null ? _textures[i].width : _settings.InitialSize;
                    _packers[i].Initialize(size, size);
                }
            }

            // Clear all textures
            for (int i = 0; i < _textures.Count; i++)
            {
                if (_textures[i] != null && _settings.Readable)
                {
                    var clearPixels = new Color32[_textures[i].width * _textures[i].height];
                    _textures[i].SetPixels32(clearPixels);
                    _textures[i].Apply(false, false);
                }
            }

            _version++;
            _isDirty = false;

#if UNITY_EDITOR
            Debug.Log($"[RuntimeAtlas] Cleared all entries from atlas '{GetAtlasName()}'");
#endif
        }

        /// <summary>
        /// Get an entry by name.
        /// </summary>
        /// <param name="name">The name of the entry to retrieve</param>
        /// <returns>The AtlasEntry if found, null otherwise</returns>
        public AtlasEntry GetEntryByName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            
            return _entriesByName.TryGetValue(name, out var entry) ? entry : null;
        }

        /// <summary>
        /// Get a sprite by name from the atlas.
        /// </summary>
        /// <param name="name">The name of the texture</param>
        /// <param name="pixelsPerUnit">Optional pixels per unit for the sprite</param>
        /// <param name="pivot">Optional pivot point (0-1 normalized). Default is center (0.5, 0.5)</param>
        /// <returns>A sprite if the named texture exists, null otherwise</returns>
        public Sprite GetSprite(string name, float pixelsPerUnit = 100f, Vector2? pivot = null)
        {
            var entry = GetEntryByName(name);
            if (entry == null || !entry.IsValid)
                return null;
            
            return entry.CreateSprite(pixelsPerUnit, pivot ?? new Vector2(0.5f, 0.5f));
        }
        
        /// <summary>
        /// Get the sprite version number for a named entry.
        /// </summary>
        /// <param name="name">The name of the sprite</param>
        /// <returns>The sprite version number, or -1 if not found</returns>
        public int GetSpriteVersion(string name)
        {
            var entry = GetEntryByName(name);
            return entry?.SpriteVersion ?? -1;
        }
        
        /// <summary>
        /// Validate that no entries overlap with each other.
        /// Only runs in editor for performance.
        /// </summary>
#if UNITY_EDITOR
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void ValidateNoOverlaps()
        {
            // Skip validation for large atlases for performance
            if (_entries.Count > 100)
            {
                return;
            }
                
            var entries = _entries.Values.ToArray();
            for (var i = 0; i < entries.Length; i++)
            {
                for (var j = i + 1; j < entries.Length; j++)
                {
                    // Only check entries on the same page
                    if (entries[i].TextureIndex != entries[j].TextureIndex)
                    {
                        continue;
                    }
                        
                    var rect1 = entries[i].Rect;
                    var rect2 = entries[j].Rect;
                    
                    // Check if rects overlap
                    if (!(rect1.xMax <= rect2.xMin || rect2.xMax <= rect1.xMin ||
                          rect1.yMax <= rect2.yMin || rect2.yMax <= rect1.yMin))
                    {
                        Debug.LogError($"[RuntimeAtlas] OVERLAP DETECTED! '{entries[i].Name}' [{rect1}] overlaps with '{entries[j].Name}' [{rect2}] on page {entries[i].TextureIndex}");
                    }
                }
            }
        }
#else
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void ValidateNoOverlaps()
        {
        }
#endif

        /// <summary>
        /// Add multiple textures to the atlas in a single batch.
        /// More efficient than adding one at a time.
        /// Returns only successfully added entries (skips failures).
        /// <para>⚠️ <b>IMPORTANT</b>: This method COPIES the texture data into the atlas.</para>
        /// <para>The input textures array is NOT modified. If you created or downloaded these textures, 
        /// you MUST destroy them after calling this method to avoid memory leaks.</para>
        /// </summary>
        /// <param name="textures">Array of textures to add</param>
        /// <returns>Array of successfully added AtlasEntry objects</returns>
        /// <example>
        /// <code>
        /// // Example: Batch add with cleanup
        /// var downloadedTextures = new Texture2D[10];
        /// for (int i = 0; i &lt; 10; i++)
        ///     downloadedTextures[i] = await DownloadTextureAsync(urls[i]);
        /// 
        /// var entries = atlas.AddBatch(downloadedTextures);
        /// 
        /// // REQUIRED: Destroy input textures to prevent memory leak
        /// foreach (var tex in downloadedTextures)
        ///     if (tex != null) Object.Destroy(tex);
        /// </code>
        /// </example>
        public AtlasEntry[] AddBatch(Texture2D[] textures)
        {
            if (_isDisposed)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[RuntimeAtlas.AddBatch] Atlas is disposed");
#endif
                return Array.Empty<AtlasEntry>();
            }
            
            if (textures == null || textures.Length == 0)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[RuntimeAtlas.AddBatch] No textures provided");
#endif
                return Array.Empty<AtlasEntry>();
            }

            // ✅ OPTIMIZATION: Pre-allocate and avoid LINQ allocations
            var textureDict = new Dictionary<string, Texture2D>(textures.Length);
            for (var i = 0; i < textures.Length; i++)
            {
                if (textures[i] != null)
                {
                    // Use texture name or index as key
                    var key = string.IsNullOrEmpty(textures[i].name) ? $"texture_{i}" : textures[i].name;
                    textureDict[key] = textures[i];
                }
            }

            var results = AddBatch(textureDict);
            
            // ✅ OPTIMIZATION: Avoid LINQ allocation - use List
            var entries = new AtlasEntry[results.Count];
            var index = 0;
            foreach (var entry in results.Values)
            {
                entries[index++] = entry;
            }
            return entries;
        }

        /// <summary>
        /// Add multiple textures to the atlas in a single batch with named keys.
        /// More efficient than adding one at a time.
        /// Returns a dictionary mapping keys to successfully added entries (skips failures).
        /// If a texture with the same key already exists, it will be replaced.
        /// <para>⚠️ <b>IMPORTANT</b>: This method COPIES the texture data into the atlas.</para>
        /// <para>The input dictionary is NOT modified. If you created or downloaded these textures,
        /// you MUST destroy them after calling this method to avoid memory leaks.</para>
        /// </summary>
        /// <param name="textures">Dictionary of sprite keys to textures</param>
        /// <param name="spriteVersion">Optional sprite version number (default is 0). Applied to all sprites in the batch.</param>
        /// <returns>Dictionary mapping keys to successfully added AtlasEntry objects</returns>
        /// <example>
        /// <code>
        /// // Example: Batch add with cleanup and version
        /// var textureDict = new Dictionary&lt;string, Texture2D&gt;();
        /// textureDict["enemy"] = await DownloadTextureAsync(url1);
        /// textureDict["player"] = await DownloadTextureAsync(url2);
        /// 
        /// var entries = atlas.AddBatch(textureDict, spriteVersion: 2);
        /// 
        /// // REQUIRED: Destroy input textures to prevent memory leak
        /// foreach (var tex in textureDict.Values)
        ///     if (tex != null) Object.Destroy(tex);
        /// </code>
        /// </example>
        public Dictionary<string, AtlasEntry> AddBatch(Dictionary<string, Texture2D> textures, int spriteVersion = 0)
        {
            if (_isDisposed)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[RuntimeAtlas.AddBatch] Atlas is disposed");
#endif
                return new Dictionary<string, AtlasEntry>();
            }
            
            if (textures == null || textures.Count == 0)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[RuntimeAtlas.AddBatch] No textures provided");
#endif
                return new Dictionary<string, AtlasEntry>();
            }

#if UNITY_EDITOR
            var profiler = RuntimeAtlasProfiler.Begin("AddBatch", GetAtlasName(), $"{textures.Count} textures");
#endif

#if UNITY_EDITOR
            Debug.Log($"[RuntimeAtlas] AddBatch: Starting batch of {textures.Count} textures with keys");
#endif

            // ✅ OPTIMIZATION: Batch remove existing entries
            foreach (var key in textures.Keys)
            {
                if (_entriesByName.TryGetValue(key, out var existingEntry))
                {
#if UNITY_EDITOR
                    Debug.Log($"[RuntimeAtlas] AddBatch: Removing existing entry with key '{key}' (ID: {existingEntry.Id})");
#endif
                    RemoveById(existingEntry.Id);
                }
            }

            var successfulEntries = new Dictionary<string, AtlasEntry>(textures.Count);

            // ✅ OPTIMIZATION: Validate all textures are readable BEFORE starting batch
            foreach (var kvp in textures)
            {
                if (kvp.Value != null && !EnsureTextureReadable(kvp.Value))
                {
#if UNITY_EDITOR
                    Debug.LogError($"[RuntimeAtlas.AddBatch] Texture '{kvp.Value.name}' (key: '{kvp.Key}') is not readable. Aborting batch operation.");
#endif
                    return new Dictionary<string, AtlasEntry>();
                }
            }

            // ✅ OPTIMIZATION: Avoid LINQ - use manual sort with array
            var sortedList = new List<(string key, Texture2D tex, int area)>(textures.Count);
            foreach (var kvp in textures)
            {
                if (kvp.Value != null)
                {
                    // Use texture name or index as key
                    var key = string.IsNullOrEmpty(kvp.Value.name) ? kvp.Value.GetInstanceID().ToString() : kvp.Value.name;
                    sortedList.Add((kvp.Key, kvp.Value, kvp.Value.width * kvp.Value.height));
                }
            }
            
            // Sort descending by area for better packing
            sortedList.Sort((a, b) => b.area.CompareTo(a.area));

            // ✅ MEMORY OPTIMIZATION: Track which pages are modified and collect blit operations
            var modifiedPages = new Dictionary<int, List<(Texture2D source, int x, int y)>>();

            // Pack all textures WITHOUT blitting yet - just track positions
            var successCount = 0;
            var failCount = 0;
            var pendingBlits = new List<(string key, Texture2D tex, int pageIndex, RectInt contentRect, int id, Rect uvRect)>();
            
            for (var i = 0; i < sortedList.Count; i++)
            {
                var (key, tex, _) = sortedList[i];
                
                // Try to pack without blitting
                var (result, pageIndex, packedRect) = TryPackTextureInternal(tex, spriteVersion);
                
                if (result == AddResult.Success)
                {
                    // Calculate rects but don't blit yet
                    var contentRect = new RectInt(
                        packedRect.x + _settings.Padding,
                        packedRect.y + _settings.Padding,
                        tex.width,
                        tex.height
                    );
                    
                    var currentTexture = _textures[pageIndex];
                    var uvRect = new Rect(
                        (float)contentRect.x / (float)currentTexture.width,
                        (float)contentRect.y / (float)currentTexture.height,
                        (float)contentRect.width / (float)currentTexture.width,
                        (float)contentRect.height / (float)currentTexture.height
                    );
                    
                    var id = GetNextId();
                    
                    // Add to pending blits
                    pendingBlits.Add((key, tex, pageIndex, contentRect, id, uvRect));
                    
                    // Track modified page
                    if (!modifiedPages.ContainsKey(pageIndex))
                    {
                        modifiedPages[pageIndex] = new List<(Texture2D, int, int)>();
                    }
                    modifiedPages[pageIndex].Add((tex, contentRect.x, contentRect.y));
                    
                    successCount++;
#if UNITY_EDITOR
                    Debug.Log($"[RuntimeAtlas] Batch ['{key}']: Queued '{tex.name}' for page {pageIndex}");
#endif
                }
                else
                {
                    failCount++;
#if UNITY_EDITOR
                    Debug.LogWarning($"[RuntimeAtlas] Batch ['{key}']: Failed to pack '{tex.name}' -> Result: {result}");
#endif
                    
                    // If atlas is full, stop trying to add more
                    if (result == AddResult.Full)
                    {
#if UNITY_EDITOR
                        Debug.LogWarning($"[RuntimeAtlas] AddBatch: Atlas full after {successCount} textures. Stopping batch.");
#endif
                        break;
                    }
                }
            }
            
            // ✅ CRITICAL MEMORY FIX: Batch all blits per page to avoid creating RenderTexture for each sprite
            if (successCount > 0)
            {
#if UNITY_EDITOR
                Debug.Log($"[RuntimeAtlas] AddBatch: Performing batched blits for {successCount} textures across {modifiedPages.Count} pages");
#endif
                
                foreach (var kvp in modifiedPages)
                {
                    var pageIndex = kvp.Key;
                    var operations = kvp.Value.ToArray();
                    
                    try
                    {
                        // Batch blit all textures to this page at once (creates only ONE RenderTexture per page)
                        TextureBlitter.BatchBlit(_textures[pageIndex], operations);
#if UNITY_EDITOR
                        Debug.Log($"[RuntimeAtlas] AddBatch: Batched {operations.Length} blits to page {pageIndex}");
#endif
                    }
                    catch (Exception ex)
                    {
#if UNITY_EDITOR
                        Debug.LogError($"[RuntimeAtlas] AddBatch: Batch blit failed for page {pageIndex}: {ex.Message}");
#endif
                        // If batch blit fails, clear pending entries for this page
                        pendingBlits.RemoveAll(p => p.pageIndex == pageIndex);
                        continue;
                    }
                }
                
                // Now create all the atlas entries
                foreach (var (key, tex, pageIndex, contentRect, id, uvRect) in pendingBlits)
                {
                    var entry = new AtlasEntry(this, id, pageIndex, contentRect, uvRect, tex.name, default, default, 100f, spriteVersion);
                    _entries[id] = entry;
                    _entriesByName[key] = entry;
                    successfulEntries[key] = entry;
                }
                
                _version++;
                _isDirty = true;

                // ✅ OPTIMIZATION: Apply changes ONCE to all modified pages after ALL textures have been added
                // This avoids toggling readable state multiple times per texture
                bool makeNoLongerReadable = !_settings.Readable;
                
#if UNITY_EDITOR || UNITY_IOS
                if (makeNoLongerReadable)
                {
                    Debug.Log($"[RuntimeAtlas.AddBatch] Applying and making {modifiedPages.Count} pages non-readable to save memory...");
                }
                else
                {
                    Debug.Log($"[RuntimeAtlas.AddBatch] Applying changes to {modifiedPages.Count} pages (keeping readable)...");
                }
#endif
                
                try
                {
                    foreach (var pageIndex in modifiedPages.Keys)
                    {
                        if (pageIndex >= 0 && pageIndex < _textures.Count)
                        {
                            var pageTexture = _textures[pageIndex];
                            
                            // Skip if we're trying to make non-readable but it's already non-readable
                            if (makeNoLongerReadable && !pageTexture.isReadable)
                            {
#if UNITY_EDITOR
                                Debug.Log($"[RuntimeAtlas.AddBatch] Page {pageIndex} already non-readable, skipping Apply");
#endif
                                continue;
                            }
                            
                            // ✅ KEY OPTIMIZATION: Call Apply ONCE per page after all textures added
                            // makeNoLongerReadable=true: Frees CPU memory (texture becomes non-readable)
                            // makeNoLongerReadable=false: Keeps CPU memory (texture stays readable)
                            pageTexture.Apply(false, makeNoLongerReadable);
                            
#if UNITY_EDITOR || UNITY_IOS
                            Debug.Log($"[RuntimeAtlas.AddBatch] ✓ Page {pageIndex}: Applied (readable={!makeNoLongerReadable})");
#endif
                        }
                    }
                    _isDirty = false;
                }
                catch (UnityException ex)
                {
#if UNITY_EDITOR || UNITY_IOS
                    Debug.LogError($"[RuntimeAtlas.AddBatch] ✗ Failed to apply texture changes: {ex.Message}\nStack: {ex.StackTrace}");
#endif
                }
            }
            
#if UNITY_EDITOR || UNITY_IOS
            Debug.Log($"[RuntimeAtlas.AddBatch] ✓ COMPLETE. Success: {successCount}, Failed: {failCount}, Final memory: {System.GC.GetTotalMemory(false) / 1024 / 1024} MB");
#endif
            
#if UNITY_EDITOR
            Debug.Log($"[RuntimeAtlas] AddBatch: Complete. Added: {successCount}, Failed: {failCount}, Total entries in atlas: {_entries.Count}");
            RuntimeAtlasProfiler.End(profiler);
#endif

            return successfulEntries;
        }

        /// <summary>
        /// Add multiple textures to the atlas in a single batch with individual versions for each sprite.
        /// More efficient than adding one at a time.
        /// Returns a dictionary mapping keys to successfully added entries (skips failures).
        /// If a texture with the same key already exists, it will be replaced based on version comparison.
        /// <para>⚠️ <b>IMPORTANT</b>: This method COPIES the texture data into the atlas.</para>
        /// <para>The input dictionaries are NOT modified. If you created or downloaded these textures,
        /// you MUST destroy them after calling this method to avoid memory leaks.</para>
        /// </summary>
        /// <param name="textures">Dictionary of sprite keys to textures</param>
        /// <param name="versions">Dictionary of sprite keys to version numbers. If a key is missing, version defaults to 0.</param>
        /// <returns>Dictionary mapping keys to successfully added AtlasEntry objects</returns>
        /// <example>
        /// <code>
        /// // Example: Batch add with individual versions
        /// var textureDict = new Dictionary&lt;string, Texture2D&gt;();
        /// textureDict["enemy"] = enemyTexture;
        /// textureDict["player"] = playerTexture;
        /// 
        /// var versionDict = new Dictionary&lt;string, int&gt;();
        /// versionDict["enemy"] = 2;   // Enemy is version 2
        /// versionDict["player"] = 5;  // Player is version 5
        /// 
        /// var entries = atlas.AddBatch(textureDict, versionDict);
        /// 
        /// // REQUIRED: Destroy input textures to prevent memory leak
        /// foreach (var tex in textureDict.Values)
        ///     if (tex != null) Object.Destroy(tex);
        /// </code>
        /// </example>
        public Dictionary<string, AtlasEntry> AddBatch(Dictionary<string, Texture2D> textures, Dictionary<string, int> versions)
        {
            if (_isDisposed)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[RuntimeAtlas.AddBatch] Atlas is disposed");
#endif
                return new Dictionary<string, AtlasEntry>();
            }
            
            if (textures == null || textures.Count == 0)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[RuntimeAtlas.AddBatch] No textures provided");
#endif
                return new Dictionary<string, AtlasEntry>();
            }

#if UNITY_EDITOR
            var profiler = RuntimeAtlasProfiler.Begin("AddBatch", GetAtlasName(), $"{textures.Count} textures with individual versions");
#endif

#if UNITY_EDITOR
            Debug.Log($"[RuntimeAtlas] AddBatch: Starting batch of {textures.Count} textures with individual versions");
#endif

            // ✅ OPTIMIZATION: Validate all textures are readable BEFORE starting batch
            foreach (var kvp in textures)
            {
                if (kvp.Value != null && !EnsureTextureReadable(kvp.Value))
                {
#if UNITY_EDITOR
                    Debug.LogError($"[RuntimeAtlas.AddBatch] Texture '{kvp.Value.name}' (key: '{kvp.Key}') is not readable. Aborting batch operation.");
#endif
                    return new Dictionary<string, AtlasEntry>();
                }
            }

            // ✅ OPTIMIZATION: Batch remove/check existing entries based on version
            foreach (var key in textures.Keys)
            {
                if (_entriesByName.TryGetValue(key, out var existingEntry))
                {
                    int newVersion = versions != null && versions.TryGetValue(key, out var v) ? v : 0;
                    
                    if (existingEntry.SpriteVersion == newVersion)
                    {
#if UNITY_EDITOR
                        Debug.Log($"[RuntimeAtlas] AddBatch: Entry '{key}' with version {newVersion} already exists, will skip");
#endif
                        continue;
                    }
                    
#if UNITY_EDITOR
                    Debug.Log($"[RuntimeAtlas] AddBatch: Removing existing entry '{key}' (v{existingEntry.SpriteVersion}) to replace with v{newVersion}");
#endif
                    RemoveById(existingEntry.Id);
                }
            }

            var successfulEntries = new Dictionary<string, AtlasEntry>(textures.Count);

            // ✅ OPTIMIZATION: Avoid LINQ - use manual sort with array
            var sortedList = new List<(string key, Texture2D tex, int area, int version)>(textures.Count);
            foreach (var kvp in textures)
            {
                if (kvp.Value != null)
                {
                    int version = versions != null && versions.TryGetValue(kvp.Key, out var v) ? v : 0;
                    sortedList.Add((kvp.Key, kvp.Value, kvp.Value.width * kvp.Value.height, version));
                }
            }
            
            // Sort descending by area for better packing
            sortedList.Sort((a, b) => b.area.CompareTo(a.area));

            // ✅ OPTIMIZATION: Track which pages are modified to only apply those
            var modifiedPages = new HashSet<int>();

            // Pack all textures WITHOUT applying after each one
            var successCount = 0;
            var failCount = 0;
            var skippedCount = 0;
            
            for (var i = 0; i < sortedList.Count; i++)
            {
                var (key, tex, _, version) = sortedList[i];
                
                // Check if entry with same version already exists (might have been skipped in removal phase)
                if (_entriesByName.TryGetValue(key, out var existingEntry) && existingEntry.SpriteVersion == version)
                {
                    successfulEntries[key] = existingEntry;
                    skippedCount++;
#if UNITY_EDITOR
                    Debug.Log($"[RuntimeAtlas] Batch ['{key}']: Using existing entry with version {version}");
#endif
                    continue;
                }
                
                var (result, entry) = AddInternal(tex, version);
                if (result == AddResult.Success && entry != null)
                {
                    // Store in name dictionary
                    _entriesByName[key] = entry;
                    successfulEntries[key] = entry;
                    modifiedPages.Add(entry.TextureIndex);
                    successCount++;
#if UNITY_EDITOR
                    Debug.Log($"[RuntimeAtlas] Batch ['{key}']: Added '{tex.name}' with version {version} -> Entry ID {entry.Id}");
#endif
                }
                else
                {
                    failCount++;
#if UNITY_EDITOR
                    Debug.LogWarning($"[RuntimeAtlas] Batch ['{key}']: Failed to add '{tex.name}' -> Result: {result}");
#endif
                    
                    // If atlas is full, stop trying to add more
                    if (result == AddResult.Full)
                    {
#if UNITY_EDITOR
                        Debug.LogWarning($"[RuntimeAtlas] AddBatch: Atlas full after {successCount} textures. Stopping batch.");
#endif
                        break;
                    }
                }
            }

            // ✅ OPTIMIZATION: Apply changes ONCE to all modified pages after ALL textures have been added
            // This avoids toggling readable state multiple times per texture
            if (successCount > 0)
            {
                bool makeNoLongerReadable = !_settings.Readable;
                
                try
                {
#if UNITY_EDITOR
                    if (makeNoLongerReadable)
                    {
                        Debug.Log($"[RuntimeAtlas] AddBatch: Applying and making {modifiedPages.Count} pages non-readable to save memory...");
                    }
                    else
                    {
                        Debug.Log($"[RuntimeAtlas] AddBatch: Applying changes to {modifiedPages.Count} pages (keeping readable)...");
                    }
#endif
                    
                    foreach (var pageIndex in modifiedPages)
                    {
                        if (pageIndex >= 0 && pageIndex < _textures.Count)
                        {
                            var pageTexture = _textures[pageIndex];
                            
                            // Skip if we're trying to make non-readable but it's already non-readable
                            if (makeNoLongerReadable && !pageTexture.isReadable)
                            {
#if UNITY_EDITOR
                                Debug.Log($"[RuntimeAtlas] AddBatch: Page {pageIndex} already non-readable, skipping Apply");
#endif
                                continue;
                            }
                            
                            // ✅ KEY OPTIMIZATION: Call Apply ONCE per page after all textures added
                            // makeNoLongerReadable=true: Frees CPU memory (texture becomes non-readable)
                            // makeNoLongerReadable=false: Keeps CPU memory (texture stays readable)
                            pageTexture.Apply(false, makeNoLongerReadable);
                            
#if UNITY_EDITOR
                            Debug.Log($"[RuntimeAtlas] AddBatch: ✓ Page {pageIndex}: Applied (readable={!makeNoLongerReadable})");
#endif
                        }
                    }
                    _isDirty = false;
                }
                catch (UnityException ex)
                {
#if UNITY_EDITOR
                    Debug.LogError($"[RuntimeAtlas] Failed to apply batch changes: {ex.Message}");
#endif
                }

                // ✅ OPTIMIZATION: Skip validation for large batches
#if UNITY_EDITOR
                if (successCount <= 100)
                {
                    ValidateNoOverlaps();
                }
#endif
            }
            
#if UNITY_EDITOR
            Debug.Log($"[RuntimeAtlas] AddBatch: Complete. Added: {successCount}, Skipped (existing): {skippedCount}, Failed: {failCount}, Total entries in atlas: {_entries.Count}");
            RuntimeAtlasProfiler.End(profiler);
#endif

            return successfulEntries;
        }


        /// <summary>
        /// Download images from remote URLs and add them as a batch to the atlas.
        /// This is an optimized method that downloads concurrently and adds all textures in one batch operation.
        /// </summary>
        /// <param name="urls">List of URLs to download images from</param>
        /// <param name="maxConcurrentDownloads">Maximum number of concurrent downloads (default: 4)</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>Dictionary mapping URLs to their atlas entries (null for failed downloads)</returns>
        public async Task<Dictionary<string, AtlasEntry>> DownloadAndAddBatchAsync(
            IEnumerable<string> urls, 
            int maxConcurrentDownloads = 4,
            CancellationToken cancellationToken = default)
        {
            if (urls == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[RuntimeAtlas.DownloadAndAddBatchAsync] URLs collection is null");
#endif
                return new Dictionary<string, AtlasEntry>();
            }

            var urlList = urls.ToList();
            if (urlList.Count == 0)
            {
                return new Dictionary<string, AtlasEntry>();
            }

            // Dictionary to hold the downloaded native textures
            // We MUST reference these to destroy them later
            Dictionary<string, Texture2D> downloadedTextures = null;

            try
            {
                // Create temporary web loader
                using (var webLoader = new AtlasWebLoader(this, maxConcurrentDownloads))
                {
                    // Assume webLoader returns Dictionary<string, Texture2D>
                    // The keys match the keys provided in urlsWithKeys
                    var urlsWithKeys = urls.ToDictionary(u => u, u => u);
                    downloadedTextures = await webLoader.LoadBatchAsync(urlsWithKeys, cancellationToken);
                }

                if (downloadedTextures == null || downloadedTextures.Count == 0)
                {
                    return new Dictionary<string, AtlasEntry>();
                }

                // Add to atlas (This copies pixels, it does NOT take ownership of the texture objects)
                // We use version 0 or maintain existing logic
                return AddBatch(downloadedTextures); 
            }
            finally
            {
                // CRITICAL MEMORY FIX: Destroy all downloaded textures
                // Once AddBatch returns, the pixels are in the atlas. The source textures are garbage.
                if (downloadedTextures != null)
                {
                    foreach (var tex in downloadedTextures.Values)
                    {
                        if (tex != null)
                        {
                            if (Application.isPlaying)
                                UnityEngine.Object.Destroy(tex);
                            else
                                UnityEngine.Object.DestroyImmediate(tex);
                        }
                    }
                    downloadedTextures.Clear();
                }
            }
        }

        /// <summary>
        /// Download images from remote URLs with custom keys and add them as a batch to the atlas.
        /// This is the most optimized method for bulk downloads with named entries.
        /// </summary>
        /// <param name="urlsWithKeys">Dictionary mapping URLs to custom entry keys/names</param>
        /// <param name="maxConcurrentDownloads">Maximum number of concurrent downloads (default: 4)</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>Dictionary mapping keys to their atlas entries (null for failed downloads)</returns>
        public async Task<Dictionary<string, AtlasEntry>> DownloadAndAddBatchAsync(
            Dictionary<string, string> urlsWithKeys,
            Dictionary<string, int> versions = null,
            int maxConcurrentDownloads = 4,
            CancellationToken cancellationToken = default)
        {
            if (urlsWithKeys == null || urlsWithKeys.Count == 0)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[RuntimeAtlas.DownloadAndAddBatchAsync] URLs dictionary is null or empty");
#endif
                return new Dictionary<string, AtlasEntry>();
            }

#if UNITY_EDITOR
            var profiler = RuntimeAtlasProfiler.Begin("DownloadAndAddBatchAsync", GetAtlasName(), $"{urlsWithKeys.Count} URLs");
#endif

#if UNITY_EDITOR
            Debug.Log($"[RuntimeAtlas] DownloadAndAddBatchAsync: Starting download of {urlsWithKeys.Count} images");
#endif

            // Create temporary web loader
            using (var webLoader = new AtlasWebLoader(this, maxConcurrentDownloads))
            {
                // Download all images with custom keys
                var results = await webLoader.DownloadAndAddBatchAsync(urlsWithKeys, versions, cancellationToken);
                
                // Convert sprites to entries
                var entries = new Dictionary<string, AtlasEntry>();
                foreach (var kvp in results)
                {
                    if (kvp.Value != null)
                    {
                        // Get entry by name (the key we provided)
                        var entry = GetEntryByName(kvp.Key);
                        if (entry != null && entry.IsValid)
                        {
                            entries[kvp.Key] = entry;
                        }
                    }
                }

#if UNITY_EDITOR
                Debug.Log($"[RuntimeAtlas] DownloadAndAddBatchAsync: Complete. Added {entries.Count}/{urlsWithKeys.Count} entries");
                RuntimeAtlasProfiler.End(profiler);
#endif

                return entries;
            }
        }

        /// <summary>
        /// Download a single image from a remote URL and add it to the atlas.
        /// For multiple images, use DownloadAndAddBatchAsync for better performance.
        /// </summary>
        /// <param name="url">URL to download image from</param>
        /// <param name="key">Optional custom key/name for the entry (uses URL hash if null)</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>Atlas entry for the downloaded image, or null if failed</returns>
        public async Task<AtlasEntry> DownloadAndAddAsync(
            string url,
            string key = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(url))
            {
#if UNITY_EDITOR
                Debug.LogWarning("[RuntimeAtlas.DownloadAndAddAsync] URL is null or empty");
#endif
                return null;
            }

            // Use single-item batch for consistency
            var urlsWithKeys = new Dictionary<string, string>
            {
                [url] = key ?? $"Remote_{url.GetHashCode():X8}"
            };

            var results = await DownloadAndAddBatchAsync(urlsWithKeys, versions: null, maxConcurrentDownloads: 1, cancellationToken);
            
            return results.Values.FirstOrDefault();
        }
        
        /// <summary>
        /// Ensure a texture is readable before performing operations on it.
        /// </summary>
        /// <param name="texture">The texture to check</param>
        /// <returns>True if the texture is readable or if check is not available, false otherwise</returns>
        private bool EnsureTextureReadable(Texture2D texture)
        {
            if (texture == null)
                return false;

            try
            {
                // Check if texture is readable
                if (!texture.isReadable)
                {
#if UNITY_EDITOR
                    Debug.LogError($"[RuntimeAtlas] Texture '{texture.name}' is not readable. Enable Read/Write in import settings.");
#endif
                    return false;
                }
            }
            catch (Exception)
            {
                // Some texture types don't support isReadable check
                // In this case, we'll try to proceed and catch errors later
                return true;
            }

            return true;
        }

        /// <summary>
        /// Internal add method that doesn't call Apply - for batch operations
        /// Automatically creates new page if current page is full.
        /// </summary>
        private (AddResult result, AtlasEntry entry) AddInternal(Texture2D texture, int spriteVersion = 0)
        {
            if (texture == null)
            {
                return (AddResult.InvalidTexture, null);
            }

            var width = texture.width + _settings.Padding * 2;
            var height = texture.height + _settings.Padding * 2;

            // Check if texture is too large to ever fit
            if (texture.width > _settings.MaxSize || texture.height > _settings.MaxSize)
            {
#if UNITY_EDITOR
                Debug.LogError($"[RuntimeAtlas.AddInternal] Texture '{texture.name}' ({texture.width}x{texture.height}) exceeds MaxSize ({_settings.MaxSize})");
#endif
                return (AddResult.TooLarge, null);
            }

            // Try to pack in current page first
            var pageIndex = _currentPageIndex;
            var packed = TryPackInPage(pageIndex, width, height, out var packedRect);
            
            if (!packed)
            {
                // Try all existing pages before creating a new one
                for (var i = 0; i < _textures.Count; i++)
                {
                    if (i == pageIndex)
                    {
                        continue; // Already tried current page
                    }
                    
                    if (TryPackInPage(i, width, height, out packedRect))
                    {
                        packed = true;
                        pageIndex = i;
                        break;
                    }
                }
                
                // If still not packed, try creating a new page
                if (!packed)
                {
                    // Check if we can create more pages
                    var canCreatePage = _settings.MaxPageCount == -1 || _textures.Count < _settings.MaxPageCount;
                    
                    if (!canCreatePage)
                    {
#if UNITY_EDITOR
                        Debug.LogError($"[RuntimeAtlas.AddInternal] Cannot add texture '{texture.name}': Atlas has reached maximum page limit ({_settings.MaxPageCount} pages) and all pages are full!");
#endif
                        return (AddResult.Full, null);
                    }
                    
                    // Create new page
                    CreateNewPage();
                    pageIndex = _currentPageIndex;
                    
                    // Try packing in new page
                    packed = TryPackInPage(pageIndex, width, height, out packedRect);
                    
                    if (!packed)
                    {
#if UNITY_EDITOR
                        Debug.LogError($"[RuntimeAtlas.AddInternal] Failed to pack '{texture.name}' even in new page! This should not happen.");
#endif
                        return (AddResult.Full, null);
                    }
                }
            }

            var currentTexture = _textures[pageIndex];

            // Adjust for padding - contentRect is the actual visible area
            var contentRect = new RectInt(
                packedRect.x + _settings.Padding,
                packedRect.y + _settings.Padding,
                texture.width,
                texture.height
            );

            // Blit texture to atlas page
            try
            {
                TextureBlitter.Blit(texture, currentTexture, contentRect.x, contentRect.y);
            }
            catch (Exception ex)
            {
#if UNITY_EDITOR
                Debug.LogError($"[RuntimeAtlas.AddInternal] BLIT FAILED for '{texture.name}': {ex.Message}");
#endif
                return (AddResult.Failed, null);
            }
            
            // Calculate UV - ✅ FIX: Ensure float division for precision
            var uvRect = new Rect(
                (float)contentRect.x / (float)currentTexture.width,
                (float)contentRect.y / (float)currentTexture.height,
                (float)contentRect.width / (float)currentTexture.width,
                (float)contentRect.height / (float)currentTexture.height
            );

            // Create entry with texture index
            var id = GetNextId();
            var entry = new AtlasEntry(this, id, pageIndex, contentRect, uvRect, texture.name, default, default, 100f, spriteVersion);
            _entries[id] = entry;
            
            _version++;
            _isDirty = true;

            // Auto-repack if enabled
            if (_settings.RepackOnAdd)
            {
                RepackPage(pageIndex);
            }

            return (AddResult.Success, entry);
        }

        /// <summary>
        /// Internal add method with sprite properties that doesn't call Apply - for batch operations
        /// </summary>
        private (AddResult result, AtlasEntry entry) AddInternal(Texture2D texture, Vector4 border, Vector2 pivot, float pixelsPerUnit, int spriteVersion = 0)
        {
            if (texture == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[RuntimeAtlas.AddInternal] NULL TEXTURE passed!");
#endif
                return (AddResult.InvalidTexture, null);
            }

#if UNITY_EDITOR
            Debug.Log($"[RuntimeAtlas.AddInternal] ===== Starting pack for '{texture.name}' with sprite properties =====");
#endif

            var width = texture.width + _settings.Padding * 2;
            var height = texture.height + _settings.Padding * 2;

            // Check if texture is too large to ever fit
            if (texture.width > _settings.MaxSize || texture.height > _settings.MaxSize)
            {
#if UNITY_EDITOR
                Debug.LogError($"[RuntimeAtlas.AddInternal] Texture '{texture.name}' ({texture.width}x{texture.height}) exceeds MaxSize ({_settings.MaxSize})");
#endif
                return (AddResult.TooLarge, null);
            }

            // Try to pack in current page first
            var pageIndex = _currentPageIndex;
            var packed = TryPackInPage(pageIndex, width, height, out var packedRect);
            
            if (!packed)
            {
                // Try all existing pages before creating a new one
                for (var i = 0; i < _textures.Count; i++)
                {
                    if (i == pageIndex)
                    {
                        continue;
                    }
                    
                    if (TryPackInPage(i, width, height, out packedRect))
                    {
                        packed = true;
                        pageIndex = i;
                        break;
                    }
                }
                
                // If still not packed, try creating a new page
                if (!packed)
                {
                    var canCreatePage = _settings.MaxPageCount == -1 || _textures.Count < _settings.MaxPageCount;
                    
                    if (!canCreatePage)
                    {
#if UNITY_EDITOR
                        Debug.LogError($"[RuntimeAtlas.AddInternal] Cannot add texture '{texture.name}': Atlas full!");
#endif
                        return (AddResult.Full, null);
                    }
                    
                    CreateNewPage();
                    pageIndex = _currentPageIndex;
                    packed = TryPackInPage(pageIndex, width, height, out packedRect);
                    
                    if (!packed)
                    {
#if UNITY_EDITOR
                        Debug.LogError($"[RuntimeAtlas.AddInternal] Failed to pack '{texture.name}' even in new page!");
#endif
                        return (AddResult.Full, null);
                    }
                }
            }

            var currentTexture = _textures[pageIndex];
            var contentRect = new RectInt(
                packedRect.x + _settings.Padding,
                packedRect.y + _settings.Padding,
                texture.width,
                texture.height
            );

            // Blit texture to atlas page
            try
            {
                TextureBlitter.Blit(texture, currentTexture, contentRect.x, contentRect.y);
            }
            catch (Exception ex)
            {
#if UNITY_EDITOR
                Debug.LogError($"[RuntimeAtlas.AddInternal] BLIT FAILED for '{texture.name}': {ex.Message}");
#endif
                return (AddResult.Failed, null);
            }
            
            // Calculate UV - ✅ FIX: Ensure float division for precision
            var uvRect = new Rect(
                (float)contentRect.x / (float)currentTexture.width,
                (float)contentRect.y / (float)currentTexture.height,
                (float)contentRect.width / (float)currentTexture.width,
                (float)contentRect.height / (float)currentTexture.height
            );

            // Create entry with sprite properties
            var id = GetNextId();
            var entry = new AtlasEntry(this, id, pageIndex, contentRect, uvRect, texture.name, border, pivot, pixelsPerUnit, spriteVersion);
            _entries[id] = entry;
            
#if UNITY_EDITOR
            Debug.Log($"[RuntimeAtlas.AddInternal] ===== COMPLETE: Entry ID {id} created for '{texture.name}' on page {pageIndex} with sprite properties =====");
#endif
            
            _version++;
            _isDirty = true;

            if (_settings.RepackOnAdd)
            {
                RepackPage(pageIndex);
            }

            return (AddResult.Success, entry);
        }

        /// <summary>
        /// Try to pack a texture and return its position without blitting.
        /// Used for batch operations to collect all packing info before blitting.
        /// </summary>
        private (AddResult result, int pageIndex, RectInt packedRect) TryPackTextureInternal(Texture2D texture, int spriteVersion = 0)
        {
            if (texture == null)
            {
                return (AddResult.InvalidTexture, -1, default);
            }

            var width = texture.width + _settings.Padding * 2;
            var height = texture.height + _settings.Padding * 2;

            // Check if texture is too large to ever fit
            if (texture.width > _settings.MaxSize || texture.height > _settings.MaxSize)
            {
                return (AddResult.TooLarge, -1, default);
            }

            // Try to pack in current page first
            var pageIndex = _currentPageIndex;
            var packed = TryPackInPage(pageIndex, width, height, out var packedRect);
            
            if (!packed)
            {
                // Try all existing pages before creating a new one
                for (var i = 0; i < _textures.Count; i++)
                {
                    if (i == pageIndex)
                    {
                        continue; // Already tried current page
                    }
                    
                    if (TryPackInPage(i, width, height, out packedRect))
                    {
                        packed = true;
                        pageIndex = i;
                        break;
                    }
                }
                
                // If still not packed, try creating a new page
                if (!packed)
                {
                    // Check if we can create more pages
                    var canCreatePage = _settings.MaxPageCount == -1 || _textures.Count < _settings.MaxPageCount;
                    
                    if (!canCreatePage)
                    {
                        return (AddResult.Full, -1, default);
                    }
                    
                    // Create new page
                    CreateNewPage();
                    pageIndex = _currentPageIndex;
                    
                    // Try packing in new page
                    packed = TryPackInPage(pageIndex, width, height, out packedRect);
                    
                    if (!packed)
                    {
                        return (AddResult.Full, -1, default);
                    }
                }
            }

            return (AddResult.Success, pageIndex, packedRect);
        }

        private bool TryPackInPage(int pageIndex, int width, int height, out RectInt packedRect)
        {
            packedRect = default;
            if (pageIndex < 0 || pageIndex >= _packers.Count)
            {
                return false;
            }

            var packer = _packers[pageIndex];
            var texture = _textures[pageIndex];
            
            // Try to pack at current size
            if (packer.TryPack(width, height, out packedRect))
            {
                return true;
            }

            // Try growing the page (up to MaxSize)
            if (_settings.GrowthStrategy == GrowthStrategy.None)
            {
                return false; // Can't grow, page is full
            }

            var currentSize = texture.width;
            while (currentSize < _settings.MaxSize)
            {
                var newSize = _settings.GrowthStrategy == GrowthStrategy.Double 
                    ? currentSize * 2 
                    : currentSize + currentSize / 2;
                    
                newSize = Mathf.Min(newSize, _settings.MaxSize);
                
                if (newSize == currentSize)
                {
                    break; // Already at max size
                }

#if UNITY_EDITOR
                Debug.Log($"[RuntimeAtlas] Attempting to grow page {pageIndex} from {currentSize} to {newSize}");
#endif
                
                if (TryGrowPage(pageIndex, newSize))
                {
                    if (packer.TryPack(width, height, out packedRect))
                    {
                        return true;
                    }
                }
                
                currentSize = newSize;
            }

            return false; // Page is full and can't grow more
        }

        private bool TryGrowPage(int pageIndex, int newSize)
        {
            if (pageIndex < 0 || pageIndex >= _textures.Count)
            {
                return false;
            }

            var oldTexture = _textures[pageIndex];
            var oldSize = oldTexture.width;
            
            if (newSize <= oldSize || newSize > _settings.MaxSize)
            {
#if UNITY_EDITOR
                Debug.LogError($"[TryGrowPage] Invalid new size {newSize} (old: {oldSize}, max: {_settings.MaxSize})");
#endif
                return false;
            }

#if UNITY_EDITOR
            Debug.Log($"[TryGrowPage] Growing page {pageIndex} from {oldSize}x{oldSize} to {newSize}x{newSize}");
#endif

            Texture2D newTexture = null;
            try
            {
                // Create new larger texture
                newTexture = new Texture2D(newSize, newSize, _settings.Format, _settings.GenerateMipMaps);
                newTexture.filterMode = _settings.FilterMode;
                newTexture.wrapMode = TextureWrapMode.Clamp;
                newTexture.name = $"RuntimeAtlas_Page{pageIndex}";

                // Clear to transparent
                var clearPixels = new Color32[newSize * newSize];
                newTexture.SetPixels32(clearPixels);
                // ✅ MEMORY FIX: Use makeNoLongerReadable to free CPU memory immediately
                bool makeNoLongerReadable = !_settings.Readable;
                newTexture.Apply(false, makeNoLongerReadable);

                // Copy old texture data
                Graphics.CopyTexture(oldTexture, 0, 0, 0, 0, oldSize, oldSize, newTexture, 0, 0, 0, 0);

                // Update packer
                _packers[pageIndex].Resize(newSize, newSize);

                // MEMORY LEAK FIX: Destroy old texture BEFORE replacing reference
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(oldTexture);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(oldTexture);
                }

                // Replace texture
                _textures[pageIndex] = newTexture;

                // Update UVs for all entries on this page
                foreach (var entry in _entries.Values)
                {
                    if (entry.TextureIndex == pageIndex)
                    {
                        // ✅ FIX: Ensure float division for precision
                        var uvRect = new Rect(
                            (float)entry.Rect.x / (float)newSize,
                            (float)entry.Rect.y / (float)newSize,
                            (float)entry.Rect.width / (float)newSize,
                            (float)entry.Rect.height / (float)newSize
                        );
                        entry.UpdateRect(entry.Rect, uvRect);
                        OnEntryUpdated?.Invoke(this, entry);
                    }
                }

#if UNITY_EDITOR
                Debug.Log($"[TryGrowPage] Page {pageIndex} grown successfully to {newSize}x{newSize}");
#endif
                OnAtlasResized?.Invoke(this);

                return true;
            }
            catch (Exception ex)
            {
#if UNITY_EDITOR
                Debug.LogError($"[TryGrowPage] Failed to grow page {pageIndex}: {ex.Message}");
#endif
                // MEMORY LEAK FIX: Clean up new texture if creation failed
                if (newTexture != null)
                {
                    if (Application.isPlaying)
                    {
                        UnityEngine.Object.Destroy(newTexture);
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(newTexture);
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Add multiple textures asynchronously.

        /// <summary>
        /// Remove an entry from the atlas.
        /// </summary>
        public bool Remove(AtlasEntry entry)
        {
            if (_isDisposed)
            {
                return false;
            }
            if (entry == null || entry.Atlas != this)
            {
                return false;
            }

            return RemoveById(entry.Id);
        }

        /// <summary>
        /// Remove an entry by its name.
        /// </summary>
        /// <param name="name">The name of the entry to remove</param>
        /// <returns>True if the entry was found and removed, false otherwise</returns>
        public bool RemoveByName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            if (_entriesByName.TryGetValue(name, out var entry))
            {
                return RemoveById(entry.Id);
            }

            return false;
        }

        /// <summary>
        /// Remove an entry by ID.
        /// </summary>
        public bool RemoveById(int id)
        {
            if (!_entries.TryGetValue(id, out var entry))
            {
                return false;
            }

#if UNITY_EDITOR
            var profiler = RuntimeAtlasProfiler.Begin("Remove", GetAtlasName(), $"Entry ID: {id}");
#endif

            var fullRect = new RectInt(
                entry.Rect.x - _settings.Padding,
                entry.Rect.y - _settings.Padding,
                entry.Rect.width + _settings.Padding * 2,
                entry.Rect.height + _settings.Padding * 2
            );

            // Free the space in the appropriate packer
            int pageIndex = entry.TextureIndex;
            if (pageIndex >= 0 && pageIndex < _packers.Count)
            {
                _packers[pageIndex].Free(fullRect);
            }

            // Clear the texture region (optional, helps debugging)
            if (_settings.Readable && pageIndex >= 0 && pageIndex < _textures.Count)
            {
                TextureBlitter.ClearRegion(_textures[pageIndex], entry.Rect);
            }

            // Cleanup
            entry.Dispose();
            _entries.Remove(id);
            
            // Remove from name dictionary if it exists
            var nameToRemove = _entriesByName.FirstOrDefault(kvp => kvp.Value.Id == id).Key;
            if (nameToRemove != null)
            {
                _entriesByName.Remove(nameToRemove);
            }
            
            _recycledIds.Enqueue(id);
            
            _version++;
            _isDirty = true;

#if UNITY_EDITOR
            RuntimeAtlasProfiler.End(profiler);
#endif
            
            return true;
        }
        /// <summary>
        /// Check if an entry exists in the atlas.
        /// </summary>
        public bool ContainsEntry(int id)
        {
            return _entries.ContainsKey(id);
        }

        /// <summary>
        /// Get an entry by ID.
        /// </summary>
        public AtlasEntry GetEntry(int id)
        {
            return _entries.TryGetValue(id, out var entry) ? entry : null;
        }

        /// <summary>
        /// Get all entries in the atlas.
        /// </summary>
        public IEnumerable<AtlasEntry> GetAllEntries()
        {
            return _entries.Values;
        }

        /// <summary>
        /// Get estimated memory usage of cached sprites in bytes.
        /// Rough estimate based on sprite object overhead.
        /// </summary>
        public long GetCachedSpriteMemoryUsage()
        {
            // Rough estimate: ~200 bytes per sprite object (Unity overhead + metadata)
            const long bytesPerSprite = 200;
            return bytesPerSprite;
        }

        /// <summary>
        /// Clear all cached sprites in the atlas.
        /// </summary>
        public void ClearAllSpriteCaches()
        {
            foreach (var entry in _entries.Values)
            {
                entry.ClearSpriteCache();
            }
        }

        /// <summary>
        /// Save this atlas to disk at the specified path.
        /// </summary>
        /// <param name="filePath">Path to save the atlas (without extension)</param>
        /// <returns>True if successful</returns>
        public bool Save(string filePath)
        {
            return AtlasPersistence.SaveAtlas(this, filePath);
        }

        /// <summary>
        /// Save this atlas to disk asynchronously at the specified path.
        /// </summary>
        /// <param name="filePath">Path to save the atlas (without extension)</param>
        /// <returns>Task that completes with true if successful</returns>
        public System.Threading.Tasks.Task<bool> SaveAsync(string filePath)
        {
            return AtlasPersistence.SaveAtlasAsync(this, filePath);
        }

        /// <summary>
        /// Load an atlas from disk.
        /// </summary>
        /// <param name="filePath">Path to the atlas file (without extension)</param>
        /// <returns>The loaded atlas, or null if failed</returns>
        public static RuntimeAtlas Load(string filePath)
        {
            return AtlasPersistence.LoadAtlas(filePath);
        }

        /// <summary>
        /// Load an atlas from disk asynchronously.
        /// </summary>
        /// <param name="filePath">Path to the atlas file (without extension)</param>
        /// <returns>Task that completes with the loaded atlas, or null if failed</returns>
        public static System.Threading.Tasks.Task<RuntimeAtlas> LoadAsync(string filePath)
        {
            return AtlasPersistence.LoadAtlasAsync(filePath);
        }

        /// <summary>
        /// Check if an atlas exists on disk.
        /// Verifies that both the JSON metadata file and at least one page PNG file exist.
        /// </summary>
        /// <param name="filePath">Path to the atlas file (without extension)</param>
        /// <returns>True if the atlas exists on disk, false otherwise</returns>
        public static bool Exists(string filePath)
        {
            return AtlasPersistence.AtlasExists(filePath);
        }

        /// <summary>
        /// Force a full repack of the atlas.
        /// Useful after many removes to reclaim fragmented space.
        /// </summary>
        public void Repack()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(RuntimeAtlas));
            }
            if (_entries.Count == 0)
            {
                return;
            }

#if UNITY_EDITOR
            Debug.Log("[RuntimeAtlas] Starting full repack...");
#endif

            // Repack each page individually
            for (var pageIndex = 0; pageIndex < _textures.Count; pageIndex++)
            {
                RepackPage(pageIndex);
            }
            
            _isDirty = true;
            Apply();
#if UNITY_EDITOR
            Debug.Log("[RuntimeAtlas] Repack complete.");
#endif
        }

        private void RepackPage(int pageIndex)
        {
            if (pageIndex < 0 || pageIndex >= _textures.Count)
            {
                return;
            }

            var texture = _textures[pageIndex];
            var packer = _packers[pageIndex];
            
            // List to hold temp textures. Declared outside verification for try/finally access
            var pageEntries = new List<(AtlasEntry entry, Texture2D texture)>();
            
            try
            {
                // Collect all entries on this page
                foreach (var entry in _entries.Values)
                {
                    if (entry.TextureIndex == pageIndex)
                    {
                        // Copy current texture data
                        // This creates a NEW texture which must be cleaned up
                        var tex = TextureBlitter.CopyRegion(texture, entry.Rect);
                        if (tex != null)
                        {
                            pageEntries.Add((entry, tex));
                        }
                    }
                }

                if (pageEntries.Count == 0)
                {
                    return;
                }

#if UNITY_EDITOR
                Debug.Log($"[RuntimeAtlas] Repacking page {pageIndex} with {pageEntries.Count} entries...");
#endif

                // Clear page
                packer.Clear();
                
                // Use optimized clearing based on readability
                if (_settings.Readable)
                {
                    var clearPixels = new Color32[texture.width * texture.height];
                    texture.SetPixels32(clearPixels);
                    texture.Apply(); // Force clear to ensure no artifacts remain
                }
                else
                {
                    // Optimization for non-readable: Use RenderTexture to clear
                    var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    GL.Clear(true, true, Color.clear);
                    RenderTexture.active = prev;
                    Graphics.CopyTexture(rt, texture);
                    RenderTexture.ReleaseTemporary(rt);
                }

                // Sort entries by area (descending) for better packing
                pageEntries.Sort((a, b) => (b.entry.Width * b.entry.Height).CompareTo(a.entry.Width * a.entry.Height));

                // Re-pack
                foreach (var (entry, tex) in pageEntries)
                {
                    var width = tex.width + _settings.Padding * 2;
                    var height = tex.height + _settings.Padding * 2;

                    if (packer.TryPack(width, height, out var packedRect))
                    {
                        var contentRect = new RectInt(
                            packedRect.x + _settings.Padding,
                            packedRect.y + _settings.Padding,
                            tex.width,
                            tex.height
                        );

                        // Blit back to atlas
                        TextureBlitter.Blit(tex, texture, contentRect.x, contentRect.y);

                        // Update entry - ✅ FIX: Ensure float division for precision
                        var uvRect = new Rect(
                            (float)contentRect.x / (float)texture.width,
                            (float)contentRect.y / (float)texture.height,
                            (float)contentRect.width / (float)texture.width,
                            (float)contentRect.height / (float)texture.height
                        );
                        
                        entry.UpdateRect(contentRect, uvRect);
                        OnEntryUpdated?.Invoke(this, entry);
                    }
                    else
                    {
#if UNITY_EDITOR
                        Debug.LogError($"[RuntimeAtlas] Failed to repack entry {entry.Id} on page {pageIndex}. This should not happen if the page was big enough before.");
#endif
                    }
                }
                
                // Apply changes to the atlas page
                // We preserve the readability setting
                bool makeNoLongerReadable = !_settings.Readable;
                texture.Apply(false, makeNoLongerReadable);
            }
            finally
            {
                // CRITICAL MEMORY FIX: Always destroy temporary textures
                // Even if packer.TryPack crashes or sorting fails
                foreach (var item in pageEntries)
                {
                    if (item.texture != null)
                    {
                        if (Application.isPlaying)
                            UnityEngine.Object.Destroy(item.texture);
                        else
                            UnityEngine.Object.DestroyImmediate(item.texture);
                    }
                }
                pageEntries.Clear();
            }
        }

        private int GetNextId()
        {
            if (_recycledIds.Count > 0)
            {
                return _recycledIds.Dequeue();
            }
            return _nextId++;
        }

        private string GetAtlasName()
        {
            return _textures != null && _textures.Count > 0 && _textures[0] != null ? _textures[0].name : "RuntimeAtlas";
        }

        /// <summary>
        /// Apply pending changes to the texture.
        /// Call this after batching multiple operations.
        /// </summary>
        public void Apply()
        {
            if (!_isDirty)
            {
                return;
            }
            try
            {
                foreach (var texture in _textures)
                {
                    texture.Apply(_settings.GenerateMipMaps, false);
                }
            }
            catch (UnityException ex)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[RuntimeAtlas] Could not apply texture changes: {ex.Message}");
#endif
            }
            _isDirty = false;
        }

        /// <summary>
        /// Public API for deserialization: Add a pre-loaded texture page to the atlas.
        /// Used by AtlasPersistence when loading an atlas from disk with pre-rendered texture pages.
        /// </summary>
        /// <param name="texture">The pre-loaded texture page</param>
        /// <param name="packerState">The serialized packing algorithm state for this page</param>
        public void AddLoadedPage(Texture2D texture, PackingAlgorithmState packerState)
        {
            if (texture == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[RuntimeAtlas.AddLoadedPage] Null texture provided");
#endif
                return;
            }

            if (packerState == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[RuntimeAtlas.AddLoadedPage] Null packer state provided");
#endif
                return;
            }

#if UNITY_EDITOR
            Debug.Log($"[RuntimeAtlas.AddLoadedPage] Adding loaded page {_textures.Count}: {texture.name}, Size: {texture.width}x{texture.height}");
#endif

            // Create packer for this page and restore its state
            IPackingAlgorithm packer = packerState.Algorithm switch
            {
#if PACKING_BURST_ENABLED
                PackingAlgorithm.MaxRects => new MaxRectsAlgorithm(),
                PackingAlgorithm.Skyline => new SkylineAlgorithm(),
#else
                PackingAlgorithm.MaxRects => new MaxRectsAlgorithmNoBurst(),
                PackingAlgorithm.Skyline => new MaxRectsAlgorithmNoBurst(),
#endif
                _ => new MaxRectsAlgorithmNoBurst()
            };

            // Initialize packer with loaded dimensions
            packer.Initialize(packerState.Width, packerState.Height);

            // Restore used rectangles by replaying the packing operations
            // This marks regions as occupied without actually copying texture data
            if (packerState.UsedRects != null)
            {
                foreach (var rectData in packerState.UsedRects)
                {
                    var rect = rectData.ToRectInt();
                    // Replay the packing to mark this region as used
                    if (packer.TryPack(rect.width, rect.height, out var packedRect))
                    {
                        // Successfully marked as used
                        // Note: The returned position might differ, but that's okay
                        // because we're just restoring the filled state
                    }
                    else
                    {
#if UNITY_EDITOR
                        Debug.LogWarning($"[RuntimeAtlas.AddLoadedPage] Could not restore used rect {rect} during deserialization");
#endif
                    }
                }
            }

            _packers.Add(packer);
            _textures.Add(texture);
            _currentPageIndex = _textures.Count - 1;

#if UNITY_EDITOR
            Debug.Log($"[RuntimeAtlas.AddLoadedPage] ✓ Loaded page added. Total pages: {_textures.Count}");
#endif
        }

        /// <summary>
        /// Public API for deserialization: Restore entries from serialized data during atlas loading.
        /// Used by AtlasPersistence to reconstruct the atlas state from saved data.
        /// </summary>
        /// <param name="entryDataList">List of serialized entry data to restore</param>
        public void RestoreEntries(List<AtlasEntryData> entryDataList)
        {
            if (entryDataList == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[RuntimeAtlas.RestoreEntries] Null entry data list provided");
#endif
                return;
            }

#if UNITY_EDITOR
            Debug.Log($"[RuntimeAtlas.RestoreEntries] Restoring {entryDataList.Count} entries...");
#endif

            // Clear existing entries
            _entries.Clear();
            _entriesByName.Clear();

            // Reconstruct each entry
            foreach (var entryData in entryDataList)
            {
                try
                {
                    // Create AtlasEntry from serialized data
                    var entry = new AtlasEntry(
                        this,
                        entryData.Id,
                        entryData.TextureIndex,
                        entryData.PixelRect.ToRectInt(),
                        entryData.UVRect.ToRect(),
                        entryData.Name,
                        entryData.Border.ToVector4(),
                        entryData.Pivot.ToVector2(),
                        entryData.PixelsPerUnit,
                        entryData.SpriteVersion
                    );

                    // Add to internal dictionaries
                    _entries[entryData.Id] = entry;

                    if (!string.IsNullOrEmpty(entryData.Name))
                    {
                        _entriesByName[entryData.Name] = entry;
                    }

                    // Update _nextId to avoid ID conflicts
                    if (entryData.Id >= _nextId)
                    {
                        _nextId = entryData.Id + 1;
                    }

#if UNITY_EDITOR
                    Debug.Log($"[RuntimeAtlas.RestoreEntries] Restored entry {entryData.Id}: '{entryData.Name}' on page {entryData.TextureIndex}");
#endif
                }
                catch (Exception ex)
                {
#if UNITY_EDITOR
                    Debug.LogError($"[RuntimeAtlas.RestoreEntries] Failed to restore entry {entryData.Id}: {ex.Message}");
#endif
                }
            }

#if UNITY_EDITOR
            Debug.Log($"[RuntimeAtlas.RestoreEntries] Complete. Total entries restored: {_entries.Count}");
#endif
        }

        /// <summary>
        /// Public API for testing: Replace a texture page at the specified index.
        /// WARNING: This is an advanced operation intended for testing scenarios.
        /// Use with caution as it can corrupt the atlas if used incorrectly.
        /// </summary>
        /// <param name="pageIndex">The page index to replace</param>
        /// <param name="texture">The new texture to use for this page</param>
        /// <returns>True if successful, false if invalid parameters</returns>
        public bool ReplaceTexturePage(int pageIndex, Texture2D texture)
        {
            if (pageIndex < 0 || pageIndex >= _textures.Count)
            {
#if UNITY_EDITOR
                Debug.LogError($"[RuntimeAtlas.ReplaceTexturePage] Invalid page index: {pageIndex} (valid range: 0-{_textures.Count - 1})");
#endif
                return false;
            }

            if (texture == null)
            {
#if UNITY_EDITOR
                Debug.LogError("[RuntimeAtlas.ReplaceTexturePage] Cannot replace with null texture");
#endif
                return false;
            }

#if UNITY_EDITOR
            Debug.LogWarning($"[RuntimeAtlas.ReplaceTexturePage] Replacing texture page {pageIndex}. This is an advanced operation for testing.");
#endif

            // Destroy old texture if it exists
            var oldTexture = _textures[pageIndex];
            if (oldTexture != null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(oldTexture);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(oldTexture);
                }
            }

            // Replace with new texture
            _textures[pageIndex] = texture;
            _version++;
            _isDirty = true;

#if UNITY_EDITOR
            Debug.Log($"[RuntimeAtlas.ReplaceTexturePage] Successfully replaced page {pageIndex}");
#endif

            return true;
        }

        public void Dispose()
        {
            // ✅ FIX: Check if already disposed
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            
            // Remove from global registry
            _allAtlases.Remove(this);

            try
            {
                // Dispose entries
                if (_entries != null)
                {
                    foreach (var entry in _entries.Values)
                    {
                        try
                        {
                            entry?.Dispose();
                        }
                        catch (Exception ex)
                        {
#if UNITY_EDITOR
                            Debug.LogWarning($"[RuntimeAtlas] Error disposing entry: {ex.Message}");
#endif
                        }
                    }
                    _entries.Clear();
                }

                _entriesByName?.Clear();

                // Dispose all packers
                if (_packers != null)
                {
                    foreach (var packer in _packers)
                    {
                        try
                        {
                            packer?.Dispose();
                        }
                        catch (Exception ex)
                        {
#if UNITY_EDITOR
                            Debug.LogWarning($"[RuntimeAtlas] Error disposing packer: {ex.Message}");
#endif
                        }
                    }
                    _packers.Clear();
                }

                // Dispose all textures
                if (_textures != null)
                {
                    foreach (var texture in _textures)
                    {
                        if (texture != null)
                        {
                            try
                            {
                                if (Application.isPlaying)
                                {
                                    UnityEngine.Object.Destroy(texture);
                                }
                                else
                                {
                                    UnityEngine.Object.DestroyImmediate(texture);
                                }
                            }
                            catch (Exception ex)
                            {
#if UNITY_EDITOR
                                Debug.LogWarning($"[RuntimeAtlas] Error destroying texture: {ex.Message}");
#endif
                            }
                        }
                    }
                    _textures.Clear();
                }

                OnAtlasResized = null;
                OnEntryUpdated = null;
            }
            catch (Exception ex)
            {
#if UNITY_EDITOR
                Debug.LogError($"[RuntimeAtlas] Critical error during dispose: {ex.Message}");
#endif
            }
        }
    }
}
