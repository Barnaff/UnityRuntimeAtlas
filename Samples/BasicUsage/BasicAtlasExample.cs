using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using RuntimeAtlasPacker;

namespace RuntimeAtlasPacker.Samples
{
    /// <summary>
    /// Example demonstrating basic usage of the Runtime Atlas Packer.
    /// </summary>
    public class BasicAtlasExample : MonoBehaviour
    {
        [Header("Textures to Pack")]
        public Texture2D[] texturesToPack;
        
        [Header("Target Renderers")]
        public SpriteRenderer[] spriteRenderers;

        [Header("Settings")]
        public bool useAsync = true;
        public int atlasSize = 1024;
        public int maxAtlasSize = 4096;

        private RuntimeAtlas _atlas;
        private List<AtlasEntry> _entries = new();
        private List<Sprite> _sprites = new();

        async void Start()
        {
            // Create atlas with custom settings
            _atlas = new RuntimeAtlas(new AtlasSettings
            {
                InitialSize = atlasSize,
                MaxSize = maxAtlasSize,
                Padding = 2,
                Format = TextureFormat.RGBA32,
                Algorithm = PackingAlgorithm.MaxRects,
                GrowthStrategy = GrowthStrategy.Double
            });

            // Pack textures
            if (useAsync)
            {
                await PackTexturesAsync();
            }
            else
            {
                PackTexturesSync();
            }

            // Display atlas info
            Debug.Log($"Atlas created: {_atlas.Width}x{_atlas.Height}, " +
                      $"Entries: {_atlas.EntryCount}, " +
                      $"Fill: {_atlas.FillRatio:P1}");
        }

        void PackTexturesSync()
        {
            // Batch pack is more efficient than one at a time
            var entries = _atlas.AddBatch(texturesToPack);
            
            for (int i = 0; i < entries.Length; i++)
            {
                _entries.Add(entries[i]);
                
                // Apply to sprite renderer if available
                if (i < spriteRenderers.Length && spriteRenderers[i] != null)
                {
                    var sprite = entries[i].CreateSprite();
                    _sprites.Add(sprite);
                    spriteRenderers[i].sprite = sprite;
                }

                // Subscribe to UV changes
                entries[i].OnUVChanged += OnEntryUVChanged;
            }
        }

        async Task PackTexturesAsync()
        {
            // Batch pack (synchronous as it requires main thread for texture operations)
            var entries = _atlas.AddBatch(texturesToPack);
            
            // Yield to allow frame update if needed, though packing is usually fast
            await Task.Yield();
            
            for (int i = 0; i < entries.Length; i++)
            {
                _entries.Add(entries[i]);
                
                if (i < spriteRenderers.Length && spriteRenderers[i] != null)
                {
                    var sprite = entries[i].CreateSprite();
                    _sprites.Add(sprite);
                    spriteRenderers[i].sprite = sprite;
                }

                entries[i].OnUVChanged += OnEntryUVChanged;
            }
        }

        void OnEntryUVChanged(AtlasEntry entry)
        {
            Debug.Log($"Entry {entry.Id} UV changed. New UV: {entry.UV}");
            
            // Find and update the corresponding sprite
            int index = _entries.IndexOf(entry);
            if (index >= 0 && index < spriteRenderers.Length && spriteRenderers[index] != null)
            {
                // Destroy old sprite
                if (index < _sprites.Count && _sprites[index] != null)
                {
                    Destroy(_sprites[index]);
                }
                
                // Create and apply new sprite
                var newSprite = entry.CreateSprite();
                _sprites[index] = newSprite;
                spriteRenderers[index].sprite = newSprite;
            }
        }

        /// <summary>
        /// Add a new texture at runtime.
        /// </summary>
        public AtlasEntry AddTexture(Texture2D texture)
        {
            var (result, entry) = _atlas.Add(texture);
            if (result != AddResult.Success || entry == null)
            {
                Debug.LogWarning($"Failed to add texture to atlas: {result}");
                return null;
            }
            
            _entries.Add(entry);
            entry.OnUVChanged += OnEntryUVChanged;
            
            Debug.Log($"Added texture. Atlas size: {_atlas.Width}x{_atlas.Height}, " +
                      $"Fill: {_atlas.FillRatio:P1}");
            
            return entry;
        }

        /// <summary>
        /// Remove a texture from the atlas.
        /// </summary>
        public void RemoveTexture(int index)
        {
            if (index < 0 || index >= _entries.Count) return;
            
            var entry = _entries[index];
            entry.OnUVChanged -= OnEntryUVChanged;
            _atlas.Remove(entry);
            _entries.RemoveAt(index);
            
            if (index < _sprites.Count)
            {
                if (_sprites[index] != null)
                    Destroy(_sprites[index]);
                _sprites.RemoveAt(index);
            }
            
            Debug.Log($"Removed texture at index {index}. " +
                      $"Fill: {_atlas.FillRatio:P1}");
        }

        /// <summary>
        /// Repack the atlas to reclaim fragmented space.
        /// </summary>
        public void Repack()
        {
            _atlas.Repack();
            Debug.Log($"Repacked atlas. Fill: {_atlas.FillRatio:P1}");
        }

        void OnDestroy()
        {
            // Clean up
            foreach (var sprite in _sprites)
            {
                if (sprite != null)
                    Destroy(sprite);
            }
            _sprites.Clear();
            
            foreach (var entry in _entries)
            {
                entry.OnUVChanged -= OnEntryUVChanged;
            }
            _entries.Clear();
            
            _atlas?.Dispose();
        }
    }
}
