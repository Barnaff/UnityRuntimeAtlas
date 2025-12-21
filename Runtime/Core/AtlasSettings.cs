using UnityEngine;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// Configuration settings for runtime atlas generation.
    /// </summary>
    [System.Serializable]
    public struct AtlasSettings
    {
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
        
        /// <summary>Whether to make texture readable (required for CPU access but uses more memory).</summary>
        public bool Readable;
        
        /// <summary>Growth strategy when atlas is full.</summary>
        public GrowthStrategy GrowthStrategy;
        
        /// <summary>Packing algorithm to use.</summary>
        public PackingAlgorithm Algorithm;

        public static AtlasSettings Default => new AtlasSettings
        {
            InitialSize = 1024,
            MaxSize = 4096,
            Padding = 2,
            Format = TextureFormat.RGBA32,
            FilterMode = FilterMode.Bilinear,
            GenerateMipMaps = false,
            Readable = false,
            GrowthStrategy = GrowthStrategy.Double,
            Algorithm = PackingAlgorithm.MaxRects
        };

        public static AtlasSettings Mobile => new AtlasSettings
        {
            InitialSize = 512,
            MaxSize = 2048,
            Padding = 1,
            Format = TextureFormat.RGBA32,
            FilterMode = FilterMode.Bilinear,
            GenerateMipMaps = false,
            Readable = false,
            GrowthStrategy = GrowthStrategy.Double,
            Algorithm = PackingAlgorithm.Skyline
        };

        public static AtlasSettings HighQuality => new AtlasSettings
        {
            InitialSize = 2048,
            MaxSize = 8192,
            Padding = 4,
            Format = TextureFormat.RGBA32,
            FilterMode = FilterMode.Trilinear,
            GenerateMipMaps = true,
            Readable = false,
            GrowthStrategy = GrowthStrategy.Double,
            Algorithm = PackingAlgorithm.MaxRects
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
        Skyline
    }
}
