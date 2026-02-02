using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using RuntimeAtlasPacker;

namespace RuntimeAtlasPacker.Samples
{
    /// <summary>
    /// Example showing how to create a UI image gallery using atlas textures.
    /// Demonstrates AtlasImage with different image types (Simple, Sliced, Tiled).
    /// </summary>
    public class UIGalleryExample : MonoBehaviour
    {
        [Header("Textures")]
        [Tooltip("Textures to display in the gallery")]
        public Texture2D[] galleryTextures;
        
        [Header("UI Setup")]
        public RectTransform galleryContainer;
        public GameObject imageItemPrefab;
        
        [Header("Layout")]
        public int columns = 4;
        public float itemSize = 100f;
        public float spacing = 10f;
        
        [Header("Atlas Settings")]
        public string atlasName = "UIGallery";

        private RuntimeAtlas _atlas;
        private List<AtlasEntry> _entries = new();
        private List<GameObject> _items = new();

        private void Start()
        {
            // Create atlas for UI textures
            _atlas = AtlasPacker.GetOrCreate(atlasName, new AtlasSettings
            {
                InitialSize = 512,
                MaxSize = 2048,
                Padding = 2,
                Format = TextureFormat.ARGB32,
                Algorithm = PackingAlgorithm.MaxRects
            });

            // Load from Resources if no textures assigned
            if (galleryTextures == null || galleryTextures.Length == 0)
            {
                galleryTextures = Resources.LoadAll<Texture2D>("");
                Debug.Log($"Loaded {galleryTextures.Length} textures from Resources");
            }

            if (galleryTextures.Length > 0)
            {
                CreateGallery();
            }
            else
            {
                Debug.LogWarning("No textures found. Add textures to the array or Resources folder.");
            }
        }

        private void CreateGallery()
        {
            // Pack all textures
            _entries.AddRange(_atlas.AddBatch(galleryTextures));
            
            Debug.Log($"Gallery atlas: {_atlas.Width}x{_atlas.Height}, {_entries.Count} items, Fill: {_atlas.FillRatio:P1}");

            // Create UI items
            for (int i = 0; i < _entries.Count; i++)
            {
                CreateGalleryItem(_entries[i], i);
            }

            // Update layout
            UpdateLayout();
        }

        private void CreateGalleryItem(AtlasEntry entry, int index)
        {
            GameObject item;
            
            if (imageItemPrefab != null)
            {
                item = Instantiate(imageItemPrefab, galleryContainer);
            }
            else
            {
                // Create default item
                item = CreateDefaultItem();
            }

            item.name = $"GalleryItem_{index}";

            // Get or add AtlasImage
            var atlasImage = item.GetComponent<AtlasImage>();
            if (atlasImage == null)
            {
                atlasImage = item.AddComponent<AtlasImage>();
            }

            atlasImage.SetEntry(entry);
            atlasImage.PreserveAspect = true;

            // Add click handler
            var button = item.GetComponent<Button>();
            if (button == null)
            {
                button = item.AddComponent<Button>();
            }
            
            int capturedIndex = index;
            button.onClick.AddListener(() => OnItemClicked(capturedIndex));

            _items.Add(item);
        }

        private GameObject CreateDefaultItem()
        {
            var item = new GameObject("GalleryItem");
            item.transform.SetParent(galleryContainer);
            
            var rectTransform = item.AddComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(itemSize, itemSize);
            
            // Add background
            var bg = item.AddComponent<Image>();
            bg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            
            // Add atlas image as child
            var imageGo = new GameObject("Image");
            imageGo.transform.SetParent(item.transform);
            
            var imageRect = imageGo.AddComponent<RectTransform>();
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = new Vector2(5, 5);
            imageRect.offsetMax = new Vector2(-5, -5);
            
            imageGo.AddComponent<AtlasImage>();
            
            return item;
        }

        private void UpdateLayout()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                int row = i / columns;
                int col = i % columns;
                
                var rectTransform = _items[i].GetComponent<RectTransform>();
                rectTransform.anchorMin = new Vector2(0, 1);
                rectTransform.anchorMax = new Vector2(0, 1);
                rectTransform.pivot = new Vector2(0, 1);
                
                float x = col * (itemSize + spacing) + spacing;
                float y = -(row * (itemSize + spacing) + spacing);
                
                rectTransform.anchoredPosition = new Vector2(x, y);
                rectTransform.sizeDelta = new Vector2(itemSize, itemSize);
            }

            // Update container size
            if (galleryContainer != null)
            {
                int rows = Mathf.CeilToInt((float)_items.Count / columns);
                float height = rows * (itemSize + spacing) + spacing;
                galleryContainer.sizeDelta = new Vector2(galleryContainer.sizeDelta.x, height);
            }
        }

        private void OnItemClicked(int index)
        {
            var entry = _entries[index];
            Debug.Log($"Clicked item {index}: {entry.Width}x{entry.Height}, UV: {entry.UV}");
            
            // Example: Show larger preview
            // ShowPreview(entry);
        }

        /// <summary>
        /// Add a new texture to the gallery at runtime.
        /// </summary>
        public AtlasEntry AddTexture(Texture2D texture)
        {
            var (result, entry) = _atlas.Add(texture);
            if (result != AddResult.Success || entry == null)
            {
                Debug.LogWarning($"Failed to add texture to gallery: {result}");
                return null;
            }
            
            _entries.Add(entry);
            
            CreateGalleryItem(entry, _entries.Count - 1);
            UpdateLayout();
            
            return entry;
        }

        /// <summary>
        /// Remove an item from the gallery.
        /// </summary>
        public void RemoveItem(int index)
        {
            if (index < 0 || index >= _entries.Count) return;
            
            var entry = _entries[index];
            entry.Remove();
            _entries.RemoveAt(index);
            
            var item = _items[index];
            Destroy(item);
            _items.RemoveAt(index);
            
            UpdateLayout();
        }

        /// <summary>
        /// Clear all items from the gallery.
        /// </summary>
        public void ClearGallery()
        {
            foreach (var item in _items)
            {
                Destroy(item);
            }
            _items.Clear();
            
            foreach (var entry in _entries)
            {
                entry.Remove();
            }
            _entries.Clear();
        }

        private void OnDestroy()
        {
            // Named atlas is managed by AtlasPacker, don't dispose directly
            // Just clear our references
            _entries.Clear();
        }
    }

    /// <summary>
    /// Example showing sliced/9-patch images with atlas.
    /// </summary>
    public class SlicedImageExample : MonoBehaviour
    {
        [Header("Sliced Image Setup")]
        public Texture2D slicedTexture;
        public Vector4 border = new Vector4(10, 10, 10, 10); // Left, Bottom, Right, Top
        
        [Header("Test Sizes")]
        public Vector2[] testSizes = new[]
        {
            new Vector2(50, 50),
            new Vector2(100, 100),
            new Vector2(200, 100),
            new Vector2(100, 200),
            new Vector2(300, 150)
        };

        public RectTransform container;

        private void Start()
        {
            if (slicedTexture == null)
            {
                Debug.LogWarning("Assign a sliced texture with 9-patch borders");
                return;
            }

            CreateSlicedImages();
        }

        private void CreateSlicedImages()
        {
            // Pack texture
            var entry = AtlasPacker.Pack(slicedTexture);
            
            // Create sprite with border info
            var sprite = Sprite.Create(
                entry.Texture,
                new Rect(entry.Rect.x, entry.Rect.y, entry.Rect.width, entry.Rect.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                border
            );

            float xOffset = 0;
            
            foreach (var size in testSizes)
            {
                var go = new GameObject($"Sliced_{size.x}x{size.y}");
                go.transform.SetParent(container);
                
                var rect = go.AddComponent<RectTransform>();
                rect.anchorMin = new Vector2(0, 0.5f);
                rect.anchorMax = new Vector2(0, 0.5f);
                rect.pivot = new Vector2(0, 0.5f);
                rect.anchoredPosition = new Vector2(xOffset, 0);
                rect.sizeDelta = size;
                
                var image = go.AddComponent<AtlasImage>();
                image.SetEntry(entry);
                image.Type = AtlasImage.ImageType.Sliced;
                image.FillCenter = true;
                
                xOffset += size.x + 20;
            }
        }
    }
}
