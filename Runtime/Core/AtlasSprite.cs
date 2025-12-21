using System;
using UnityEngine;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// Component that renders a sprite from a runtime atlas.
    /// Automatically updates when the atlas is modified.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class AtlasSprite : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _renderer;
        
        private AtlasEntry _entry;
        private Sprite _currentSprite;
        private int _lastVersion = -1;
        private float _pixelsPerUnit = 100f;
        private Vector2 _pivot = new(0.5f, 0.5f);

        /// <summary>The atlas entry this sprite is using.</summary>
        public AtlasEntry Entry => _entry;

        /// <summary>Whether this sprite is valid and has an entry.</summary>
        public bool IsValid => _entry != null && _entry.IsValid;

        /// <summary>The underlying sprite renderer.</summary>
        public SpriteRenderer Renderer => _renderer;

        private void Awake()
        {
            if (_renderer == null)
                _renderer = GetComponent<SpriteRenderer>();
        }

        private void OnDestroy()
        {
            Unbind();
            
            if (_currentSprite != null)
            {
                Destroy(_currentSprite);
                _currentSprite = null;
            }
        }

        /// <summary>
        /// Bind this component to an atlas entry.
        /// </summary>
        public void Bind(AtlasEntry entry, float pixelsPerUnit = 100f, Vector2? pivot = null)
        {
            Unbind();

            _entry = entry;
            _pixelsPerUnit = pixelsPerUnit;
            _pivot = pivot ?? new Vector2(0.5f, 0.5f);

            if (_entry != null)
            {
                _entry.OnUVChanged += OnEntryChanged;
                UpdateSprite();
            }
        }

        /// <summary>
        /// Unbind from the current entry.
        /// </summary>
        public void Unbind()
        {
            if (_entry != null)
            {
                _entry.OnUVChanged -= OnEntryChanged;
                _entry = null;
            }
        }

        private void OnEntryChanged(AtlasEntry entry)
        {
            UpdateSprite();
        }

        private void UpdateSprite()
        {
            if (_entry == null || !_entry.IsValid)
            {
                _renderer.sprite = null;
                return;
            }

            if (_lastVersion == _entry.Version && _currentSprite != null)
                return;

            // Destroy old sprite
            if (_currentSprite != null)
            {
                Destroy(_currentSprite);
            }

            // Create new sprite
            _currentSprite = _entry.CreateSprite(_pixelsPerUnit, _pivot);
            _renderer.sprite = _currentSprite;
            _lastVersion = _entry.Version;
        }

        private void LateUpdate()
        {
            // Safety check for version updates we might have missed
            if (_entry != null && _entry.IsValid && _entry.Version != _lastVersion)
            {
                UpdateSprite();
            }
        }
    }
}
