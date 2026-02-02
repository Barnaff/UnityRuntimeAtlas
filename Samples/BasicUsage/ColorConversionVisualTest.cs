using UnityEngine;
using System.Threading.Tasks;
using System.IO;

namespace RuntimeAtlasPacker.Samples
{
    /// <summary>
    /// Visual test for color conversion - creates a reference texture and compares it after save/load.
    /// Run this in Play Mode and check the console for results.
    /// </summary>
    public class ColorConversionVisualTest : MonoBehaviour
    {
        [Header("Test Configuration")]
        [SerializeField] private bool runOnStart = true;
        [SerializeField] private string testName = "ColorTest";
        
        [Header("Visual Output")]
        [SerializeField] private Texture2D originalTexture;
        [SerializeField] private Texture2D loadedTexture;
        [SerializeField] private bool testPassed;

        private async void Start()
        {
            if (runOnStart)
            {
                await RunColorTest();
            }
        }

        [ContextMenu("Run Color Test")]
        public async Task RunColorTest()
        {
            Debug.Log("=== Color Conversion Visual Test ===");

            // Create a test texture with known colors
            originalTexture = CreateTestTexture();
            
            // Create atlas and save
            var atlas = CreateAtlasFromTexture(originalTexture);
            var savePath = Path.Combine(Application.temporaryCachePath, testName);
            
            Debug.Log($"Saving test atlas to: {savePath}");
            var saveSuccess = await AtlasPersistence.SaveAtlasAsync(atlas, savePath);
            
            if (!saveSuccess)
            {
                Debug.LogError("❌ Save failed!");
                atlas.Dispose();
                return;
            }

            Debug.Log("✓ Save successful");

            // Load atlas back
            var loadedAtlas = AtlasPersistence.LoadAtlas(savePath);
            if (loadedAtlas == null)
            {
                Debug.LogError("❌ Load failed!");
                atlas.Dispose();
                return;
            }

            Debug.Log("✓ Load successful");

            // Get loaded texture
            loadedTexture = loadedAtlas.GetTexture(0);
            
            // Compare pixels
            testPassed = CompareTextures(originalTexture, loadedTexture);
            
            if (testPassed)
            {
                Debug.Log("✅ COLOR TEST PASSED - All colors match!");
            }
            else
            {
                Debug.LogError("❌ COLOR TEST FAILED - Colors do not match!");
            }

            // Cleanup
            loadedAtlas.Dispose();
            atlas.Dispose();

            Debug.Log("=== Test Complete ===");
        }

        private Texture2D CreateTestTexture()
        {
            // Create a 4x4 texture with distinct colors
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color32[16];
            
            // Row 1: Primary colors
            pixels[0] = new Color32(255, 0, 0, 255);    // Red
            pixels[1] = new Color32(0, 255, 0, 255);    // Green
            pixels[2] = new Color32(0, 0, 255, 255);    // Blue
            pixels[3] = new Color32(255, 255, 0, 255);  // Yellow
            
            // Row 2: Secondary colors
            pixels[4] = new Color32(255, 0, 255, 255);  // Magenta
            pixels[5] = new Color32(0, 255, 255, 255);  // Cyan
            pixels[6] = new Color32(255, 128, 0, 255);  // Orange
            pixels[7] = new Color32(128, 0, 255, 255);  // Purple
            
            // Row 3: Grayscale
            pixels[8] = new Color32(0, 0, 0, 255);      // Black
            pixels[9] = new Color32(85, 85, 85, 255);   // Dark gray
            pixels[10] = new Color32(170, 170, 170, 255); // Light gray
            pixels[11] = new Color32(255, 255, 255, 255); // White
            
            // Row 4: Alpha variations
            pixels[12] = new Color32(255, 0, 0, 255);   // Opaque red
            pixels[13] = new Color32(255, 0, 0, 192);   // 75% red
            pixels[14] = new Color32(255, 0, 0, 128);   // 50% red
            pixels[15] = new Color32(255, 0, 0, 64);    // 25% red

            texture.SetPixels32(pixels);
            texture.Apply();

            Debug.Log("Created test texture with 16 distinct colors");
            return texture;
        }

        private RuntimeAtlas CreateAtlasFromTexture(Texture2D texture)
        {
            var settings = new AtlasSettings
            {
                InitialSize = 4,
                MaxSize = 4,
                Format = AtlasSettings.DefaultFormat,
                FilterMode = FilterMode.Point,
                Readable = true,
                GenerateMipMaps = false,
                Padding = 0
            };

            var atlas = new RuntimeAtlas(settings);
            
            // Replace default texture using public API
            atlas.ReplaceTexturePage(0, texture);

            return atlas;
        }

        private bool CompareTextures(Texture2D original, Texture2D loaded)
        {
            if (original.width != loaded.width || original.height != loaded.height)
            {
                Debug.LogError($"Size mismatch: Original {original.width}x{original.height} vs Loaded {loaded.width}x{loaded.height}");
                return false;
            }

            var originalPixels = original.GetPixels32();
            var loadedPixels = loaded.GetPixels32();

            bool allMatch = true;
            int tolerance = 2; // Allow ±2 for PNG compression

            for (int i = 0; i < originalPixels.Length; i++)
            {
                var orig = originalPixels[i];
                var load = loadedPixels[i];

                bool rMatch = Mathf.Abs(orig.r - load.r) <= tolerance;
                bool gMatch = Mathf.Abs(orig.g - load.g) <= tolerance;
                bool bMatch = Mathf.Abs(orig.b - load.b) <= tolerance;
                bool aMatch = Mathf.Abs(orig.a - load.a) <= tolerance;

                if (!rMatch || !gMatch || !bMatch || !aMatch)
                {
                    int x = i % 4;
                    int y = i / 4;
                    Debug.LogError($"Pixel [{x},{y}] mismatch:");
                    Debug.LogError($"  Original: RGBA({orig.r}, {orig.g}, {orig.b}, {orig.a})");
                    Debug.LogError($"  Loaded:   RGBA({load.r}, {load.g}, {load.b}, {load.a})");
                    allMatch = false;
                }
            }

            if (allMatch)
            {
                Debug.Log($"✓ All {originalPixels.Length} pixels match within tolerance of ±{tolerance}");
            }

            return allMatch;
        }

        [ContextMenu("Log Color Details")]
        public void LogColorDetails()
        {
            if (originalTexture != null)
            {
                Debug.Log("=== Original Texture ===");
                LogTexturePixels(originalTexture);
            }

            if (loadedTexture != null)
            {
                Debug.Log("=== Loaded Texture ===");
                LogTexturePixels(loadedTexture);
            }
        }

        private void LogTexturePixels(Texture2D texture)
        {
            var pixels = texture.GetPixels32();
            for (int y = texture.height - 1; y >= 0; y--)
            {
                string row = $"Row {y}: ";
                for (int x = 0; x < texture.width; x++)
                {
                    int i = y * texture.width + x;
                    var p = pixels[i];
                    row += $"[{p.r},{p.g},{p.b},{p.a}] ";
                }
                Debug.Log(row);
            }
        }
    }
}

