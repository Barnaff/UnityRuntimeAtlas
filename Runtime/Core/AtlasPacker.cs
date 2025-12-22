using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// Simplified static API for common atlas packing operations.
    /// Manages a default atlas instance for quick use cases.
    /// Automatically creates overflow atlases when existing atlases are full.
    /// </summary>
    public static class AtlasPacker
    {
        private static RuntimeAtlas _defaultAtlas;
        private static readonly Dictionary<string, RuntimeAtlas> _namedAtlases = new();
        private static readonly Dictionary<string, int> _overflowCounters = new();
        private static readonly object _lock = new();

        /// <summary>
        /// Get or create the default atlas.
        /// </summary>
        public static RuntimeAtlas Default
        {
            get
            {
                if (_defaultAtlas == null)
                {
                    lock (_lock)
                    {
                        _defaultAtlas ??= new RuntimeAtlas(AtlasSettings.Default);
                    }
                }
                return _defaultAtlas;
            }
        }

        /// <summary>
        /// Get or create a named atlas.
        /// </summary>
        public static RuntimeAtlas GetOrCreate(string name, AtlasSettings? settings = null)
        {
            lock (_lock)
            {
                if (!_namedAtlases.TryGetValue(name, out var atlas))
                {
                    atlas = new RuntimeAtlas(settings ?? AtlasSettings.Default);
                    _namedAtlases[name] = atlas;
                }
                return atlas;
            }
        }

        /// <summary>
        /// Pack a texture into the default atlas.
        /// Automatically creates overflow atlases if the current one is full.
        /// </summary>
        public static AtlasEntry Pack(Texture2D texture)
        {
            lock (_lock)
            {
                // Try default atlas first
                var (result, entry) = Default.Add(texture);
                if (result == AddResult.Success)
                {
                    return entry;
                }

                // Check if texture is too large or invalid
                if (result == AddResult.TooLarge)
                {
#if UNITY_EDITOR
                    Debug.LogError($"[AtlasPacker] Texture is too large to fit in any atlas (MaxSize: {Default.Settings.MaxSize})");
#endif
                    return null;
                }
                
                if (result == AddResult.InvalidTexture)
                {
#if UNITY_EDITOR
                    Debug.LogError("[AtlasPacker] Invalid texture provided");
#endif
                    return null;
                }

                // Default atlas is full, try overflow atlases
#if UNITY_EDITOR
                Debug.Log("[AtlasPacker] Default atlas is full, checking overflow atlases...");
#endif

                // Try existing overflow atlases
                var overflowIndex = 1;
                while (true)
                {
                    var overflowName = $"[Default_Overflow_{overflowIndex}]";
                    
                    if (_namedAtlases.TryGetValue(overflowName, out var overflowAtlas))
                    {
                        (result, entry) = overflowAtlas.Add(texture);
                        if (result == AddResult.Success)
                        {
#if UNITY_EDITOR
                            Debug.Log($"[AtlasPacker] Added to existing overflow atlas: {overflowName}");
#endif
                            return entry;
                        }
                        overflowIndex++;
                    }
                    else
                    {
                        // Create new overflow atlas
#if UNITY_EDITOR
                        Debug.Log($"[AtlasPacker] Creating new overflow atlas: {overflowName}");
#endif
                        var newAtlas = new RuntimeAtlas(Default.Settings);
                        _namedAtlases[overflowName] = newAtlas;
                        _overflowCounters["[Default]"] = overflowIndex;
                        
                        (result, entry) = newAtlas.Add(texture);
                        if (result == AddResult.Success)
                        {
#if UNITY_EDITOR
                            Debug.Log($"[AtlasPacker] Successfully added to new overflow atlas: {overflowName}");
#endif
                            return entry;
                        }
                        
#if UNITY_EDITOR
                        Debug.LogError($"[AtlasPacker] Failed to add texture even to new overflow atlas! Result: {result}");
#endif
                        return null;
                    }
                }
            }
        }


        /// <summary>
        /// Pack multiple textures into the default atlas.
        /// Automatically creates overflow atlases when needed.
        /// Uses efficient batching to minimize frame drops.
        /// </summary>
        public static AtlasEntry[] PackBatch(params Texture2D[] textures)
        {
            if (textures == null || textures.Length == 0)
            {
                return Array.Empty<AtlasEntry>();
            }

            lock (_lock)
            {
                // Use AddBatch on the atlas directly for much better performance
                var entries = Default.AddBatch(textures);
                
                // If some failed due to full atlas, try overflow atlases
                if (entries.Length < textures.Length)
                {
                    var failedTextures = new List<Texture2D>();
                    var successIds = new HashSet<int>(entries.Select(e => Array.IndexOf(textures, e.Name)));
                    
                    for (var i = 0; i < textures.Length; i++)
                    {
                        if (!successIds.Contains(i))
                        {
                            failedTextures.Add(textures[i]);
                        }
                    }
                    
                    if (failedTextures.Count > 0)
                    {
#if UNITY_EDITOR
                        Debug.Log($"[AtlasPacker] Default atlas couldn't fit {failedTextures.Count} textures, trying overflow atlases...");
#endif
                        
                        // Try overflow atlases
                        var overflowIndex = 1;
                        while (failedTextures.Count > 0)
                        {
                            var overflowName = $"[Default_Overflow_{overflowIndex}]";
                            RuntimeAtlas overflowAtlas;
                            
                            if (_namedAtlases.TryGetValue(overflowName, out overflowAtlas))
                            {
                                // Try existing overflow
                                var overflowEntries = overflowAtlas.AddBatch(failedTextures.ToArray());
                                entries = entries.Concat(overflowEntries).ToArray();
                                
                                if (overflowEntries.Length < failedTextures.Count)
                                {
                                    // Remove successful ones from failed list
                                    var successNames = new HashSet<string>(overflowEntries.Select(e => e.Name));
                                    failedTextures.RemoveAll(t => successNames.Contains(t.name));
                                }
                                else
                                {
                                    break; // All packed
                                }
                            }
                            else
                            {
                                // Create new overflow atlas
#if UNITY_EDITOR
                                Debug.Log($"[AtlasPacker] Creating overflow atlas: {overflowName}");
#endif
                                overflowAtlas = new RuntimeAtlas(Default.Settings);
                                _namedAtlases[overflowName] = overflowAtlas;
                                _overflowCounters["[Default]"] = overflowIndex;
                                
                                var overflowEntries = overflowAtlas.AddBatch(failedTextures.ToArray());
                                entries = entries.Concat(overflowEntries).ToArray();
                                
                                if (overflowEntries.Length < failedTextures.Count)
                                {
                                    var successNames = new HashSet<string>(overflowEntries.Select(e => e.Name));
                                    failedTextures.RemoveAll(t => successNames.Contains(t.name));
                                }
                                else
                                {
                                    break;
                                }
                            }
                            
                            overflowIndex++;
                            
                            // Safety check to prevent infinite loop
                            if (overflowIndex > 100)
                            {
#if UNITY_EDITOR
                                Debug.LogError($"[AtlasPacker] Exceeded maximum overflow atlases. {failedTextures.Count} textures could not be packed.");
#endif
                                break;
                            }
                        }
                    }
                }
                
                return entries;
            }
        }


        /// <summary>
        /// Pack a sprite into the default atlas and return a new sprite.
        /// </summary>
        public static Sprite PackSprite(Sprite sprite, float? pixelsPerUnit = null)
        {
            var (result, entry) = Default.Add(sprite.texture);
            if (result != AddResult.Success || entry == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[AtlasPacker] Failed to pack sprite '{sprite.name}': {result}");
#endif
                return null;
            }
            return entry.CreateSprite(pixelsPerUnit ?? sprite.pixelsPerUnit, sprite.pivot / sprite.rect.size);
        }

        /// <summary>
        /// Pack into a named atlas.
        /// Automatically creates overflow atlases if the named atlas is full.
        /// </summary>
        public static AtlasEntry Pack(string atlasName, Texture2D texture)
        {
            lock (_lock)
            {
                // Try main named atlas first
                var atlas = GetOrCreate(atlasName);
                var (result, entry) = atlas.Add(texture);
                if (result == AddResult.Success)
                    return entry;

                // Check if texture is too large or invalid
                if (result == AddResult.TooLarge)
                {
                    Debug.LogError($"[AtlasPacker] Texture is too large to fit in atlas '{atlasName}' (MaxSize: {atlas.Settings.MaxSize})");
                    return null;
                }
                
                if (result == AddResult.InvalidTexture)
                {
                    Debug.LogError($"[AtlasPacker] Invalid texture provided for atlas '{atlasName}'");
                    return null;
                }

                // Named atlas is full, try overflow atlases
                Debug.Log($"[AtlasPacker] Named atlas '{atlasName}' is full, checking overflow atlases...");

                // Try existing overflow atlases
                int overflowIndex = 1;
                while (true)
                {
                    string overflowName = $"{atlasName}_Overflow_{overflowIndex}";
                    
                    if (_namedAtlases.TryGetValue(overflowName, out var overflowAtlas))
                    {
                        (result, entry) = overflowAtlas.Add(texture);
                        if (result == AddResult.Success)
                        {
                            Debug.Log($"[AtlasPacker] Added to existing overflow atlas: {overflowName}");
                            return entry;
                        }
                        overflowIndex++;
                    }
                    else
                    {
                        // Create new overflow atlas
                        Debug.Log($"[AtlasPacker] Creating new overflow atlas: {overflowName}");
                        var newAtlas = new RuntimeAtlas(atlas.Settings);
                        _namedAtlases[overflowName] = newAtlas;
                        _overflowCounters[atlasName] = overflowIndex;
                        
                        (result, entry) = newAtlas.Add(texture);
                        if (result == AddResult.Success)
                        {
                            Debug.Log($"[AtlasPacker] Successfully added to new overflow atlas: {overflowName}");
                            return entry;
                        }
                        
                        Debug.LogError($"[AtlasPacker] Failed to add texture even to new overflow atlas! Result: {result}");
                        return null;
                    }
                }
            }
        }

        /// <summary>
        /// Check if a named atlas exists.
        /// </summary>
        public static bool HasAtlas(string name)
        {
            lock (_lock)
            {
                return _namedAtlases.ContainsKey(name);
            }
        }

        /// <summary>
        /// Dispose a named atlas.
        /// </summary>
        public static void DisposeAtlas(string name)
        {
            lock (_lock)
            {
                if (_namedAtlases.TryGetValue(name, out var atlas))
                {
                    atlas.Dispose();
                    _namedAtlases.Remove(name);
                }
            }
        }

        /// <summary>
        /// Dispose all atlases including the default one.
        /// </summary>
        public static void DisposeAll()
        {
            lock (_lock)
            {
                _defaultAtlas?.Dispose();
                _defaultAtlas = null;

                foreach (var atlas in _namedAtlases.Values)
                {
                    atlas.Dispose();
                }
                _namedAtlases.Clear();
                _overflowCounters.Clear();
            }
        }

        /// <summary>
        /// Get the number of overflow atlases for the default atlas.
        /// </summary>
        public static int GetDefaultOverflowCount()
        {
            lock (_lock)
            {
                return _overflowCounters.TryGetValue("[Default]", out var count) ? count : 0;
            }
        }

        /// <summary>
        /// Get the number of overflow atlases for a named atlas.
        /// </summary>
        public static int GetOverflowCount(string atlasName)
        {
            lock (_lock)
            {
                return _overflowCounters.TryGetValue(atlasName, out var count) ? count : 0;
            }
        }

        /// <summary>
        /// Get all atlases including overflow atlases for the default atlas.
        /// </summary>
        public static RuntimeAtlas[] GetAllDefaultAtlases()
        {
            lock (_lock)
            {
                var atlases = new List<RuntimeAtlas>();
                
                if (_defaultAtlas != null)
                    atlases.Add(_defaultAtlas);
                
                int overflowCount = GetDefaultOverflowCount();
                for (int i = 1; i <= overflowCount; i++)
                {
                    string overflowName = $"[Default_Overflow_{i}]";
                    if (_namedAtlases.TryGetValue(overflowName, out var atlas))
                    {
                        atlases.Add(atlas);
                    }
                }
                
                return atlases.ToArray();
            }
        }

        /// <summary>
        /// Get all atlases including overflow atlases for a named atlas.
        /// </summary>
        public static RuntimeAtlas[] GetAllAtlases(string atlasName)
        {
            lock (_lock)
            {
                var atlases = new List<RuntimeAtlas>();
                
                if (_namedAtlases.TryGetValue(atlasName, out var mainAtlas))
                    atlases.Add(mainAtlas);
                
                int overflowCount = GetOverflowCount(atlasName);
                for (int i = 1; i <= overflowCount; i++)
                {
                    string overflowName = $"{atlasName}_Overflow_{i}";
                    if (_namedAtlases.TryGetValue(overflowName, out var atlas))
                    {
                        atlases.Add(atlas);
                    }
                }
                
                return atlases.ToArray();
            }
        }

        /// <summary>
        /// Clear and dispose all atlases. Use this to prevent memory leaks between play mode runs.
        /// </summary>
        public static void ClearAllAtlases()
        {
            lock (_lock)
            {
#if UNITY_EDITOR
                Debug.Log($"[AtlasPacker] Clearing {_namedAtlases.Count + (_defaultAtlas != null ? 1 : 0)} atlases");
#endif

                // Dispose default atlas
                if (_defaultAtlas != null)
                {
                    _defaultAtlas.Dispose();
                    _defaultAtlas = null;
#if UNITY_EDITOR
                    Debug.Log("[AtlasPacker] Disposed default atlas");
#endif
                }

                // Dispose all named atlases
                foreach (var kvp in _namedAtlases)
                {
                    if (kvp.Value != null)
                    {
                        kvp.Value.Dispose();
#if UNITY_EDITOR
                        Debug.Log($"[AtlasPacker] Disposed atlas: {kvp.Key}");
#endif
                    }
                }

                // Clear collections
                _namedAtlases.Clear();
                _overflowCounters.Clear();
                
#if UNITY_EDITOR
                Debug.Log("[AtlasPacker] All atlases cleared");
#endif
            }
        }

        /// <summary>
        /// Get count of all active atlases (for debugging/monitoring).
        /// </summary>
        public static int GetActiveAtlasCount()
        {
            lock (_lock)
            {
                return _namedAtlases.Count + (_defaultAtlas != null ? 1 : 0);
            }
        }

        /// <summary>
        /// Get names of all active atlases (for debugging/monitoring).
        /// </summary>
        public static string[] GetActiveAtlasNames()
        {
            lock (_lock)
            {
                var names = new List<string>();
                
                if (_defaultAtlas != null)
                    names.Add("[Default]");
                
                names.AddRange(_namedAtlases.Keys);
                
                return names.ToArray();
            }
        }

        /// <summary>
        /// Pack multiple textures with progress callback and frame spreading.
        /// Use this for large batches to avoid frame drops.
        /// Returns successfully packed entries via callback.
        /// </summary>
        public static IEnumerator PackBatchAsync(Texture2D[] textures, Action<AtlasEntry[], float> onProgress = null, int texturesPerFrame = 5)
        {
            if (textures == null || textures.Length == 0)
            {
                onProgress?.Invoke(Array.Empty<AtlasEntry>(), 1f);
                yield break;
            }

            var allEntries = new List<AtlasEntry>();
            int processed = 0;
            int batchSize = Mathf.Max(1, texturesPerFrame);

            Debug.Log($"[AtlasPacker] Starting async batch pack of {textures.Length} textures ({batchSize} per frame)");

            lock (_lock)
            {
                // Process in smaller batches to spread across frames
                for (int i = 0; i < textures.Length; i += batchSize)
                {
                    int count = Mathf.Min(batchSize, textures.Length - i);
                    var batch = new Texture2D[count];
                    Array.Copy(textures, i, batch, 0, count);

                    // Use the optimized AddBatch
                    var entries = Default.AddBatch(batch);
                    allEntries.AddRange(entries);

                    // Handle overflow if needed
                    if (entries.Length < batch.Length)
                    {
                        var failed = batch.Where(t => !entries.Any(e => e.Name == t.name)).ToArray();
                        if (failed.Length > 0)
                        {
                            // Try overflow atlases
                            var overflowEntries = TryPackInOverflow(failed);
                            allEntries.AddRange(overflowEntries);
                        }
                    }

                    processed += count;
                    float progress = (float)processed / textures.Length;
                    onProgress?.Invoke(allEntries.ToArray(), progress);

                    // Yield to next frame
                    yield return null;
                }
            }

            Debug.Log($"[AtlasPacker] Async batch complete: {allEntries.Count}/{textures.Length} textures packed");
            onProgress?.Invoke(allEntries.ToArray(), 1f);
        }

        /// <summary>
        /// Helper method to try packing failed textures in overflow atlases.
        /// </summary>
        private static List<AtlasEntry> TryPackInOverflow(Texture2D[] textures)
        {
            var entries = new List<AtlasEntry>();
            var remaining = new List<Texture2D>(textures);

            int overflowIndex = 1;
            while (remaining.Count > 0 && overflowIndex <= 100)
            {
                string overflowName = $"[Default_Overflow_{overflowIndex}]";
                RuntimeAtlas overflowAtlas;

                if (!_namedAtlases.TryGetValue(overflowName, out overflowAtlas))
                {
                    // Create new overflow atlas
                    overflowAtlas = new RuntimeAtlas(Default.Settings);
                    _namedAtlases[overflowName] = overflowAtlas;
                    _overflowCounters["[Default]"] = overflowIndex;
                    Debug.Log($"[AtlasPacker] Created overflow atlas: {overflowName}");
                }

                var overflowEntries = overflowAtlas.AddBatch(remaining.ToArray());
                entries.AddRange(overflowEntries);

                if (overflowEntries.Length > 0)
                {
                    var successNames = new HashSet<string>(overflowEntries.Select(e => e.Name));
                    remaining.RemoveAll(t => successNames.Contains(t.name));
                }
                else
                {
                    // Atlas couldn't fit anything, move to next
                    overflowIndex++;
                }
            }

            return entries;
        }

        /// <summary>
        /// Pack textures for a named atlas asynchronously with frame spreading.
        /// </summary>
        public static IEnumerator PackBatchAsync(string atlasName, Texture2D[] textures, Action<AtlasEntry[], float> onProgress = null, int texturesPerFrame = 5)
        {
            if (textures == null || textures.Length == 0)
            {
                onProgress?.Invoke(Array.Empty<AtlasEntry>(), 1f);
                yield break;
            }

            var allEntries = new List<AtlasEntry>();
            int processed = 0;
            int batchSize = Mathf.Max(1, texturesPerFrame);

            Debug.Log($"[AtlasPacker] Starting async batch pack for '{atlasName}': {textures.Length} textures");

            lock (_lock)
            {
                var atlas = GetOrCreate(atlasName);

                for (int i = 0; i < textures.Length; i += batchSize)
                {
                    int count = Mathf.Min(batchSize, textures.Length - i);
                    var batch = new Texture2D[count];
                    Array.Copy(textures, i, batch, 0, count);

                    var entries = atlas.AddBatch(batch);
                    allEntries.AddRange(entries);

                    processed += count;
                    float progress = (float)processed / textures.Length;
                    onProgress?.Invoke(allEntries.ToArray(), progress);

                    yield return null;
                }
            }

            Debug.Log($"[AtlasPacker] Async batch complete for '{atlasName}': {allEntries.Count}/{textures.Length} packed");
            onProgress?.Invoke(allEntries.ToArray(), 1f);
        }
    }
}
