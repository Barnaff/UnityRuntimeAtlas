using System;
using System.Threading.Tasks;
using UnityEngine;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// Self-contained sprite renderer that automatically binds to atlas entries.
    /// No additional components needed - just assign an entry or texture and it handles everything.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public class AtlasSpriteRenderer : MonoBehaviour
    {
        [SerializeField] private float _pixelsPerUnit = 100f;
        [SerializeField] private Vector2 _pivot = new(0.5f, 0.5f);
        [SerializeField] private bool _autoPackOnAssign = true;
        [SerializeField] private string _targetAtlasName = "";

        private SpriteRenderer _renderer;
        private AtlasEntry _entry;
        private Sprite _managedSprite;
        private int _lastEntryVersion = -1;
        private bool _isDestroyed;

        /// <summary>The underlying SpriteRenderer.</summary>
        public SpriteRenderer Renderer
        {
            get
            {
                if (_renderer == null)
                    _renderer = GetComponent<SpriteRenderer>();
                return _renderer;
            }
        }

        /// <summary>The current atlas entry.</summary>
        public AtlasEntry Entry => _entry;

        /// <summary>Whether this renderer has a valid entry.</summary>
        public bool HasEntry => _entry != null && _entry.IsValid;

        /// <summary>The atlas this entry belongs to.</summary>
        public RuntimeAtlas Atlas => _entry?.Atlas;

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

        /// <summary>Pivot point for sprite creation (0-1 range).</summary>
        public Vector2 Pivot
        {
            get => _pivot;
            set
            {
                if (_pivot != value)
                {
                    _pivot = value;
                    ForceRefresh();
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

        /// <summary>
        /// Set the sprite from an atlas entry.
        /// </summary>
        public AtlasSpriteRenderer SetEntry(AtlasEntry entry)
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

            return this;
        }

        /// <summary>
        /// Set the sprite from a texture. If AutoPackOnAssign is true, packs into atlas first.
        /// </summary>
        public AtlasSpriteRenderer SetTexture(Texture2D texture)
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
                    Debug.LogWarning($"[AtlasSpriteRenderer] Failed to pack texture '{texture.name}': {result}");
                    ClearSprite();
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
                    _pivot,
                    _pixelsPerUnit
                );
                Renderer.sprite = _managedSprite;
            }

            return this;
        }

        /// <summary>
        /// Set the sprite from an existing Unity Sprite (no atlas).
        /// </summary>
        public AtlasSpriteRenderer SetSprite(Sprite sprite)
        {
            Unbind();
            ClearManagedSprite();
            Renderer.sprite = sprite;
            return this;
        }

        /// <summary>
        /// Pack and set a texture asynchronously.

        /// <summary>
        /// Clear the current sprite.
        /// </summary>
        public void Clear()
        {
            Unbind();
            ClearSprite();
        }

        /// <summary>
        /// Force refresh the sprite from the current entry.
        /// </summary>
        public void ForceRefresh()
        {
            _lastEntryVersion = -1;
            UpdateSprite();
        }

        /// <summary>
        /// Remove the entry from its atlas (if owned).
        /// </summary>
        public void RemoveFromAtlas()
        {
            if (_entry != null && _entry.IsValid)
            {
                var entryToRemove = _entry;
                Unbind();
                ClearSprite();
                entryToRemove.Remove();
            }
        }

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
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
                UpdateSprite();
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
            ClearManagedSprite();
        }

        private void LateUpdate()
        {
            // Safety check for version changes we might have missed
            if (_entry != null && _entry.IsValid && _entry.Version != _lastEntryVersion)
            {
                UpdateSprite();
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
        }

        private void OnAtlasResized(RuntimeAtlas atlas)
        {
            UpdateSprite();
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

            _managedSprite = _entry.CreateSprite(_pixelsPerUnit, _pivot);
            Renderer.sprite = _managedSprite;
            _lastEntryVersion = _entry.Version;
        }

        private void ClearSprite()
        {
            ClearManagedSprite();
            Renderer.sprite = null;
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
