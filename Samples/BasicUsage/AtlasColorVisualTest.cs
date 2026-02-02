using UnityEngine;
using System.IO;
using System.Threading.Tasks;

namespace RuntimeAtlasPacker.Samples
{
    /// <summary>
    /// Simple visual test to verify atlas color preservation.
    /// Creates a test image with known colors, saves it, loads it, and displays both.
    /// </summary>
    public class AtlasColorVisualTest : MonoBehaviour
    {
        [Header("Test Settings")]
        [SerializeField] private bool runOnStart = true;
        [SerializeField] private string testFileName = "ColorTest";
        
        [Header("Display")]
        [SerializeField] private UnityEngine.UI.RawImage originalDisplay;
        [SerializeField] private UnityEngine.UI.RawImage loadedDisplay;
        [SerializeField] private UnityEngine.UI.Text statusText;

        private RuntimeAtlas _testAtlas;
        private RuntimeAtlas _loadedAtlas;
        private string _savePath;

        private void Start()
        {
            _savePath = Path.Combine(Application.persistentDataPath, testFileName);
            
            if (runOnStart)
            {
                RunColorTest();
            }
        }

        [ContextMenu("Run Color Test")]
        public async void RunColorTest()
        {
            UpdateStatus("Starting color test...");

            // Clean up previous test
            CleanupAtlases();

            // Create test texture with known colors
            var testTexture = CreateTestTexture();
            
            // Create atlas and add texture
            var settings = new AtlasSettings
            {
                InitialSize = 256,
                MaxSize = 256,
                Format = AtlasSettings.DefaultFormat,
                FilterMode = FilterMode.Point,
                Readable = true,
                GenerateMipMaps = false,
                Padding = 0
            };

            _testAtlas = new RuntimeAtlas(settings);
            
            // Replace default texture with our test texture
            var atlasType = typeof(RuntimeAtlas);
            var texturesField = atlasType.GetField("_textures", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var textures = texturesField.GetValue(_testAtlas) as System.Collections.Generic.List<Texture2D>;
            Destroy(textures[0]);
            textures[0] = testTexture;

            // Display original
            if (originalDisplay != null)
            {
                originalDisplay.texture = testTexture;
            }

            UpdateStatus("Saving atlas...");

            // Save atlas
            var saveSuccess = await AtlasPersistence.SaveAtlasAsync(_testAtlas, _savePath);
            
            if (!saveSuccess)
            {
                UpdateStatus("❌ Save failed!");
                return;
            }

            UpdateStatus("Loading atlas...");

            // Load atlas
            _loadedAtlas = AtlasPersistence.LoadAtlas(_savePath);
            
            if (_loadedAtlas == null)
            {
                UpdateStatus("❌ Load failed!");
                return;
            }

            // Display loaded
            var loadedTexture = _loadedAtlas.GetTexture(0);
            if (loadedDisplay != null && loadedTexture != null)
            {
                loadedDisplay.texture = loadedTexture;
            }

            // Verify colors
            var result = VerifyColors(testTexture, loadedTexture);
            UpdateStatus(result);
        }

        private Texture2D CreateTestTexture()
        {
            // Create 8x8 texture with distinct color blocks
            var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color32[64];
            
            // Row 0: Pure colors
            pixels[0] = new Color32(255, 0, 0, 255);   // Red
            pixels[1] = new Color32(0, 255, 0, 255);   // Green
            pixels[2] = new Color32(0, 0, 255, 255);   // Blue
            pixels[3] = new Color32(255, 255, 0, 255); // Yellow
            pixels[4] = new Color32(255, 0, 255, 255); // Magenta
            pixels[5] = new Color32(0, 255, 255, 255); // Cyan
            pixels[6] = new Color32(255, 255, 255, 255); // White
            pixels[7] = new Color32(0, 0, 0, 255);     // Black

            // Row 1: Mid-tones
            for (int i = 0; i < 8; i++)
            {
                byte val = (byte)(i * 32);
                pixels[8 + i] = new Color32(val, val, val, 255);
            }

            // Row 2: Alpha test
            for (int i = 0; i < 8; i++)
            {
                byte alpha = (byte)(i * 32);
                pixels[16 + i] = new Color32(128, 128, 128, alpha);
            }

            // Fill remaining rows
            for (int i = 24; i < 64; i++)
            {
                int x = i % 8;
                int y = i / 8;
                byte r = (byte)(x * 32);
                byte g = (byte)(y * 32);
                pixels[i] = new Color32(r, g, 128, 255);
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            
            return tex;
        }

        private string VerifyColors(Texture2D original, Texture2D loaded)
        {
            if (original == null || loaded == null)
            {
                return "❌ Texture is null";
            }

            if (original.width != loaded.width || original.height != loaded.height)
            {
                return $"❌ Size mismatch: {original.width}x{original.height} vs {loaded.width}x{loaded.height}";
            }

            var origPixels = original.GetPixels32();
            var loadPixels = loaded.GetPixels32();

            int perfectMatches = 0;
            int closeMatches = 0;
            int mismatches = 0;
            int maxDiff = 0;

            for (int i = 0; i < origPixels.Length; i++)
            {
                var orig = origPixels[i];
                var load = loadPixels[i];

                int dr = Mathf.Abs(orig.r - load.r);
                int dg = Mathf.Abs(orig.g - load.g);
                int db = Mathf.Abs(orig.b - load.b);
                int da = Mathf.Abs(orig.a - load.a);
                int diff = Mathf.Max(Mathf.Max(dr, dg), Mathf.Max(db, da));

                maxDiff = Mathf.Max(maxDiff, diff);

                if (diff == 0)
                {
                    perfectMatches++;
                }
                else if (diff <= 2)
                {
                    closeMatches++;
                }
                else
                {
                    mismatches++;
                }
            }

            float perfectPercent = (perfectMatches * 100f) / origPixels.Length;
            float closePercent = (closeMatches * 100f) / origPixels.Length;

            if (perfectPercent >= 95f)
            {
                return $"✅ EXCELLENT! {perfectPercent:F1}% perfect matches, max diff: {maxDiff}";
            }
            else if (perfectPercent + closePercent >= 95f)
            {
                return $"✅ GOOD! {perfectPercent:F1}% perfect, {closePercent:F1}% close, max diff: {maxDiff}";
            }
            else
            {
                return $"⚠️ ISSUES: {perfectPercent:F1}% perfect, {closePercent:F1}% close, {mismatches} mismatches, max diff: {maxDiff}";
            }
        }

        private void UpdateStatus(string message)
        {
            Debug.Log($"[ColorTest] {message}");
            if (statusText != null)
            {
                statusText.text = message;
            }
        }

        private void CleanupAtlases()
        {
            if (_testAtlas != null)
            {
                _testAtlas.Dispose();
                _testAtlas = null;
            }

            if (_loadedAtlas != null)
            {
                _loadedAtlas.Dispose();
                _loadedAtlas = null;
            }
        }

        private void OnDestroy()
        {
            CleanupAtlases();
        }
    }
}

