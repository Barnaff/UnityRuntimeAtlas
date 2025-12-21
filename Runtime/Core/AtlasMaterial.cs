using System;
using UnityEngine;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// Component that manages material properties for rendering atlas entries.
    /// Useful for custom shaders, UI, and mesh-based rendering.
    /// </summary>
    public class AtlasMaterial : MonoBehaviour
    {
        [SerializeField] private Renderer _targetRenderer;
        [SerializeField] private string _texturePropertyName = "_MainTex";
        [SerializeField] private string _uvPropertyName = "_MainTex_ST";
        [SerializeField] private bool _usePropertyBlock = true;

        private AtlasEntry _entry;
        private MaterialPropertyBlock _propertyBlock;
        private int _lastVersion = -1;
        private int _texturePropertyId;
        private int _uvPropertyId;

        /// <summary>The atlas entry this material is using.</summary>
        public AtlasEntry Entry => _entry;

        /// <summary>Whether to use MaterialPropertyBlock (recommended) or modify material directly.</summary>
        public bool UsePropertyBlock
        {
            get => _usePropertyBlock;
            set => _usePropertyBlock = value;
        }

        private void Awake()
        {
            if (_targetRenderer == null)
                _targetRenderer = GetComponent<Renderer>();

            _propertyBlock = new MaterialPropertyBlock();
            _texturePropertyId = Shader.PropertyToID(_texturePropertyName);
            _uvPropertyId = Shader.PropertyToID(_uvPropertyName);
        }

        private void OnDestroy()
        {
            Unbind();
        }

        /// <summary>
        /// Bind this component to an atlas entry.
        /// </summary>
        public void Bind(AtlasEntry entry)
        {
            Unbind();

            _entry = entry;

            if (_entry != null)
            {
                _entry.OnUVChanged += OnEntryChanged;
                if (_entry.Atlas != null)
                {
                    _entry.Atlas.OnAtlasResized += OnAtlasResized;
                }
                UpdateMaterial();
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
                if (_entry.Atlas != null)
                {
                    _entry.Atlas.OnAtlasResized -= OnAtlasResized;
                }
                _entry = null;
            }
        }

        private void OnEntryChanged(AtlasEntry entry)
        {
            UpdateMaterial();
        }

        private void OnAtlasResized(RuntimeAtlas atlas)
        {
            UpdateMaterial();
        }

        private void UpdateMaterial()
        {
            if (_entry == null || !_entry.IsValid || _targetRenderer == null)
                return;

            if (_lastVersion == _entry.Version)
                return;

            var uv = _entry.UV;
            var uvST = new Vector4(uv.width, uv.height, uv.x, uv.y);

            if (_usePropertyBlock)
            {
                _targetRenderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetTexture(_texturePropertyId, _entry.Texture);
                _propertyBlock.SetVector(_uvPropertyId, uvST);
                _targetRenderer.SetPropertyBlock(_propertyBlock);
            }
            else
            {
                var mat = _targetRenderer.material;
                mat.SetTexture(_texturePropertyId, _entry.Texture);
                mat.SetVector(_uvPropertyId, uvST);
            }

            _lastVersion = _entry.Version;
        }

        private void LateUpdate()
        {
            if (_entry != null && _entry.IsValid && _entry.Version != _lastVersion)
            {
                UpdateMaterial();
            }
        }

        /// <summary>
        /// Set the target renderer at runtime.
        /// </summary>
        public void SetRenderer(Renderer renderer)
        {
            _targetRenderer = renderer;
            if (_entry != null)
            {
                _lastVersion = -1;
                UpdateMaterial();
            }
        }

        /// <summary>
        /// Set property names for custom shaders.
        /// </summary>
        public void SetPropertyNames(string textureName, string uvName)
        {
            _texturePropertyName = textureName;
            _uvPropertyName = uvName;
            _texturePropertyId = Shader.PropertyToID(_texturePropertyName);
            _uvPropertyId = Shader.PropertyToID(_uvPropertyName);
            _lastVersion = -1;
            UpdateMaterial();
        }
    }
}
