using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using RuntimeAtlasPacker;
using UnityEditor;

namespace RuntimeAtlasPacker.Samples
{
    /// <summary>
    /// Example demonstrating downloading images from URLs and dynamically adding/removing them from atlases.
    /// Supports batch downloads and automatically creates new atlases when the current one is full.
    /// </summary>
    public class WebDownloadExample : MonoBehaviour
    {
        [Header("Initial Settings")]
        [Tooltip("Number of images to download initially")]
        public int initialImageCount = 5;
        
        [Tooltip("Image size in pixels")]
        public int imageSize = 128;
        
        [Header("Dynamic Timer")]
        [Tooltip("Time in seconds between adding new images")]
        public float addImageInterval = 3f;
        
        [Tooltip("Time in seconds between removing images")]
        public float removeImageInterval = 5f;
        
        [Tooltip("Enable automatic adding of images")]
        public bool autoAddImages = true;
        
        [Tooltip("Enable automatic removing of images")]
        public bool autoRemoveImages = true;
        
        [Tooltip("Maximum number of images to keep")]
        public int maxImages = 30;
        
        [Header("Batch Download")]
        [Tooltip("Number of downloads before doing a batch")]
        public int downloadsBeforeBatch = 3;
        
        [Tooltip("Number of images to download in a batch")]
        public int batchSize = 5;
        
        [Header("Display")]
        public Transform uiContainer;
        public GameObject uiImagePrefab;
        
        [Header("Atlas Settings")]
        [Tooltip("Maximum atlas texture size")]
        public int maxAtlasSize = 2048;
        
        [Tooltip("Initial atlas texture size")]
        public int initialAtlasSize = 512;
        
        [Tooltip("Maximum number of atlas pages (-1 = unlimited, 0 = single page only, >0 = specific limit)")]
        public int maxPageCount = -1;
        
        [Tooltip("Padding between textures in pixels")]
        public int padding = 2;
        
        [Tooltip("Use a named atlas instead of default")]
        public bool useNamedAtlas = true;
        
        [Tooltip("Name of the atlas to use (only if useNamedAtlas is true)")]
        public string atlasName = "WebDownloadAtlas";

        [Tooltip("Packing algorithm to use for the atlas")]
        public PackingAlgorithm packingAlgorithm = PackingAlgorithm.MaxRects;

        [Tooltip("Automatically repack atlas when adding new images to optimize space")]
        public bool repackOnAdd = false; 

        private List<ImageData> _imageDataList = new();
        private CancellationTokenSource _cts;
        private int _imageCounter = 0;
        private int _downloadsSinceLastBatch = 0;
        private string _activeAtlasName; // Track which atlas we're using

        private class ImageData
        {
            public AtlasEntry Entry;
            public GameObject GameObject;
        }

        private void Start()
        {
            _cts = new CancellationTokenSource();
            
            // Configure atlas settings
            var settings = AtlasSettings.Default;
            settings.MaxSize = maxAtlasSize;
            settings.InitialSize = Mathf.Clamp(initialAtlasSize, 256, maxAtlasSize);
            settings.MaxPageCount = maxPageCount;
            settings.Padding = padding;
            settings.Algorithm = packingAlgorithm;
            settings.RepackOnAdd = repackOnAdd;
            // Create or get the atlas with our custom settings
            if (useNamedAtlas && !string.IsNullOrEmpty(atlasName))
            {
                _activeAtlasName = atlasName;
                var atlas = AtlasPacker.GetOrCreate(atlasName, settings);
                Debug.Log($"[WebDownload] Created/using named atlas '{atlasName}' with MaxSize: {maxAtlasSize}, MaxPages: {(maxPageCount == -1 ? "unlimited" : maxPageCount.ToString())}, Padding: {padding}");
            }
            else
            {
                _activeAtlasName = null; // Use default
                // Note: Can't change default atlas settings after it's created
                // So we just access it to ensure it exists
                var defaultAtlas = AtlasPacker.Default;
                Debug.Log($"[WebDownload] Using default atlas (settings may not be customizable if already created)");
            }

            // Start with initial images
            _ = LoadInitialImages();
            
            // Start dynamic add/remove timers
            if (autoAddImages && addImageInterval > 0)
            {
                StartCoroutine(AddImageTimer());
            }
            
            if (autoRemoveImages && removeImageInterval > 0)
            {
                StartCoroutine(RemoveImageTimer());
            }
        }

        private async Task LoadInitialImages()
        {
            Debug.Log($"[WebDownload] Loading {initialImageCount} initial images...");

            for (int i = 0; i < initialImageCount; i++)
            {
                await AddNewImage();
            }

            // Get overflow count based on which atlas we're using
            int overflowCount = !string.IsNullOrEmpty(_activeAtlasName) 
                ? AtlasPacker.GetOverflowCount(_activeAtlasName)
                : AtlasPacker.GetDefaultOverflowCount();
                
            Debug.Log($"[WebDownload] Initialized with {overflowCount + 1} atlas(es), Total images: {_imageDataList.Count}");
        }

        private IEnumerator AddImageTimer()
        {
            while (true)
            {
                yield return new WaitForSeconds(addImageInterval);
                
                if (_imageDataList.Count < maxImages)
                {
                    _downloadsSinceLastBatch++;
                    
                    // Every few downloads, do a batch instead
                    if (_downloadsSinceLastBatch >= downloadsBeforeBatch)
                    {
                        Debug.Log($"[WebDownload] Starting batch download of {batchSize} images...");
                        _ = AddBatchImages(batchSize);
                        _downloadsSinceLastBatch = 0;
                    }
                    else
                    {
                        _ = AddNewImage();
                    }
                }
            }
        }

        private IEnumerator RemoveImageTimer()
        {
            while (true)
            {
                yield return new WaitForSeconds(removeImageInterval);
                
                if (_imageDataList.Count > 0)
                {
                    RemoveRandomImage();
                }
            }
        }

        private async Task AddNewImage()
        {
            string url = GetRandomImageUrl(_imageCounter++);
            var texture = await DownloadImageAsync(url, _cts.Token);
            
            if (texture == null) return;

            // Use configured atlas (named or default)
            AtlasEntry entry;
            if (!string.IsNullOrEmpty(_activeAtlasName))
            {
                entry = AtlasPacker.Pack(_activeAtlasName, texture);
            }
            else
            {
                entry = AtlasPacker.Pack(texture);
            }
            
            Destroy(texture);

            if (entry == null)
            {
                Debug.LogWarning($"[WebDownload] Failed to pack texture - atlas may be at page limit!");
                return;
            }

            var go = CreateImageObject(entry);
            
            _imageDataList.Add(new ImageData
            {
                Entry = entry,
                GameObject = go
            });

            // Get overflow count based on which atlas we're using
            int overflowCount = !string.IsNullOrEmpty(_activeAtlasName) 
                ? AtlasPacker.GetOverflowCount(_activeAtlasName)
                : AtlasPacker.GetDefaultOverflowCount();
                
            Debug.Log($"[WebDownload] Added image {_imageCounter} - " +
                     $"Total atlases: {overflowCount + 1}, " +
                     $"Total images: {_imageDataList.Count}");
        }
        
        private async Task AddBatchImages(int count)
        {
            Debug.Log($"[WebDownload] Starting batch download of {count} images...");
            
            // Download all textures concurrently
            var downloadTasks = new List<Task<Texture2D>>();
            
            for (int i = 0; i < count; i++)
            {
                if (_imageDataList.Count + i >= maxImages) break;
                
                string url = GetRandomImageUrl(_imageCounter++);
                downloadTasks.Add(DownloadImageAsync(url, _cts.Token));
            }
            
            var textures = await Task.WhenAll(downloadTasks);
            
            // Filter out failed downloads
            var validTextures = textures.Where(t => t != null).ToArray();
            
            if (validTextures.Length == 0)
            {
                Debug.LogWarning("[WebDownload] Batch download failed - no valid textures");
                return;
            }
            
            Debug.Log($"[WebDownload] Successfully downloaded {validTextures.Length}/{count} textures. Adding as batch...");
            
            // Use AtlasPacker.PackBatch with the configured atlas
            AtlasEntry[] entries;
            if (!string.IsNullOrEmpty(_activeAtlasName))
            {
                // For named atlas, need to get the atlas and use AddBatch directly
                var atlas = AtlasPacker.GetOrCreate(_activeAtlasName);
                entries = atlas.AddBatch(validTextures);
            }
            else
            {
                // Use default atlas
                entries = AtlasPacker.PackBatch(validTextures);
            }
            
            // Clean up textures
            foreach (var tex in validTextures)
            {
                Destroy(tex);
            }
            
            if (entries == null || entries.Length == 0)
            {
                Debug.LogWarning("[WebDownload] Batch pack failed - atlas may be at page limit!");
                return;
            }
            
            // Create UI objects for all entries
            foreach (var entry in entries)
            {
                var go = CreateImageObject(entry);
                _imageDataList.Add(new ImageData
                {
                    Entry = entry,
                    GameObject = go
                });
            }
            
            // Get overflow count based on which atlas we're using
            int overflowCount = !string.IsNullOrEmpty(_activeAtlasName) 
                ? AtlasPacker.GetOverflowCount(_activeAtlasName)
                : AtlasPacker.GetDefaultOverflowCount();
                
            Debug.Log($"[WebDownload] Batch complete! " +
                     $"Total atlases: {overflowCount + 1}, " +
                     $"Total images: {_imageDataList.Count}");
        }

        private void RemoveRandomImage()
        {
            if (_imageDataList.Count == 0) return;

            int index = UnityEngine.Random.Range(0, _imageDataList.Count);
            var imageData = _imageDataList[index];

            if (imageData.GameObject != null)
            {
                Destroy(imageData.GameObject);
            }

            if (imageData.Entry != null)
            {
                // Entry will be removed from its atlas automatically
                imageData.Entry = null;
            }

            _imageDataList.RemoveAt(index);

            // Get overflow count based on which atlas we're using
            int overflowCount = !string.IsNullOrEmpty(_activeAtlasName) 
                ? AtlasPacker.GetOverflowCount(_activeAtlasName)
                : AtlasPacker.GetDefaultOverflowCount();
                
            Debug.Log($"[WebDownload] Removed image - " +
                     $"Total atlases: {overflowCount + 1}, " +
                     $"Total images: {_imageDataList.Count}");
        }

        private GameObject CreateImageObject(AtlasEntry entry)
        {
            if (uiContainer == null || uiImagePrefab == null)
            {
                Debug.LogWarning("UI Container or Image Prefab not set!");
                return null;
            }

            var go = Instantiate(uiImagePrefab, uiContainer);
            go.name = $"WebImage_{_imageCounter}";

            var atlasImage = go.GetComponent<AtlasImage>();
            if (atlasImage == null)
            {
                atlasImage = go.AddComponent<AtlasImage>();
            }
            atlasImage.SetEntry(entry);

            return go;
        }

        private string GetRandomImageUrl(int index)
        {
            // Generate various sizes around 256±50px
            int variation = UnityEngine.Random.Range(-50, 51);
            int size = Mathf.Clamp(imageSize + variation, 128, 512);
            var guid = Guid.NewGuid().ToString();
            var randomIndex = UnityEngine.Random.Range(1, 99999);
            return $"https://picsum.photos//{size}/{size}/?random={randomIndex}";
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
                texture.name = $"WebImage_{url.GetHashCode():X8}";
                return texture;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Exception downloading {url}: {e.Message}");
                return null;
            }
        }


        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            
            foreach (var imageData in _imageDataList)
            {
                if (imageData.GameObject != null)
                {
                    Destroy(imageData.GameObject);
                }
            }
            
            // AtlasPacker manages atlas disposal
            // Uncomment if you want to dispose all atlases on destroy:
            // AtlasPacker.DisposeAll();
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

                var (result, entry) = atlas.Add(texture);
                _entry = entry;
                Destroy(texture);
                
                if (result != AddResult.Success || _entry == null)
                {
                    Debug.LogWarning($"[SingleImageLoader] Failed to pack texture: {result}");
                    return;
                }

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
