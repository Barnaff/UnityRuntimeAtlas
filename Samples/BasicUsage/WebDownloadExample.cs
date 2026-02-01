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
    /// Last modified: 2026-01-09 - Added diagnostic logging
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
        private string _activeAtlasName;
        private bool _useAsyncApiNext = false; // Alternate between batch and async API

        private enum DownloadMethod
        {
            Batch,
            SingleAsync
        }

        private class ImageData
        {
            public AtlasEntry Entry;
            public GameObject GameObject;
            public DownloadMethod Method;
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
            Debug.Log($"[WebDownload] LoadInitialImages: Starting to load {initialImageCount} initial images...");

            for (int i = 0; i < initialImageCount; i++)
            {
                Debug.Log($"[WebDownload] LoadInitialImages: Loading image {i + 1}/{initialImageCount}");
                try
                {
                    await AddNewImage();
                    Debug.Log($"[WebDownload] LoadInitialImages: Image {i + 1} completed");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[WebDownload] LoadInitialImages: Exception loading image {i + 1}: {ex.Message}\n{ex.StackTrace}");
                }
            }

            // Get overflow count based on which atlas we're using
            int overflowCount = !string.IsNullOrEmpty(_activeAtlasName) 
                ? AtlasPacker.GetOverflowCount(_activeAtlasName)
                : AtlasPacker.GetDefaultOverflowCount();
                
            Debug.Log($"[WebDownload] LoadInitialImages: COMPLETE - Initialized with {overflowCount + 1} atlas(es), Total images: {_imageDataList.Count}");
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
                        // Alternate between old API and new DownloadAndAddAsync API
                        if (_useAsyncApiNext)
                        {
                            _ = AddNewImageUsingAsyncAPI();
                        }
                        else
                        {
                            _ = AddNewImage();
                        }
                        _useAsyncApiNext = !_useAsyncApiNext;
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
            Debug.Log($"[WebDownload] AddNewImage: START");
            
            string url = GetRandomImageUrl(_imageCounter++);
            Debug.Log($"[WebDownload] AddNewImage: Downloading from URL: {url}");
            
            var texture = await DownloadImageAsync(url, _cts.Token);
            
            Debug.Log($"[WebDownload] AddNewImage: Download complete. Texture = {texture != null}, Size = {texture?.width}x{texture?.height}");
            
            if (texture == null)
            {
                Debug.LogWarning($"[WebDownload] AddNewImage: Download failed, texture is null");
                return;
            }

            // Use configured atlas (named or default)
            AtlasEntry entry;
            Debug.Log($"[WebDownload] AddNewImage: Packing texture into atlas...");
            
            if (!string.IsNullOrEmpty(_activeAtlasName))
            {
                entry = AtlasPacker.Pack(_activeAtlasName, texture);
                Debug.Log($"[WebDownload] Packed to named atlas '{_activeAtlasName}': Entry={entry != null}, IsValid={entry?.IsValid}, Texture={entry?.Texture != null}");
            }
            else
            {
                entry = AtlasPacker.Pack(texture);
                Debug.Log($"[WebDownload] Packed to default atlas: Entry={entry != null}, IsValid={entry?.IsValid}, Texture={entry?.Texture != null}");
            }
            
            Destroy(texture);

            if (entry == null)
            {
                Debug.LogWarning($"[WebDownload] Failed to pack texture - atlas may be at page limit!");
                return;
            }
            
            if (!entry.IsValid)
            {
                Debug.LogError($"[WebDownload] Entry created but IsValid=false! Texture={entry.Texture != null}, Width={entry.Width}, Height={entry.Height}");
                return;
            }
            
            Debug.Log($"[WebDownload] AddNewImage: Creating UI object for entry...");

            var go = CreateImageObject(entry);
            
            if (go == null)
            {
                Debug.LogError($"[WebDownload] Failed to create image object!");
                return;
            }
            
            Debug.Log($"[WebDownload] AddNewImage: UI object created: {go.name}");
            
            _imageDataList.Add(new ImageData
            {
                Entry = entry,
                GameObject = go,
                Method = DownloadMethod.Batch
            });

            // Get overflow count based on which atlas we're using
            int overflowCount = !string.IsNullOrEmpty(_activeAtlasName) 
                ? AtlasPacker.GetOverflowCount(_activeAtlasName)
                : AtlasPacker.GetDefaultOverflowCount();
                
            Debug.Log($"[WebDownload] Added image {_imageCounter} - " +
                     $"Total atlases: {overflowCount + 1}, " +
                     $"Total images: {_imageDataList.Count}");
        }

        private async Task AddNewImageUsingAsyncAPI()
        {
            Debug.Log($"[WebDownload] AddNewImageUsingAsyncAPI: START (using DownloadAndAddAsync)");
            
            string url = GetRandomImageUrl(_imageCounter++);
            Debug.Log($"[WebDownload] AddNewImageUsingAsyncAPI: Downloading from URL: {url}");
            
            // Use RuntimeAtlas.DownloadAndAddAsync - downloads and adds in one call!
            AtlasEntry entry;
            if (!string.IsNullOrEmpty(_activeAtlasName))
            {
                var atlas = AtlasPacker.GetOrCreate(_activeAtlasName);
                entry = await atlas.DownloadAndAddAsync(url, key: $"AsyncImage_{_imageCounter}", version: 0, _cts.Token);
                Debug.Log($"[WebDownload] Downloaded and added to named atlas '{_activeAtlasName}': Entry={entry != null}");
            }
            else
            {
                var atlas = AtlasPacker.Default;
                entry = await atlas.DownloadAndAddAsync(url, key: $"AsyncImage_{_imageCounter}", version: 0, _cts.Token);
                Debug.Log($"[WebDownload] Downloaded and added to default atlas: Entry={entry != null}");
            }

            if (entry == null)
            {
                Debug.LogWarning($"[WebDownload] DownloadAndAddAsync failed - returned null");
                return;
            }
            
            if (!entry.IsValid)
            {
                Debug.LogError($"[WebDownload] Entry created but IsValid=false!");
                return;
            }
            
            Debug.Log($"[WebDownload] AddNewImageUsingAsyncAPI: Creating UI object for entry...");

            var go = CreateImageObject(entry, DownloadMethod.SingleAsync);
            
            if (go == null)
            {
                Debug.LogError($"[WebDownload] Failed to create image object!");
                return;
            }
            
            Debug.Log($"[WebDownload] AddNewImageUsingAsyncAPI: UI object created: {go.name}");
            
            _imageDataList.Add(new ImageData
            {
                Entry = entry,
                GameObject = go,
                Method = DownloadMethod.SingleAsync
            });

            int overflowCount = !string.IsNullOrEmpty(_activeAtlasName) 
                ? AtlasPacker.GetOverflowCount(_activeAtlasName)
                : AtlasPacker.GetDefaultOverflowCount();
                
            Debug.Log($"[WebDownload] Added image {_imageCounter} via DownloadAndAddAsync - " +
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
                var go = CreateImageObject(entry, DownloadMethod.Batch);
                _imageDataList.Add(new ImageData
                {
                    Entry = entry,
                    GameObject = go,
                    Method = DownloadMethod.Batch
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

        private GameObject CreateImageObject(AtlasEntry entry, DownloadMethod method = DownloadMethod.Batch)
        {
            if (uiContainer == null || uiImagePrefab == null)
            {
                Debug.LogWarning("UI Container or Image Prefab not set!");
                return null;
            }

            var go = Instantiate(uiImagePrefab, uiContainer);
            go.name = $"WebImage_{_imageCounter}_{method}";

            var atlasImage = go.GetComponent<AtlasImage>();
            if (atlasImage == null)
            {
                atlasImage = go.AddComponent<AtlasImage>();
            }
            atlasImage.SetEntry(entry);

            // Add visual distinction based on download method
            // Find the actual Image component (might be on child)
            var image = go.GetComponentInChildren<UnityEngine.UI.Image>(true);
            if (image != null)
            {
                // Add color tint to the image for visual distinction
                switch (method)
                {
                    case DownloadMethod.Batch:
                        // Slight green tint for batch downloads
                        image.color = new Color(0.8f, 1f, 0.8f, 1f);
                        break;
                        
                    case DownloadMethod.SingleAsync:
                        // Slight magenta tint for DownloadAndAddAsync
                        image.color = new Color(1f, 0.8f, 1f, 1f);
                        break;
                }
                
                // Add colored outline to the image GameObject
                var imageGO = image.gameObject;
                var outline = imageGO.GetComponent<UnityEngine.UI.Outline>();
                if (outline == null)
                {
                    outline = imageGO.AddComponent<UnityEngine.UI.Outline>();
                }
                
                // Add shadow for extra prominence
                var shadow = imageGO.GetComponent<UnityEngine.UI.Shadow>();
                if (shadow == null)
                {
                    shadow = imageGO.AddComponent<UnityEngine.UI.Shadow>();
                }
                
                // Set outline and shadow colors based on download method
                switch (method)
                {
                    case DownloadMethod.Batch:
                        // Bright green outline and shadow for batch downloads
                        outline.effectColor = new Color(0f, 1f, 0f, 1f);
                        outline.effectDistance = new Vector2(5, -5);
                        shadow.effectColor = new Color(0f, 0.6f, 0f, 0.8f);
                        shadow.effectDistance = new Vector2(3, -3);
                        break;
                        
                    case DownloadMethod.SingleAsync:
                        // Bright magenta outline and shadow for DownloadAndAddAsync
                        outline.effectColor = new Color(1f, 0f, 1f, 1f);
                        outline.effectDistance = new Vector2(5, -5);
                        shadow.effectColor = new Color(0.8f, 0f, 0.8f, 0.8f);
                        shadow.effectDistance = new Vector2(3, -3);
                        break;
                }
            }

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
            Debug.Log($"[WebDownload] DownloadImageAsync: Starting download from {url}");
            
            try
            {
                // ✅ IMPROVED: Use UnityWebRequest with DownloadHandlerBuffer for better texture lifecycle control
                var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET);
                request.downloadHandler = new DownloadHandlerBuffer();
                
                try
                {
                    Debug.Log($"[WebDownload] DownloadImageAsync: Sending web request...");
                    var operation = request.SendWebRequest();
                    
                    while (!operation.isDone)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            Debug.LogWarning($"[WebDownload] DownloadImageAsync: Cancelled");
                            request.Abort();
                            return null;
                        }
                        await Task.Yield();
                    }

                    Debug.Log($"[WebDownload] DownloadImageAsync: Request complete. Result: {request.result}");

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogWarning($"[WebDownload] Failed to download {url}: {request.error}");
                        return null;
                    }

                    // Get raw image bytes
                    byte[] imageData = request.downloadHandler.data;
                    
                    if (imageData == null || imageData.Length == 0)
                    {
                        Debug.LogWarning($"[WebDownload] Downloaded data is empty for {url}");
                        return null;
                    }

                    // Create texture and load image data
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    texture.name = $"WebImage_{url.GetHashCode():X8}";
                    
                    if (!texture.LoadImage(imageData))
                    {
                        Debug.LogError($"[WebDownload] Failed to load image data for {url}");
                        Destroy(texture);
                        return null;
                    }
                    
                    Debug.Log($"[WebDownload] DownloadImageAsync: SUCCESS - Texture: {texture.name}, Size: {texture.width}x{texture.height}");
                    
                    return texture;
                }
                finally
                {
                    request?.Dispose();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[WebDownload] Exception downloading {url}: {e.Message}\n{e.StackTrace}");
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
}
