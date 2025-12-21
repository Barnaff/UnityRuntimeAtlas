namespace RuntimeAtlasPacker
{
    /// <summary>
    /// Result of adding a texture to an atlas.
    /// </summary>
    public enum AddResult
    {
        /// <summary>Texture was successfully added to the atlas.</summary>
        Success,
        
        /// <summary>Atlas is full and cannot fit the texture (even after growing).</summary>
        Full,
        
        /// <summary>Failed to add texture due to an error.</summary>
        Failed,
        
        /// <summary>Texture is too large to fit in atlas at any size.</summary>
        TooLarge,
        
        /// <summary>Invalid texture provided (null or invalid).</summary>
        InvalidTexture
    }
}

