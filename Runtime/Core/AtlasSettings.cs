using UnityEngine;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// Configuration settings for runtime atlas generation.
    /// </summary>
    [System.Serializable]
    public struct AtlasSettings
    {
        /// <summary>
        /// Default texture format for all atlases.
        /// ARGB32 provides best quality with full alpha support and is compatible with all platforms.
        /// 4 bytes per pixel (32-bit ARGB).
        /// Change this constant to modify the default format across the entire package.
        /// </summary>
        public const TextureFormat DefaultFormat = TextureFormat.ARGB32;
        
        /// <summary>Initial atlas size. Will grow if needed.</summary>
        public int InitialSize;
        
        /// <summary>Maximum atlas size. Atlas won't grow beyond this.</summary>
        public int MaxSize;
        
        /// <summary>Padding between sprites in pixels.</summary>
        public int Padding;
        
        /// <summary>Texture format for the atlas.</summary>
        public TextureFormat Format;
        
        /// <summary>Filter mode for the atlas texture.</summary>
        public FilterMode FilterMode;
        
        /// <summary>Whether to use mipmaps.</summary>
        public bool GenerateMipMaps;
        
        /// <summary>
        /// Whether to make texture readable (required for CPU access but uses more memory).
        /// Default: true for safety and backwards compatibility.
        /// Set to false for mobile devices to save 50% memory.
        /// </summary>
        public bool Readable;
        
        /// <summary>
        /// Whether to use RenderTexture for clearing and blitting operations.
        /// When true: Uses GPU-accelerated RenderTexture operations (better for large textures, non-readable textures).
        /// When false: Uses direct CPU pixel operations (better for small textures, debugging).
        /// Default: true for better performance and mobile compatibility.
        /// Note: Must be true when Readable is false.
        /// </summary>
        public bool UseRenderTextures;
        
        /// <summary>Growth strategy when atlas is full.</summary>
        public GrowthStrategy GrowthStrategy;
        
        /// <summary>Packing algorithm to use.</summary>
        public PackingAlgorithm Algorithm;
        
        /// <summary>Maximum number of texture pages allowed. -1 = unlimited, 0 = single page only, >0 = specific limit.</summary>
        public int MaxPageCount;

        /// <summary>Whether to automatically repack the atlas when adding new textures to optimize space.</summary>
        public bool RepackOnAdd;

        /// <summary>Whether to cache created sprites for reuse. Reduces memory allocations but uses more memory.</summary>
        public bool EnableSpriteCache;

        public static AtlasSettings Default => new AtlasSettings
        {
            InitialSize = 1024,
            MaxSize = 4096,
            Padding = 2,
            Format = DefaultFormat,
            FilterMode = FilterMode.Bilinear,
            GenerateMipMaps = false,
            Readable = true,  // ✅ Safe default - set to false for mobile to save memory
            UseRenderTextures = true,  // ✅ Use GPU-accelerated operations by default
            GrowthStrategy = GrowthStrategy.Double,
            Algorithm = PackingAlgorithm.MaxRects,
            MaxPageCount = -1, // Unlimited pages by default
            RepackOnAdd = false,
            EnableSpriteCache = true
        };

        public static AtlasSettings Mobile => new AtlasSettings
        {
            InitialSize = 512,
            MaxSize = 2048,
            Padding = 1,
            Format = DefaultFormat,
            FilterMode = FilterMode.Bilinear,
            GenerateMipMaps = false,
            Readable = false,  // ✅ Memory optimized - saves 50% memory on mobile
            UseRenderTextures = true,  // ✅ Required when Readable is false
            GrowthStrategy = GrowthStrategy.Double,
            Algorithm = PackingAlgorithm.Skyline,
            EnableSpriteCache = true
        };

        public static AtlasSettings HighQuality => new AtlasSettings
        {
            InitialSize = 2048,
            MaxSize = 8192,
            Padding = 4,
            Format = DefaultFormat,
            FilterMode = FilterMode.Trilinear,
            GenerateMipMaps = true,
            Readable = true,  // ✅ High quality preset - readable for flexibility
            UseRenderTextures = true,  // ✅ Use GPU-accelerated operations
            GrowthStrategy = GrowthStrategy.Double,
            Algorithm = PackingAlgorithm.MaxRects,
            EnableSpriteCache = true
        };
    }

    public enum GrowthStrategy
    {
        /// <summary>Double the size when full.</summary>
        Double,
        /// <summary>Add 50% more space when full.</summary>
        Grow50Percent,
        /// <summary>Don't grow, return failure.</summary>
        None
    }

    public enum PackingAlgorithm
    {
        /// <summary>MaxRects algorithm - best quality packing.</summary>
        MaxRects,
        /// <summary>Skyline algorithm - faster but slightly less efficient.</summary>
        Skyline,
        /// <summary>Guillotine algorithm - splits free areas into smaller rectangles.</summary>
        Guillotine,
        /// <summary>Shelf algorithm - simple row-based packing.</summary>
        Shelf
    }
}
