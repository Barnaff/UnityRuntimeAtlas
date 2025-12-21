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
        // Multi-page texture support
        private List<Texture2D> _textures;
        private List<IPackingAlgorithm> _packers;
        private int _currentPageIndex;
        
        private readonly AtlasSettings _settings;
        private readonly Dictionary<int, AtlasEntry> _entries;
        private readonly Queue<int> _recycledIds;
        private int _nextId;
        private bool _isDisposed;
        private bool _isDirty;
        private int _version;

        /// <summary>The primary atlas texture (page 0).</summary>
        public Texture2D Texture => _textures != null && _textures.Count > 0 ? _textures[0] : null;

        /// <summary>Get a specific texture page by index.</summary>
        public Texture2D GetTexture(int pageIndex)
        {
            if (_textures == null || pageIndex < 0 || pageIndex >= _textures.Count)
                return null;
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
                if (_packers == null || _packers.Count == 0) return 0f;
                float sum = 0f;
                foreach (var packer in _packers)
                {
                    sum += packer?.GetFillRatio() ?? 0f;
                }
                return sum / _packers.Count;
            }
        }

        /// <summary>Settings used for this atlas.</summary>
        public AtlasSettings Settings => _settings;

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
            
            _settings = settings;
            _entries = new Dictionary<int, AtlasEntry>(64);
            _recycledIds = new Queue<int>(16);
            _textures = new List<Texture2D>();
            _packers = new List<IPackingAlgorithm>();
            _currentPageIndex = 0;
            
            // Create first page
            CreateNewPage();
        }

        private void CreateNewPage()
        {
            int pageIndex = _textures.Count;
            
            // Create packer for this page
            IPackingAlgorithm packer = _settings.Algorithm switch
            {
                PackingAlgorithm.MaxRects => new MaxRectsAlgorithm(),
                PackingAlgorithm.Skyline => new SkylineAlgorithm(),
                _ => new MaxRectsAlgorithm()
            };
            
            // Initialize packer
            packer.Initialize(_settings.InitialSize, _settings.InitialSize);
            _packers.Add(packer);
            
            // Create texture
            var texture = new Texture2D(_settings.InitialSize, _settings.InitialSize, _settings.Format, _settings.GenerateMipMaps);
            texture.filterMode = _settings.FilterMode;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.name = $"RuntimeAtlas_Page{pageIndex}";
            
            // Clear to transparent
            var clearPixels = new Color32[_settings.InitialSize * _settings.InitialSize];
            texture.SetPixels32(clearPixels);
            texture.Apply(false, false);
            
            _textures.Add(texture);
            _currentPageIndex = pageIndex;
            
            Debug.Log($"[RuntimeAtlas] Created new page {pageIndex}: {_settings.InitialSize}x{_settings.InitialSize}");
        }

        /// <summary>
        /// Add a texture to the atlas.
        /// </summary>
        /// <returns>A tuple containing the result status and an AtlasEntry reference (null if not successful).</returns>
        public (AddResult result, AtlasEntry entry) Add(Texture2D texture)
        {
            if (_isDisposed)
            {
                Debug.LogWarning("[RuntimeAtlas.Add] Atlas is disposed");
                return (AddResult.Failed, null);
            }
            
            if (texture == null)
            {
                Debug.LogWarning("[RuntimeAtlas.Add] Null texture provided");
                return (AddResult.InvalidTexture, null);
            }

            var profiler = RuntimeAtlasProfiler.Begin("Add", GetAtlasName(), $"{texture.name} ({texture.width}x{texture.height})");

            Debug.Log($"[RuntimeAtlas] Add: Packing '{texture.name}': {texture.width}x{texture.height}");

            // Use internal method for packing
            var (result, entry) = AddInternal(texture);
            
            if (result != AddResult.Success)
            {
                Debug.LogWarning($"[RuntimeAtlas] Add: Failed with result: {result}");
                RuntimeAtlasProfiler.End(profiler);
                return (result, null);
            }
            
            // Apply texture changes immediately for single adds to the page that was modified
            if (entry != null)
            {
                try
                {
                    var pageTexture = _textures[entry.TextureIndex];
                    pageTexture.Apply(false, false);
                    _isDirty = false;
                }
                catch (UnityException ex)
                {
                    Debug.LogWarning($"[RuntimeAtlas] Could not apply texture changes: {ex.Message}");
                }
            }

            // Validate no overlaps
            ValidateNoOverlaps();
            
            Debug.Log($"[RuntimeAtlas] Add: Complete. Entry ID: {entry.Id}, Page: {entry.TextureIndex}, Total entries: {_entries.Count}");

            RuntimeAtlasProfiler.End(profiler);
            return (AddResult.Success, entry);
        }
        
        /// <summary>
        /// Validate that no entries overlap with each other.
        /// </summary>
        private void ValidateNoOverlaps()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var entries = _entries.Values.ToArray();
            for (int i = 0; i < entries.Length; i++)
            {
                for (int j = i + 1; j < entries.Length; j++)
                {
                    var rect1 = entries[i].PixelRect;
                    var rect2 = entries[j].PixelRect;
                    
                    // Check if rects overlap
                    if (!(rect1.xMax <= rect2.xMin || rect2.xMax <= rect1.xMin ||
                          rect1.yMax <= rect2.yMin || rect2.yMax <= rect1.yMin))
                    {
                        Debug.LogError($"[RuntimeAtlas] OVERLAP DETECTED! '{entries[i].Name}' [{rect1}] overlaps with '{entries[j].Name}' [{rect2}]");
                    }
                }
            }
#endif
        }

        /// <summary>
        /// Add multiple textures to the atlas in a single batch.
        /// More efficient than adding one at a time.
        /// Returns only successfully added entries (skips failures).
        /// </summary>
        public AtlasEntry[] AddBatch(Texture2D[] textures)
        {
            if (_isDisposed)
            {
                Debug.LogWarning("[RuntimeAtlas.AddBatch] Atlas is disposed");
                return Array.Empty<AtlasEntry>();
            }
            
            if (textures == null || textures.Length == 0)
            {
                Debug.LogWarning("[RuntimeAtlas.AddBatch] No textures provided");
                return Array.Empty<AtlasEntry>();
            }

            var profiler = RuntimeAtlasProfiler.Begin("AddBatch", GetAtlasName(), $"{textures.Length} textures");

            Debug.Log($"[RuntimeAtlas] AddBatch: Starting batch of {textures.Length} textures");
            
            var successfulEntries = new List<AtlasEntry>();

            // Sort by area descending for better packing
            var sorted = new (int index, Texture2D tex, int area)[textures.Length];
            for (int i = 0; i < textures.Length; i++)
            {
                sorted[i] = (i, textures[i], textures[i].width * textures[i].height);
            }
            Array.Sort(sorted, (a, b) => b.area.CompareTo(a.area));

            // Pack all textures WITHOUT applying after each one
            int successCount = 0;
            int failCount = 0;
            foreach (var (index, tex, _) in sorted)
            {
                var (result, entry) = AddInternal(tex);
                if (result == AddResult.Success && entry != null)
                {
                    successfulEntries.Add(entry);
                    successCount++;
                    Debug.Log($"[RuntimeAtlas] Batch [{index}]: Added '{tex.name}' -> Entry ID {entry.Id}");
                }
                else
                {
                    failCount++;
                    Debug.LogWarning($"[RuntimeAtlas] Batch [{index}]: Failed to add '{tex.name}' -> Result: {result}");
                    
                    // If atlas is full, stop trying to add more
                    if (result == AddResult.Full)
                    {
                        Debug.LogWarning($"[RuntimeAtlas] AddBatch: Atlas full after {successCount} textures. Stopping batch.");
                        break;
                    }
                }
            }

            // Apply once at the end for efficiency (apply all modified pages)
            if (successCount > 0)
            {
                try
                {
                    Debug.Log($"[RuntimeAtlas] AddBatch: Applying texture changes for {successCount} successful textures across {_textures.Count} pages");
                    foreach (var texture in _textures)
                    {
                        texture.Apply(false, false);
                    }
                    _isDirty = false;
                }
                catch (UnityException ex)
                {
                    Debug.LogError($"[RuntimeAtlas] Failed to apply batch changes: {ex.Message}");
                }

                // Validate at the end
                ValidateNoOverlaps();
            }
            
            Debug.Log($"[RuntimeAtlas] AddBatch: Complete. Added: {successCount}, Failed: {failCount}, Total entries in atlas: {_entries.Count}");

            RuntimeAtlasProfiler.End(profiler);
            return successfulEntries.ToArray();
        }
        
        /// <summary>
        /// Internal add method that doesn't call Apply - for batch operations
        /// Automatically creates new page if current page is full.
        /// </summary>
        private (AddResult result, AtlasEntry entry) AddInternal(Texture2D texture)
        {
            if (texture == null)
            {
                Debug.LogWarning("[RuntimeAtlas.AddInternal] NULL TEXTURE passed!");
                return (AddResult.InvalidTexture, null);
            }

            Debug.Log($"[RuntimeAtlas.AddInternal] ===== Starting pack for '{texture.name}' =====");
            Debug.Log($"[RuntimeAtlas.AddInternal] Texture size: {texture.width}x{texture.height}");
            Debug.Log($"[RuntimeAtlas.AddInternal] Padding: {_settings.Padding}");

            int width = texture.width + _settings.Padding * 2;
            int height = texture.height + _settings.Padding * 2;

            // Check if texture is too large to ever fit
            if (texture.width > _settings.MaxSize || texture.height > _settings.MaxSize)
            {
                Debug.LogWarning($"[RuntimeAtlas.AddInternal] Texture '{texture.name}' ({texture.width}x{texture.height}) exceeds MaxSize ({_settings.MaxSize})");
                return (AddResult.TooLarge, null);
            }

            Debug.Log($"[RuntimeAtlas.AddInternal] With padding: {width}x{height}");
            Debug.Log($"[RuntimeAtlas.AddInternal] Current page: {_currentPageIndex}, Pages: {_textures.Count}");
            Debug.Log($"[RuntimeAtlas.AddInternal] Current entries: {_entries.Count}");

            // Try to pack in current page
            int pageIndex = _currentPageIndex;
            bool packed = TryPackInPage(pageIndex, width, height, out var packedRect);
            
            if (!packed)
            {
                Debug.Log($"[RuntimeAtlas.AddInternal] Current page {pageIndex} is full. Creating new page...");
                
                // Current page is full, create new page
                CreateNewPage();
                pageIndex = _currentPageIndex;
                
                // Try packing in new page
                packed = TryPackInPage(pageIndex, width, height, out packedRect);
                
                if (!packed)
                {
                    Debug.LogWarning($"[RuntimeAtlas.AddInternal] Failed to pack even in new page!");
                    return (AddResult.Full, null);
                }
            }

            var currentTexture = _textures[pageIndex];
            Debug.Log($"[RuntimeAtlas.AddInternal] Packed successfully in page {pageIndex} at: x={packedRect.x}, y={packedRect.y}, w={packedRect.width}, h={packedRect.height}");

            // Adjust for padding - contentRect is the actual visible area
            var contentRect = new RectInt(
                packedRect.x + _settings.Padding,
                packedRect.y + _settings.Padding,
                texture.width,
                texture.height
            );

            Debug.Log($"[RuntimeAtlas.AddInternal] Content rect (removing padding): {contentRect}");

            // Blit texture to atlas page
            try
            {
                TextureBlitter.Blit(texture, currentTexture, contentRect.x, contentRect.y);
                Debug.Log($"[RuntimeAtlas.AddInternal] Blit successful to page {pageIndex}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RuntimeAtlas.AddInternal] BLIT FAILED for '{texture.name}': {ex.Message}");
                return (AddResult.Failed, null);
            }
            
            // Calculate UV
            var uvRect = new Rect(
                (float)contentRect.x / currentTexture.width,
                (float)contentRect.y / currentTexture.height,
                (float)contentRect.width / currentTexture.width,
                (float)contentRect.height / currentTexture.height
            );

            Debug.Log($"[RuntimeAtlas.AddInternal] UV calculated: ({uvRect.x:F4}, {uvRect.y:F4}, {uvRect.width:F4}, {uvRect.height:F4})");

            // Create entry with texture index
            int id = GetNextId();
            var entry = new AtlasEntry(this, id, pageIndex, contentRect, uvRect, texture.name);
            _entries[id] = entry;
            
            Debug.Log($"[RuntimeAtlas.AddInternal] ===== COMPLETE: Entry ID {id} created for '{texture.name}' on page {pageIndex}. Total entries: {_entries.Count} =====");
            
            _version++;
            _isDirty = true;

            return (AddResult.Success, entry);
        }

        private bool TryPackInPage(int pageIndex, int width, int height, out RectInt packedRect)
        {
            packedRect = default;
            if (pageIndex < 0 || pageIndex >= _packers.Count)
                return false;

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
                return false;
            }

            int currentSize = texture.width;
            while (currentSize < _settings.MaxSize)
            {
                int newSize = _settings.GrowthStrategy == GrowthStrategy.Double 
                    ? currentSize * 2 
                    : currentSize + currentSize / 2;
                    
                newSize = Mathf.Min(newSize, _settings.MaxSize);
                
                if (newSize == currentSize)
                    break;

                Debug.Log($"[RuntimeAtlas] Attempting to grow page {pageIndex} from {currentSize} to {newSize}");
                
                if (TryGrowPage(pageIndex, newSize))
                {
                    if (packer.TryPack(width, height, out packedRect))
                    {
                        return true;
                    }
                }
                
                currentSize = newSize;
            }

            return false;
        }

        private bool TryGrowPage(int pageIndex, int newSize)
        {
            if (pageIndex < 0 || pageIndex >= _textures.Count)
                return false;

            var oldTexture = _textures[pageIndex];
            int oldSize = oldTexture.width;
            
            if (newSize <= oldSize || newSize > _settings.MaxSize)
            {
                Debug.LogError($"[TryGrowPage] Invalid new size {newSize} (old: {oldSize}, max: {_settings.MaxSize})");
                return false;
            }

            Debug.Log($"[TryGrowPage] Growing page {pageIndex} from {oldSize}x{oldSize} to {newSize}x{newSize}");

            // Create new larger texture
            var newTexture = new Texture2D(newSize, newSize, _settings.Format, _settings.GenerateMipMaps);
            newTexture.filterMode = _settings.FilterMode;
            newTexture.wrapMode = TextureWrapMode.Clamp;
            newTexture.name = $"RuntimeAtlas_Page{pageIndex}";

            // Clear to transparent
            var clearPixels = new Color32[newSize * newSize];
            newTexture.SetPixels32(clearPixels);
            newTexture.Apply(false, false);

            // Copy old texture data
            Graphics.CopyTexture(oldTexture, 0, 0, 0, 0, oldSize, oldSize, newTexture, 0, 0, 0, 0);

            // Update packer
            _packers[pageIndex].Resize(newSize, newSize);

            // Replace texture
            UnityEngine.Object.Destroy(oldTexture);
            _textures[pageIndex] = newTexture;

            // Update UVs for all entries on this page
            foreach (var entry in _entries.Values)
            {
                if (entry.TextureIndex == pageIndex)
                {
                    var uvRect = new Rect(
                        (float)entry.PixelRect.x / newSize,
                        (float)entry.PixelRect.y / newSize,
                        (float)entry.PixelRect.width / newSize,
                        (float)entry.PixelRect.height / newSize
                    );
                    entry.UpdateRect(entry.PixelRect, uvRect);
                    OnEntryUpdated?.Invoke(this, entry);
                }
            }

            Debug.Log($"[TryGrowPage] Page {pageIndex} grown successfully to {newSize}x{newSize}");
            OnAtlasResized?.Invoke(this);

            return true;
        }

        /// <summary>
        /// Add multiple textures asynchronously.

        /// <summary>
        /// Remove an entry from the atlas.
        /// </summary>
        public bool Remove(AtlasEntry entry)
        {
            if (_isDisposed) return false;
            if (entry == null || entry.Atlas != this) return false;

            return RemoveById(entry.Id);
        }

        /// <summary>
        /// Remove an entry by ID.
        /// </summary>
        public bool RemoveById(int id)
        {
            if (!_entries.TryGetValue(id, out var entry))
                return false;

            var profiler = RuntimeAtlasProfiler.Begin("Remove", GetAtlasName(), $"Entry ID: {id}");

            var fullRect = new RectInt(
                entry.PixelRect.x - _settings.Padding,
                entry.PixelRect.y - _settings.Padding,
                entry.PixelRect.width + _settings.Padding * 2,
                entry.PixelRect.height + _settings.Padding * 2
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
                TextureBlitter.ClearRegion(_textures[pageIndex], entry.PixelRect);
            }

            // Cleanup
            entry.Dispose();
            _entries.Remove(id);
            _recycledIds.Enqueue(id);
            
            _version++;
            _isDirty = true;

            RuntimeAtlasProfiler.End(profiler);
            
            return true;
        }
        /// <summary>
        /// Check if an entry exists in the atlas.
        /// </summary>
        public bool ContainsEntry(int id) => _entries.ContainsKey(id);

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
        public IEnumerable<AtlasEntry> GetAllEntries() => _entries.Values;

        /// <summary>
        /// Force a full repack of the atlas.
        /// Useful after many removes to reclaim fragmented space.
        /// Note: Currently not supported for multi-page atlases.
        /// </summary>
        public void Repack()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(RuntimeAtlas));
            if (_entries.Count == 0) return;

            Debug.LogWarning("[RuntimeAtlas] Repack is not yet implemented for multi-page atlases. This operation is skipped.");
            // TODO: Implement per-page repacking for multi-page atlases
        }

        private int GetNextId()
        {
            if (_recycledIds.Count > 0)
                return _recycledIds.Dequeue();
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
            if (!_isDirty) return;
            try
            {
                foreach (var texture in _textures)
                {
                    texture.Apply(_settings.GenerateMipMaps, false);
                }
            }
            catch (UnityException ex)
            {
                Debug.LogWarning($"[RuntimeAtlas] Could not apply texture changes: {ex.Message}");
            }
            _isDirty = false;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            foreach (var entry in _entries.Values)
            {
                entry.Dispose();
            }
            _entries.Clear();

            // Dispose all packers
            if (_packers != null)
            {
                foreach (var packer in _packers)
                {
                    packer?.Dispose();
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
                        if (Application.isPlaying)
                            UnityEngine.Object.Destroy(texture);
                        else
                            UnityEngine.Object.DestroyImmediate(texture);
                    }
                }
                _textures.Clear();
            }

            OnAtlasResized = null;
            OnEntryUpdated = null;
        }
    }
}
