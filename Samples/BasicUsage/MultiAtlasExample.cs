using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using RuntimeAtlasPacker;

namespace RuntimeAtlasPacker.Samples
{
    /// <summary>
    /// Example demonstrating multiple named atlases for different categories.
    /// Shows how to organize textures into separate atlases (UI, Characters, Effects, etc.)
    /// </summary>
    public class MultiAtlasExample : MonoBehaviour
    {
        [Header("Texture Categories")]
        public Texture2D[] uiTextures;
        public Texture2D[] characterTextures;
        public Texture2D[] effectTextures;
        public Texture2D[] backgroundTextures;

        [Header("Atlas Names")]
        public string uiAtlasName = "UI";
        public string characterAtlasName = "Characters";
        public string effectAtlasName = "Effects";
        public string backgroundAtlasName = "Backgrounds";

        private Dictionary<string, RuntimeAtlas> _atlases = new();
        private Dictionary<string, List<AtlasEntry>> _entries = new();

        private void Start()
        {
            // Create specialized atlases with different settings
            CreateAtlases();
            
            // Pack textures into appropriate atlases
            PackTextures();
            
            // Log summary
            LogAtlasSummary();
        }

        private void CreateAtlases()
        {
            // UI Atlas - Smaller, optimized for UI rendering
            _atlases[uiAtlasName] = AtlasPacker.GetOrCreate(uiAtlasName, new AtlasSettings
            {
                InitialSize = 512,
                MaxSize = 2048,
                Padding = 2,
                Format = TextureFormat.RGBA32,
                FilterMode = FilterMode.Bilinear,
                Algorithm = PackingAlgorithm.MaxRects,
                GenerateMipMaps = false
            });

            // Character Atlas - Medium size, might need mipmaps for scaling
            _atlases[characterAtlasName] = AtlasPacker.GetOrCreate(characterAtlasName, new AtlasSettings
            {
                InitialSize = 1024,
                MaxSize = 4096,
                Padding = 2,
                Format = TextureFormat.RGBA32,
                FilterMode = FilterMode.Bilinear,
                Algorithm = PackingAlgorithm.MaxRects,
                GenerateMipMaps = true
            });

            // Effects Atlas - Often smaller textures, point filtering for pixel effects
            _atlases[effectAtlasName] = AtlasPacker.GetOrCreate(effectAtlasName, new AtlasSettings
            {
                InitialSize = 256,
                MaxSize = 1024,
                Padding = 1,
                Format = TextureFormat.RGBA32,
                FilterMode = FilterMode.Point,
                Algorithm = PackingAlgorithm.Skyline, // Faster for many small textures
                GenerateMipMaps = false
            });

            // Background Atlas - Large textures, needs more space
            _atlases[backgroundAtlasName] = AtlasPacker.GetOrCreate(backgroundAtlasName, new AtlasSettings
            {
                InitialSize = 2048,
                MaxSize = 4096,
                Padding = 4,
                Format = TextureFormat.RGBA32,
                FilterMode = FilterMode.Trilinear,
                Algorithm = PackingAlgorithm.MaxRects,
                GenerateMipMaps = true
            });

            foreach (var name in _atlases.Keys)
            {
                _entries[name] = new List<AtlasEntry>();
            }
        }

        private void PackTextures()
        {
            // Pack UI textures
            if (uiTextures != null && uiTextures.Length > 0)
            {
                var entries = _atlases[uiAtlasName].AddBatch(uiTextures);
                _entries[uiAtlasName].AddRange(entries);
            }

            // Pack character textures
            if (characterTextures != null && characterTextures.Length > 0)
            {
                var entries = _atlases[characterAtlasName].AddBatch(characterTextures);
                _entries[characterAtlasName].AddRange(entries);
            }

            // Pack effect textures
            if (effectTextures != null && effectTextures.Length > 0)
            {
                var entries = _atlases[effectAtlasName].AddBatch(effectTextures);
                _entries[effectAtlasName].AddRange(entries);
            }

            // Pack background textures
            if (backgroundTextures != null && backgroundTextures.Length > 0)
            {
                var entries = _atlases[backgroundAtlasName].AddBatch(backgroundTextures);
                _entries[backgroundAtlasName].AddRange(entries);
            }
        }

        private void LogAtlasSummary()
        {
            Debug.Log("=== Multi-Atlas Summary ===");
            
            foreach (var kvp in _atlases)
            {
                var atlas = kvp.Value;
                Debug.Log($"{kvp.Key}: {atlas.Width}x{atlas.Height}, " +
                         $"{atlas.EntryCount} entries, " +
                         $"Fill: {atlas.FillRatio:P1}");
            }

            // Calculate total memory
            long totalMemory = 0;
            foreach (var atlas in _atlases.Values)
            {
                totalMemory += (long)atlas.Width * atlas.Height * 4; // RGBA32
            }
            Debug.Log($"Total atlas memory: {totalMemory / 1024f / 1024f:F2} MB");
        }

        /// <summary>
        /// Get an atlas by category name.
        /// </summary>
        public RuntimeAtlas GetAtlas(string category)
        {
            return _atlases.TryGetValue(category, out var atlas) ? atlas : null;
        }

        /// <summary>
        /// Add a texture to a specific atlas category.
        /// </summary>
        public AtlasEntry AddToAtlas(string category, Texture2D texture)
        {
            if (!_atlases.TryGetValue(category, out var atlas))
            {
                Debug.LogWarning($"Atlas category '{category}' not found");
                return null;
            }

            var (result, entry) = atlas.Add(texture);
            if (result == AddResult.Success && entry != null)
            {
                _entries[category].Add(entry);
                return entry;
            }
            else
            {
                Debug.LogWarning($"Failed to add texture to atlas '{category}': {result}");
                return null;
            }
        }

        /// <summary>
        /// Get all entries from a category.
        /// </summary>
        public List<AtlasEntry> GetEntries(string category)
        {
            return _entries.TryGetValue(category, out var entries) ? entries : new List<AtlasEntry>();
        }

        /// <summary>
        /// Create a sprite renderer using a texture from a specific atlas.
        /// </summary>
        public AtlasSpriteRenderer CreateSprite(string category, int entryIndex, Vector3 position)
        {
            var entries = GetEntries(category);
            if (entryIndex < 0 || entryIndex >= entries.Count)
            {
                Debug.LogWarning($"Invalid entry index {entryIndex} for category '{category}'");
                return null;
            }

            var go = new GameObject($"{category}Sprite_{entryIndex}");
            go.transform.position = position;
            
            var renderer = go.AddComponent<AtlasSpriteRenderer>();
            renderer.SetEntry(entries[entryIndex]);
            
            return renderer;
        }

        private void OnDestroy()
        {
            // Named atlases are managed by AtlasPacker
            // Clear references
            _atlases.Clear();
            _entries.Clear();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 250, 200));
            GUILayout.Label("Multi-Atlas Example", GUI.skin.box);
            
            foreach (var kvp in _atlases)
            {
                var atlas = kvp.Value;
                GUILayout.Label($"{kvp.Key}: {atlas.Width}x{atlas.Height} ({atlas.EntryCount} entries)");
            }
            
            GUILayout.EndArea();
        }
    }

    /// <summary>
    /// Example showing how to switch between atlases based on quality settings.
    /// </summary>
    public class QualityBasedAtlasExample : MonoBehaviour
    {
        [Header("Quality Atlases")]
        public Texture2D[] lowQualityTextures;
        public Texture2D[] highQualityTextures;

        private RuntimeAtlas _currentAtlas;
        private List<AtlasSpriteRenderer> _renderers = new();
        private bool _isHighQuality = true;

        private void Start()
        {
            // Start with quality based on system
            _isHighQuality = SystemInfo.systemMemorySize > 4096;
            
            CreateAtlas();
            SpawnTestSprites();
        }

        private void CreateAtlas()
        {
            var textures = _isHighQuality ? highQualityTextures : lowQualityTextures;
            
            if (textures == null || textures.Length == 0)
            {
                Debug.LogWarning("No textures assigned for quality level");
                return;
            }

            var settings = _isHighQuality
                ? AtlasSettings.HighQuality
                : AtlasSettings.Mobile;

            _currentAtlas = new RuntimeAtlas(settings);
            _currentAtlas.AddBatch(textures);
            
            Debug.Log($"Created {(_isHighQuality ? "High" : "Low")} quality atlas: " +
                     $"{_currentAtlas.Width}x{_currentAtlas.Height}");
        }

        private void SpawnTestSprites()
        {
            if (_currentAtlas == null || _currentAtlas.EntryCount == 0) return;

            var entries = _currentAtlas.GetAllEntries();
            float x = 0;
            
            foreach (var entry in entries)
            {
                var go = new GameObject("QualitySprite");
                go.transform.position = new Vector3(x, 0, 0);
                
                var renderer = go.AddComponent<AtlasSpriteRenderer>();
                renderer.SetEntry(entry);
                _renderers.Add(renderer);
                
                x += 1.5f;
            }
        }

        /// <summary>
        /// Switch quality level - rebuilds atlas with different textures.
        /// </summary>
        public void SetQuality(bool highQuality)
        {
            if (_isHighQuality == highQuality) return;
            
            _isHighQuality = highQuality;
            
            // Clear existing
            foreach (var renderer in _renderers)
            {
                if (renderer != null)
                    Destroy(renderer.gameObject);
            }
            _renderers.Clear();
            
            _currentAtlas?.Dispose();
            
            // Recreate
            CreateAtlas();
            SpawnTestSprites();
        }

        private void OnDestroy()
        {
            _currentAtlas?.Dispose();
        }
    }
}
