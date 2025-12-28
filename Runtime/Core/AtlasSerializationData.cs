using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// Serializable data structure for saving/loading runtime atlases.
    /// </summary>
    [Serializable]
    public class AtlasSerializationData
    {
        /// <summary>Atlas settings</summary>
        public AtlasSettingsData Settings;
        
        /// <summary>All texture pages</summary>
        public List<AtlasPageData> Pages;
        
        /// <summary>All sprite entries</summary>
        public List<AtlasEntryData> Entries;
        
        /// <summary>Mapping of sprite keys to entry IDs</summary>
        public Dictionary<string, int> NameToIdMap;
        
        /// <summary>Next available ID for new entries</summary>
        public int NextId;
        
        /// <summary>Atlas version</summary>
        public int Version;
    }

    /// <summary>
    /// Serializable atlas settings
    /// </summary>
    [Serializable]
    public class AtlasSettingsData
    {
        public int InitialSize;
        public int MaxSize;
        public int MaxPageCount;
        public int Padding;
        public TextureFormat Format;
        public FilterMode FilterMode;
        public bool GenerateMipMaps;
        public bool Readable;
        public GrowthStrategy GrowthStrategy;
        public PackingAlgorithm Algorithm;
        public bool RepackOnAdd;
        public bool EnableSpriteCache;

        public static AtlasSettingsData FromSettings(AtlasSettings settings)
        {
            return new AtlasSettingsData
            {
                InitialSize = settings.InitialSize,
                MaxSize = settings.MaxSize,
                MaxPageCount = settings.MaxPageCount,
                Padding = settings.Padding,
                Format = settings.Format,
                FilterMode = settings.FilterMode,
                GenerateMipMaps = settings.GenerateMipMaps,
                Readable = settings.Readable,
                GrowthStrategy = settings.GrowthStrategy,
                Algorithm = settings.Algorithm,
                RepackOnAdd = settings.RepackOnAdd,
                EnableSpriteCache = settings.EnableSpriteCache
            };
        }

        public AtlasSettings ToSettings()
        {
            return new AtlasSettings
            {
                InitialSize = InitialSize,
                MaxSize = MaxSize,
                MaxPageCount = MaxPageCount,
                Padding = Padding,
                Format = Format,
                FilterMode = FilterMode,
                GenerateMipMaps = GenerateMipMaps,
                Readable = Readable,
                GrowthStrategy = GrowthStrategy,
                Algorithm = Algorithm,
                RepackOnAdd = RepackOnAdd,
                EnableSpriteCache = EnableSpriteCache
            };
        }
    }

    /// <summary>
    /// Serializable texture page data (PNG files are saved separately)
    /// </summary>
    [Serializable]
    public class AtlasPageData
    {
        public int PageIndex;
        public int Width;
        public int Height;
        // TextureData removed - PNG files are saved as separate binary files for efficiency
        public PackingAlgorithmState PackerState;
    }

    /// <summary>
    /// Serializable packing algorithm state
    /// </summary>
    [Serializable]
    public class PackingAlgorithmState
    {
        public PackingAlgorithm Algorithm;
        public int Width;
        public int Height;
        public List<RectIntSerializable> UsedRects;
    }

    /// <summary>
    /// Serializable RectInt (Unity's RectInt is not serializable by default)
    /// </summary>
    [Serializable]
    public struct RectIntSerializable
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;

        public RectIntSerializable(RectInt rect)
        {
            X = rect.x;
            Y = rect.y;
            Width = rect.width;
            Height = rect.height;
        }

        public RectInt ToRectInt()
        {
            return new RectInt(X, Y, Width, Height);
        }
    }

    /// <summary>
    /// Serializable atlas entry data
    /// </summary>
    [Serializable]
    public class AtlasEntryData
    {
        public int Id;
        public int TextureIndex;
        public string Name;
        public RectIntSerializable PixelRect;
        public RectSerializable UVRect;
        public Vector4Serializable Border;
        public Vector2Serializable Pivot;
        public float PixelsPerUnit;
    }

    /// <summary>
    /// Serializable Rect
    /// </summary>
    [Serializable]
    public struct RectSerializable
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;

        public RectSerializable(Rect rect)
        {
            X = rect.x;
            Y = rect.y;
            Width = rect.width;
            Height = rect.height;
        }

        public Rect ToRect()
        {
            return new Rect(X, Y, Width, Height);
        }
    }

    /// <summary>
    /// Serializable Vector4
    /// </summary>
    [Serializable]
    public struct Vector4Serializable
    {
        public float X;
        public float Y;
        public float Z;
        public float W;

        public Vector4Serializable(Vector4 v)
        {
            X = v.x;
            Y = v.y;
            Z = v.z;
            W = v.w;
        }

        public Vector4 ToVector4()
        {
            return new Vector4(X, Y, Z, W);
        }
    }

    /// <summary>
    /// Serializable Vector2
    /// </summary>
    [Serializable]
    public struct Vector2Serializable
    {
        public float X;
        public float Y;

        public Vector2Serializable(Vector2 v)
        {
            X = v.x;
            Y = v.y;
        }

        public Vector2 ToVector2()
        {
            return new Vector2(X, Y);
        }
    }
}

