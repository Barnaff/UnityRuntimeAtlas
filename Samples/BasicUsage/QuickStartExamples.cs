using UnityEngine;
using RuntimeAtlasPacker;

namespace RuntimeAtlasPacker.Samples
{
    /// <summary>
    /// Simple quickstart examples showing the most common use cases.
    /// Start here if you're new to Runtime Atlas Packer!
    /// </summary>
    public class QuickStartExamples : MonoBehaviour
    {
        [Header("Your Textures")]
        [Tooltip("Drag your textures here")]
        public Texture2D[] myTextures;

        [Header("Test Objects")]
        public SpriteRenderer targetSpriteRenderer;
        public Transform spawnContainer;

        private RuntimeAtlas _atlas;
        private AtlasEntry[] _entries;

        private void Start()
        {
            // Load from Resources if no textures assigned
            if (myTextures == null || myTextures.Length == 0)
            {
                myTextures = Resources.LoadAll<Texture2D>("");
            }

            if (myTextures.Length == 0)
            {
                Debug.Log("No textures found. Creating colorful placeholders.");
                myTextures = CreateColorfulTextures();
            }

            // Run examples
            // Example1_SimplestUsage();
            // Example2_CreateSprites();
            Example3_BatchPacking();
        }

        /// <summary>
        /// EXAMPLE 1: The simplest way to use atlas packing.
        /// Just one line to pack a texture!
        /// </summary>
        private void Example1_SimplestUsage()
        {
            Debug.Log("=== Example 1: Simplest Usage ===");

            // Pack a texture into the default atlas
            AtlasEntry entry = AtlasPacker.Pack(myTextures[0]);

            // That's it! The texture is now in an atlas.
            Debug.Log($"Packed texture! Entry ID: {entry.Id}, UV: {entry.UV}");

            // You can use the entry to get info:
            Debug.Log($"Size: {entry.Width}x{entry.Height}");
            Debug.Log($"Atlas texture: {entry.Texture.width}x{entry.Texture.height}");

            // Apply to a sprite renderer if available
            if (targetSpriteRenderer != null)
            {
                var sprite = entry.CreateSprite(pixelsPerUnit: 100f);
                targetSpriteRenderer.sprite = sprite;
                Debug.Log($"Created sprite with name: '{sprite.name}'");
            }
        }

        /// <summary>
        /// EXAMPLE 2: Creating sprites that auto-update.
        /// The sprites automatically update when the atlas changes!
        /// </summary>
        private void Example2_CreateSprites()
        {
            Debug.Log("=== Example 2: Auto-Updating Sprites ===");

            if (spawnContainer == null)
            {
                spawnContainer = transform;
            }

            // Create a sprite that auto-updates
            var go = new GameObject("MyAtlasSprite");
            go.transform.SetParent(spawnContainer);
            go.transform.localPosition = new Vector3(-3, 0, 0);

            // Add AtlasSpriteRenderer - it handles everything!
            var renderer = go.AddComponent<AtlasSpriteRenderer>();

            // Option A: Set from texture (auto-packs)
            renderer.SetTexture(myTextures[0]);

            // Option B: Set from entry
            // var entry = AtlasPacker.Pack(myTextures[0]);
            // renderer.SetEntry(entry);

            Debug.Log($"Created auto-updating sprite at {go.transform.position}");
        }

        /// <summary>
        /// EXAMPLE 3: Pack multiple textures at once.
        /// More efficient than packing one at a time!
        /// </summary>
        private void Example3_BatchPacking()
        {
            Debug.Log("=== Example 3: Batch Packing ===");

            // Create your own atlas with custom settings
            _atlas = new RuntimeAtlas(new AtlasSettings
            {
                InitialSize = 512,      // Starting size
                MaxSize = 2048,         // Maximum size
                Padding = 2,            // Pixels between sprites
                Format = TextureFormat.RGBA32,  // Texture format
                FilterMode = FilterMode.Bilinear,
                GenerateMipMaps = false,
                Readable = false,
                GrowthStrategy = GrowthStrategy.Double,
                Algorithm = PackingAlgorithm.MaxRects
            });

            Debug.Log($"Created atlas: {_atlas.Width}x{_atlas.Height}");
            Debug.Log($"Packing {myTextures.Length} textures...");

            // Pack all textures at once - much faster!
            _entries = _atlas.AddBatch(myTextures);

            Debug.Log($"=== Packing Complete ===");
            Debug.Log($"Packed {_entries.Length} textures!");
            Debug.Log($"Atlas size: {_atlas.Width}x{_atlas.Height}");
            Debug.Log($"Fill ratio: {_atlas.FillRatio:P1}");
            
            // Log each entry for debugging
            Debug.Log("=== Entry Details ===");
            for (int i = 0; i < _entries.Length; i++)
            {
                var entry = _entries[i];
                if (entry == null)
                {
                    Debug.LogError($"[{i}] NULL ENTRY!");
                    continue;
                }
                
                Debug.Log($"[{i}] '{entry.Name}' - ID: {entry.Id}, Valid: {entry.IsValid}, Rect: {entry.Rect}, UV: ({entry.UV.x:F3}, {entry.UV.y:F3}, {entry.UV.width:F3}, {entry.UV.height:F3})");
            }

            // Create sprites for each entry
            float xOffset = 0;
            float maxHeight = 0;
            
            Debug.Log("=== Creating Sprite GameObjects ===");
            foreach (var entry in _entries)
            {
                if (entry == null || !entry.IsValid)
                {
                    Debug.LogError($"Invalid entry found!");
                    continue;
                }
                
                var go = new GameObject($"Sprite_{entry.Name}");
                go.transform.SetParent(spawnContainer);
                go.transform.localPosition = new Vector3(xOffset, -2, 0);

                var renderer = go.AddComponent<AtlasSpriteRenderer>();
                renderer.SetEntry(entry);
                renderer.PixelsPerUnit = 64;

                // Calculate sprite world size and add spacing
                float spriteWidth = entry.Width / renderer.PixelsPerUnit;
                maxHeight = Mathf.Max(maxHeight, entry.Height / renderer.PixelsPerUnit);
                
                Debug.Log($"Spawned '{entry.Name}' at world position ({xOffset:F2}, -2, 0), sprite width in world units: {spriteWidth:F2}");
                
                xOffset += spriteWidth + 0.5f; // Add 0.5 unit spacing between sprites
            }
            
            Debug.Log($"=== Batch Packing Example Complete ===");
            
            // Save atlas texture for debugging
            SaveAtlasTexture();
        }
        
        // Debug helper: Save atlas texture to file
        private void SaveAtlasTexture()
        {
            if (_atlas == null || _atlas.Texture == null) return;
            
            try
            {
                var bytes = _atlas.Texture.EncodeToPNG();
                var path = System.IO.Path.Combine(UnityEngine.Application.dataPath, "AtlasDebug.png");
                System.IO.File.WriteAllBytes(path, bytes);
                Debug.Log($"[Debug] Saved atlas texture to: {path}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Debug] Could not save atlas texture: {ex.Message}");
            }
        }

        // Helper: Create colorful placeholder textures
        private Texture2D[] CreateColorfulTextures()
        {
            var colors = new[] { 
                Color.red, Color.green, Color.blue, 
                Color.yellow, Color.cyan, Color.magenta 
            };

            var textures = new Texture2D[colors.Length];

            for (int i = 0; i < colors.Length; i++)
            {
                // Create readable texture with proper name
                textures[i] = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                textures[i].name = $"Color_{colors[i].ToString()}";
                
                var pixels = new Color[64 * 64];
                
                // Create a simple pattern so we can see if it's rendering
                for (int y = 0; y < 64; y++)
                {
                    for (int x = 0; x < 64; x++)
                    {
                        // Add a border to help identify each texture
                        bool isBorder = x < 2 || x >= 62 || y < 2 || y >= 62;
                        pixels[y * 64 + x] = isBorder ? Color.white : colors[i];
                    }
                }
                
                textures[i].SetPixels(pixels);
                textures[i].Apply(false, false); // Don't make it non-readable
                textures[i].filterMode = FilterMode.Point;
            }

            Debug.Log($"Created {textures.Length} placeholder textures with visible borders");
            return textures;
        }

        private void OnDestroy()
        {
            _atlas?.Dispose();
        }

        // Show info in game view
        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 400, 300));
            
            GUILayout.Label("QuickStart Examples", GUI.skin.box);
            GUILayout.Label($"Textures loaded: {myTextures?.Length ?? 0}");
            
            if (_atlas != null)
            {
                GUILayout.Label($"Atlas: {_atlas.Width}x{_atlas.Height}");
                GUILayout.Label($"Entries: {_atlas.EntryCount}");
                GUILayout.Label($"Fill: {_atlas.FillRatio:P1}");
                
                GUILayout.Space(10);
                
                // Show atlas texture preview
                if (_atlas.Texture != null)
                {
                    GUILayout.Label("Atlas Preview:");
                    float previewSize = 150;
                    Rect previewRect = GUILayoutUtility.GetRect(previewSize, previewSize);
                    GUI.DrawTexture(previewRect, _atlas.Texture, ScaleMode.ScaleToFit);
                }
            }

            GUILayout.Space(10);
            GUILayout.Label("See console for detailed output!");
            
            GUILayout.EndArea();
        }
    }

    /// <summary>
    /// Minimal example - just the absolute basics.
    /// </summary>
    public class MinimalExample : MonoBehaviour
    {
        public Texture2D texture;

        void Start()
        {
            // 1. Pack texture
            var entry = AtlasPacker.Pack(texture);

            // 2. Create sprite
            var sprite = entry.CreateSprite();

            // 3. Apply to renderer
            GetComponent<SpriteRenderer>().sprite = sprite;
        }
    }

    /// <summary>
    /// Minimal UI example.
    /// </summary>
    public class MinimalUIExample : MonoBehaviour
    {
        public Texture2D texture;

        void Start()
        {
            // Add AtlasImage component and set texture
            var image = gameObject.AddComponent<AtlasImage>();
            image.SetTexture(texture); // Auto-packs!
        }
    }
}
