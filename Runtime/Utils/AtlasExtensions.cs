using UnityEngine;
using UnityEngine.UI;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// Extension methods for easier integration with Unity components.
    /// </summary>
    public static class AtlasExtensions
    {
        #region AtlasEntry Extensions

        /// <summary>
        /// Apply an atlas entry to a SpriteRenderer.
        /// </summary>
        public static Sprite ApplyTo(this AtlasEntry entry, SpriteRenderer renderer, float pixelsPerUnit = 100f)
        {
            if (entry == null || !entry.IsValid) return null;
            
            var sprite = entry.CreateSprite(pixelsPerUnit);
            renderer.sprite = sprite;
            return sprite;
        }

        /// <summary>
        /// Apply an atlas entry to a UI Image.
        /// </summary>
        public static Sprite ApplyTo(this AtlasEntry entry, Image image, float pixelsPerUnit = 100f)
        {
            if (entry == null || !entry.IsValid) return null;
            
            var sprite = entry.CreateSprite(pixelsPerUnit);
            image.sprite = sprite;
            return sprite;
        }

        /// <summary>
        /// Apply an atlas entry to a RawImage using UV coordinates.
        /// </summary>
        public static void ApplyTo(this AtlasEntry entry, RawImage rawImage)
        {
            if (entry == null || !entry.IsValid) return;
            
            rawImage.texture = entry.Texture;
            rawImage.uvRect = entry.UV;
        }

        /// <summary>
        /// Apply an atlas entry to a MeshRenderer using a MaterialPropertyBlock.
        /// </summary>
        public static void ApplyTo(this AtlasEntry entry, MeshRenderer renderer, 
            string textureProperty = "_MainTex", string uvProperty = "_MainTex_ST")
        {
            if (entry == null || !entry.IsValid) return;

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            
            block.SetTexture(textureProperty, entry.Texture);
            var uv = entry.UV;
            block.SetVector(uvProperty, new Vector4(uv.width, uv.height, uv.x, uv.y));
            
            renderer.SetPropertyBlock(block);
        }

        /// <summary>
        /// Apply an atlas entry to an AtlasSpriteRenderer.
        /// </summary>
        public static AtlasSpriteRenderer ApplyTo(this AtlasEntry entry, AtlasSpriteRenderer renderer)
        {
            renderer.SetEntry(entry);
            return renderer;
        }

        /// <summary>
        /// Apply an atlas entry to an AtlasImage.
        /// </summary>
        public static AtlasImage ApplyTo(this AtlasEntry entry, AtlasImage image)
        {
            image.SetEntry(entry);
            return image;
        }

        /// <summary>
        /// Apply an atlas entry to an AtlasRawImage.
        /// </summary>
        public static AtlasRawImage ApplyTo(this AtlasEntry entry, AtlasRawImage rawImage)
        {
            rawImage.SetEntry(entry);
            return rawImage;
        }

        /// <summary>
        /// Create UV coordinates for a quad mesh from an atlas entry.
        /// </summary>
        public static Vector2[] GetQuadUVs(this AtlasEntry entry)
        {
            if (entry == null || !entry.IsValid)
                return new Vector2[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };

            var uv = entry.UV;
            return new Vector2[]
            {
                new Vector2(uv.xMin, uv.yMin),
                new Vector2(uv.xMax, uv.yMin),
                new Vector2(uv.xMax, uv.yMax),
                new Vector2(uv.xMin, uv.yMax)
            };
        }

        #endregion

        #region GameObject Extensions - Legacy Components

        /// <summary>
        /// Add an AtlasSprite component and bind it to an entry.
        /// </summary>
        public static AtlasSprite AddAtlasSprite(this GameObject go, AtlasEntry entry, float pixelsPerUnit = 100f)
        {
            var atlasSprite = go.GetComponent<AtlasSprite>();
            if (atlasSprite == null)
                atlasSprite = go.AddComponent<AtlasSprite>();
            
            atlasSprite.Bind(entry, pixelsPerUnit);
            return atlasSprite;
        }

        /// <summary>
        /// Add an AtlasMaterial component and bind it to an entry.
        /// </summary>
        public static AtlasMaterial AddAtlasMaterial(this GameObject go, AtlasEntry entry)
        {
            var atlasMat = go.GetComponent<AtlasMaterial>();
            if (atlasMat == null)
                atlasMat = go.AddComponent<AtlasMaterial>();
            
            atlasMat.Bind(entry);
            return atlasMat;
        }

        #endregion

        #region GameObject Extensions - Integrated Components

        /// <summary>
        /// Add or get an AtlasSpriteRenderer and set an entry.
        /// This is a self-contained component - no additional components needed.
        /// </summary>
        public static AtlasSpriteRenderer AddAtlasSpriteRenderer(this GameObject go, AtlasEntry entry, float pixelsPerUnit = 100f)
        {
            var renderer = go.GetComponent<AtlasSpriteRenderer>();
            if (renderer == null)
                renderer = go.AddComponent<AtlasSpriteRenderer>();
            
            renderer.PixelsPerUnit = pixelsPerUnit;
            renderer.SetEntry(entry);
            return renderer;
        }

        /// <summary>
        /// Add or get an AtlasSpriteRenderer and set a texture (auto-packs).
        /// </summary>
        public static AtlasSpriteRenderer AddAtlasSpriteRenderer(this GameObject go, Texture2D texture, float pixelsPerUnit = 100f)
        {
            var renderer = go.GetComponent<AtlasSpriteRenderer>();
            if (renderer == null)
                renderer = go.AddComponent<AtlasSpriteRenderer>();
            
            renderer.PixelsPerUnit = pixelsPerUnit;
            renderer.SetTexture(texture);
            return renderer;
        }

        /// <summary>
        /// Add or get an AtlasImage (UI) and set an entry.
        /// </summary>
        public static AtlasImage AddAtlasImage(this GameObject go, AtlasEntry entry, float pixelsPerUnit = 100f)
        {
            var image = go.GetComponent<AtlasImage>();
            if (image == null)
                image = go.AddComponent<AtlasImage>();
            
            image.PixelsPerUnit = pixelsPerUnit;
            image.SetEntry(entry);
            return image;
        }

        /// <summary>
        /// Add or get an AtlasImage (UI) and set a texture (auto-packs).
        /// </summary>
        public static AtlasImage AddAtlasImage(this GameObject go, Texture2D texture, float pixelsPerUnit = 100f)
        {
            var image = go.GetComponent<AtlasImage>();
            if (image == null)
                image = go.AddComponent<AtlasImage>();
            
            image.PixelsPerUnit = pixelsPerUnit;
            image.SetTexture(texture);
            return image;
        }

        /// <summary>
        /// Add or get an AtlasRawImage (UI) and set an entry.
        /// Most performant option for simple rectangular images.
        /// </summary>
        public static AtlasRawImage AddAtlasRawImage(this GameObject go, AtlasEntry entry)
        {
            var rawImage = go.GetComponent<AtlasRawImage>();
            if (rawImage == null)
                rawImage = go.AddComponent<AtlasRawImage>();
            
            rawImage.SetEntry(entry);
            return rawImage;
        }

        /// <summary>
        /// Add or get an AtlasRawImage (UI) and set a texture (auto-packs).
        /// </summary>
        public static AtlasRawImage AddAtlasRawImage(this GameObject go, Texture2D texture)
        {
            var rawImage = go.GetComponent<AtlasRawImage>();
            if (rawImage == null)
                rawImage = go.AddComponent<AtlasRawImage>();
            
            rawImage.SetTexture(texture);
            return rawImage;
        }

        #endregion

        #region Texture2D Extensions

        /// <summary>
        /// Pack a texture and immediately apply to a SpriteRenderer.
        /// </summary>
        public static AtlasEntry PackAndApply(this Texture2D texture, SpriteRenderer renderer, 
            RuntimeAtlas atlas = null, float pixelsPerUnit = 100f)
        {
            var targetAtlas = atlas ?? AtlasPacker.Default;
            var entry = targetAtlas.Add(texture);
            entry.ApplyTo(renderer, pixelsPerUnit);
            return entry;
        }

        /// <summary>
        /// Pack a texture and immediately apply to a UI Image.
        /// </summary>
        public static AtlasEntry PackAndApply(this Texture2D texture, Image image, 
            RuntimeAtlas atlas = null, float pixelsPerUnit = 100f)
        {
            var targetAtlas = atlas ?? AtlasPacker.Default;
            var entry = targetAtlas.Add(texture);
            entry.ApplyTo(image, pixelsPerUnit);
            return entry;
        }

        /// <summary>
        /// Pack a texture and apply to an AtlasSpriteRenderer (creates auto-updating binding).
        /// </summary>
        public static AtlasSpriteRenderer PackAndBind(this Texture2D texture, AtlasSpriteRenderer renderer, 
            RuntimeAtlas atlas = null)
        {
            var targetAtlas = atlas ?? AtlasPacker.Default;
            var entry = targetAtlas.Add(texture);
            renderer.SetEntry(entry);
            return renderer;
        }

        /// <summary>
        /// Pack a texture and apply to an AtlasImage (creates auto-updating binding).
        /// </summary>
        public static AtlasImage PackAndBind(this Texture2D texture, AtlasImage image, 
            RuntimeAtlas atlas = null)
        {
            var targetAtlas = atlas ?? AtlasPacker.Default;
            var entry = targetAtlas.Add(texture);
            image.SetEntry(entry);
            return image;
        }

        /// <summary>
        /// Pack a texture and apply to an AtlasRawImage (creates auto-updating binding).
        /// </summary>
        public static AtlasRawImage PackAndBind(this Texture2D texture, AtlasRawImage rawImage, 
            RuntimeAtlas atlas = null)
        {
            var targetAtlas = atlas ?? AtlasPacker.Default;
            var entry = targetAtlas.Add(texture);
            rawImage.SetEntry(entry);
            return rawImage;
        }

        #endregion
    }
}
