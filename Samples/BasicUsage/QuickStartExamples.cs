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
            Example1_SimplestUsage();
            Example2_CreateSprites();
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
                Algorithm = PackingAlgorithm.MaxRects
            });

            // Pack all textures at once - much faster!
            _entries = _atlas.AddBatch(myTextures);

            Debug.Log($"Packed {_entries.Length} textures!");
            Debug.Log($"Atlas size: {_atlas.Width}x{_atlas.Height}");
            Debug.Log($"Fill ratio: {_atlas.FillRatio:P1}");

            // Create sprites for each entry
            float xOffset = 0;
            foreach (var entry in _entries)
            {
                var go = new GameObject($"Sprite_{entry.Id}");
                go.transform.SetParent(spawnContainer);
                go.transform.localPosition = new Vector3(xOffset, -2, 0);

                var renderer = go.AddComponent<AtlasSpriteRenderer>();
                renderer.SetEntry(entry);
                renderer.PixelsPerUnit = 64;

                xOffset += 1.5f;
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
                textures[i] = new Texture2D(32, 32);
                var pixels = new Color[32 * 32];
                
                for (int p = 0; p < pixels.Length; p++)
                {
                    pixels[p] = colors[i];
                }
                
                textures[i].SetPixels(pixels);
                textures[i].Apply();
                textures[i].name = $"Color_{colors[i]}";
            }

            return textures;
        }

        private void OnDestroy()
        {
            _atlas?.Dispose();
        }

        // Show info in game view
        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 350, 200));
            GUILayout.Label("QuickStart Examples", GUI.skin.box);
            GUILayout.Label($"Textures loaded: {myTextures?.Length ?? 0}");
            
            if (_atlas != null)
            {
                GUILayout.Label($"Atlas: {_atlas.Width}x{_atlas.Height}");
                GUILayout.Label($"Entries: {_atlas.EntryCount}");
                GUILayout.Label($"Fill: {_atlas.FillRatio:P1}");
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
