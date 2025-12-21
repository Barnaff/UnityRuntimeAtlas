using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
                    return entry;

                // Check if texture is too large or invalid
                if (result == AddResult.TooLarge)
                {
                    Debug.LogError($"[AtlasPacker] Texture is too large to fit in any atlas (MaxSize: {Default.Settings.MaxSize})");
                    return null;
                }
                
                if (result == AddResult.InvalidTexture)
                {
                    Debug.LogError("[AtlasPacker] Invalid texture provided");
                    return null;
                }

                // Default atlas is full, try overflow atlases
                Debug.Log("[AtlasPacker] Default atlas is full, checking overflow atlases...");

                // Try existing overflow atlases
                int overflowIndex = 1;
                while (true)
                {
                    string overflowName = $"[Default_Overflow_{overflowIndex}]";
                    
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
                        var newAtlas = new RuntimeAtlas(Default.Settings);
                        _namedAtlases[overflowName] = newAtlas;
                        _overflowCounters["[Default]"] = overflowIndex;
                        
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
        /// Pack multiple textures into the default atlas.
        /// Automatically creates overflow atlases when needed.
        /// </summary>
        public static AtlasEntry[] PackBatch(params Texture2D[] textures)
        {
            lock (_lock)
            {
                var entries = new List<AtlasEntry>();
                
                foreach (var texture in textures)
                {
                    var entry = Pack(texture); // Use Pack which handles overflow
                    if (entry != null)
                    {
                        entries.Add(entry);
                    }
                }
                
                return entries.ToArray();
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
                Debug.LogWarning($"[AtlasPacker] Failed to pack sprite '{sprite.name}': {result}");
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
        /// Create a standalone atlas with the specified settings.
        /// This atlas is not managed by the static API.
        /// </summary>
        public static RuntimeAtlas Create(AtlasSettings settings)
        {
            return new RuntimeAtlas(settings);
        }

        /// <summary>
        /// Create a standalone atlas with preset settings.
        /// </summary>
        public static RuntimeAtlas CreateMobile() => new RuntimeAtlas(AtlasSettings.Mobile);
        
        /// <summary>
        /// Create a high-quality atlas with preset settings.
        /// </summary>
        public static RuntimeAtlas CreateHighQuality() => new RuntimeAtlas(AtlasSettings.HighQuality);
    }
}
