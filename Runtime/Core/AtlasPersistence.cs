using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// Handles saving and loading of runtime atlases to/from disk.
    /// Optimized for fast serialization and deserialization with thread-safe operations.
    /// </summary>
    public static class AtlasPersistence
    {
        // Thread-safe locks per file path to prevent corruption
        private static readonly Dictionary<string, object> _saveLocks = new Dictionary<string, object>();
        private static readonly object _locksLock = new object();

        /// <summary>
        /// Get or create a lock object for a specific file path.
        /// </summary>
        private static object GetLockForPath(string filePath)
        {
            lock (_locksLock)
            {
                if (!_saveLocks.TryGetValue(filePath, out var lockObj))
                {
                    lockObj = new object();
                    _saveLocks[filePath] = lockObj;
                }
                return lockObj;
            }
        }

        /// <summary>
        /// Save a runtime atlas to disk (thread-safe).
        /// Multiple save requests to the same path will be queued and executed in order.
        /// </summary>
        /// <param name="atlas">The atlas to save</param>
        /// <param name="filePath">Path to save the atlas data (without extension)</param>
        /// <returns>True if successful</returns>
        public static bool SaveAtlas(RuntimeAtlas atlas, string filePath)
        {
            if (atlas == null)
            {
                Debug.LogError("[AtlasPersistence] Cannot save null atlas");
                return false;
            }

            // Get file-specific lock to prevent concurrent writes to same file
            var lockObj = GetLockForPath(filePath);

            lock (lockObj)
            {
                try
                {
#if UNITY_EDITOR
                    var profiler = RuntimeAtlasProfiler.Begin("SaveAtlas", "AtlasPersistence", filePath);
#endif

                    // Serialize atlas data (without texture data)
                    var data = SerializeAtlas(atlas);

                    // Save texture pages as PNG files first
                    // ✅ CRITICAL FIX: Track original readable state and temporarily make textures readable for EncodeToPNG
                    var originalReadableStates = new bool[atlas.PageCount];
                    
                    for (var i = 0; i < atlas.PageCount; i++)
                    {
                        var texture = atlas.GetTexture(i);
                        if (texture != null)
                        {
                            originalReadableStates[i] = texture.isReadable;
                            
                            // If texture is not readable, we need to read it from GPU
                            if (!texture.isReadable)
                            {
#if UNITY_EDITOR
                                Debug.LogWarning($"[AtlasPersistence] Texture page {i} is not readable, reading from GPU for save operation");
#endif
                                // Create a temporary readable copy from GPU
                                var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
                                Graphics.Blit(texture, rt);
                                
                                var previous = RenderTexture.active;
                                RenderTexture.active = rt;
                                
                                var readableTexture = new Texture2D(texture.width, texture.height, texture.format, false);
                                readableTexture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                                readableTexture.Apply();
                                
                                RenderTexture.active = previous;
                                RenderTexture.ReleaseTemporary(rt);
                                
                                // Encode and save
                                var texturePath = $"{filePath}_page{i}.png";
                                var pngData = readableTexture.EncodeToPNG();
                                
#if UNITY_EDITOR
                                Debug.Log($"[AtlasPersistence] Page {i} PNG encoded: {pngData?.Length ?? 0} bytes (from non-readable texture)");
#endif
                                
                                if (pngData == null || pngData.Length == 0)
                                {
                                    Debug.LogError($"[AtlasPersistence] PNG encoding FAILED for page {i} - no data produced!");
                                    continue;
                                }
                                
                                File.WriteAllBytes(texturePath, pngData);
                                
#if UNITY_EDITOR
                                Debug.Log($"[AtlasPersistence] Page {i} saved to: {texturePath}");
#endif
                                
                                // Clean up temporary texture
                                UnityEngine.Object.Destroy(readableTexture);
                            }
                            else
                            {
                                // Texture is already readable, encode directly
                                var texturePath = $"{filePath}_page{i}.png";
                                var pngData = texture.EncodeToPNG();
                                
#if UNITY_EDITOR
                                Debug.Log($"[AtlasPersistence] Page {i} PNG encoded: {pngData?.Length ?? 0} bytes (from readable texture)");
#endif
                                
                                if (pngData == null || pngData.Length == 0)
                                {
                                    Debug.LogError($"[AtlasPersistence] PNG encoding FAILED for page {i} - no data produced!");
                                    continue;
                                }
                                
                                File.WriteAllBytes(texturePath, pngData);
                                
#if UNITY_EDITOR
                                Debug.Log($"[AtlasPersistence] Page {i} saved to: {texturePath}");
#endif
                            }
                        }
                    }

                    // Save metadata as JSON (compact format without whitespace for smaller file size)
                    var jsonPath = $"{filePath}.json";
                    var json = JsonUtility.ToJson(data, false);
                    File.WriteAllText(jsonPath, json);

#if UNITY_EDITOR
                    RuntimeAtlasProfiler.End(profiler);
                    Debug.Log($"[AtlasPersistence] Successfully saved atlas to {jsonPath} with {data.Pages.Count} page(s)");
#endif
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AtlasPersistence] Failed to save atlas: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Save a runtime atlas to disk asynchronously (thread-safe).
        /// Multiple save requests to the same path will be queued and executed in order.
        /// </summary>
        /// <param name="atlas">The atlas to save</param>
        /// <param name="filePath">Path to save the atlas data (without extension)</param>
        /// <returns>Task that completes with true if successful</returns>
        public static async Task<bool> SaveAtlasAsync(RuntimeAtlas atlas, string filePath)
        {
            if (atlas == null)
            {
                Debug.LogError("[AtlasPersistence] Cannot save null atlas");
                return false;
            }

            try
            {
#if UNITY_EDITOR
                var profiler = RuntimeAtlasProfiler.Begin("SaveAtlasAsync", "AtlasPersistence", filePath);
#endif

                // Serialize atlas data (without texture data) - on main thread
                var data = SerializeAtlas(atlas);

                // Encode PNGs on main thread (Unity requirement)
                // ✅ CRITICAL FIX: Handle non-readable textures by reading from GPU
                var pngDataList = new List<(int index, byte[] data)>();
                for (var i = 0; i < atlas.PageCount; i++)
                {
                    var texture = atlas.GetTexture(i);
                    if (texture != null)
                    {
                        byte[] pngData;
                        
                        if (!texture.isReadable)
                        {
#if UNITY_EDITOR
                            Debug.LogWarning($"[AtlasPersistence] Texture page {i} is not readable, reading from GPU for async save");
#endif
                            // Create a temporary readable copy from GPU
                            var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
                            Graphics.Blit(texture, rt);
                            
                            var previous = RenderTexture.active;
                            RenderTexture.active = rt;
                            
                            var readableTexture = new Texture2D(texture.width, texture.height, texture.format, false);
                            readableTexture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                            readableTexture.Apply();
                            
                            RenderTexture.active = previous;
                            RenderTexture.ReleaseTemporary(rt);
                            
                            // Encode to PNG
                            pngData = readableTexture.EncodeToPNG();
                            
                            // Clean up temporary texture
                            UnityEngine.Object.Destroy(readableTexture);
                        }
                        else
                        {
                            // Texture is readable, encode directly
                            pngData = texture.EncodeToPNG();
                        }
                        
                        pngDataList.Add((i, pngData));
                    }
                }

                // Serialize JSON on main thread (compact format without whitespace)
                var json = JsonUtility.ToJson(data, false);

                // Get file-specific lock and write files on background thread
                var lockObj = GetLockForPath(filePath);
                
                await Task.Run(() =>
                {
                    lock (lockObj)
                    {
                        // Write PNG files
                        foreach (var (index, pngData) in pngDataList)
                        {
                            var texturePath = $"{filePath}_page{index}.png";
                            File.WriteAllBytes(texturePath, pngData);
                        }

                        // Write JSON file
                        var jsonPath = $"{filePath}.json";
                        File.WriteAllText(jsonPath, json);
                    }
                });

#if UNITY_EDITOR
                RuntimeAtlasProfiler.End(profiler);
                Debug.Log($"[AtlasPersistence] Successfully saved atlas to {filePath}.json with {data.Pages.Count} page(s)");
#endif
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AtlasPersistence] Failed to save atlas: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Load a runtime atlas from disk.
        /// </summary>
        /// <param name="filePath">Path to the atlas data file (without extension)</param>
        /// <returns>The loaded atlas, or null if failed</returns>
        public static RuntimeAtlas LoadAtlas(string filePath)
        {
            try
            {
#if UNITY_EDITOR
                var profiler = RuntimeAtlasProfiler.Begin("LoadAtlas", "AtlasPersistence", filePath);
#endif

                // Load JSON data
                var jsonPath = $"{filePath}.json";
                if (!File.Exists(jsonPath))
                {
                    Debug.LogError($"[AtlasPersistence] Atlas file not found: {jsonPath}");
                    return null;
                }

                var json = File.ReadAllText(jsonPath);
                var data = JsonUtility.FromJson<AtlasSerializationData>(json);

                if (data == null)
                {
                    Debug.LogError($"[AtlasPersistence] Failed to deserialize atlas data");
                    return null;
                }

                // Verify all texture page files exist before loading
                for (var i = 0; i < data.Pages.Count; i++)
                {
                    var texturePath = $"{filePath}_page{i}.png";
                    if (!File.Exists(texturePath))
                    {
                        Debug.LogError($"[AtlasPersistence] Texture page file not found: {texturePath}");
                        return null;
                    }
                }

                // Deserialize into RuntimeAtlas
                var atlas = DeserializeAtlas(data, filePath);

                // Set source file path for debugging
                if (atlas != null)
                {
                    atlas.SourceFilePath = System.IO.Path.GetFullPath(filePath);
                }

#if UNITY_EDITOR
                RuntimeAtlasProfiler.End(profiler);
                Debug.Log($"[AtlasPersistence] Successfully loaded atlas from {jsonPath}");
#endif
                return atlas;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AtlasPersistence] Failed to load atlas: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Load a runtime atlas from disk asynchronously.
        /// </summary>
        /// <param name="filePath">Path to the atlas data file (without extension)</param>
        /// <returns>Task that completes with the loaded atlas, or null if failed</returns>
        public static async Task<RuntimeAtlas> LoadAtlasAsync(string filePath)
        {
            try
            {
#if UNITY_EDITOR
                var profiler = RuntimeAtlasProfiler.Begin("LoadAtlasAsync", "AtlasPersistence", filePath);
#endif

                // Load files asynchronously (background thread)
                var (json, pngDataList) = await Task.Run(() =>
                {
                    // Load JSON
                    var jsonPath = $"{filePath}.json";
                    if (!File.Exists(jsonPath))
                    {
                        return (null, null);
                    }

                    var jsonContent = File.ReadAllText(jsonPath);
                    
                    // Parse JSON to get page count
                    var tempData = JsonUtility.FromJson<AtlasSerializationData>(jsonContent);
                    if (tempData == null)
                    {
                        return (null, null);
                    }

                    // Load all PNG files
                    var pngList = new List<(int index, byte[] data)>();
                    for (var i = 0; i < tempData.Pages.Count; i++)
                    {
                        var texturePath = $"{filePath}_page{i}.png";
                        if (!File.Exists(texturePath))
                        {
                            return (null, null);
                        }

                        var pngData = File.ReadAllBytes(texturePath);
                        pngList.Add((i, pngData));
                    }

                    return (jsonContent, pngList);
                });

                // Check if loading failed
                if (json == null || pngDataList == null)
                {
                    Debug.LogError($"[AtlasPersistence] Failed to load atlas files from: {filePath}");
                    return null;
                }

                // Parse JSON on main thread
                var data = JsonUtility.FromJson<AtlasSerializationData>(json);
                if (data == null)
                {
                    Debug.LogError($"[AtlasPersistence] Failed to deserialize atlas data");
                    return null;
                }

                // Deserialize into RuntimeAtlas (on main thread) using pre-loaded PNG data
                var atlas = DeserializeAtlasFromData(data, pngDataList);

                // Set source file path for debugging
                if (atlas != null)
                {
                    atlas.SourceFilePath = System.IO.Path.GetFullPath(filePath);
                }

#if UNITY_EDITOR
                RuntimeAtlasProfiler.End(profiler);
                Debug.Log($"[AtlasPersistence] Successfully loaded atlas asynchronously from {filePath}.json");
#endif
                return atlas;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AtlasPersistence] Failed to load atlas: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Check if an atlas exists on disk.
        /// Verifies that both the JSON metadata file and at least one page PNG file exist.
        /// </summary>
        /// <param name="filePath">Path to the atlas file (without extension)</param>
        /// <returns>True if the atlas exists on disk, false otherwise</returns>
        public static bool AtlasExists(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return false;
            }

            try
            {
                // Check if JSON file exists
                var jsonPath = $"{filePath}.json";
                if (!File.Exists(jsonPath))
                {
                    return false;
                }

                // Check if at least one page PNG exists
                var page0Path = $"{filePath}_page0.png";
                if (!File.Exists(page0Path))
                {
                    return false;
                }

                return true;
            }
            catch (Exception)
            {
                // If any file system error occurs, consider atlas as non-existent
                return false;
            }
        }

        /// <summary>
        /// Serialize a runtime atlas to data structure.
        /// </summary>
        private static AtlasSerializationData SerializeAtlas(RuntimeAtlas atlas)
        {
            var data = new AtlasSerializationData
            {
                Settings = AtlasSettingsData.FromSettings(atlas.Settings),
                Pages = new List<AtlasPageData>(),
                Entries = new List<AtlasEntryData>(),
                NameToIdMap = new Dictionary<string, int>(),
                NextId = atlas.EntryCount, // Use entry count as next ID base
                Version = atlas.Version
            };

            // Serialize pages (without texture data - saved as separate PNG files)
            for (var i = 0; i < atlas.PageCount; i++)
            {
                var texture = atlas.GetTexture(i);
                if (texture == null)
                {
                    continue;
                }

                var pageData = new AtlasPageData
                {
                    PageIndex = i,
                    Width = texture.width,
                    Height = texture.height,
                    // TextureData removed - PNG saved separately for efficiency
                    PackerState = new PackingAlgorithmState
                    {
                        Algorithm = atlas.Settings.Algorithm,
                        Width = texture.width,
                        Height = texture.height,
                        UsedRects = new List<RectIntSerializable>()
                    }
                };

                // Store used rects from entries for packer reconstruction
                foreach (var entry in atlas.GetAllEntries())
                {
                    if (entry.TextureIndex == i)
                    {
                        var paddedRect = new RectInt(
                            entry.Rect.x - atlas.Settings.Padding,
                            entry.Rect.y - atlas.Settings.Padding,
                            entry.Rect.width + atlas.Settings.Padding * 2,
                            entry.Rect.height + atlas.Settings.Padding * 2
                        );
                        pageData.PackerState.UsedRects.Add(new RectIntSerializable(paddedRect));
                    }
                }

                data.Pages.Add(pageData);
            }

            // Serialize entries
            foreach (var entry in atlas.GetAllEntries())
            {
                var entryData = new AtlasEntryData
                {
                    Id = entry.Id,
                    TextureIndex = entry.TextureIndex,
                    Name = entry.Name,
                    PixelRect = new RectIntSerializable(entry.Rect),
                    UVRect = new RectSerializable(entry.UV),
                    Border = new Vector4Serializable(entry.Border),
                    Pivot = new Vector2Serializable(entry.Pivot),
                    PixelsPerUnit = entry.PixelsPerUnit,
                    SpriteVersion = entry.SpriteVersion
                };
                data.Entries.Add(entryData);

                // Build name-to-ID map
                if (!string.IsNullOrEmpty(entry.Name))
                {
                    data.NameToIdMap[entry.Name] = entry.Id;
                }
            }

            return data;
        }

        /// <summary>
        /// Deserialize atlas data into a runtime atlas.
        /// Fast path - creates textures and adds entries directly without reflection.
        /// </summary>
        private static RuntimeAtlas DeserializeAtlas(AtlasSerializationData data, string baseFilePath)
        {
            // Create atlas with loaded settings
            var settings = data.Settings.ToSettings();
            
            // ✅ CRITICAL FIX: Force loaded atlas to be READABLE
            // When loading from disk, the atlas texture pages need to stay readable
            // because we're loading pre-rendered pixel data from PNGs
            // Setting this to false would cause the texture to become non-readable after Apply()
            var originalReadable = settings.Readable;
            settings.Readable = true; // Force readable for loaded atlases
            
#if UNITY_EDITOR
            if (!originalReadable)
            {
                Debug.Log($"[AtlasPersistence] Loading atlas: Forcing Readable=true (was {originalReadable}) to preserve loaded texture data");
            }
#endif
            
            var atlas = new RuntimeAtlas(settings);
            var loadedTextures = new List<Texture2D>();

            try
            {
                // Load and add texture pages directly
                for (var i = 0; i < data.Pages.Count; i++)
                {
                    var pageData = data.Pages[i];

                    // Load PNG file directly
                    var texturePath = $"{baseFilePath}_page{i}.png";
                    
                    if (!File.Exists(texturePath))
                    {
                        Debug.LogError($"[AtlasPersistence] PNG file not found: {texturePath}");
                        continue;
                    }
                    
                    var pngData = File.ReadAllBytes(texturePath);
                    
#if UNITY_EDITOR
                    Debug.Log($"[AtlasPersistence] Loading page {i}: Read {pngData.Length} bytes from {texturePath}");
#endif

                    // Create texture from PNG data
                    // ✅ CRITICAL FIX: Create as READABLE 
                    // The 3rd parameter (mipChain) determines if mipmaps are generated
                    // We do NOT pass a 5th parameter - Unity will create a readable texture by default
                    var texture = new Texture2D(2, 2, settings.Format, settings.GenerateMipMaps);
                    texture.filterMode = settings.FilterMode;
                    texture.wrapMode = TextureWrapMode.Clamp;
                    texture.name = $"RuntimeAtlas_Page{i}_Loaded";

                    // ✅ LoadImage will resize the texture and load pixel data
                    if (!texture.LoadImage(pngData))
                    {
                        Debug.LogError($"[AtlasPersistence] LoadImage FAILED for page {i}");
                        UnityEngine.Object.Destroy(texture);
                        continue;
                    }
                    
#if UNITY_EDITOR
                    Debug.Log($"[AtlasPersistence] LoadImage SUCCESS for page {i}: Size = {texture.width}x{texture.height}, Format = {texture.format}");
                    
                    // Verify texture has data
                    if (texture.width == 2 && texture.height == 2)
                    {
                        Debug.LogError($"[AtlasPersistence] ⚠️ Texture size still 2x2 after LoadImage - PNG might be corrupt!");
                    }
                    
                    // Verify texture is readable before Apply
                    if (!texture.isReadable)
                    {
                        Debug.LogError($"[AtlasPersistence] ⚠️ Texture is non-readable after LoadImage!");
                    }
#endif

                    // ✅ CRITICAL FIX: Apply changes but KEEP texture readable (makeNoLongerReadable = false)
                    // This is essential because the atlas needs to be able to read from this texture
                    texture.Apply(settings.GenerateMipMaps, false);
                    
#if UNITY_EDITOR
                    // Verify texture is still readable after Apply
                    if (!texture.isReadable)
                    {
                        Debug.LogError($"[AtlasPersistence] ⚠️ Texture became non-readable after Apply(false, false)! This should never happen.");
                    }
#endif
                    
#if UNITY_EDITOR
                    // ✅ DIAGNOSTIC: Check if texture has actual pixel data
                    try
                    {
                        var testPixel = texture.GetPixel(0, 0);
                        var isBlank = (testPixel.r == 0 && testPixel.g == 0 && testPixel.b == 0 && testPixel.a == 0);
                        Debug.Log($"[AtlasPersistence] Texture pixel test at (0,0): {testPixel}, IsBlank: {isBlank}");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[AtlasPersistence] Failed to read texture pixel: {ex.Message}");
                    }
#endif
                    
                    loadedTextures.Add(texture);

#if UNITY_EDITOR
                    Debug.Log($"[AtlasPersistence] ✓ Loaded page {i}: {texture.name}, Size: {texture.width}x{texture.height}, Readable: {texture.isReadable}, Format: {texture.format}");
#endif

                    // Add loaded page to atlas using reflection (only once per page)
                    AddLoadedPageToAtlas(atlas, texture, pageData.PackerState);
                }

                // Reconstruct entries using reflection (necessary for internal constructor)
                RestoreAtlasEntries(atlas, data);

#if UNITY_EDITOR
                Debug.Log($"[AtlasPersistence] ✓ SYNC LOAD COMPLETE:");
                Debug.Log($"  - Total pages loaded: {data.Pages.Count}");
                Debug.Log($"  - Total entries restored: {data.Entries.Count}");
                Debug.Log($"  - Atlas PageCount: {atlas.PageCount}");
                Debug.Log($"  - Atlas EntryCount: {atlas.EntryCount}");
                
                // Verify each page
                for (var i = 0; i < atlas.PageCount; i++)
                {
                    var tex = atlas.GetTexture(i);
                    Debug.Log($"  - Page {i}: {tex?.name}, {tex?.width}x{tex?.height}");
                }
#endif

                return atlas;
            }
            catch (Exception ex)
            {
                // MEMORY LEAK FIX: Clean up any loaded textures on failure
                Debug.LogError($"[AtlasPersistence] Failed to deserialize atlas: {ex.Message}");
                foreach (var texture in loadedTextures)
                {
                    if (texture != null)
                    {
                        UnityEngine.Object.Destroy(texture);
                    }
                }
                
                // Dispose the atlas to clean up any partial state
                atlas?.Dispose();
                return null;
            }
        }

        /// <summary>
        /// Deserialize atlas data from pre-loaded PNG data (for async loading).
        /// Fast path - creates textures and adds entries directly without reflection.
        /// </summary>
        private static RuntimeAtlas DeserializeAtlasFromData(AtlasSerializationData data, List<(int index, byte[] data)> pngDataList)
        {
            // Create atlas with loaded settings
            var settings = data.Settings.ToSettings();
            var atlas = new RuntimeAtlas(settings);
            var loadedTextures = new List<Texture2D>();

            try
            {
                // Create textures from pre-loaded PNG data
                for (var i = 0; i < data.Pages.Count; i++)
                {
                    var pageData = data.Pages[i];

                    // Find corresponding PNG data
                    var pngDataTuple = pngDataList.Find(x => x.index == i);
                    if (pngDataTuple.data == null)
                    {
                        Debug.LogError($"[AtlasPersistence] Missing PNG data for page {i}");
                        continue;
                    }

                    // Create texture from PNG data
                    // ✅ CRITICAL: Create as readable so it can be made non-readable later
                    var texture = new Texture2D(2, 2, settings.Format, settings.GenerateMipMaps);
                    texture.filterMode = settings.FilterMode;
                    texture.wrapMode = TextureWrapMode.Clamp;
                    texture.name = $"RuntimeAtlas_Page{i}_Loaded";

                    if (!texture.LoadImage(pngDataTuple.data))
                    {
                        Debug.LogError($"[AtlasPersistence] Failed to load texture for page {i}");
                        UnityEngine.Object.Destroy(texture);
                        continue;
                    }

                    // ✅ Keep texture readable on load
                    // It will be made non-readable during first Add/AddBatch if settings.Readable = false
                    texture.Apply(false, false);
                    loadedTextures.Add(texture);

#if UNITY_EDITOR
                    Debug.Log($"[AtlasPersistence] ✓ Async loaded page {i}: {texture.name}, Size: {texture.width}x{texture.height}, Readable: {texture.isReadable}");
#endif

                    // Add loaded page to atlas using reflection (only once per page)
                    AddLoadedPageToAtlas(atlas, texture, pageData.PackerState);
                }

                // Reconstruct entries using reflection (necessary for internal constructor)
                RestoreAtlasEntries(atlas, data);

#if UNITY_EDITOR
                Debug.Log($"[AtlasPersistence] ✓ ASYNC LOAD COMPLETE:");
                Debug.Log($"  - Total pages loaded: {data.Pages.Count}");
                Debug.Log($"  - Total entries restored: {data.Entries.Count}");
                Debug.Log($"  - Atlas PageCount: {atlas.PageCount}");
                Debug.Log($"  - Atlas EntryCount: {atlas.EntryCount}");
                
                // Verify each page
                for (var i = 0; i < atlas.PageCount; i++)
                {
                    var tex = atlas.GetTexture(i);
                    Debug.Log($"  - Page {i}: {tex?.name}, {tex?.width}x{tex?.height}");
                }
#endif

                return atlas;
            }
            catch (Exception ex)
            {
                // MEMORY LEAK FIX: Clean up any loaded textures on failure
                Debug.LogError($"[AtlasPersistence] Failed to deserialize atlas from data: {ex.Message}");
                foreach (var texture in loadedTextures)
                {
                    if (texture != null)
                    {
                        UnityEngine.Object.Destroy(texture);
                    }
                }
                
                // Dispose the atlas to clean up any partial state
                atlas?.Dispose();
                return null;
            }
        }

        /// <summary>
        /// Create a packing algorithm from saved state.
        /// </summary>
        private static IPackingAlgorithm CreatePackerFromState(PackingAlgorithmState state)
        {
            IPackingAlgorithm packer = state.Algorithm switch
            {
                PackingAlgorithm.MaxRects => new MaxRectsAlgorithm(),
                PackingAlgorithm.Skyline => new SkylineAlgorithm(),
                PackingAlgorithm.Guillotine => new GuillotineAlgorithm(),
                PackingAlgorithm.Shelf => new ShelfAlgorithm(),
                _ => new MaxRectsAlgorithm()
            };

            packer.Initialize(state.Width, state.Height);

            // Mark used rects in packer by simulating packs
            // Sort rectangles to pack larger ones first for better results
            var sortedRects = state.UsedRects
                .Select(r => r.ToRectInt())
                .OrderByDescending(r => r.width * r.height)
                .ToList();

            foreach (var rect in sortedRects)
            {
                // Try to pack the rectangle - this will mark the space as used
                // Note: The position might not match exactly, but the space will be occupied
                if (!packer.TryPack(rect.width, rect.height, out var packedRect))
                {
#if UNITY_EDITOR
                    Debug.LogWarning($"[AtlasPersistence] Could not restore rect {rect.width}x{rect.height} in packer state");
#endif
                }
            }

#if UNITY_EDITOR
            Debug.Log($"[AtlasPersistence] Restored packer with {sortedRects.Count} rectangles, fill ratio: {packer.GetFillRatio():P1}");
#endif

            return packer;
        }

        /// <summary>
        /// Add a loaded page (texture + packer) to the atlas using minimal reflection.
        /// Only adds to existing lists, doesn't replace entire fields.
        /// </summary>
        private static void AddLoadedPageToAtlas(RuntimeAtlas atlas, Texture2D texture, PackingAlgorithmState packerState)
        {
            var atlasType = typeof(RuntimeAtlas);
            var bindingFlags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            // Get textures list and add our loaded texture
            var texturesField = atlasType.GetField("_textures", bindingFlags);
            if (texturesField != null)
            {
                var textures = texturesField.GetValue(atlas) as List<Texture2D>;
                if (textures != null)
                {
                    // ✅ CRITICAL FIX: Remove the default empty texture ONLY if it's the very first page being loaded
                    // We check textures.Count == 1 AND EntryCount == 0 AND the texture size matches the initial size
                    // This ensures we only remove the default page created by the RuntimeAtlas constructor
                    // NOT pages that were just loaded in previous iterations of the loop
                    if (textures.Count == 1 && textures[0] != null && 
                        textures[0].width == atlas.Settings.InitialSize && 
                        textures[0].height == atlas.Settings.InitialSize && 
                        atlas.EntryCount == 0 &&
                        !textures[0].name.Contains("_Loaded")) // ✅ Make sure it's not a loaded page
                    {
                        var oldTexture = textures[0];
                        textures.Clear();
                        
                        // Properly destroy in both play and edit mode
                        if (UnityEngine.Application.isPlaying)
                        {
                            UnityEngine.Object.Destroy(oldTexture);
                        }
                        else
                        {
                            UnityEngine.Object.DestroyImmediate(oldTexture);
                        }
                        
#if UNITY_EDITOR
                        Debug.Log("[AtlasPersistence] Removed default empty page before loading");
#endif
                    }
                    textures.Add(texture);
                }
            }

            // Get packers list and add our loaded packer
            var packersField = atlasType.GetField("_packers", bindingFlags);
            if (packersField != null)
            {
                var packers = packersField.GetValue(atlas) as List<IPackingAlgorithm>;
                if (packers != null)
                {
                    // ✅ CRITICAL FIX: Only remove the default packer if packers list matches textures list state
                    // This ensures we only clear once, when loading the very first page
                    if (packers.Count == 1 && atlas.EntryCount == 0)
                    {
                        // Get the textures list to verify we haven't started adding loaded pages yet
                        var texturesField2 = atlasType.GetField("_textures", bindingFlags);
                        var textures2 = texturesField2?.GetValue(atlas) as List<Texture2D>;
                        
                        // Only clear if we have exactly 1 texture and it's NOT a loaded page
                        // (It would be 1 if we just added the first loaded page, or 0 if we just removed the default)
                        if (textures2 != null && textures2.Count <= 1)
                        {
                            packers[0]?.Dispose();
                            packers.Clear();
                            
#if UNITY_EDITOR
                            Debug.Log("[AtlasPersistence] Removed default empty packer before loading");
#endif
                        }
                    }
                    
                    // Create new packer and initialize with loaded state
                    var packer = CreatePackerFromState(packerState);
                    packers.Add(packer);
                }
            }

            // Update current page index
            var currentPageIndexField = atlasType.GetField("_currentPageIndex", bindingFlags);
            if (currentPageIndexField != null)
            {
                var texturesField2 = atlasType.GetField("_textures", bindingFlags);
                if (texturesField2 != null)
                {
                    var textures = texturesField2.GetValue(atlas) as List<Texture2D>;
                    if (textures != null)
                    {
                        currentPageIndexField.SetValue(atlas, Math.Max(0, textures.Count - 1));
                    }
                }
            }
        }

        /// <summary>
        /// Restore atlas entries using minimal reflection.
        /// Only reconstructs entries, doesn't replace entire dictionaries.
        /// </summary>
        private static void RestoreAtlasEntries(RuntimeAtlas atlas, AtlasSerializationData data)
        {
            var atlasType = typeof(RuntimeAtlas);
            var bindingFlags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            // Get entries dictionaries
            var entriesField = atlasType.GetField("_entries", bindingFlags);
            var entriesByNameField = atlasType.GetField("_entriesByName", bindingFlags);
            
            if (entriesField != null && entriesByNameField != null)
            {
                var entries = entriesField.GetValue(atlas) as Dictionary<int, AtlasEntry>;
                var entriesByName = entriesByNameField.GetValue(atlas) as Dictionary<string, AtlasEntry>;
                
                if (entries != null && entriesByName != null)
                {
                    // Clear existing entries (created by default constructor)
                    entries.Clear();
                    entriesByName.Clear();

                    // Recreate entries
                    foreach (var entryData in data.Entries)
                    {
                        var entry = CreateAtlasEntry(
                            atlas,
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

                        if (entry != null)
                        {
                            entries[entryData.Id] = entry;

                            if (!string.IsNullOrEmpty(entryData.Name))
                            {
                                entriesByName[entryData.Name] = entry;
                            }
                        }
                    }
                }
            }

            // Set next ID
            var nextIdField = atlasType.GetField("_nextId", bindingFlags);
            if (nextIdField != null)
            {
                nextIdField.SetValue(atlas, data.NextId);
            }

            // Set version
            var versionField = atlasType.GetField("_version", bindingFlags);
            if (versionField != null)
            {
                versionField.SetValue(atlas, data.Version);
            }
        }

        /// <summary>
        /// OLD METHOD - Kept for reference but not used anymore.
        /// Replace atlas internal data using reflection.
        /// This allows us to fully restore a saved atlas.
        /// </summary>
        [System.Obsolete("Use AddLoadedPageToAtlas and RestoreAtlasEntries instead for better performance")]
        private static void ReplaceAtlasInternals(RuntimeAtlas atlas, List<Texture2D> textures, List<IPackingAlgorithm> packers, AtlasSerializationData data)
        {
            var atlasType = typeof(RuntimeAtlas);
            var bindingFlags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            // Replace textures
            var texturesField = atlasType.GetField("_textures", bindingFlags);
            if (texturesField != null)
            {
                // Dispose old textures first
                var oldTextures = texturesField.GetValue(atlas) as List<Texture2D>;
                if (oldTextures != null)
                {
                    foreach (var oldTex in oldTextures)
                    {
                        if (oldTex != null)
                        {
                            UnityEngine.Object.Destroy(oldTex);
                        }
                    }
                }
                texturesField.SetValue(atlas, textures);
            }

            // Replace packers
            var packersField = atlasType.GetField("_packers", bindingFlags);
            if (packersField != null)
            {
                // Dispose old packers first
                var oldPackers = packersField.GetValue(atlas) as List<IPackingAlgorithm>;
                if (oldPackers != null)
                {
                    foreach (var oldPacker in oldPackers)
                    {
                        oldPacker?.Dispose();
                    }
                }
                packersField.SetValue(atlas, packers);
            }

            // Replace entries
            var entriesField = atlasType.GetField("_entries", bindingFlags);
            var entriesByNameField = atlasType.GetField("_entriesByName", bindingFlags);
            
            if (entriesField != null && entriesByNameField != null)
            {
                var entries = entriesField.GetValue(atlas) as Dictionary<int, AtlasEntry>;
                var entriesByName = entriesByNameField.GetValue(atlas) as Dictionary<string, AtlasEntry>;
                
                if (entries != null && entriesByName != null)
                {
                    // Dispose old entries
                    foreach (var entry in entries.Values)
                    {
                        entry?.Dispose();
                    }
                    entries.Clear();
                    entriesByName.Clear();

                    // Recreate entries
                    foreach (var entryData in data.Entries)
                    {
                        var entry = CreateAtlasEntry(
                            atlas,
                            entryData.Id,
                            entryData.TextureIndex,
                            entryData.PixelRect.ToRectInt(),
                            entryData.UVRect.ToRect(),
                            entryData.Name,
                            entryData.Border.ToVector4(),
                            entryData.Pivot.ToVector2(),
                            entryData.PixelsPerUnit
                        );

                        entries[entryData.Id] = entry;

                        if (!string.IsNullOrEmpty(entryData.Name))
                        {
                            entriesByName[entryData.Name] = entry;
                        }
                    }
                }
            }

            // Set next ID
            var nextIdField = atlasType.GetField("_nextId", bindingFlags);
            if (nextIdField != null)
            {
                nextIdField.SetValue(atlas, data.NextId);
            }

            // Set version
            var versionField = atlasType.GetField("_version", bindingFlags);
            if (versionField != null)
            {
                versionField.SetValue(atlas, data.Version);
            }

            // Set current page index
            var currentPageIndexField = atlasType.GetField("_currentPageIndex", bindingFlags);
            if (currentPageIndexField != null)
            {
                currentPageIndexField.SetValue(atlas, Math.Max(0, textures.Count - 1));
            }

#if UNITY_EDITOR
            Debug.Log($"[AtlasPersistence] Restored atlas with {data.Entries.Count} entries across {textures.Count} pages");
#endif
        }

        /// <summary>
        /// Create an AtlasEntry using reflection (since constructor is internal).
        /// </summary>
        private static AtlasEntry CreateAtlasEntry(
            RuntimeAtlas atlas,
            int id,
            int textureIndex,
            RectInt pixelRect,
            Rect uvRect,
            string name,
            Vector4 border,
            Vector2 pivot,
            float pixelsPerUnit,
            int spriteVersion = 0)
        {
            var entryType = typeof(AtlasEntry);
            var constructor = entryType.GetConstructor(
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new[] { typeof(RuntimeAtlas), typeof(int), typeof(int), typeof(RectInt), typeof(Rect), typeof(string), typeof(Vector4), typeof(Vector2), typeof(float), typeof(int) },
                null
            );

            if (constructor != null)
            {
                return (AtlasEntry)constructor.Invoke(new object[] { atlas, id, textureIndex, pixelRect, uvRect, name, border, pivot, pixelsPerUnit, spriteVersion });
            }

            Debug.LogError("[AtlasPersistence] Failed to create AtlasEntry via reflection");
            return null;
        }
    }
}

