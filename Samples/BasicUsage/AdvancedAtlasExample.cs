using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using RuntimeAtlasPacker;

namespace RuntimeAtlasPacker.Samples
{
    /// <summary>
    /// Advanced example showing dynamic loading, UI integration, and performance optimization.
    /// </summary>
    public class AdvancedAtlasExample : MonoBehaviour
    {
        [Header("Atlas Configuration")]
        public string atlasName = "DynamicSprites";
        public AtlasSettings settings = AtlasSettings.Default;

        [Header("UI Elements")]
        public RawImage atlasPreview;
        public Text statsText;

        [Header("Dynamic Loading")]
        public string resourcePath = "Sprites/";

        private RuntimeAtlas _atlas;
        private Dictionary<string, AtlasEntry> _entryCache = new();
        private CancellationTokenSource _cts;

        void Awake()
        {
            _cts = new CancellationTokenSource();
            
            // Create or get named atlas
            _atlas = AtlasPacker.GetOrCreate(atlasName, settings);
            _atlas.OnAtlasResized += OnAtlasResized;
            _atlas.OnEntryUpdated += OnEntryUpdated;
            
            UpdatePreview();
        }

        /// <summary>
        /// Load and pack a sprite by name with caching.
        /// </summary>
        public async Task<AtlasEntry> LoadSpriteAsync(string spriteName)
        {
            // Check cache first
            if (_entryCache.TryGetValue(spriteName, out var cached) && cached.IsValid)
            {
                return cached;
            }

            // Load from resources
            var resourceRequest = Resources.LoadAsync<Texture2D>(resourcePath + spriteName);
            
            while (!resourceRequest.isDone)
            {
                await Task.Yield();
                if (_cts.Token.IsCancellationRequested)
                    return null;
            }

            var texture = resourceRequest.asset as Texture2D;
            if (texture == null)
            {
                Debug.LogWarning($"Failed to load sprite: {spriteName}");
                return null;
            }

            // Pack and cache
            var entry = await _atlas.AddAsync(texture, _cts.Token);
            _entryCache[spriteName] = entry;
            
            UpdatePreview();
            UpdateStats();
            
            return entry;
        }

        /// <summary>
        /// Load multiple sprites in parallel.
        /// </summary>
        public async Task<AtlasEntry[]> LoadSpritesAsync(string[] spriteNames)
        {
            var tasks = new List<Task<AtlasEntry>>();
            
            foreach (var name in spriteNames)
            {
                tasks.Add(LoadSpriteAsync(name));
            }

            return await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Unload a sprite from the atlas.
        /// </summary>
        public void UnloadSprite(string spriteName)
        {
            if (_entryCache.TryGetValue(spriteName, out var entry))
            {
                entry.Remove();
                _entryCache.Remove(spriteName);
                
                UpdatePreview();
                UpdateStats();
            }
        }

        /// <summary>
        /// Get a sprite for immediate use (sync, with fallback).
        /// </summary>
        public Sprite GetSprite(string spriteName, float pixelsPerUnit = 100f)
        {
            if (_entryCache.TryGetValue(spriteName, out var entry) && entry.IsValid)
            {
                return entry.CreateSprite(pixelsPerUnit);
            }

            // Return null or a placeholder - caller should use async loading
            return null;
        }

        /// <summary>
        /// Check if atlas needs defragmentation.
        /// </summary>
        public bool NeedsRepack()
        {
            // Heuristic: if fill ratio is low but we have many entries,
            // there might be fragmentation
            float fillRatio = _atlas.FillRatio;
            int entryCount = _atlas.EntryCount;
            
            // If less than 50% full with more than 10 entries removed,
            // consider repacking
            return fillRatio < 0.5f && entryCount > 0;
        }

        /// <summary>
        /// Defragment the atlas by repacking all entries.
        /// </summary>
        public void DefragmentAtlas()
        {
            _atlas.Repack();
            UpdatePreview();
            UpdateStats();
        }

        /// <summary>
        /// Pre-analyze textures before packing.
        /// </summary>
        public PackingStats AnalyzeTextures(Texture2D[] textures)
        {
            return AtlasBatchProcessor.AnalyzeBatch(textures, settings.InitialSize, settings.Padding);
        }

        private void OnAtlasResized(RuntimeAtlas atlas)
        {
            Debug.Log($"Atlas '{atlasName}' resized to {atlas.Width}x{atlas.Height}");
            UpdatePreview();
            UpdateStats();
        }

        private void OnEntryUpdated(RuntimeAtlas atlas, AtlasEntry entry)
        {
            // Individual entry updates can be handled here
        }

        private void UpdatePreview()
        {
            if (atlasPreview != null && _atlas.Texture != null)
            {
                atlasPreview.texture = _atlas.Texture;
            }
        }

        private void UpdateStats()
        {
            if (statsText != null)
            {
                statsText.text = $"Atlas: {_atlas.Width}x{_atlas.Height}\n" +
                                $"Entries: {_atlas.EntryCount}\n" +
                                $"Fill: {_atlas.FillRatio:P1}\n" +
                                $"Cached: {_entryCache.Count}";
            }
        }

        void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            
            if (_atlas != null)
            {
                _atlas.OnAtlasResized -= OnAtlasResized;
                _atlas.OnEntryUpdated -= OnEntryUpdated;
            }
            
            _entryCache.Clear();
            
            // Note: Named atlas is managed by AtlasPacker, don't dispose here
            // unless you want to remove it from the cache
        }
    }

    /// <summary>
    /// Example pooling system that uses atlas entries efficiently.
    /// </summary>
    public class AtlasSpritePool : MonoBehaviour
    {
        [SerializeField] private int poolSize = 20;
        [SerializeField] private GameObject spritePrefab;
        
        private Queue<AtlasSprite> _pool = new();
        private List<AtlasSprite> _active = new();

        void Awake()
        {
            // Pre-warm pool
            for (int i = 0; i < poolSize; i++)
            {
                var go = Instantiate(spritePrefab, transform);
                go.SetActive(false);
                
                var atlasSprite = go.GetComponent<AtlasSprite>();
                if (atlasSprite == null)
                    atlasSprite = go.AddComponent<AtlasSprite>();
                
                _pool.Enqueue(atlasSprite);
            }
        }

        /// <summary>
        /// Get a sprite from the pool and bind it to an entry.
        /// </summary>
        public AtlasSprite Spawn(AtlasEntry entry, Vector3 position, float pixelsPerUnit = 100f)
        {
            AtlasSprite sprite;
            
            if (_pool.Count > 0)
            {
                sprite = _pool.Dequeue();
            }
            else
            {
                // Pool exhausted, create new
                var go = Instantiate(spritePrefab, transform);
                sprite = go.GetComponent<AtlasSprite>();
                if (sprite == null)
                    sprite = go.AddComponent<AtlasSprite>();
            }

            sprite.transform.position = position;
            sprite.Bind(entry, pixelsPerUnit);
            sprite.gameObject.SetActive(true);
            
            _active.Add(sprite);
            return sprite;
        }

        /// <summary>
        /// Return a sprite to the pool.
        /// </summary>
        public void Despawn(AtlasSprite sprite)
        {
            if (!_active.Contains(sprite)) return;
            
            sprite.Unbind();
            sprite.gameObject.SetActive(false);
            
            _active.Remove(sprite);
            _pool.Enqueue(sprite);
        }

        /// <summary>
        /// Return all sprites to the pool.
        /// </summary>
        public void DespawnAll()
        {
            foreach (var sprite in _active)
            {
                sprite.Unbind();
                sprite.gameObject.SetActive(false);
                _pool.Enqueue(sprite);
            }
            _active.Clear();
        }
    }
}
