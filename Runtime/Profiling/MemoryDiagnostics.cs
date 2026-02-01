using System;
using UnityEngine;
using UnityEngine.Profiling;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// Memory diagnostics utility for debugging iOS memory crashes.
    /// Provides detailed memory state logging for texture operations.
    /// </summary>
    public static class MemoryDiagnostics
    {
        /// <summary>
        /// Log comprehensive memory state information.
        /// Call this before and after suspicious operations to track memory changes.
        /// </summary>
        /// <param name="context">Description of what operation is being performed</param>
        public static void LogMemoryState(string context)
        {
            Debug.Log($"[MemoryDiagnostics] ========== {context} ==========");

            // Managed memory (GC heap)
            var managedMemory = GC.GetTotalMemory(false);
            Debug.Log($"[MemoryDiagnostics] Managed Memory: {managedMemory / 1024 / 1024} MB ({managedMemory:N0} bytes)");

#if UNITY_2020_1_OR_NEWER
            // Unity Profiler memory stats (includes native memory)
            var totalAllocated = Profiler.GetTotalAllocatedMemoryLong();
            var totalReserved = Profiler.GetTotalReservedMemoryLong();
            var totalUnused = Profiler.GetTotalUnusedReservedMemoryLong();

            Debug.Log($"[MemoryDiagnostics] Total Allocated: {totalAllocated / 1024 / 1024} MB");
            Debug.Log($"[MemoryDiagnostics] Total Reserved: {totalReserved / 1024 / 1024} MB");
            Debug.Log($"[MemoryDiagnostics] Total Unused: {totalUnused / 1024 / 1024} MB");

            // Graphics memory (textures, render textures, etc.)
            var graphicsMemory = Profiler.GetAllocatedMemoryForGraphicsDriver();
            Debug.Log($"[MemoryDiagnostics] Graphics Memory: {graphicsMemory / 1024 / 1024} MB");

            // Mono heap size
            var monoHeap = Profiler.GetMonoHeapSizeLong();
            var monoUsed = Profiler.GetMonoUsedSizeLong();
            Debug.Log($"[MemoryDiagnostics] Mono Heap: {monoHeap / 1024 / 1024} MB (Used: {monoUsed / 1024 / 1024} MB)");
#endif

#if UNITY_IOS && !UNITY_EDITOR
            // iOS-specific: Try to get system memory info
            Debug.Log($"[MemoryDiagnostics] Platform: iOS Device");
            Debug.Log($"[MemoryDiagnostics] SystemInfo.systemMemorySize: {SystemInfo.systemMemorySize} MB");
            Debug.Log($"[MemoryDiagnostics] SystemInfo.graphicsMemorySize: {SystemInfo.graphicsMemorySize} MB");
#endif

            Debug.Log($"[MemoryDiagnostics] ==========================================");
        }

        /// <summary>
        /// Log detailed texture information for debugging.
        /// </summary>
        public static void LogTextureInfo(Texture2D texture, string label = "Texture")
        {
            if (texture == null)
            {
                Debug.Log($"[MemoryDiagnostics] {label}: NULL");
                return;
            }

            var estimatedSize = EstimateTextureMemory(texture);

            Debug.Log($"[MemoryDiagnostics] {label} Info:");
            Debug.Log($"  - Name: {texture.name}");
            Debug.Log($"  - Size: {texture.width}x{texture.height}");
            Debug.Log($"  - Format: {texture.format}");
            Debug.Log($"  - Readable: {texture.isReadable}");
            Debug.Log($"  - MipMap Count: {texture.mipmapCount}");
            Debug.Log($"  - Filter Mode: {texture.filterMode}");
            Debug.Log($"  - Estimated Memory: {estimatedSize / 1024 / 1024:F2} MB ({estimatedSize:N0} bytes)");
        }

        /// <summary>
        /// Log detailed RenderTexture information for debugging.
        /// </summary>
        public static void LogRenderTextureInfo(RenderTexture rt, string label = "RenderTexture")
        {
            if (rt == null)
            {
                Debug.Log($"[MemoryDiagnostics] {label}: NULL");
                return;
            }

            var estimatedSize = rt.width * rt.height * 4; // ARGB32 estimate

            Debug.Log($"[MemoryDiagnostics] {label} Info:");
            Debug.Log($"  - Name: {rt.name}");
            Debug.Log($"  - Size: {rt.width}x{rt.height}");
            Debug.Log($"  - Format: {rt.format}");
            Debug.Log($"  - Depth: {rt.depth}");
            Debug.Log($"  - IsCreated: {rt.IsCreated()}");
            Debug.Log($"  - Dimension: {rt.dimension}");
            Debug.Log($"  - Estimated Memory: {estimatedSize / 1024 / 1024:F2} MB ({estimatedSize:N0} bytes)");
        }

        /// <summary>
        /// Estimate memory usage of a texture in bytes.
        /// </summary>
        public static long EstimateTextureMemory(Texture2D texture)
        {
            if (texture == null) return 0;

            int bpp = GetBitsPerPixel(texture.format);
            long baseSize = (long)texture.width * texture.height * bpp / 8;

            // Account for mipmaps (adds ~33% if enabled)
            if (texture.mipmapCount > 1)
            {
                baseSize = (long)(baseSize * 1.33f);
            }

            // If readable, CPU copy doubles memory
            if (texture.isReadable)
            {
                baseSize *= 2;
            }

            return baseSize;
        }

        /// <summary>
        /// Get bits per pixel for common texture formats.
        /// </summary>
        private static int GetBitsPerPixel(TextureFormat format)
        {
            switch (format)
            {
                case TextureFormat.RGBA32:
                case TextureFormat.ARGB32:
                case TextureFormat.BGRA32:
                    return 32;
                case TextureFormat.RGB24:
                    return 24;
                case TextureFormat.RGB565:
                case TextureFormat.RGBA4444:
                case TextureFormat.ARGB4444:
                    return 16;
                case TextureFormat.Alpha8:
                case TextureFormat.R8:
                    return 8;
                case TextureFormat.RGBAHalf:
                    return 64;
                case TextureFormat.RGBAFloat:
                    return 128;
                case TextureFormat.PVRTC_RGB2:
                case TextureFormat.PVRTC_RGBA2:
                    return 2;
                case TextureFormat.PVRTC_RGB4:
                case TextureFormat.PVRTC_RGBA4:
                    return 4;
                case TextureFormat.ETC_RGB4:
                case TextureFormat.ETC2_RGB:
                    return 4;
                case TextureFormat.ETC2_RGBA8:
                    return 8;
                case TextureFormat.ASTC_4x4:
                case TextureFormat.ASTC_5x5:
                case TextureFormat.ASTC_6x6:
                case TextureFormat.ASTC_8x8:
                case TextureFormat.ASTC_10x10:
                case TextureFormat.ASTC_12x12:
                    return 8; // Varies but 8 is common
                default:
                    return 32; // Default assumption
            }
        }

        /// <summary>
        /// Force garbage collection and log memory change.
        /// Use sparingly as GC is expensive.
        /// </summary>
        public static void ForceGCAndLog(string context)
        {
            var before = GC.GetTotalMemory(false);

            Debug.Log($"[MemoryDiagnostics] Forcing GC... ({context})");

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var after = GC.GetTotalMemory(true);
            var freed = before - after;

            Debug.Log($"[MemoryDiagnostics] GC Complete: {before / 1024 / 1024} MB -> {after / 1024 / 1024} MB (freed {freed / 1024 / 1024} MB)");
        }

        /// <summary>
        /// Check if memory is critically low (iOS threshold).
        /// Returns true if action should be taken to free memory.
        /// </summary>
        public static bool IsMemoryCritical()
        {
#if UNITY_2020_1_OR_NEWER
            var totalAllocated = Profiler.GetTotalAllocatedMemoryLong();
            var threshold = 800L * 1024 * 1024; // 800 MB threshold for iOS

            if (totalAllocated > threshold)
            {
                Debug.LogWarning($"[MemoryDiagnostics] CRITICAL: Memory usage {totalAllocated / 1024 / 1024} MB exceeds threshold {threshold / 1024 / 1024} MB!");
                return true;
            }
#endif
            return false;
        }
    }
}
