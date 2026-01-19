using NUnit.Framework;
using RuntimeAtlasPacker;
using UnityEngine;

namespace Packages.UnityRuntimeAtlas.Tests.Runtime
{
    public class PackingAlgorithmTests
    {
        private const int ATLAS_SIZE = 1024;

        [Test]
        public void MaxRectsAlgorithm_PacksCorrectly()
        {
            using (var packer = new MaxRectsAlgorithm())
            {
                packer.Initialize(ATLAS_SIZE, ATLAS_SIZE);
                Assert.IsTrue(packer.TryPack(100, 100, out var rect1));
                Assert.IsTrue(packer.TryPack(100, 100, out var rect2));
                
                Assert.AreNotEqual(rect1, rect2);
                Assert.IsFalse(RectsOverlap(rect1, rect2));
            }
        }

        [Test]
        public void SkylineAlgorithm_PacksCorrectly()
        {
            using (var packer = new SkylineAlgorithm())
            {
                packer.Initialize(ATLAS_SIZE, ATLAS_SIZE);
                Assert.IsTrue(packer.TryPack(100, 100, out var rect1));
                Assert.IsTrue(packer.TryPack(100, 100, out var rect2));
                
                Assert.AreNotEqual(rect1, rect2);
                Assert.IsFalse(RectsOverlap(rect1, rect2));
            }
        }

        [Test]
        public void GuillotineAlgorithm_PacksCorrectly()
        {
            using (var packer = new GuillotineAlgorithm())
            {
                packer.Initialize(ATLAS_SIZE, ATLAS_SIZE);
                Assert.IsTrue(packer.TryPack(100, 100, out var rect1));
                Assert.IsTrue(packer.TryPack(100, 100, out var rect2));
                
                Assert.AreNotEqual(rect1, rect2);
                Assert.IsFalse(RectsOverlap(rect1, rect2));
            }
        }

        [Test]
        public void ShelfAlgorithm_PacksCorrectly()
        {
            using (var packer = new ShelfAlgorithm())
            {
                packer.Initialize(ATLAS_SIZE, ATLAS_SIZE);
                Assert.IsTrue(packer.TryPack(100, 100, out var rect1));
                Assert.IsTrue(packer.TryPack(100, 100, out var rect2));
                
                Assert.AreNotEqual(rect1, rect2);
                Assert.IsFalse(RectsOverlap(rect1, rect2));
            }
        }

    #if UNITY_2020_1_OR_NEWER
        [Test]
        public void MaxRectsAlgorithm_DisposesNativeCollections()
        {
            // We can't easily check internal state of NativeList managed by Unity's leakage detection in a unit test
            // without reflection or using UnsafeUtility to check pointers.
            // However, if we run this and Unity's leak detection is on settings, it will shout in the console.
            // We can force a check by creating many instances.
            
            for (int i = 0; i < 100; i++)
            {
                var packer = new MaxRectsAlgorithm();
                packer.Initialize(ATLAS_SIZE, ATLAS_SIZE);
                packer.TryPack(100, 100, out _);
                packer.Dispose(); 
            }
            
            // If this test passes without Unity error log about "NativeCollection was not disposed", then it's good.
        }

        [Test]
        public void SkylineAlgorithm_DisposesNativeCollections()
        {
            for (int i = 0; i < 100; i++)
            {
                var packer = new SkylineAlgorithm();
                packer.Initialize(ATLAS_SIZE, ATLAS_SIZE);
                packer.TryPack(100, 100, out _);
                packer.Dispose();
            }
        }
    #endif

        [Test]
        public void MaxRectsAlgorithmNoBurst_PacksCorrectly()
        {
            using (IPackingAlgorithm packer = new MaxRectsAlgorithmNoBurst())
            {
                packer.Initialize(ATLAS_SIZE, ATLAS_SIZE);
                Assert.IsTrue(packer.TryPack(100, 100, out var rect1));
                Assert.IsTrue(packer.TryPack(100, 100, out var rect2));
                
                Assert.AreNotEqual(rect1, rect2);
                Assert.IsFalse(RectsOverlap(rect1, rect2));
            }
        }

        [Test]
        public void Algorithms_Clear_ResetsState()
        {
            // Tests that Clear() effectively resets the packer so it can be reused
            var algorithms = new IPackingAlgorithm[] 
            { 
#if PACKING_BURST_ENABLED
                new MaxRectsAlgorithm(),
#else
                new MaxRectsAlgorithmNoBurst(), /* Fallback or test both? Let's assume NoBurst is available */
#endif
                new MaxRectsAlgorithmNoBurst(), /* Explicitly test NoBurst */
                new SkylineAlgorithm(), 
                new GuillotineAlgorithm(), 
                new ShelfAlgorithm() 
            };

            foreach (var algo in algorithms)
            {
                try
                {
                    algo.Initialize(500, 500);
                    
                    // Pack something big
                    Assert.IsTrue(algo.TryPack(400, 400, out var _), $"{algo.GetType().Name} failed first pack");
                    
                    // Try pack something that wont fit
                    Assert.IsFalse(algo.TryPack(400, 400, out var _), $"{algo.GetType().Name} packed when it shouldn't");
                    
                    // Clear
                    algo.Clear();
                    
                    // Should fit now
                    Assert.IsTrue(algo.TryPack(400, 400, out var _), $"{algo.GetType().Name} failed pack after Clear");
                }
                finally
                {
                    algo.Dispose();
                }
            }
        }

        private bool RectsOverlap(RectInt a, RectInt b)
        {
            return a.x < b.x + b.width && a.x + a.width > b.x &&
                   a.y < b.y + b.height && a.y + a.height > b.y;
        }
    }
}
