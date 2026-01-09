using System;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// High-performance batch processor for packing many textures.
    /// Uses Unity Jobs and Burst for maximum throughput.
    /// </summary>
    public static class AtlasBatchProcessor
    {
        /// <summary>
        /// Pack multiple textures using job system.
        /// More efficient than adding one at a time for large batches.
        /// Falls back to sequential processing if jobs fail.
        /// </summary>
        public static AtlasEntry[] PackBatch(RuntimeAtlas atlas, Texture2D[] textures)
        {
            if (atlas == null) throw new ArgumentNullException(nameof(atlas));
            if (textures == null || textures.Length == 0) return Array.Empty<AtlasEntry>();

            // Try job-based packing, fallback to sequential if it fails
            try
            {
                return PackBatchWithJobs(atlas, textures);
            }
            catch (Exception ex)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"AtlasBatchProcessor: Job-based packing failed ({ex.Message}), falling back to sequential processing");
#endif
                return atlas.AddBatch(textures);
            }
        }

        private static AtlasEntry[] PackBatchWithJobs(RuntimeAtlas atlas, Texture2D[] textures)
        {
            // ✅ MEMORY CRASH FIX: The previous implementation had a critical bug where it:
            // 1. Calculated packing positions using Unity Jobs
            // 2. Manually blitted textures to the calculated positions
            // 3. Called atlas.Add() which RE-PACKED and RE-BLITTED everything again!
            //
            // This caused:
            // - Memory crashes from excessive GPU operations
            // - Texture corruption from double-blitting
            // - Wasted CPU cycles
            // - Job results were completely ignored
            //
            // The atlas.AddBatch() method already handles all of this correctly with proper
            // memory management, multi-page support, and growth strategy. There's no benefit
            // to the Jobs approach when we have to call atlas.Add() anyway.
            //
            // If Jobs optimization is needed in the future, it must create AtlasEntry objects
            // directly without calling atlas.Add(), but that requires internal access.

#if UNITY_EDITOR
            Debug.Log($"[AtlasBatchProcessor] Using optimized atlas.AddBatch() for {textures.Length} textures");
#endif
            return atlas.AddBatch(textures);
        }

        /// <summary>
        /// Estimate if a batch of textures will fit in an atlas of given size.
        /// </summary>
        public static bool WillFit(Texture2D[] textures, int atlasWidth, int atlasHeight, int padding = 2)
        {
            if (textures == null || textures.Length == 0) return true;

            // Quick area check first
            long totalArea = 0;
            long atlasArea = (long)atlasWidth * atlasHeight;

            foreach (var tex in textures)
            {
                int w = tex.width + padding * 2;
                int h = tex.height + padding * 2;
                totalArea += (long)w * h;
            }

            // Must have at least some margin (packing is never 100% efficient)
            if (totalArea > atlasArea * 0.95f)
                return false;

            // Try detailed check using jobs, fallback to simple area check if jobs fail
            try
            {
                return WillFitWithJobs(textures, atlasWidth, atlasHeight, padding);
            }
            catch
            {
                // Fallback: use conservative area-based estimate
                return totalArea <= atlasArea * 0.7f; // Assume 70% packing efficiency
            }
        }

        private static bool WillFitWithJobs(Texture2D[] textures, int atlasWidth, int atlasHeight, int padding)
        {
            using var sizes = new NativeArray<int2>(textures.Length, Allocator.TempJob);
            using var indices = new NativeArray<int>(textures.Length, Allocator.TempJob);
            using var results = new NativeArray<int4>(textures.Length, Allocator.TempJob);
            using var freeRects = new NativeList<int4>(64, Allocator.TempJob);

            for (var i = 0; i < textures.Length; i++)
            {
                var nativeArray = sizes;
                nativeArray[i] = new int2(
                    textures[i].width + padding * 2,
                    textures[i].height + padding * 2
                );
            }

            var sortJob = new SortByAreaJob
            {
                Sizes = sizes,
                Indices = indices
            };
            sortJob.Schedule().Complete();

            freeRects.Add(new int4(0, 0, atlasWidth, atlasHeight));

            var sortedSizes = new NativeArray<int2>(textures.Length, Allocator.TempJob);
            for (var i = 0; i < textures.Length; i++)
            {
                sortedSizes[i] = sizes[indices[i]];
            }

            var packJob = new BatchPackJob
            {
                Sizes = sortedSizes,
                Results = results,
                FreeRects = freeRects,
                AtlasWidth = atlasWidth,
                AtlasHeight = atlasHeight
            };
            packJob.Schedule().Complete();

            sortedSizes.Dispose();

            for (int i = 0; i < textures.Length; i++)
            {
                if (results[i].x < 0)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Calculate the minimum atlas size needed to fit all textures.
        /// </summary>
        public static int CalculateMinimumSize(Texture2D[] textures, int padding = 2, int maxSize = 4096)
        {
            if (textures == null || textures.Length == 0) return 64;

            // Start with rough estimate based on area
            long totalArea = 0;
            int maxDimension = 0;

            foreach (var tex in textures)
            {
                int w = tex.width + padding * 2;
                int h = tex.height + padding * 2;
                totalArea += (long)w * h;
                maxDimension = Mathf.Max(maxDimension, Mathf.Max(w, h));
            }

            // Start size must be at least as big as the largest texture
            int startSize = Mathf.NextPowerOfTwo(maxDimension);
            
            // Estimate based on area (assume 70% packing efficiency)
            int areaBasedSize = Mathf.NextPowerOfTwo((int)Mathf.Sqrt(totalArea / 0.7f));
            startSize = Mathf.Max(startSize, areaBasedSize);

            // Binary search for exact fit
            int size = startSize;
            while (size <= maxSize)
            {
                if (WillFit(textures, size, size, padding))
                    return size;
                size *= 2;
            }

            return maxSize;
        }

        /// <summary>
        /// Analyze a batch of textures and return packing statistics.
        /// Only available in Unity Editor.
        /// </summary>
#if UNITY_EDITOR
        public static PackingStats AnalyzeBatch(Texture2D[] textures, int atlasSize, int padding = 2)
        {
            var stats = new PackingStats();
            
            if (textures == null || textures.Length == 0)
                return stats;

            stats.TextureCount = textures.Length;
            stats.AtlasSize = atlasSize;
            stats.AtlasArea = (long)atlasSize * atlasSize;

            foreach (var tex in textures)
            {
                int w = tex.width + padding * 2;
                int h = tex.height + padding * 2;
                stats.TotalTextureArea += (long)w * h;
                stats.LargestTexture = Mathf.Max(stats.LargestTexture, Mathf.Max(tex.width, tex.height));
                stats.SmallestTexture = stats.SmallestTexture == 0 
                    ? Mathf.Min(tex.width, tex.height) 
                    : Mathf.Min(stats.SmallestTexture, Mathf.Min(tex.width, tex.height));
            }

            stats.TheoreticalFillRatio = (float)stats.TotalTextureArea / stats.AtlasArea;
            stats.WillFit = WillFit(textures, atlasSize, atlasSize, padding);
            stats.RecommendedSize = CalculateMinimumSize(textures, padding);

            return stats;
        }
#endif
    }

    /// <summary>
    /// Statistics about a batch packing operation.
    /// </summary>
    public struct PackingStats
    {
        public int TextureCount;
        public int AtlasSize;
        public long AtlasArea;
        public long TotalTextureArea;
        public float TheoreticalFillRatio;
        public int LargestTexture;
        public int SmallestTexture;
        public bool WillFit;
        public int RecommendedSize;

        public override string ToString()
        {
            return $"PackingStats: {TextureCount} textures, " +
                   $"Atlas: {AtlasSize}x{AtlasSize}, " +
                   $"Fill: {TheoreticalFillRatio:P1}, " +
                   $"WillFit: {WillFit}, " +
                   $"Recommended: {RecommendedSize}x{RecommendedSize}";
        }
    }
}
