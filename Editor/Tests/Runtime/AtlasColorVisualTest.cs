using UnityEngine;
using System.Threading.Tasks;
using System.IO;

namespace RuntimeAtlasPacker.Tests
{
    /// <summary>
    /// Visual test component to verify color accuracy in atlas save/load.
    /// Attach this to a GameObject and press Play to run the test.
    /// </summary>
    public class AtlasColorVisualTest : MonoBehaviour
    {
        [Header("Test Settings")]
        [SerializeField] private bool runTestOnStart = true;
        [SerializeField] private string testDirectory = "TestAtlas";
        
        [Header("Test Results")]
        [SerializeField] private bool testCompleted = false;
        [SerializeField] private bool testPassed = false;
        [SerializeField] private string testMessage = "";

        private async void Start()
        {
            if (runTestOnStart)
            {
                await RunColorTest();
            }
        }

        [ContextMenu("Run Color Test")]
        public async Task RunColorTest()
        {
            testCompleted = false;
            testPassed = false;
            testMessage = "Test running...";

            try
            {
                Debug.Log("[ColorVisualTest] Starting color accuracy test...");

                // Create a 2x2 test texture with known colors
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;

                // Set 4 distinct colors: Red, Green, Blue, White
                var pixels = new Color32[]
                {
                    new Color32(255, 0, 0, 255),     // Pure Red (0, 0)
                    new Color32(0, 255, 0, 255),     // Pure Green (1, 0)
                    new Color32(0, 0, 255, 255),     // Pure Blue (0, 1)
                    new Color32(255, 255, 255, 255)  // Pure White (1, 1)
                };
                texture.SetPixels32(pixels);
                texture.Apply();

                Debug.Log("[ColorVisualTest] Created test texture:");
                for (int i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    Debug.Log($"  Pixel {i}: R={p.r}, G={p.g}, B={p.b}, A={p.a}");
                }

                // Create atlas with this texture
                var settings = new AtlasSettings
                {
                    InitialSize = 2,
                    MaxSize = 2,
                    Format = TextureFormat.RGBA32,
                    FilterMode = FilterMode.Point,
                    Readable = true,
                    GenerateMipMaps = false,
                    Padding = 0
                };

                var atlas = new RuntimeAtlas(settings);
                
                // Replace default texture with our test texture
                var atlasType = typeof(RuntimeAtlas);
                var texturesField = atlasType.GetField("_textures", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var textures = texturesField.GetValue(atlas) as System.Collections.Generic.List<Texture2D>;
                DestroyImmediate(textures[0]);
                textures[0] = texture;

                // Save the atlas
                var filePath = Path.Combine(Application.persistentDataPath, testDirectory, "color_test");
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                
                Debug.Log($"[ColorVisualTest] Saving atlas to: {filePath}");
                var saveSuccess = await AtlasPersistence.SaveAtlasAsync(atlas, filePath);
                
                if (!saveSuccess)
                {
                    testMessage = "❌ Save failed!";
                    Debug.LogError($"[ColorVisualTest] {testMessage}");
                    testCompleted = true;
                    return;
                }

                Debug.Log("[ColorVisualTest] Atlas saved successfully");

                // Load the atlas back
                Debug.Log($"[ColorVisualTest] Loading atlas from: {filePath}");
                var loadedAtlas = AtlasPersistence.LoadAtlas(filePath);
                
                if (loadedAtlas == null)
                {
                    testMessage = "❌ Load failed!";
                    Debug.LogError($"[ColorVisualTest] {testMessage}");
                    testCompleted = true;
                    return;
                }

                Debug.Log("[ColorVisualTest] Atlas loaded successfully");

                // Get loaded texture and verify colors
                var loadedTexture = loadedAtlas.GetTexture(0);
                if (loadedTexture == null)
                {
                    testMessage = "❌ Loaded texture is null!";
                    Debug.LogError($"[ColorVisualTest] {testMessage}");
                    testCompleted = true;
                    return;
                }

                Debug.Log($"[ColorVisualTest] Loaded texture: {loadedTexture.width}x{loadedTexture.height}, Format: {loadedTexture.format}, Readable: {loadedTexture.isReadable}");

                var loadedPixels = loadedTexture.GetPixels32();
                Debug.Log($"[ColorVisualTest] Loaded {loadedPixels.Length} pixels");

                // Compare pixels
                bool allMatch = true;
                for (int i = 0; i < pixels.Length && i < loadedPixels.Length; i++)
                {
                    var original = pixels[i];
                    var loaded = loadedPixels[i];
                    
                    bool match = (original.r == loaded.r && original.g == loaded.g && 
                                  original.b == loaded.b && original.a == loaded.a);
                    
                    string status = match ? "✅" : "❌";
                    Debug.Log($"[ColorVisualTest] Pixel {i}: {status}");
                    Debug.Log($"  Original: R={original.r}, G={original.g}, B={original.b}, A={original.a}");
                    Debug.Log($"  Loaded:   R={loaded.r}, G={loaded.g}, B={loaded.b}, A={loaded.a}");
                    
                    if (!match)
                    {
                        allMatch = false;
                        Debug.LogError($"[ColorVisualTest] Pixel {i} MISMATCH!");
                    }
                }

                testPassed = allMatch;
                testMessage = allMatch ? "✅ All colors match perfectly!" : "❌ Color mismatch detected!";
                Debug.Log($"[ColorVisualTest] {testMessage}");

                // Cleanup
                loadedAtlas.Dispose();
                atlas.Dispose();
                DestroyImmediate(texture);
                
                testCompleted = true;
            }
            catch (System.Exception ex)
            {
                testMessage = $"❌ Exception: {ex.Message}";
                Debug.LogError($"[ColorVisualTest] {testMessage}\n{ex.StackTrace}");
                testCompleted = true;
            }
        }
    }
}

