using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// Lightweight UI image using RawImage approach for atlas entries.
    /// More performant than AtlasImage as it doesn't create Sprite objects.
    /// Best for simple rectangular images without slicing.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RawImage))]
    [AddComponentMenu("UI/Atlas Raw Image")]
    public class AtlasRawImage : MonoBehaviour
    {
        [SerializeField] private bool _preserveAspect = false;
        [SerializeField] private bool _autoPackOnAssign = true;
        [SerializeField] private string _targetAtlasName = "";

        private RawImage _rawImage;
        private AtlasEntry _entry;
        private int _lastEntryVersion = -1;
        private bool _isDestroyed;

        /// <summary>The underlying RawImage.</summary>
        public RawImage RawImage
        {
            get
            {
                if (_rawImage == null)
                {
                    _rawImage = GetComponent<RawImage>();
                }
                return _rawImage;
            }
        }

        /// <summary>The current atlas entry.</summary>
        public AtlasEntry Entry => _entry;

        /// <summary>Whether this image has a valid entry.</summary>
        public bool HasEntry => _entry != null && _entry.IsValid;

        /// <summary>The atlas this entry belongs to.</summary>
        public RuntimeAtlas Atlas => _entry?.Atlas;

        /// <summary>Preserve aspect ratio when rendering.</summary>
        public bool PreserveAspect
        {
            get => _preserveAspect;
            set => _preserveAspect = value;
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

        /// <summary>
        /// Set the image from an atlas entry.
        /// </summary>
        public AtlasRawImage SetEntry(AtlasEntry entry)
        {
            if (entry == null)
            {
                Unbind();
                _entry = null;
                ClearImage();
                return this;
            }
            
            if (_entry == entry)
            {
                return this;
            }

            Unbind();

            _entry = entry;

            if (_entry != null)
            {
                _entry.OnUVChanged += OnEntryChanged;
                if (_entry.Atlas != null)
                {
                    _entry.Atlas.OnAtlasResized += OnAtlasResized;
                }
                UpdateImage();
            }
            else
            {
                ClearImage();
            }

            return this;
        }

        /// <summary>
        /// Set the image from a texture. If AutoPackOnAssign is true, packs into atlas first.
        /// </summary>
        public AtlasRawImage SetTexture(Texture2D texture)
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
                    Debug.LogWarning($"[AtlasRawImage] Failed to pack texture '{texture.name}': {result}");
                    ClearImage();
                }
            }
            else
            {
                // Direct texture without atlas
                Unbind();
                RawImage.texture = texture;
                RawImage.uvRect = new Rect(0, 0, 1, 1);
            }

            return this;
        }


        /// <summary>
        /// Clear the current image.
        /// </summary>
        public void Clear()
        {
            Unbind();
            ClearImage();
        }

        /// <summary>
        /// Force refresh from the current entry.
        /// </summary>
        public void ForceRefresh()
        {
            _lastEntryVersion = -1;
            UpdateImage();
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
                ClearImage();
                entryToRemove.Remove();
            }
        }

        /// <summary>
        /// Set native size based on entry dimensions.
        /// </summary>
        public void SetNativeSize()
        {
            if (_entry == null || !_entry.IsValid)
            {
                return;
            }

            var rt = GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.sizeDelta = new Vector2(_entry.Width, _entry.Height);
            }
        }

        private void Awake()
        {
            _rawImage = GetComponent<RawImage>();
        }

        private void OnEnable()
        {
            if (_entry != null && _entry.IsValid)
            {
                _entry.OnUVChanged += OnEntryChanged;
                if (_entry.Atlas != null)
                {
                    _entry.Atlas.OnAtlasResized += OnAtlasResized;
                }
                UpdateImage();
            }
        }

        private void OnDisable()
        {
            if (_entry != null)
            {
                _entry.OnUVChanged -= OnEntryChanged;
                if (_entry.Atlas != null)
                {
                    _entry.Atlas.OnAtlasResized -= OnAtlasResized;
                }
            }
        }

        private void OnDestroy()
        {
            _isDestroyed = true;
            Unbind();
        }

        private void LateUpdate()
        {
            if (_entry != null && _entry.IsValid && _entry.Version != _lastEntryVersion)
            {
                UpdateImage();
            }

            if (_preserveAspect && _entry != null && _entry.IsValid)
            {
                ApplyAspectRatio();
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
            UpdateImage();
        }

        private void OnAtlasResized(RuntimeAtlas atlas)
        {
            UpdateImage();
        }

        private void UpdateImage()
        {
            if (_entry == null || !_entry.IsValid)
            {
                ClearImage();
                return;
            }

            if (_lastEntryVersion == _entry.Version)
            {
                return;
            }

            RawImage.texture = _entry.Texture;
            RawImage.uvRect = _entry.UV;
            _lastEntryVersion = _entry.Version;
        }

        private void ClearImage()
        {
            RawImage.texture = null;
            RawImage.uvRect = new Rect(0, 0, 1, 1);
        }

        private void ApplyAspectRatio()
        {
            if (_entry == null || _entry.Width == 0 || _entry.Height == 0)
            {
                return;
            }

            var rt = GetComponent<RectTransform>();
            if (rt == null)
            {
                return;
            }

            var parentRect = rt.rect;
            var spriteRatio = (float)_entry.Width / _entry.Height;
            var rectRatio = parentRect.width / parentRect.height;

            // This is a simplified version - for full aspect fitting you'd want
            // to use an AspectRatioFitter or implement more complex logic
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_entry != null && _entry.IsValid)
            {
                ForceRefresh();
            }
        }
#endif
    }
}
