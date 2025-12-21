using System;
using UnityEngine;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// Represents a sprite entry in a runtime atlas.
    /// This reference automatically updates when the atlas is modified.
    /// </summary>
    public sealed class AtlasEntry : IDisposable
    {
        private RuntimeAtlas _atlas;
        private int _id;
        private bool _isDisposed;

        // Cached values - updated by atlas
        internal RectInt PixelRect;
        internal Rect UVRect;
        internal int Version;

        /// <summary>Unique identifier for this entry.</summary>
        public int Id => _id;

        /// <summary>The atlas this entry belongs to.</summary>
        public RuntimeAtlas Atlas => _atlas;

        /// <summary>Whether this entry is valid and hasn't been disposed.</summary>
        public bool IsValid => !_isDisposed && _atlas != null && _atlas.ContainsEntry(_id);

        /// <summary>Original width of the sprite in pixels.</summary>
        public int Width => PixelRect.width;

        /// <summary>Original height of the sprite in pixels.</summary>
        public int Height => PixelRect.height;

        /// <summary>UV coordinates in the atlas (0-1 range).</summary>
        public Rect UV => UVRect;

        /// <summary>Pixel coordinates in the atlas.</summary>
        public RectInt Rect => PixelRect;

        /// <summary>The atlas texture.</summary>
        public Texture2D Texture => _atlas?.Texture;

        /// <summary>Event fired when this entry's UV coordinates change.</summary>
        public event Action<AtlasEntry> OnUVChanged;

        internal AtlasEntry(RuntimeAtlas atlas, int id, RectInt pixelRect, Rect uvRect)
        {
            _atlas = atlas;
            _id = id;
            PixelRect = pixelRect;
            UVRect = uvRect;
            Version = 0;
        }

        internal void UpdateRect(RectInt newPixelRect, Rect newUVRect)
        {
            PixelRect = newPixelRect;
            UVRect = newUVRect;
            Version++;
            OnUVChanged?.Invoke(this);
        }

        /// <summary>
        /// Gets UV coordinates as a Vector4 (x, y, width, height) for shader use.
        /// </summary>
        public Vector4 GetUVVector4()
        {
            return new Vector4(UVRect.x, UVRect.y, UVRect.width, UVRect.height);
        }

        /// <summary>
        /// Gets UV coordinates as min/max Vector4 (minX, minY, maxX, maxY) for shader use.
        /// </summary>
        public Vector4 GetUVMinMax()
        {
            return new Vector4(UVRect.xMin, UVRect.yMin, UVRect.xMax, UVRect.yMax);
        }

        /// <summary>
        /// Applies this entry's UV to a material property block.
        /// </summary>
        public void ApplyToMaterialPropertyBlock(MaterialPropertyBlock block, string uvPropertyName = "_MainTex_ST")
        {
            block.SetVector(uvPropertyName, new Vector4(UVRect.width, UVRect.height, UVRect.x, UVRect.y));
        }

        /// <summary>
        /// Creates a Unity Sprite from this atlas entry.
        /// </summary>
        public Sprite CreateSprite(float pixelsPerUnit = 100f, Vector2? pivot = null)
        {
            if (!IsValid) return null;

            var p = pivot ?? new Vector2(0.5f, 0.5f);
            return Sprite.Create(
                Texture,
                new UnityEngine.Rect(PixelRect.x, PixelRect.y, PixelRect.width, PixelRect.height),
                p,
                pixelsPerUnit
            );
        }

        /// <summary>
        /// Removes this entry from the atlas.
        /// </summary>
        public void Remove()
        {
            if (!_isDisposed && _atlas != null)
            {
                _atlas.Remove(this);
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            OnUVChanged = null;
            _atlas = null;
        }
    }
}
