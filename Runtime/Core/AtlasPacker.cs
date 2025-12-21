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
    /// </summary>
    public static class AtlasPacker
    {
        private static RuntimeAtlas _defaultAtlas;
        private static readonly Dictionary<string, RuntimeAtlas> _namedAtlases = new();
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
        /// </summary>
        public static AtlasEntry Pack(Texture2D texture)
        {
            return Default.Add(texture);
        }

        /// <summary>
        /// Pack a texture into the default atlas asynchronously.
        /// </summary>
        public static Task<AtlasEntry> PackAsync(Texture2D texture, CancellationToken cancellationToken = default)
        {
            return Default.AddAsync(texture, cancellationToken);
        }

        /// <summary>
        /// Pack multiple textures into the default atlas.
        /// </summary>
        public static AtlasEntry[] PackBatch(params Texture2D[] textures)
        {
            return Default.AddBatch(textures);
        }

        /// <summary>
        /// Pack multiple textures asynchronously.
        /// </summary>
        public static Task<AtlasEntry[]> PackBatchAsync(Texture2D[] textures, CancellationToken cancellationToken = default)
        {
            return Default.AddBatchAsync(textures, cancellationToken);
        }

        /// <summary>
        /// Pack a sprite into the default atlas and return a new sprite.
        /// </summary>
        public static Sprite PackSprite(Sprite sprite, float? pixelsPerUnit = null)
        {
            var entry = Default.Add(sprite.texture);
            return entry.CreateSprite(pixelsPerUnit ?? sprite.pixelsPerUnit, sprite.pivot / sprite.rect.size);
        }

        /// <summary>
        /// Pack into a named atlas.
        /// </summary>
        public static AtlasEntry Pack(string atlasName, Texture2D texture)
        {
            return GetOrCreate(atlasName).Add(texture);
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
