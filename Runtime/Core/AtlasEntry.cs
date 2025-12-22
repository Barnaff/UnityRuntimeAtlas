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
        private int _textureIndex;
        private bool _isDisposed;
        private string _name;
        private Vector4 _border;
        private Vector2 _pivot;
        private float _pixelsPerUnit;

        // Cached values - updated by atlas
        internal RectInt _pixelRect;
        internal Rect _uvRect;
        internal int _version;

        /// <summary>Unique identifier for this entry.</summary>
        public int Id => _id;

        /// <summary>The atlas this entry belongs to.</summary>
        public RuntimeAtlas Atlas => _atlas;

        /// <summary>The texture page index this entry is on (0 = first page, 1 = second page, etc.).</summary>
        public int TextureIndex => _textureIndex;

        /// <summary>Whether this entry is valid and hasn't been disposed.</summary>
        public bool IsValid => !_isDisposed && _atlas != null && _atlas.ContainsEntry(_id);

        /// <summary>Original width of the sprite in pixels.</summary>
        public int Width => _pixelRect.width;

        /// <summary>Original height of the sprite in pixels.</summary>
        public int Height => _pixelRect.height;

        /// <summary>UV coordinates in the atlas (0-1 range).</summary>
        public Rect UV => _uvRect;

        /// <summary>Pixel coordinates in the atlas.</summary>
        public RectInt Rect => _pixelRect;

        /// <summary>The atlas texture for this specific entry (uses TextureIndex).</summary>
        public Texture2D Texture => _atlas?.GetTexture(_textureIndex);

        /// <summary>Name of the original texture (if available).</summary>
        public string Name => _name;
        
        /// <summary>Border values for 9-slicing (left, bottom, right, top).</summary>
        public Vector4 Border => _border;
        
        /// <summary>Pivot point of the sprite (0-1 normalized).</summary>
        public Vector2 Pivot => _pivot;
        
        /// <summary>Pixels per unit value of the sprite.</summary>
        public float PixelsPerUnit => _pixelsPerUnit;
        
        /// <summary>Version number that increments whenever the UVs change.</summary>
        public int Version => _version;

        /// <summary>Event fired when this entry's UV coordinates change.</summary>
        public event Action<AtlasEntry> OnUVChanged;

        internal AtlasEntry(RuntimeAtlas atlas, int id, int textureIndex, RectInt pixelRect, Rect uvRect, string name = null, Vector4 border = default, Vector2 pivot = default, float pixelsPerUnit = 100f)
        {
            _atlas = atlas;
            _id = id;
            _textureIndex = textureIndex;
            _pixelRect = pixelRect;
            _uvRect = uvRect;
            _version = 0;
            _name = name ?? $"Entry_{id}";
            _border = border;
            _pivot = pivot == default ? new Vector2(0.5f, 0.5f) : pivot;
            _pixelsPerUnit = pixelsPerUnit > 0 ? pixelsPerUnit : 100f;
        }

        internal void UpdateRect(RectInt newPixelRect, Rect newUVRect)
        {
            _pixelRect = newPixelRect;
            _uvRect = newUVRect;
            _version++;
            OnUVChanged?.Invoke(this);
        }

        internal void UpdateTextureIndex(int newTextureIndex)
        {
            _textureIndex = newTextureIndex;
        }

        /// <summary>
        /// Gets UV coordinates as a Vector4 (x, y, width, height) for shader use.
        /// </summary>
        public Vector4 GetUVVector4()
        {
            return new Vector4(_uvRect.x, _uvRect.y, _uvRect.width, _uvRect.height);
        }

        /// <summary>
        /// Gets UV coordinates as min/max Vector4 (minX, minY, maxX, maxY) for shader use.
        /// </summary>
        public Vector4 GetUVMinMax()
        {
            return new Vector4(_uvRect.xMin, _uvRect.yMin, _uvRect.xMax, _uvRect.yMax);
        }

        /// <summary>
        /// Applies this entry's UV to a material property block.
        /// </summary>
        public void ApplyToMaterialPropertyBlock(MaterialPropertyBlock block, string uvPropertyName = "_MainTex_ST")
        {
            block.SetVector(uvPropertyName, new Vector4(_uvRect.width, _uvRect.height, _uvRect.x, _uvRect.y));
        }

        /// <summary>
        /// Creates a Unity Sprite from this atlas entry.
        /// </summary>
        /// <param name="pixelsPerUnit">Pixels per unit for the sprite. If null, uses stored value.</param>
        /// <param name="pivot">Pivot point (0-1 normalized). If null, uses stored value.</param>
        /// <param name="border">Border values for 9-slicing (left, bottom, right, top). If null, uses stored value.</param>
        public Sprite CreateSprite(float? pixelsPerUnit = null, Vector2? pivot = null, Vector4? border = null)
        {
            if (!IsValid)
            {
                return null;
            }

            var ppu = pixelsPerUnit ?? _pixelsPerUnit;
            var p = pivot ?? _pivot;
            var b = border ?? _border;
            
            // Use the full Sprite.Create overload for proper sprite properties
            var sprite = Sprite.Create(
                texture: Texture,
                rect: new UnityEngine.Rect(_pixelRect.x, _pixelRect.y, _pixelRect.width, _pixelRect.height),
                pivot: p,
                pixelsPerUnit: ppu,
                extrude: 0,
                meshType: SpriteMeshType.Tight,
                border: b,
                generateFallbackPhysicsShape: false
            );

            // Set sprite name from atlas entry name
            if (sprite != null && !string.IsNullOrEmpty(_name))
            {
                sprite.name = _name;
            }

            return sprite;
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
