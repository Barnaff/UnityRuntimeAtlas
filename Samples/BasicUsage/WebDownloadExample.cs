using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using RuntimeAtlasPacker;

namespace RuntimeAtlasPacker.Samples
{
    /// <summary>
    /// Example demonstrating downloading images from URLs and packing them into atlases.
    /// Uses placeholder image services for demo purposes.
    /// </summary>
    public class WebDownloadExample : MonoBehaviour
    {
        [Header("Download Settings")]
        [Tooltip("Number of images to download")]
        public int imageCount = 10;
        
        [Tooltip("Image size in pixels")]
        public int imageSize = 128;
        
        [Header("Display")]
        public Transform spriteContainer;
        public float spriteSpacing = 1.5f;
        
        [Header("UI Mode (Optional)")]
        public Transform uiContainer;
        public GameObject uiImagePrefab;

        private RuntimeAtlas _atlas;
        private List<AtlasEntry> _entries = new();
        private List<GameObject> _spawnedObjects = new();
        private CancellationTokenSource _cts;

        // Image placeholder services that provide random images
        private static readonly string[] ImageServices = new[]
        {
            "https://picsum.photos/{0}/{1}?random={2}",           // Lorem Picsum - random photos
            "https://placekitten.com/{0}/{1}?image={2}",          // Placekitten - cat images
            "https://placedog.net/{0}/{1}?id={2}",                // Placedog - dog images
            "https://loremflickr.com/{0}/{1}?random={2}",         // Lorem Flickr - random photos
        };

        private void Start()
        {
            _cts = new CancellationTokenSource();
            
            // Create atlas for downloaded images
            _atlas = new RuntimeAtlas(new AtlasSettings
            {
                InitialSize = 512,
                MaxSize = 2048,
                Padding = 2,
                Format = TextureFormat.RGBA32,
                Algorithm = PackingAlgorithm.MaxRects,
                GrowthStrategy = GrowthStrategy.Double
            });

            // Start downloading
            _ = DownloadAndDisplayImages();
        }

        private async Task DownloadAndDisplayImages()
        {
            Debug.Log($"Starting download of {imageCount} images...");

            var downloadTasks = new List<Task<Texture2D>>();
            
            for (int i = 0; i < imageCount; i++)
            {
                // Use different image services for variety
                string url = GetRandomImageUrl(i);
                downloadTasks.Add(DownloadImageAsync(url, _cts.Token));
            }

            // Wait for all downloads
            Texture2D[] textures;
            try
            {
                textures = await Task.WhenAll(downloadTasks);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("Download cancelled");
                return;
            }

            // Filter out failed downloads
            var validTextures = new List<Texture2D>();
            foreach (var tex in textures)
            {
                if (tex != null)
                    validTextures.Add(tex);
            }

            Debug.Log($"Successfully downloaded {validTextures.Count}/{imageCount} images");

            if (validTextures.Count == 0)
            {
                Debug.LogWarning("No images downloaded. Check your internet connection.");
                return;
            }

            // Pack all textures into atlas
            var entries = _atlas.AddBatch(validTextures.ToArray());
            _entries.AddRange(entries);

            Debug.Log($"Atlas created: {_atlas.Width}x{_atlas.Height}, Fill: {_atlas.FillRatio:P1}");

            // Display as sprites or UI
            if (spriteContainer != null)
            {
                DisplayAsSprites(entries);
            }
            
            if (uiContainer != null && uiImagePrefab != null)
            {
                DisplayAsUI(entries);
            }

            // Cleanup downloaded textures (they're now in the atlas)
            foreach (var tex in validTextures)
            {
                Destroy(tex);
            }
        }

        private string GetRandomImageUrl(int index)
        {
            // Use Lorem Picsum as primary (most reliable)
            // Format: https://picsum.photos/width/height?random=seed
            return $"https://picsum.photos/{imageSize}/{imageSize}?random={index + UnityEngine.Random.Range(1000, 9999)}";
        }

        private async Task<Texture2D> DownloadImageAsync(string url, CancellationToken ct)
        {
            try
            {
                using var request = UnityWebRequestTexture.GetTexture(url);
                
                var operation = request.SendWebRequest();
                
                while (!operation.isDone)
                {
                    if (ct.IsCancellationRequested)
                    {
                        request.Abort();
                        return null;
                    }
                    await Task.Yield();
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"Failed to download {url}: {request.error}");
                    return null;
                }

                var texture = DownloadHandlerTexture.GetContent(request);
                texture.name = $"Downloaded_{url.GetHashCode():X8}";
                return texture;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Exception downloading {url}: {e.Message}");
                return null;
            }
        }

        private void DisplayAsSprites(AtlasEntry[] entries)
        {
            int columns = Mathf.CeilToInt(Mathf.Sqrt(entries.Length));
            
            for (int i = 0; i < entries.Length; i++)
            {
                int row = i / columns;
                int col = i % columns;
                
                var go = new GameObject($"WebSprite_{i}");
                go.transform.SetParent(spriteContainer);
                go.transform.localPosition = new Vector3(col * spriteSpacing, -row * spriteSpacing, 0);
                
                // Use AtlasSpriteRenderer for auto-updating
                var renderer = go.AddComponent<AtlasSpriteRenderer>();
                renderer.PixelsPerUnit = imageSize;
                renderer.SetEntry(entries[i]);
                
                _spawnedObjects.Add(go);
            }
        }

        private void DisplayAsUI(AtlasEntry[] entries)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                var go = Instantiate(uiImagePrefab, uiContainer);
                go.name = $"WebImage_{i}";
                
                // Try to get AtlasImage or AtlasRawImage
                var atlasImage = go.GetComponent<AtlasImage>();
                if (atlasImage != null)
                {
                    atlasImage.SetEntry(entries[i]);
                }
                else
                {
                    var rawImage = go.GetComponent<AtlasRawImage>();
                    if (rawImage != null)
                    {
                        rawImage.SetEntry(entries[i]);
                    }
                }
                
                _spawnedObjects.Add(go);
            }
        }

        /// <summary>
        /// Download and add a single image at runtime.
        /// </summary>
        public async Task<AtlasEntry> DownloadAndAddImage(string url)
        {
            var texture = await DownloadImageAsync(url, _cts.Token);
            if (texture == null) return null;
            
            var entry = _atlas.Add(texture);
            _entries.Add(entry);
            
            Destroy(texture);
            return entry;
        }

        /// <summary>
        /// Download a random placeholder image and add to atlas.
        /// </summary>
        public async Task<AtlasEntry> DownloadRandomImage(int width = 128, int height = 128)
        {
            string url = $"https://picsum.photos/{width}/{height}?random={UnityEngine.Random.Range(1, 99999)}";
            return await DownloadAndAddImage(url);
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            
            foreach (var go in _spawnedObjects)
            {
                if (go != null) Destroy(go);
            }
            
            _atlas?.Dispose();
        }
    }

    /// <summary>
    /// Simplified component for downloading a single image to an atlas renderer.
    /// </summary>
    public class WebImageLoader : MonoBehaviour
    {
        [Header("Image URL")]
        [Tooltip("URL of the image to download")]
        public string imageUrl = "https://picsum.photos/128/128";
        
        [Header("Placeholder Settings")]
        public bool useRandomPlaceholder = true;
        public int placeholderWidth = 128;
        public int placeholderHeight = 128;
        
        [Header("Options")]
        public bool loadOnStart = true;
        public string targetAtlasName = ""; // Empty = default atlas

        private AtlasEntry _entry;
        private bool _isLoading;

        private async void Start()
        {
            if (loadOnStart)
            {
                await LoadImage();
            }
        }

        public async Task LoadImage()
        {
            if (_isLoading) return;
            _isLoading = true;

            try
            {
                string url = useRandomPlaceholder 
                    ? $"https://picsum.photos/{placeholderWidth}/{placeholderHeight}?random={UnityEngine.Random.Range(1, 99999)}"
                    : imageUrl;

                Debug.Log($"Loading image from: {url}");

                using var request = UnityWebRequestTexture.GetTexture(url);
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"Failed to load image: {request.error}");
                    return;
                }

                var texture = DownloadHandlerTexture.GetContent(request);

                // Get or create atlas
                var atlas = string.IsNullOrEmpty(targetAtlasName)
                    ? AtlasPacker.Default
                    : AtlasPacker.GetOrCreate(targetAtlasName);

                _entry = atlas.Add(texture);
                Destroy(texture);

                // Apply to any atlas component on this GameObject
                var spriteRenderer = GetComponent<AtlasSpriteRenderer>();
                if (spriteRenderer != null)
                {
                    spriteRenderer.SetEntry(_entry);
                    return;
                }

                var atlasImage = GetComponent<AtlasImage>();
                if (atlasImage != null)
                {
                    atlasImage.SetEntry(_entry);
                    return;
                }

                var rawImage = GetComponent<AtlasRawImage>();
                if (rawImage != null)
                {
                    rawImage.SetEntry(_entry);
                }
            }
            finally
            {
                _isLoading = false;
            }
        }

        public async Task LoadFromUrl(string url)
        {
            imageUrl = url;
            useRandomPlaceholder = false;
            await LoadImage();
        }
    }
}
