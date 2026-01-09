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
        private int _spriteVersion;

        // Sprite cache - only stores default sprite (100 PPU, center pivot, no border)
        private Sprite _cachedSprite;

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
        
        /// <summary>Sprite version number for tracking content changes (default is 0).</summary>
        public int SpriteVersion => _spriteVersion;
        
        /// <summary>Version number that increments whenever the UVs change.</summary>
        public int Version => _version;

        /// <summary>Whether this entry has a cached default sprite.</summary>
        public bool HasCachedSprite => _cachedSprite != null;

        /// <summary>Event fired when this entry's UV coordinates change.</summary>
        public event Action<AtlasEntry> OnUVChanged;

        internal AtlasEntry(RuntimeAtlas atlas, int id, int textureIndex, RectInt pixelRect, Rect uvRect, string name = null, Vector4 border = default, Vector2 pivot = default, float pixelsPerUnit = 100f, int spriteVersion = 0)
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
            _spriteVersion = spriteVersion;
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
        /// Only default sprites (100 PPU, center pivot, no border) are cached.
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

            // Check if this is a default configuration
            bool isDefaultConfig = IsDefaultConfiguration(ppu, p, b);

            // Check cache if enabled and this is a default configuration
            if (_atlas != null && _atlas.Settings.EnableSpriteCache && isDefaultConfig)
            {
                // Return cached sprite if available and still valid
                if (_cachedSprite != null)
                {
                    return _cachedSprite;
                }
                
                // Create new sprite and cache it
                var sprite = CreateSpriteInternal(ppu, p, b);
                if (sprite != null)
                {
                    _cachedSprite = sprite;
                }
                return sprite;
            }
            
            // Caching disabled or custom configuration, create sprite directly without caching
            return CreateSpriteInternal(ppu, p, b);
        }

        private Sprite CreateSpriteInternal(float pixelsPerUnit, Vector2 pivot, Vector4 border)
        {
            // Use the full Sprite.Create overload for proper sprite properties
            var sprite = Sprite.Create(
                texture: Texture,
                rect: new UnityEngine.Rect(_pixelRect.x, _pixelRect.y, _pixelRect.width, _pixelRect.height),
                pivot: pivot,
                pixelsPerUnit: pixelsPerUnit,
                extrude: 0,
                meshType: SpriteMeshType.Tight,
                border: border,
                generateFallbackPhysicsShape: false
            );

            // Set sprite name from atlas entry name
            if (sprite != null && !string.IsNullOrEmpty(_name))
            {
                sprite.name = _name;
            }

            return sprite;
        }

        private bool IsDefaultConfiguration(float pixelsPerUnit, Vector2 pivot, Vector4 border)
        {
            const float pivotTolerance = 0.001f;
            const float ppu = 100f;
            
            // Check if PPU is 100
            if (Mathf.Abs(pixelsPerUnit - ppu) > 0.001f)
                return false;
            
            // Check if pivot is center (0.5, 0.5)
            if (Mathf.Abs(pivot.x - 0.5f) > pivotTolerance || Mathf.Abs(pivot.y - 0.5f) > pivotTolerance)
                return false;
            
            // Check if border is zero (no 9-slicing)
            if (border.x != 0 || border.y != 0 || border.z != 0 || border.w != 0)
                return false;
            
            return true;
        }

        /// <summary>
        /// Clears the sprite cache for this entry, destroying the cached sprite.
        /// </summary>
        public void ClearSpriteCache()
        {
            if (_cachedSprite != null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(_cachedSprite);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(_cachedSprite);
                }
                _cachedSprite = null;
            }
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
            
            // Clear sprite cache
            ClearSpriteCache();
            
            OnUVChanged = null;
            _atlas = null;
        }
    }
}
