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
    }
}
