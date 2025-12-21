using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// Self-contained UI Image that automatically binds to atlas entries.
    /// Replaces standard Image component with built-in atlas support.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("UI/Atlas Image")]
    public class AtlasImage : MaskableGraphic
    {
        [SerializeField] private float _pixelsPerUnit = 100f;
        [SerializeField] private bool _preserveAspect = false;
        [SerializeField] private bool _autoPackOnAssign = true;
        [SerializeField] private string _targetAtlasName = "";
        [SerializeField] private ImageType _imageType = ImageType.Simple;
        [SerializeField] private bool _fillCenter = true;

        private AtlasEntry _entry;
        private Sprite _managedSprite;
        private int _lastEntryVersion = -1;
        private bool _isDestroyed;

        public enum ImageType
        {
            Simple,
            Sliced,
            Tiled,
            Filled
        }

        /// <summary>The current atlas entry.</summary>
        public AtlasEntry Entry => _entry;

        /// <summary>Whether this image has a valid entry.</summary>
        public bool HasEntry => _entry != null && _entry.IsValid;

        /// <summary>The atlas this entry belongs to.</summary>
        public RuntimeAtlas Atlas => _entry?.Atlas;

        /// <summary>The current sprite (created from atlas entry).</summary>
        public Sprite Sprite => _managedSprite;

        /// <summary>Pixels per unit for sprite creation.</summary>
        public float PixelsPerUnit
        {
            get => _pixelsPerUnit;
            set
            {
                if (Math.Abs(_pixelsPerUnit - value) > 0.001f)
                {
                    _pixelsPerUnit = value;
                    ForceRefresh();
                }
            }
        }

        /// <summary>Preserve aspect ratio when rendering.</summary>
        public bool PreserveAspect
        {
            get => _preserveAspect;
            set
            {
                if (_preserveAspect != value)
                {
                    _preserveAspect = value;
                    SetVerticesDirty();
                }
            }
        }

        /// <summary>If true, assigning a Texture2D will auto-pack it into an atlas.</summary>
        public bool AutoPackOnAssign
        {
            get => _autoPackOnAssign;
            set => _autoPackOnAssign = value;
        }

        /// <summary>Target atlas name for auto-packing. Empty uses default atlas.</summary>
        public string TargetAtlasName
        {
            get => _targetAtlasName;
            set => _targetAtlasName = value;
        }

        /// <summary>How the image is rendered.</summary>
        public ImageType Type
        {
            get => _imageType;
            set
            {
                if (_imageType != value)
                {
                    _imageType = value;
                    SetVerticesDirty();
                }
            }
        }

        /// <summary>Fill center for sliced images.</summary>
        public bool FillCenter
        {
            get => _fillCenter;
            set
            {
                if (_fillCenter != value)
                {
                    _fillCenter = value;
                    SetVerticesDirty();
                }
            }
        }

        /// <summary>
        /// Override main texture to return atlas texture.
        /// </summary>
        public override Texture mainTexture
        {
            get
            {
                if (_entry != null && _entry.IsValid)
                    return _entry.Texture;
                return s_WhiteTexture;
            }
        }

        /// <summary>
        /// Set the image from an atlas entry.
        /// </summary>
        public AtlasImage SetEntry(AtlasEntry entry)
        {
            if (_entry == entry) return this;

            Unbind();

            _entry = entry;

            if (_entry != null)
            {
                _entry.OnUVChanged += OnEntryChanged;
                if (_entry.Atlas != null)
                {
                    _entry.Atlas.OnAtlasResized += OnAtlasResized;
                }
                UpdateSprite();
            }
            else
            {
                ClearSprite();
            }

            SetAllDirty();
            return this;
        }

        /// <summary>
        /// Set the image from a texture. If AutoPackOnAssign is true, packs into atlas first.
        /// </summary>
        public AtlasImage SetTexture(Texture2D texture)
        {
            if (texture == null)
            {
                SetEntry(null);
                return this;
            }

            if (_autoPackOnAssign)
            {
                var atlas = string.IsNullOrEmpty(_targetAtlasName)
                    ? AtlasPacker.Default
                    : AtlasPacker.GetOrCreate(_targetAtlasName);

                var (result, entry) = atlas.Add(texture);
                if (result == AddResult.Success && entry != null)
                {
                    SetEntry(entry);
                }
                else
                {
                    Debug.LogWarning($"[AtlasImage] Failed to pack texture '{texture.name}': {result}");
                    Clear();
                }
            }
            else
            {
                // Direct sprite without atlas
                Unbind();
                ClearManagedSprite();

                _managedSprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    _pixelsPerUnit
                );
                SetAllDirty();
            }

            return this;
        }

        /// <summary>
        /// Set the image from an existing Unity Sprite (no atlas).
        /// </summary>
        public AtlasImage SetSprite(Sprite sprite)
        {
            Unbind();
            ClearManagedSprite();
            _managedSprite = sprite;
            SetAllDirty();
            return this;
        }

        /// <summary>
        /// Pack and set a texture asynchronously.

        /// <summary>
        /// Clear the current image.
        /// </summary>
        public void Clear()
        {
            Unbind();
            ClearSprite();
            SetAllDirty();
        }

        /// <summary>
        /// Force refresh from the current entry.
        /// </summary>
        public void ForceRefresh()
        {
            _lastEntryVersion = -1;
            UpdateSprite();
            SetAllDirty();
        }

        /// <summary>
        /// Remove the entry from its atlas.
        /// </summary>
        public void RemoveFromAtlas()
        {
            if (_entry != null && _entry.IsValid)
            {
                var entryToRemove = _entry;
                Unbind();
                ClearSprite();
                entryToRemove.Remove();
                SetAllDirty();
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            
            if (_entry != null && _entry.IsValid)
            {
                _entry.OnUVChanged += OnEntryChanged;
                if (_entry.Atlas != null)
                {
                    _entry.Atlas.OnAtlasResized += OnAtlasResized;
                }
                UpdateSprite();
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            
            if (_entry != null)
            {
                _entry.OnUVChanged -= OnEntryChanged;
                if (_entry.Atlas != null)
                {
                    _entry.Atlas.OnAtlasResized -= OnAtlasResized;
                }
            }
        }

        protected override void OnDestroy()
        {
            _isDestroyed = true;
            Unbind();
            ClearManagedSprite();
            base.OnDestroy();
        }

        private void LateUpdate()
        {
            if (_entry != null && _entry.IsValid && _entry.Version != _lastEntryVersion)
            {
                UpdateSprite();
                SetAllDirty();
            }
        }

        private void Unbind()
        {
            if (_entry != null)
            {
                _entry.OnUVChanged -= OnEntryChanged;
                if (_entry.Atlas != null)
                {
                    _entry.Atlas.OnAtlasResized -= OnAtlasResized;
                }
                _entry = null;
            }
        }

        private void OnEntryChanged(AtlasEntry entry)
        {
            UpdateSprite();
            SetAllDirty();
        }

        private void OnAtlasResized(RuntimeAtlas atlas)
        {
            UpdateSprite();
            SetAllDirty();
        }

        private void UpdateSprite()
        {
            if (_entry == null || !_entry.IsValid)
            {
                ClearSprite();
                return;
            }

            if (_lastEntryVersion == _entry.Version && _managedSprite != null)
                return;

            ClearManagedSprite();
            _managedSprite = _entry.CreateSprite(_pixelsPerUnit);
            _lastEntryVersion = _entry.Version;
        }

        private void ClearSprite()
        {
            ClearManagedSprite();
        }

        private void ClearManagedSprite()
        {
            if (_managedSprite != null)
            {
                if (Application.isPlaying)
                    Destroy(_managedSprite);
                else
                    DestroyImmediate(_managedSprite);
                _managedSprite = null;
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            if (_entry == null || !_entry.IsValid)
            {
                return;
            }

            var rect = GetPixelAdjustedRect();
            var uv = _entry.UV;

            if (_preserveAspect && _entry.Width > 0 && _entry.Height > 0)
            {
                PreserveAspectRatio(ref rect);
            }

            var color32 = color;

            switch (_imageType)
            {
                case ImageType.Simple:
                    GenerateSimpleSprite(vh, rect, uv, color32);
                    break;
                case ImageType.Sliced:
                    if (_managedSprite != null && _managedSprite.border.sqrMagnitude > 0)
                        GenerateSlicedSprite(vh, rect, uv, color32);
                    else
                        GenerateSimpleSprite(vh, rect, uv, color32);
                    break;
                case ImageType.Tiled:
                    GenerateTiledSprite(vh, rect, uv, color32);
                    break;
                case ImageType.Filled:
                    GenerateSimpleSprite(vh, rect, uv, color32);
                    break;
            }
        }

        private void PreserveAspectRatio(ref Rect rect)
        {
            float spriteRatio = (float)_entry.Width / _entry.Height;
            float rectRatio = rect.width / rect.height;

            if (spriteRatio > rectRatio)
            {
                float newHeight = rect.width / spriteRatio;
                rect.y += (rect.height - newHeight) * 0.5f;
                rect.height = newHeight;
            }
            else
            {
                float newWidth = rect.height * spriteRatio;
                rect.x += (rect.width - newWidth) * 0.5f;
                rect.width = newWidth;
            }
        }

        private void GenerateSimpleSprite(VertexHelper vh, Rect rect, Rect uv, Color32 color32)
        {
            vh.AddVert(new Vector3(rect.xMin, rect.yMin), color32, new Vector2(uv.xMin, uv.yMin));
            vh.AddVert(new Vector3(rect.xMin, rect.yMax), color32, new Vector2(uv.xMin, uv.yMax));
            vh.AddVert(new Vector3(rect.xMax, rect.yMax), color32, new Vector2(uv.xMax, uv.yMax));
            vh.AddVert(new Vector3(rect.xMax, rect.yMin), color32, new Vector2(uv.xMax, uv.yMin));

            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }

        private void GenerateSlicedSprite(VertexHelper vh, Rect rect, Rect uv, Color32 color32)
        {
            if (_managedSprite == null) return;

            var border = _managedSprite.border;
            var atlasSize = new Vector2(_entry.Atlas.Width, _entry.Atlas.Height);

            // Convert border from pixels to UV space
            float leftBorderUV = border.x / atlasSize.x;
            float rightBorderUV = border.z / atlasSize.x;
            float bottomBorderUV = border.y / atlasSize.y;
            float topBorderUV = border.w / atlasSize.y;

            // Calculate positions
            float[] xPos = new float[4];
            float[] yPos = new float[4];
            float[] xUV = new float[4];
            float[] yUV = new float[4];

            xPos[0] = rect.xMin;
            xPos[1] = rect.xMin + border.x;
            xPos[2] = rect.xMax - border.z;
            xPos[3] = rect.xMax;

            yPos[0] = rect.yMin;
            yPos[1] = rect.yMin + border.y;
            yPos[2] = rect.yMax - border.w;
            yPos[3] = rect.yMax;

            xUV[0] = uv.xMin;
            xUV[1] = uv.xMin + leftBorderUV;
            xUV[2] = uv.xMax - rightBorderUV;
            xUV[3] = uv.xMax;

            yUV[0] = uv.yMin;
            yUV[1] = uv.yMin + bottomBorderUV;
            yUV[2] = uv.yMax - topBorderUV;
            yUV[3] = uv.yMax;

            // Generate 9-slice quads
            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    if (!_fillCenter && x == 1 && y == 1)
                        continue;

                    int vertIndex = vh.currentVertCount;

                    vh.AddVert(new Vector3(xPos[x], yPos[y]), color32, new Vector2(xUV[x], yUV[y]));
                    vh.AddVert(new Vector3(xPos[x], yPos[y + 1]), color32, new Vector2(xUV[x], yUV[y + 1]));
                    vh.AddVert(new Vector3(xPos[x + 1], yPos[y + 1]), color32, new Vector2(xUV[x + 1], yUV[y + 1]));
                    vh.AddVert(new Vector3(xPos[x + 1], yPos[y]), color32, new Vector2(xUV[x + 1], yUV[y]));

                    vh.AddTriangle(vertIndex, vertIndex + 1, vertIndex + 2);
                    vh.AddTriangle(vertIndex + 2, vertIndex + 3, vertIndex);
                }
            }
        }

        private void GenerateTiledSprite(VertexHelper vh, Rect rect, Rect uv, Color32 color32)
        {
            if (_entry == null || _entry.Width == 0 || _entry.Height == 0)
            {
                GenerateSimpleSprite(vh, rect, uv, color32);
                return;
            }

            float tileWidth = _entry.Width / _pixelsPerUnit;
            float tileHeight = _entry.Height / _pixelsPerUnit;

            int xTiles = Mathf.CeilToInt(rect.width / tileWidth);
            int yTiles = Mathf.CeilToInt(rect.height / tileHeight);

            // Limit tiles to prevent performance issues
            xTiles = Mathf.Min(xTiles, 100);
            yTiles = Mathf.Min(yTiles, 100);

            for (int y = 0; y < yTiles; y++)
            {
                for (int x = 0; x < xTiles; x++)
                {
                    float x0 = rect.xMin + x * tileWidth;
                    float y0 = rect.yMin + y * tileHeight;
                    float x1 = Mathf.Min(x0 + tileWidth, rect.xMax);
                    float y1 = Mathf.Min(y0 + tileHeight, rect.yMax);

                    float uvWidth = (x1 - x0) / tileWidth;
                    float uvHeight = (y1 - y0) / tileHeight;

                    float u0 = uv.xMin;
                    float v0 = uv.yMin;
                    float u1 = uv.xMin + uv.width * uvWidth;
                    float v1 = uv.yMin + uv.height * uvHeight;

                    int vertIndex = vh.currentVertCount;

                    vh.AddVert(new Vector3(x0, y0), color32, new Vector2(u0, v0));
                    vh.AddVert(new Vector3(x0, y1), color32, new Vector2(u0, v1));
                    vh.AddVert(new Vector3(x1, y1), color32, new Vector2(u1, v1));
                    vh.AddVert(new Vector3(x1, y0), color32, new Vector2(u1, v0));

                    vh.AddTriangle(vertIndex, vertIndex + 1, vertIndex + 2);
                    vh.AddTriangle(vertIndex + 2, vertIndex + 3, vertIndex);
                }
            }
        }

        /// <summary>
        /// Get the native size of the sprite.
        /// </summary>
        public override void SetNativeSize()
        {
            if (_entry == null || !_entry.IsValid) return;

            rectTransform.sizeDelta = new Vector2(_entry.Width, _entry.Height);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            
            if (_entry != null && _entry.IsValid)
            {
                ForceRefresh();
            }
        }
#endif
    }
}
