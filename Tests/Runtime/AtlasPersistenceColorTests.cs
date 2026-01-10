using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using System.Threading.Tasks;
using System.IO;

namespace RuntimeAtlasPacker.Tests
{
    /// <summary>
    /// Tests for AtlasPersistence color conversion methods.
    /// Verifies that colors are preserved correctly during save/load operations.
    /// </summary>
    public class AtlasPersistenceColorTests
    {
        private string _testDirectory;

        [SetUp]
        public void Setup()
        {
            // Create a temporary test directory
            _testDirectory = Path.Combine(Application.temporaryCachePath, "AtlasColorTests");
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
            Directory.CreateDirectory(_testDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up test directory
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }

        /// <summary>
        /// Test basic 4-pixel color conversion: Red, Green, Blue, Transparent
        /// This is the most fundamental test to verify color channel order.
        /// </summary>
        [Test]
        public async Task TestBasicColorConversion_FourPixels()
        {
            // Use a larger texture to avoid false positives in diagnostic code
            // We'll still test the same 4 pixels in the bottom-left corner
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            // Fill entire texture with a neutral color
            var allPixels = new Color32[16];
            for (int i = 0; i < 16; i++)
            {
                allPixels[i] = new Color32(64, 64, 64, 255); // Dark gray default
            }
            
            // Set 4 test pixels in bottom-left 2x2 area: Red, Green, Blue, Transparent
            allPixels[0] = new Color32(255, 0, 0, 255);   // Pure Red (0, 0)
            allPixels[1] = new Color32(0, 255, 0, 255);   // Pure Green (1, 0)
            allPixels[4] = new Color32(0, 0, 255, 255);   // Pure Blue (0, 1)
            allPixels[5] = new Color32(128, 128, 128, 0); // Gray with Alpha=0 (1, 1)
            
            texture.SetPixels32(allPixels);
            texture.Apply();

            // Create a simple atlas with this texture
            var settings = new AtlasSettings
            {
                InitialSize = 4,
                MaxSize = 4,
                Format = TextureFormat.RGBA32,
                FilterMode = FilterMode.Point,
                Readable = true,
                GenerateMipMaps = false,
                Padding = 0
            };

            var atlas = new RuntimeAtlas(settings);
            
            // Replace the default texture with our test texture (using reflection)
            var atlasType = typeof(RuntimeAtlas);
            var texturesField = atlasType.GetField("_textures", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var textures = texturesField.GetValue(atlas) as System.Collections.Generic.List<Texture2D>;
            Object.DestroyImmediate(textures[0]); // Destroy default texture
            textures[0] = texture;

            // Save the atlas
            var filePath = Path.Combine(_testDirectory, "test_colors");
            var saveSuccess = await AtlasPersistence.SaveAtlasAsync(atlas, filePath);
            Assert.IsTrue(saveSuccess, "Atlas save failed");

            // Load the atlas back
            var loadedAtlas = AtlasPersistence.LoadAtlas(filePath);
            Assert.IsNotNull(loadedAtlas, "Atlas load failed");

            // Get the loaded texture
            var loadedTexture = loadedAtlas.GetTexture(0);
            Assert.IsNotNull(loadedTexture, "Loaded texture is null");
            Assert.AreEqual(4, loadedTexture.width, "Loaded texture width incorrect");
            Assert.AreEqual(4, loadedTexture.height, "Loaded texture height incorrect");

            // Verify each of our 4 test pixels matches exactly
            var loadedPixels = loadedTexture.GetPixels32();
            Assert.AreEqual(16, loadedPixels.Length, "Loaded pixel count incorrect");

            // Check Red pixel (0, 0)
            var redPixel = loadedPixels[0];
            Assert.AreEqual(255, redPixel.r, $"Red pixel R channel incorrect: {redPixel.r}");
            Assert.AreEqual(0, redPixel.g, $"Red pixel G channel incorrect: {redPixel.g}");
            Assert.AreEqual(0, redPixel.b, $"Red pixel B channel incorrect: {redPixel.b}");
            Assert.AreEqual(255, redPixel.a, $"Red pixel A channel incorrect: {redPixel.a}");

            // Check Green pixel (1, 0)
            var greenPixel = loadedPixels[1];
            Assert.AreEqual(0, greenPixel.r, $"Green pixel R channel incorrect: {greenPixel.r}");
            Assert.AreEqual(255, greenPixel.g, $"Green pixel G channel incorrect: {greenPixel.g}");
            Assert.AreEqual(0, greenPixel.b, $"Green pixel B channel incorrect: {greenPixel.b}");
            Assert.AreEqual(255, greenPixel.a, $"Green pixel A channel incorrect: {greenPixel.a}");

            // Check Blue pixel (0, 1) - index 4 in the array
            var bluePixel = loadedPixels[4];
            Assert.AreEqual(0, bluePixel.r, $"Blue pixel R channel incorrect: {bluePixel.r}");
            Assert.AreEqual(0, bluePixel.g, $"Blue pixel G channel incorrect: {bluePixel.g}");
            Assert.AreEqual(255, bluePixel.b, $"Blue pixel B channel incorrect: {bluePixel.b}");
            Assert.AreEqual(255, bluePixel.a, $"Blue pixel A channel incorrect: {bluePixel.a}");

            // Check Transparent pixel (1, 1) - index 5 in the array - allow small tolerance for compression
            var transPixel = loadedPixels[5];
            Assert.That(transPixel.r, Is.InRange(126, 130), $"Transparent pixel R channel incorrect: {transPixel.r}");
            Assert.That(transPixel.g, Is.InRange(126, 130), $"Transparent pixel G channel incorrect: {transPixel.g}");
            Assert.That(transPixel.b, Is.InRange(126, 130), $"Transparent pixel B channel incorrect: {transPixel.b}");
            Assert.That(transPixel.a, Is.InRange(0, 2), $"Transparent pixel A channel incorrect: {transPixel.a}");

            // Cleanup
            loadedAtlas.Dispose();
            atlas.Dispose();
            Object.DestroyImmediate(texture);
        }

        /// <summary>
        /// Test a gradient to verify smooth color transitions are preserved.
        /// </summary>
        [Test]
        public async Task TestColorConversion_Gradient()
        {
            // Create an 8x1 gradient from black to white
            var texture = new Texture2D(8, 1, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color32[8];
            for (int i = 0; i < 8; i++)
            {
                byte value = (byte)(i * 255 / 7); // 0, 36, 73, 109, 146, 182, 219, 255
                pixels[i] = new Color32(value, value, value, 255);
            }
            texture.SetPixels32(pixels);
            texture.Apply();

            // Create atlas and replace texture
            var settings = new AtlasSettings { InitialSize = 8, MaxSize = 8, Format = TextureFormat.RGBA32, Readable = true, Padding = 0 };
            var atlas = new RuntimeAtlas(settings);
            
            var atlasType = typeof(RuntimeAtlas);
            var texturesField = atlasType.GetField("_textures", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var textures = texturesField.GetValue(atlas) as System.Collections.Generic.List<Texture2D>;
            Object.DestroyImmediate(textures[0]);
            textures[0] = texture;

            // Save and load
            var filePath = Path.Combine(_testDirectory, "test_gradient");
            await AtlasPersistence.SaveAtlasAsync(atlas, filePath);
            var loadedAtlas = AtlasPersistence.LoadAtlas(filePath);

            // Verify gradient
            var loadedPixels = loadedAtlas.GetTexture(0).GetPixels32();
            for (int i = 0; i < 8; i++)
            {
                byte expectedValue = (byte)(i * 255 / 7);
                var pixel = loadedPixels[i];
                
                // Allow ±2 tolerance for PNG compression
                Assert.That(pixel.r, Is.InRange(expectedValue - 2, expectedValue + 2), $"Gradient pixel {i} R incorrect");
                Assert.That(pixel.g, Is.InRange(expectedValue - 2, expectedValue + 2), $"Gradient pixel {i} G incorrect");
                Assert.That(pixel.b, Is.InRange(expectedValue - 2, expectedValue + 2), $"Gradient pixel {i} B incorrect");
                Assert.AreEqual(255, pixel.a, $"Gradient pixel {i} A incorrect");
            }

            // Cleanup
            loadedAtlas.Dispose();
            atlas.Dispose();
            Object.DestroyImmediate(texture);
        }

        /// <summary>
        /// Test alpha blending to verify transparency is handled correctly.
        /// </summary>
        [Test]
        public async Task TestColorConversion_AlphaBlending()
        {
            // Create a 4x1 texture with varying alpha values
            var texture = new Texture2D(4, 1, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color32[]
            {
                new Color32(255, 0, 0, 255),   // Fully opaque red
                new Color32(255, 0, 0, 192),   // 75% opaque red
                new Color32(255, 0, 0, 128),   // 50% opaque red
                new Color32(255, 0, 0, 64)     // 25% opaque red
            };
            texture.SetPixels32(pixels);
            texture.Apply();

            // Create atlas
            var settings = new AtlasSettings { InitialSize = 4, MaxSize = 4, Format = TextureFormat.RGBA32, Readable = true, Padding = 0 };
            var atlas = new RuntimeAtlas(settings);
            
            var atlasType = typeof(RuntimeAtlas);
            var texturesField = atlasType.GetField("_textures", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var textures = texturesField.GetValue(atlas) as System.Collections.Generic.List<Texture2D>;
            Object.DestroyImmediate(textures[0]);
            textures[0] = texture;

            // Save and load
            var filePath = Path.Combine(_testDirectory, "test_alpha");
            await AtlasPersistence.SaveAtlasAsync(atlas, filePath);
            var loadedAtlas = AtlasPersistence.LoadAtlas(filePath);

            // Verify alpha values
            var loadedPixels = loadedAtlas.GetTexture(0).GetPixels32();
            var expectedAlphas = new byte[] { 255, 192, 128, 64 };
            
            for (int i = 0; i < 4; i++)
            {
                var pixel = loadedPixels[i];
                Assert.AreEqual(255, pixel.r, $"Alpha test pixel {i} R incorrect");
                Assert.AreEqual(0, pixel.g, $"Alpha test pixel {i} G incorrect");
                Assert.AreEqual(0, pixel.b, $"Alpha test pixel {i} B incorrect");
                Assert.That(pixel.a, Is.InRange(expectedAlphas[i] - 2, expectedAlphas[i] + 2), $"Alpha test pixel {i} A incorrect");
            }

            // Cleanup
            loadedAtlas.Dispose();
            atlas.Dispose();
            Object.DestroyImmediate(texture);
        }

        /// <summary>
        /// Test edge cases: pure white, pure black, mid-gray.
        /// </summary>
        [Test]
        public async Task TestColorConversion_EdgeCases()
        {
            var texture = new Texture2D(3, 1, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;

            var pixels = new Color32[]
            {
                new Color32(0, 0, 0, 255),       // Pure black
                new Color32(128, 128, 128, 255), // Mid gray
                new Color32(255, 255, 255, 255)  // Pure white
            };
            texture.SetPixels32(pixels);
            texture.Apply();

            var settings = new AtlasSettings { InitialSize = 3, MaxSize = 3, Format = TextureFormat.RGBA32, Readable = true, Padding = 0 };
            var atlas = new RuntimeAtlas(settings);
            
            var atlasType = typeof(RuntimeAtlas);
            var texturesField = atlasType.GetField("_textures", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var textures = texturesField.GetValue(atlas) as System.Collections.Generic.List<Texture2D>;
            Object.DestroyImmediate(textures[0]);
            textures[0] = texture;

            var filePath = Path.Combine(_testDirectory, "test_edges");
            await AtlasPersistence.SaveAtlasAsync(atlas, filePath);
            var loadedAtlas = AtlasPersistence.LoadAtlas(filePath);

            var loadedPixels = loadedAtlas.GetTexture(0).GetPixels32();

            // Black
            Assert.That(loadedPixels[0].r, Is.InRange(0, 2), "Black R incorrect");
            Assert.That(loadedPixels[0].g, Is.InRange(0, 2), "Black G incorrect");
            Assert.That(loadedPixels[0].b, Is.InRange(0, 2), "Black B incorrect");

            // Gray
            Assert.That(loadedPixels[1].r, Is.InRange(126, 130), "Gray R incorrect");
            Assert.That(loadedPixels[1].g, Is.InRange(126, 130), "Gray G incorrect");
            Assert.That(loadedPixels[1].b, Is.InRange(126, 130), "Gray B incorrect");

            // White
            Assert.That(loadedPixels[2].r, Is.InRange(253, 255), "White R incorrect");
            Assert.That(loadedPixels[2].g, Is.InRange(253, 255), "White G incorrect");
            Assert.That(loadedPixels[2].b, Is.InRange(253, 255), "White B incorrect");

            loadedAtlas.Dispose();
            atlas.Dispose();
            Object.DestroyImmediate(texture);
        }

        /// <summary>
        /// Test that color channels don't get swapped (RGB vs BGR).
        /// </summary>
        [Test]
        public async Task TestColorConversion_NoChannelSwap()
        {
            var texture = new Texture2D(3, 1, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;

            // Test specific color combinations that would be wrong if channels are swapped
            var pixels = new Color32[]
            {
                new Color32(200, 50, 25, 255),   // Mostly red
                new Color32(25, 200, 50, 255),   // Mostly green
                new Color32(50, 25, 200, 255)    // Mostly blue
            };
            texture.SetPixels32(pixels);
            texture.Apply();

            var settings = new AtlasSettings { InitialSize = 3, MaxSize = 3, Format = TextureFormat.RGBA32, Readable = true, Padding = 0 };
            var atlas = new RuntimeAtlas(settings);
            
            var atlasType = typeof(RuntimeAtlas);
            var texturesField = atlasType.GetField("_textures", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var textures = texturesField.GetValue(atlas) as System.Collections.Generic.List<Texture2D>;
            Object.DestroyImmediate(textures[0]);
            textures[0] = texture;

            var filePath = Path.Combine(_testDirectory, "test_channels");
            await AtlasPersistence.SaveAtlasAsync(atlas, filePath);
            var loadedAtlas = AtlasPersistence.LoadAtlas(filePath);

            var loadedPixels = loadedAtlas.GetTexture(0).GetPixels32();

            // Verify red-dominant pixel
            Assert.That(loadedPixels[0].r, Is.GreaterThan(loadedPixels[0].g), "Red pixel R not greater than G");
            Assert.That(loadedPixels[0].r, Is.GreaterThan(loadedPixels[0].b), "Red pixel R not greater than B");

            // Verify green-dominant pixel
            Assert.That(loadedPixels[1].g, Is.GreaterThan(loadedPixels[1].r), "Green pixel G not greater than R");
            Assert.That(loadedPixels[1].g, Is.GreaterThan(loadedPixels[1].b), "Green pixel G not greater than B");

            // Verify blue-dominant pixel
            Assert.That(loadedPixels[2].b, Is.GreaterThan(loadedPixels[2].r), "Blue pixel B not greater than R");
            Assert.That(loadedPixels[2].b, Is.GreaterThan(loadedPixels[2].g), "Blue pixel B not greater than G");

            loadedAtlas.Dispose();
            atlas.Dispose();
            Object.DestroyImmediate(texture);
        }
    }
}

