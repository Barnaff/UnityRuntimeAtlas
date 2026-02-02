using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using RuntimeAtlasPacker;
using UnityEngine;

namespace Packages.UnityRuntimeAtlas.Tests.Runtime
{
    public class AtlasPersistenceTests
    {
        private string _testDirectory;
        private string _testFilePath;

        [SetUp]
        public void Setup()
        {
            _testDirectory = Path.Combine(Application.temporaryCachePath, "AtlasPersistenceTests");
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
            Directory.CreateDirectory(_testDirectory);
            _testFilePath = Path.Combine(_testDirectory, "test_atlas");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }

        [Test]
        public async Task SaveAndLoad_PreservesAtlasState()
        {
            using (var atlas = new RuntimeAtlas(new AtlasSettings 
            { 
                InitialSize = 256, 
                MaxSize = 512, 
                Readable = true,
                Format = TextureFormat.RGBA32 // Ensure reliable format for testing
            }))
            {
                // Create a texture to add
                var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                var colors = new Color32[32 * 32];
                for (int i = 0; i < colors.Length; i++) colors[i] = Color.red;
                texture.SetPixels32(colors);
                texture.Apply();
                texture.name = "RedSquare";

                // Add to atlas
                var (result, _) = atlas.Add(texture);
                Assert.AreEqual(AddResult.Success, result);
                
                // Cleanup source
                Object.Destroy(texture);

                // Save
                var success = await atlas.SaveAsync(_testFilePath);
                Assert.IsTrue(success, "Save should succeed");
            }

            // Verify file exists
            Assert.IsTrue(RuntimeAtlas.Exists(_testFilePath), "Atlas file should exist");

            // Load
            var loadedAtlas = await RuntimeAtlas.LoadAsync(_testFilePath);
            
            using (loadedAtlas)
            {
                Assert.IsNotNull(loadedAtlas, "Loaded atlas should not be null");
                Assert.AreEqual(1, loadedAtlas.EntryCount, "Should have 1 entry");
                
                var entry = loadedAtlas.GetEntryByName("RedSquare");
                Assert.IsNotNull(entry, "Should find entry by name");
                
                Assert.IsNotNull(loadedAtlas.Texture, "Loaded atlas should have a texture");
                Assert.AreEqual(256, loadedAtlas.Texture.width, "Width should match");
            }
        }

        [Test]
        public async Task Save_WithNonReadableAtlas_Succeeds()
        {
            using (var atlas = new RuntimeAtlas(new AtlasSettings 
            { 
                Readable = false, // !
                Format = TextureFormat.RGBA32
            }))
            {
                var texture = new Texture2D(32, 32);
                texture.name = "Test";
                atlas.Add(texture);
                Object.Destroy(texture);

                // SaveAsync handles ReadPixels internally for non-readable textures
                var success = await atlas.SaveAsync(_testFilePath);
                Assert.IsTrue(success);
            }
            
            // Verify loading
            var loaded = await RuntimeAtlas.LoadAsync(_testFilePath);
            
            using (loaded)
            {
                Assert.IsNotNull(loaded);
                Assert.AreEqual(1, loaded.EntryCount);
            }
        }

        [Test]
        public void Persistence_MemoryCheck_Disposal()
        {
            // This test verifies that saving/loading doesn't leave lingering resources
            // We can't strictly count memory but we can verify execution flow
            
            for (int i = 0; i < 5; i++)
            {
                // Create, Save, Load, Dispose loop
                SaveAndLoadLoop(i);
            }
        }

        private void SaveAndLoadLoop(int iteration)
        {
            var path = Path.Combine(_testDirectory, $"iter_{iteration}");
            
            using (var atlas = new RuntimeAtlas())
            {
                var tex = new Texture2D(64, 64);
                // Initialize checks with color to ensure it's not blank
                var pixels = new Color32[64 * 64];
                for(int i=0; i<pixels.Length; i++) pixels[i] = Color.blue;
                tex.SetPixels32(pixels);
                tex.Apply();
                
                tex.name = $"tex_{iteration}";
                atlas.Add(tex);
                Object.DestroyImmediate(tex);
                
                // Synchronous save
                atlas.Save(path);
            }
            
            // Load and immediately dispose
            var loaded = RuntimeAtlas.Load(path);
            Assert.IsNotNull(loaded);
            loaded.Dispose();
            
            // If native collections leaked, Unity would complain in console
        }

        [Test]
        public async Task LoadedAtlas_HasNonEmptySprites()
        {
            // Create and save an atlas with a red square
            using (var atlas = new RuntimeAtlas(new AtlasSettings 
            { 
                InitialSize = 256, 
                MaxSize = 512, 
                Readable = true,
                Format = TextureFormat.RGBA32
            }))
            {
                var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                var colors = new Color32[32 * 32];
                for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(255, 0, 0, 255); // Red
                texture.SetPixels32(colors);
                texture.Apply();
                texture.name = "RedSquare";

                var (result, _) = atlas.Add(texture);
                Assert.AreEqual(AddResult.Success, result);
                Object.Destroy(texture);

                var success = await atlas.SaveAsync(_testFilePath);
                Assert.IsTrue(success, "Save should succeed");
            }

            // Load the atlas
            var loadedAtlas = await RuntimeAtlas.LoadAsync(_testFilePath);
            
            using (loadedAtlas)
            {
                Assert.IsNotNull(loadedAtlas, "Loaded atlas should not be null");
                Assert.AreEqual(1, loadedAtlas.EntryCount, "Should have 1 entry");
                
                var entry = loadedAtlas.GetEntryByName("RedSquare");
                Assert.IsNotNull(entry, "Should find entry by name");
                
                // Create sprite from entry
                var sprite = entry.CreateSprite();
                Assert.IsNotNull(sprite, "Should be able to create sprite from entry");
                
                // Verify sprite has valid texture
                Assert.IsNotNull(sprite.texture, "Sprite should have a texture");
                Assert.Greater(sprite.texture.width, 0, "Sprite texture should have width");
                Assert.Greater(sprite.texture.height, 0, "Sprite texture should have height");
                
                // Verify sprite is not blank by checking pixels
                var atlasTexture = loadedAtlas.GetTexture(entry.TextureIndex);
                Assert.IsNotNull(atlasTexture, "Atlas texture should exist");
                Assert.IsTrue(atlasTexture.isReadable, "Atlas texture should be readable");
                
                // Sample a pixel from the sprite's rect
                var pixelX = entry.Rect.x + entry.Rect.width / 2;
                var pixelY = entry.Rect.y + entry.Rect.height / 2;
                var pixelColor = atlasTexture.GetPixel(pixelX, pixelY);
                
                // Should be red (or close to red)
                Assert.Greater(pixelColor.r, 0.5f, "Pixel should be red (r > 0.5)");
                Assert.Less(pixelColor.g, 0.5f, "Pixel should be red (g < 0.5)");
                Assert.Less(pixelColor.b, 0.5f, "Pixel should be red (b < 0.5)");
                Assert.Greater(pixelColor.a, 0.5f, "Pixel should be opaque (a > 0.5)");
                
                Object.Destroy(sprite);
            }
        }

        [Test]
        public async Task LoadSaveLoad_PreservesAllEntries()
        {
            // Test the specific issue: Load atlas, add more sprites, save, verify nothing is lost
            
            // Step 1: Create initial atlas with 2 sprites
            using (var atlas = new RuntimeAtlas(new AtlasSettings 
            { 
                InitialSize = 256, 
                MaxSize = 512, 
                Readable = true,
                Format = TextureFormat.RGBA32
            }))
            {
                // Add first sprite (red)
                var tex1 = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                var colors1 = new Color32[32 * 32];
                for (int i = 0; i < colors1.Length; i++) colors1[i] = new Color32(255, 0, 0, 255);
                tex1.SetPixels32(colors1);
                tex1.Apply();
                tex1.name = "RedSquare";
                
                var (result1, _) = atlas.Add(tex1);
                Assert.AreEqual(AddResult.Success, result1);
                Object.Destroy(tex1);

                // Add second sprite (green)
                var tex2 = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                var colors2 = new Color32[32 * 32];
                for (int i = 0; i < colors2.Length; i++) colors2[i] = new Color32(0, 255, 0, 255);
                tex2.SetPixels32(colors2);
                tex2.Apply();
                tex2.name = "GreenSquare";
                
                var (result2, _) = atlas.Add(tex2);
                Assert.AreEqual(AddResult.Success, result2);
                Object.Destroy(tex2);

                Assert.AreEqual(2, atlas.EntryCount, "Should have 2 entries before save");
                
                var success = await atlas.SaveAsync(_testFilePath);
                Assert.IsTrue(success, "Initial save should succeed");
            }

            // Step 2: Load atlas
            var loadedAtlas = await RuntimeAtlas.LoadAsync(_testFilePath);
            Assert.IsNotNull(loadedAtlas, "Loaded atlas should not be null");
            Assert.AreEqual(2, loadedAtlas.EntryCount, "Should have 2 entries after load");
            
            var loadedEntry1 = loadedAtlas.GetEntryByName("RedSquare");
            var loadedEntry2 = loadedAtlas.GetEntryByName("GreenSquare");
            Assert.IsNotNull(loadedEntry1, "Should find RedSquare after load");
            Assert.IsNotNull(loadedEntry2, "Should find GreenSquare after load");

            // Step 3: Add a third sprite to the loaded atlas
            var tex3 = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            var colors3 = new Color32[32 * 32];
            for (int i = 0; i < colors3.Length; i++) colors3[i] = new Color32(0, 0, 255, 255);
            tex3.SetPixels32(colors3);
            tex3.Apply();
            tex3.name = "BlueSquare";
            
            var (result3, _) = loadedAtlas.Add(tex3);
            Assert.AreEqual(AddResult.Success, result3, "Should be able to add to loaded atlas");
            Object.Destroy(tex3);
            
            Assert.AreEqual(3, loadedAtlas.EntryCount, "Should have 3 entries after adding");

            // Step 4: Save again
            var savePath2 = Path.Combine(_testDirectory, "test_atlas_2");
            var success2 = await loadedAtlas.SaveAsync(savePath2);
            Assert.IsTrue(success2, "Second save should succeed");
            
            loadedAtlas.Dispose();

            // Step 5: Load again and verify all 3 entries are present
            var finalAtlas = await RuntimeAtlas.LoadAsync(savePath2);
            
            using (finalAtlas)
            {
                Assert.IsNotNull(finalAtlas, "Final loaded atlas should not be null");
                Assert.AreEqual(3, finalAtlas.EntryCount, "Should have ALL 3 entries in final atlas");
                
                var finalEntry1 = finalAtlas.GetEntryByName("RedSquare");
                var finalEntry2 = finalAtlas.GetEntryByName("GreenSquare");
                var finalEntry3 = finalAtlas.GetEntryByName("BlueSquare");
                
                Assert.IsNotNull(finalEntry1, "Should find RedSquare in final atlas");
                Assert.IsNotNull(finalEntry2, "Should find GreenSquare in final atlas");
                Assert.IsNotNull(finalEntry3, "Should find BlueSquare in final atlas");
                
                // Verify sprites can be created and are not blank
                var sprite1 = finalEntry1.CreateSprite();
                var sprite2 = finalEntry2.CreateSprite();
                var sprite3 = finalEntry3.CreateSprite();
                
                Assert.IsNotNull(sprite1, "Should create sprite for RedSquare");
                Assert.IsNotNull(sprite2, "Should create sprite for GreenSquare");
                Assert.IsNotNull(sprite3, "Should create sprite for BlueSquare");
                
                // Verify pixel data is correct for each sprite
                var atlasTexture = finalAtlas.GetTexture(finalEntry1.TextureIndex);
                
                // Check red sprite
                var red = atlasTexture.GetPixel(
                    finalEntry1.Rect.x + finalEntry1.Rect.width / 2,
                    finalEntry1.Rect.y + finalEntry1.Rect.height / 2
                );
                Assert.Greater(red.r, 0.5f, "RedSquare should have red pixels");
                
                // Check green sprite
                atlasTexture = finalAtlas.GetTexture(finalEntry2.TextureIndex);
                var green = atlasTexture.GetPixel(
                    finalEntry2.Rect.x + finalEntry2.Rect.width / 2,
                    finalEntry2.Rect.y + finalEntry2.Rect.height / 2
                );
                Assert.Greater(green.g, 0.5f, "GreenSquare should have green pixels");
                
                // Check blue sprite
                atlasTexture = finalAtlas.GetTexture(finalEntry3.TextureIndex);
                var blue = atlasTexture.GetPixel(
                    finalEntry3.Rect.x + finalEntry3.Rect.width / 2,
                    finalEntry3.Rect.y + finalEntry3.Rect.height / 2
                );
                Assert.Greater(blue.b, 0.5f, "BlueSquare should have blue pixels");
                
                Object.Destroy(sprite1);
                Object.Destroy(sprite2);
                Object.Destroy(sprite3);
            }
        }

        [Test]
        public async Task MultipleLoadSaveCycles_PreservesAllData()
        {
            // Test multiple load-modify-save cycles
            var basePath = _testFilePath;
            
            // Cycle 1: Create with 1 sprite
            using (var atlas = new RuntimeAtlas(new AtlasSettings 
            { 
                InitialSize = 256, 
                Readable = true,
                Format = TextureFormat.RGBA32
            }))
            {
                var tex = CreateColoredTexture(32, 32, Color.red, "Sprite1");
                atlas.Add(tex);
                Object.Destroy(tex);
                await atlas.SaveAsync(basePath);
            }

            // Cycle 2: Load, add 1 more, save
            using (var atlas = await RuntimeAtlas.LoadAsync(basePath))
            {
                Assert.AreEqual(1, atlas.EntryCount);
                var tex = CreateColoredTexture(32, 32, Color.green, "Sprite2");
                atlas.Add(tex);
                Object.Destroy(tex);
                Assert.AreEqual(2, atlas.EntryCount);
                await atlas.SaveAsync(basePath);
            }

            // Cycle 3: Load, add 1 more, save
            using (var atlas = await RuntimeAtlas.LoadAsync(basePath))
            {
                Assert.AreEqual(2, atlas.EntryCount, "Should have 2 sprites from previous cycle");
                var tex = CreateColoredTexture(32, 32, Color.blue, "Sprite3");
                atlas.Add(tex);
                Object.Destroy(tex);
                Assert.AreEqual(3, atlas.EntryCount);
                await atlas.SaveAsync(basePath);
            }

            // Final verification: Load and check all 3 sprites
            using (var atlas = await RuntimeAtlas.LoadAsync(basePath))
            {
                Assert.AreEqual(3, atlas.EntryCount, "Should have all 3 sprites");
                Assert.IsNotNull(atlas.GetEntryByName("Sprite1"));
                Assert.IsNotNull(atlas.GetEntryByName("Sprite2"));
                Assert.IsNotNull(atlas.GetEntryByName("Sprite3"));
            }
        }

        [Test]
        public async Task SavedAtlas_EntryDataIntegrity()
        {
            // Verify that all entry metadata is preserved correctly
            var spriteBorder = new Vector4(1, 2, 3, 4);
            var spritePivot = new Vector2(0.25f, 0.75f);
            var pixelsPerUnit = 50f;
            var spriteVersion = 5;
            
            // Create and save
            using (var atlas = new RuntimeAtlas(new AtlasSettings 
            { 
                InitialSize = 256, 
                Readable = true,
                Format = TextureFormat.RGBA32
            }))
            {
                var tex = CreateColoredTexture(32, 32, Color.cyan, "DetailedSprite");
                var (result, _) = atlas.Add(tex, spriteBorder, spritePivot, pixelsPerUnit, spriteVersion);
                Assert.AreEqual(AddResult.Success, result);
                Object.Destroy(tex);
                
                await atlas.SaveAsync(_testFilePath);
            }

            // Load and verify metadata
            using (var atlas = await RuntimeAtlas.LoadAsync(_testFilePath))
            {
                var entry = atlas.GetEntryByName("DetailedSprite");
                Assert.IsNotNull(entry);
                
                Assert.AreEqual("DetailedSprite", entry.Name);
                Assert.AreEqual(spriteBorder, entry.Border, "Border should be preserved");
                Assert.AreEqual(spritePivot, entry.Pivot, "Pivot should be preserved");
                Assert.AreEqual(pixelsPerUnit, entry.PixelsPerUnit, "PixelsPerUnit should be preserved");
                Assert.AreEqual(spriteVersion, entry.SpriteVersion, "SpriteVersion should be preserved");
            }
        }

        [Test]
        public async Task LoadedAtlas_CanRemoveAndAddSprites()
        {
            // Create initial atlas with 2 sprites
            using (var atlas = new RuntimeAtlas(new AtlasSettings 
            { 
                InitialSize = 256, 
                Readable = true,
                Format = TextureFormat.RGBA32
            }))
            {
                var tex1 = CreateColoredTexture(32, 32, Color.red, "ToRemove");
                var tex2 = CreateColoredTexture(32, 32, Color.green, "ToKeep");
                atlas.Add(tex1);
                atlas.Add(tex2);
                Object.Destroy(tex1);
                Object.Destroy(tex2);
                await atlas.SaveAsync(_testFilePath);
            }

            // Load, remove one, add one, save
            using (var atlas = await RuntimeAtlas.LoadAsync(_testFilePath))
            {
                Assert.AreEqual(2, atlas.EntryCount);
                
                var toRemove = atlas.GetEntryByName("ToRemove");
                atlas.RemoveById(toRemove.Id);
                
                var tex3 = CreateColoredTexture(32, 32, Color.blue, "NewSprite");
                atlas.Add(tex3);
                Object.Destroy(tex3);
                
                Assert.AreEqual(2, atlas.EntryCount);
                await atlas.SaveAsync(_testFilePath);
            }

            // Verify final state
            using (var atlas = await RuntimeAtlas.LoadAsync(_testFilePath))
            {
                Assert.AreEqual(2, atlas.EntryCount);
                Assert.IsNull(atlas.GetEntryByName("ToRemove"));
                Assert.IsNotNull(atlas.GetEntryByName("ToKeep"));
                Assert.IsNotNull(atlas.GetEntryByName("NewSprite"));
            }
        }

        private Texture2D CreateColoredTexture(int width, int height, Color color, string name)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var colors = new Color32[width * height];
            var color32 = new Color32(
                (byte)(color.r * 255),
                (byte)(color.g * 255),
                (byte)(color.b * 255),
                (byte)(color.a * 255)
            );
            for (int i = 0; i < colors.Length; i++) colors[i] = color32;
            tex.SetPixels32(colors);
            tex.Apply();
            tex.name = name;
            return tex;
        }
    }
}
