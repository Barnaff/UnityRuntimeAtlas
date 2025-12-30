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
                    for (var i = 0; i < atlas.PageCount; i++)
                    {
                        var texture = atlas.GetTexture(i);
                        if (texture != null)
                        {
                            var texturePath = $"{filePath}_page{i}.png";
                            var pngData = texture.EncodeToPNG();
                            File.WriteAllBytes(texturePath, pngData);
                        }
                    }

                    // Save metadata as JSON (now much smaller without texture data!)
                    var jsonPath = $"{filePath}.json";
                    var json = JsonUtility.ToJson(data, true);
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

            // Get file-specific lock to prevent concurrent writes to same file
            var lockObj = GetLockForPath(filePath);

            // Acquire lock asynchronously to avoid blocking main thread
            await Task.Run(() => System.Threading.Monitor.Enter(lockObj));

            try
            {
#if UNITY_EDITOR
                var profiler = RuntimeAtlasProfiler.Begin("SaveAtlasAsync", "AtlasPersistence", filePath);
#endif

                // Serialize atlas data (without texture data) - on main thread
                var data = SerializeAtlas(atlas);

                // Encode PNGs on main thread (Unity requirement)
                var pngDataList = new List<(int index, byte[] data)>();
                for (var i = 0; i < atlas.PageCount; i++)
                {
                    var texture = atlas.GetTexture(i);
                    if (texture != null)
                    {
                        var pngData = texture.EncodeToPNG();
                        pngDataList.Add((i, pngData));
                    }
                }

                // Serialize JSON on main thread
                var json = JsonUtility.ToJson(data, true);

                // Write files asynchronously (background thread)
                await Task.Run(() =>
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
            finally
            {
                // Always release lock
                System.Threading.Monitor.Exit(lockObj);
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
                    PixelsPerUnit = entry.PixelsPerUnit
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
            var atlas = new RuntimeAtlas(settings);

            // Load and add texture pages directly
            for (var i = 0; i < data.Pages.Count; i++)
            {
                var pageData = data.Pages[i];

                // Load PNG file directly
                var texturePath = $"{baseFilePath}_page{i}.png";
                var pngData = File.ReadAllBytes(texturePath);

                // Create texture from PNG data
                var texture = new Texture2D(2, 2, settings.Format, settings.GenerateMipMaps);
                texture.filterMode = settings.FilterMode;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.name = $"RuntimeAtlas_Page{i}_Loaded";

                if (!texture.LoadImage(pngData))
                {
                    Debug.LogError($"[AtlasPersistence] Failed to load texture for page {i}");
                    UnityEngine.Object.Destroy(texture);
                    continue;
                }

                texture.Apply(false, false);

                // Add loaded page to atlas using reflection (only once per page)
                AddLoadedPageToAtlas(atlas, texture, pageData.PackerState);
            }

            // Reconstruct entries using reflection (necessary for internal constructor)
            RestoreAtlasEntries(atlas, data);

#if UNITY_EDITOR
            Debug.Log($"[AtlasPersistence] Restored atlas with {data.Entries.Count} entries across {data.Pages.Count} pages");
#endif

            return atlas;
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

                texture.Apply(false, false);

                // Add loaded page to atlas using reflection (only once per page)
                AddLoadedPageToAtlas(atlas, texture, pageData.PackerState);
            }

            // Reconstruct entries using reflection (necessary for internal constructor)
            RestoreAtlasEntries(atlas, data);

#if UNITY_EDITOR
            Debug.Log($"[AtlasPersistence] Restored atlas with {data.Entries.Count} entries across {data.Pages.Count} pages");
#endif

            return atlas;
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
                    // Remove the default empty texture if this is the first loaded page
                    if (textures.Count == 1 && textures[0] != null && textures[0].width == atlas.Settings.InitialSize && atlas.EntryCount == 0)
                    {
                        UnityEngine.Object.Destroy(textures[0]);
                        textures.Clear();
                        
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
                    // Remove the default packer if this is the first loaded page
                    if (packers.Count == 1 && atlas.EntryCount == 0)
                    {
                        packers[0]?.Dispose();
                        packers.Clear();
                        
#if UNITY_EDITOR
                        Debug.Log("[AtlasPersistence] Removed default empty packer before loading");
#endif
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
                            entryData.PixelsPerUnit
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
            float pixelsPerUnit)
        {
            var entryType = typeof(AtlasEntry);
            var constructor = entryType.GetConstructor(
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new[] { typeof(RuntimeAtlas), typeof(int), typeof(int), typeof(RectInt), typeof(Rect), typeof(string), typeof(Vector4), typeof(Vector2), typeof(float) },
                null
            );

            if (constructor != null)
            {
                return (AtlasEntry)constructor.Invoke(new object[] { atlas, id, textureIndex, pixelRect, uvRect, name, border, pivot, pixelsPerUnit });
            }

            Debug.LogError("[AtlasPersistence] Failed to create AtlasEntry via reflection");
            return null;
        }
    }
}

