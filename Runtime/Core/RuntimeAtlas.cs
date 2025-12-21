using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// High-performance runtime texture atlas with dynamic update support.
    /// Allows adding/removing sprites at runtime without breaking existing references.
    /// </summary>
    public sealed class RuntimeAtlas : IDisposable
    {
        private Texture2D _texture;
        private IPackingAlgorithm _packer;
        private readonly AtlasSettings _settings;
        private readonly Dictionary<int, AtlasEntry> _entries;
        private readonly Queue<int> _recycledIds;
        private int _nextId;
        private bool _isDisposed;
        private bool _isDirty;
        private int _version;

        /// <summary>The atlas texture.</summary>
        public Texture2D Texture => _texture;

        /// <summary>Current atlas width.</summary>
        public int Width => _texture?.width ?? 0;

        /// <summary>Current atlas height.</summary>
        public int Height => _texture?.height ?? 0;

        /// <summary>Number of entries in the atlas.</summary>
        public int EntryCount => _entries.Count;

        /// <summary>Atlas version, incremented on each modification.</summary>
        public int Version => _version;

        /// <summary>Current fill ratio (0-1).</summary>
        public float FillRatio => _packer?.GetFillRatio() ?? 0f;

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
            _settings = settings;
            _entries = new Dictionary<int, AtlasEntry>(64);
            _recycledIds = new Queue<int>(16);
            
            // Create packer
            _packer = settings.Algorithm switch
            {
                PackingAlgorithm.MaxRects => new MaxRectsAlgorithm(),
                PackingAlgorithm.Skyline => new SkylineAlgorithm(),
                _ => new MaxRectsAlgorithm()
            };
            
            // Initialize
            CreateTexture(settings.InitialSize, settings.InitialSize);
            _packer.Initialize(settings.InitialSize, settings.InitialSize);
        }

        private void CreateTexture(int width, int height)
        {
            _texture = new Texture2D(width, height, _settings.Format, _settings.GenerateMipMaps);
            _texture.filterMode = _settings.FilterMode;
            _texture.wrapMode = TextureWrapMode.Clamp;
            
            // Clear to transparent
            var clearPixels = new Color32[width * height];
            _texture.SetPixels32(clearPixels);
            _texture.Apply(false, !_settings.Readable);
        }

        /// <summary>
        /// Add a texture to the atlas.
        /// </summary>
        /// <returns>An AtlasEntry reference that auto-updates when the atlas changes.</returns>
        public AtlasEntry Add(Texture2D texture)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(RuntimeAtlas));
            if (texture == null) throw new ArgumentNullException(nameof(texture));

            int width = texture.width + _settings.Padding * 2;
            int height = texture.height + _settings.Padding * 2;

            // Try to pack
            if (!TryPackWithGrowth(width, height, out var packedRect))
            {
                throw new InvalidOperationException($"Cannot fit texture {texture.width}x{texture.height} in atlas (max size: {_settings.MaxSize})");
            }

            // Adjust for padding
            var contentRect = new RectInt(
                packedRect.x + _settings.Padding,
                packedRect.y + _settings.Padding,
                texture.width,
                texture.height
            );

            // Blit texture to atlas
            TextureBlitter.Blit(texture, _texture, contentRect.x, contentRect.y);
            
            // Calculate UV
            var uvRect = new Rect(
                (float)contentRect.x / _texture.width,
                (float)contentRect.y / _texture.height,
                (float)contentRect.width / _texture.width,
                (float)contentRect.height / _texture.height
            );

            // Create entry
            int id = GetNextId();
            var entry = new AtlasEntry(this, id, contentRect, uvRect);
            _entries[id] = entry;
            
            _version++;
            _isDirty = true;

            return entry;
        }

        /// <summary>
        /// Add a texture to the atlas asynchronously.
        /// </summary>
        public async Task<AtlasEntry> AddAsync(Texture2D texture, CancellationToken cancellationToken = default)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(RuntimeAtlas));
            if (texture == null) throw new ArgumentNullException(nameof(texture));

            // Move to background thread for packing calculation
            int width = texture.width + _settings.Padding * 2;
            int height = texture.height + _settings.Padding * 2;

            RectInt packedRect = default;
            bool success = false;

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                success = TryPackWithGrowthThreadSafe(width, height, out packedRect);
            }, cancellationToken);

            if (!success)
            {
                throw new InvalidOperationException($"Cannot fit texture {texture.width}x{texture.height} in atlas");
            }

            // Back on main thread for texture operations
            return FinalizeAdd(texture, packedRect);
        }

        private AtlasEntry FinalizeAdd(Texture2D texture, RectInt packedRect)
        {
            var contentRect = new RectInt(
                packedRect.x + _settings.Padding,
                packedRect.y + _settings.Padding,
                texture.width,
                texture.height
            );

            TextureBlitter.Blit(texture, _texture, contentRect.x, contentRect.y);

            var uvRect = new Rect(
                (float)contentRect.x / _texture.width,
                (float)contentRect.y / _texture.height,
                (float)contentRect.width / _texture.width,
                (float)contentRect.height / _texture.height
            );

            int id = GetNextId();
            var entry = new AtlasEntry(this, id, contentRect, uvRect);
            _entries[id] = entry;

            _version++;
            _isDirty = true;

            return entry;
        }

        /// <summary>
        /// Add multiple textures to the atlas in a single batch.
        /// More efficient than adding one at a time.
        /// </summary>
        public AtlasEntry[] AddBatch(Texture2D[] textures)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(RuntimeAtlas));
            if (textures == null) throw new ArgumentNullException(nameof(textures));

            var entries = new AtlasEntry[textures.Length];

            // Sort by height descending for better packing
            var sorted = new (int index, Texture2D tex, int area)[textures.Length];
            for (int i = 0; i < textures.Length; i++)
            {
                sorted[i] = (i, textures[i], textures[i].width * textures[i].height);
            }
            Array.Sort(sorted, (a, b) => b.area.CompareTo(a.area));

            // Pack all
            foreach (var (index, tex, _) in sorted)
            {
                entries[index] = Add(tex);
            }

            return entries;
        }

        /// <summary>
        /// Add multiple textures asynchronously.
        /// </summary>
        public async Task<AtlasEntry[]> AddBatchAsync(Texture2D[] textures, CancellationToken cancellationToken = default)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(RuntimeAtlas));
            if (textures == null) throw new ArgumentNullException(nameof(textures));

            var entries = new AtlasEntry[textures.Length];

            // Sort by area descending
            var sorted = new (int index, Texture2D tex, int area)[textures.Length];
            for (int i = 0; i < textures.Length; i++)
            {
                sorted[i] = (i, textures[i], textures[i].width * textures[i].height);
            }
            Array.Sort(sorted, (a, b) => b.area.CompareTo(a.area));

            foreach (var (index, tex, _) in sorted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                entries[index] = await AddAsync(tex, cancellationToken);
            }

            return entries;
        }

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

            // Free the space in the packer
            var fullRect = new RectInt(
                entry.PixelRect.x - _settings.Padding,
                entry.PixelRect.y - _settings.Padding,
                entry.PixelRect.width + _settings.Padding * 2,
                entry.PixelRect.height + _settings.Padding * 2
            );
            
            _packer.Free(fullRect);

            // Clear the texture region (optional, helps debugging)
            if (_settings.Readable)
            {
                TextureBlitter.ClearRegion(_texture, entry.PixelRect);
            }

            // Cleanup
            entry.Dispose();
            _entries.Remove(id);
            _recycledIds.Enqueue(id);
            
            _version++;
            _isDirty = true;

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
        /// </summary>
        public void Repack()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(RuntimeAtlas));
            if (_entries.Count == 0) return;

            // Store all current entries with their pixel data
            var entryData = new List<(AtlasEntry entry, Texture2D texture)>();
            
            foreach (var entry in _entries.Values)
            {
                var tex = TextureBlitter.CopyRegion(_texture, entry.PixelRect);
                entryData.Add((entry, tex));
            }

            // Reset packer
            _packer.Clear();
            
            // Clear texture
            var clearPixels = new Color32[_texture.width * _texture.height];
            _texture.SetPixels32(clearPixels);

            // Re-add all entries sorted by area
            entryData.Sort((a, b) => 
                (b.entry.Width * b.entry.Height).CompareTo(a.entry.Width * a.entry.Height));

            foreach (var (entry, tex) in entryData)
            {
                int width = tex.width + _settings.Padding * 2;
                int height = tex.height + _settings.Padding * 2;

                if (!_packer.TryPack(width, height, out var packedRect))
                {
                    // This shouldn't happen if we haven't grown
                    Debug.LogError($"Repack failed for entry {entry.Id}");
                    UnityEngine.Object.Destroy(tex);
                    continue;
                }

                var contentRect = new RectInt(
                    packedRect.x + _settings.Padding,
                    packedRect.y + _settings.Padding,
                    tex.width,
                    tex.height
                );

                TextureBlitter.Blit(tex, _texture, contentRect.x, contentRect.y);

                var uvRect = new Rect(
                    (float)contentRect.x / _texture.width,
                    (float)contentRect.y / _texture.height,
                    (float)contentRect.width / _texture.width,
                    (float)contentRect.height / _texture.height
                );

                entry.UpdateRect(contentRect, uvRect);
                OnEntryUpdated?.Invoke(this, entry);

                UnityEngine.Object.Destroy(tex);
            }

            _texture.Apply(false, !_settings.Readable);
            _version++;
        }

        private bool TryPackWithGrowth(int width, int height, out RectInt result)
        {
            if (_packer.TryPack(width, height, out result))
                return true;

            if (_settings.GrowthStrategy == GrowthStrategy.None)
                return false;

            // Try to grow
            while (!_packer.TryPack(width, height, out result))
            {
                if (!TryGrow())
                    return false;
            }

            return true;
        }

        private bool TryPackWithGrowthThreadSafe(int width, int height, out RectInt result)
        {
            lock (_packer)
            {
                return TryPackWithGrowth(width, height, out result);
            }
        }

        private bool TryGrow()
        {
            int currentSize = Mathf.Max(_texture.width, _texture.height);
            int newSize;

            switch (_settings.GrowthStrategy)
            {
                case GrowthStrategy.Double:
                    newSize = currentSize * 2;
                    break;
                case GrowthStrategy.Grow50Percent:
                    newSize = Mathf.CeilToInt(currentSize * 1.5f);
                    break;
                default:
                    return false;
            }

            // Round to power of 2
            newSize = Mathf.NextPowerOfTwo(newSize);

            if (newSize > _settings.MaxSize)
                return false;

            Resize(newSize, newSize);
            return true;
        }

        private void Resize(int newWidth, int newHeight)
        {
            var oldTexture = _texture;
            
            // Create new texture
            _texture = TextureBlitter.CreateResized(oldTexture, newWidth, newHeight, 
                _settings.Format, _settings.GenerateMipMaps);
            _texture.filterMode = _settings.FilterMode;
            
            // Update packer
            _packer.Resize(newWidth, newHeight);

            // Update all entry UVs
            foreach (var entry in _entries.Values)
            {
                var uvRect = new Rect(
                    (float)entry.PixelRect.x / newWidth,
                    (float)entry.PixelRect.y / newHeight,
                    (float)entry.PixelRect.width / newWidth,
                    (float)entry.PixelRect.height / newHeight
                );
                entry.UpdateRect(entry.PixelRect, uvRect);
                OnEntryUpdated?.Invoke(this, entry);
            }

            // Cleanup old texture
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(oldTexture);
            else
                UnityEngine.Object.DestroyImmediate(oldTexture);

            _version++;
            OnAtlasResized?.Invoke(this);
        }

        private int GetNextId()
        {
            if (_recycledIds.Count > 0)
                return _recycledIds.Dequeue();
            return _nextId++;
        }

        /// <summary>
        /// Apply pending changes to the texture.
        /// Call this after batching multiple operations.
        /// </summary>
        public void Apply()
        {
            if (!_isDirty) return;
            _texture.Apply(_settings.GenerateMipMaps, !_settings.Readable);
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

            _packer?.Dispose();
            _packer = null;

            if (_texture != null)
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(_texture);
                else
                    UnityEngine.Object.DestroyImmediate(_texture);
                _texture = null;
            }

            OnAtlasResized = null;
            OnEntryUpdated = null;
        }
    }
}
