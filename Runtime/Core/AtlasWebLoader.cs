using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// Fast, optimized system for downloading images from URLs and adding them to an atlas.
    /// Supports batching, async operations, and request deduplication.
    /// </summary>
    public class AtlasWebLoader : IDisposable
    {
        private readonly RuntimeAtlas _atlas;
        private readonly Dictionary<string, LoadRequest> _activeRequests;
        private readonly Queue<LoadRequest> _pendingQueue;
        private readonly int _maxConcurrentDownloads;
        private readonly object _atlasLock = new object(); // Lock for atlas modifications
        private int _activeDownloads;
        private bool _isDisposed;

        /// <summary>
        /// Event fired when a sprite is successfully loaded and added to the atlas.
        /// </summary>
        public event Action<string, Sprite> OnSpriteLoaded;

        /// <summary>
        /// Event fired when a download fails.
        /// </summary>
        public event Action<string, string> OnDownloadFailed;

        /// <summary>
        /// Create a new web loader for the specified atlas.
        /// </summary>
        /// <param name="atlas">The atlas to add downloaded images to</param>
        /// <param name="maxConcurrentDownloads">Maximum number of concurrent downloads (default: 4)</param>
        public AtlasWebLoader(RuntimeAtlas atlas, int maxConcurrentDownloads = 4)
        {
            _atlas = atlas ?? throw new ArgumentNullException(nameof(atlas));
            _maxConcurrentDownloads = Mathf.Max(1, maxConcurrentDownloads);
            _activeRequests = new Dictionary<string, LoadRequest>();
            _pendingQueue = new Queue<LoadRequest>();
            _activeDownloads = 0;
            _isDisposed = false;
        }

        /// <summary>
        /// Download an image from URL and add it to the atlas, returning the sprite.
        /// If the same URL is already being downloaded, waits for that download to complete.
        /// </summary>
        /// <param name="url">URL of the image to download</param>
        /// <param name="spriteName">Optional name for the sprite (uses URL hash if null)</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>The sprite from the atlas, or null if download failed</returns>
        public async Task<Sprite> GetSpriteAsync(string url, string spriteName = null, CancellationToken cancellationToken = default)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(AtlasWebLoader));
            }

            if (string.IsNullOrEmpty(url))
            {
                Debug.LogError("[AtlasWebLoader] URL cannot be null or empty");
                return null;
            }

            spriteName = string.IsNullOrEmpty(spriteName) ? GetNameFromUrl(url) : spriteName;

            // Check if already in atlas
            var existingEntry = _atlas.GetEntryByName(spriteName);
            if (existingEntry != null && existingEntry.IsValid)
            {
                return existingEntry.CreateSprite();
            }

            // Check if already downloading (request deduplication)
            LoadRequest request;
            bool shouldStartDownload = false;

            lock (_activeRequests)
            {
                if (_activeRequests.TryGetValue(url, out var existingRequest))
                {
                    // Use existing request (will wait outside the lock)
                    request = existingRequest;
                }
                else
                {
                    // Create new request
                    request = new LoadRequest(url, spriteName);
                    _activeRequests[url] = request;

                    // Queue or start download
                    if (_activeDownloads < _maxConcurrentDownloads)
                    {
                        _activeDownloads++;
                        shouldStartDownload = true;
                    }
                    else
                    {
                        _pendingQueue.Enqueue(request);
                    }
                }
            }

            // Start download outside the lock if needed
            if (shouldStartDownload)
            {
                _ = ProcessDownloadAsync(request, cancellationToken);
            }

            // Wait for completion outside the lock
            return await request.WaitForCompletionAsync(cancellationToken);
        }

        /// <summary>
        /// Download multiple images and add them to the atlas in batch.
        /// More efficient than calling GetSpriteAsync multiple times.
        /// </summary>
        /// <param name="urls">URLs of images to download</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>Dictionary mapping URLs to their sprites (null for failed downloads)</returns>
        public async Task<Dictionary<string, Sprite>> GetSpritesAsync(IEnumerable<string> urls, CancellationToken cancellationToken = default)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(AtlasWebLoader));
            }

            var tasks = new List<Task<(string url, Sprite sprite)>>();

            foreach (var url in urls)
            {
                if (!string.IsNullOrEmpty(url))
                {
                    tasks.Add(GetSpriteWithUrlAsync(url, cancellationToken));
                }
            }

            var results = await Task.WhenAll(tasks);

            var resultDict = new Dictionary<string, Sprite>();
            foreach (var (url, sprite) in results)
            {
                resultDict[url] = sprite;
            }

            return resultDict;
        }

        /// <summary>
        /// Download multiple images and add them as a batch to the atlas.
        /// Uses batch add for better performance.
        /// </summary>
        /// <param name="urlsWithNames">Dictionary of URLs to sprite names</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>Dictionary mapping sprite names to their sprites</returns>
        /// <summary>
        /// Download multiple images and add them as a batch to the atlas.
        /// Uses batch add for better performance. Handles partial failures gracefully.
        /// </summary>
        /// <param name="urlsWithNames">Dictionary of URLs to sprite names</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>Dictionary mapping sprite names to their sprites (null for failed downloads)</returns>
        public async Task<Dictionary<string, Sprite>> DownloadAndAddBatchAsync(
            Dictionary<string, string> urlsWithNames, 
            Dictionary<string, int> versions = null,
            CancellationToken cancellationToken = default)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(AtlasWebLoader));
            }

            var totalCount = urlsWithNames.Count;
            Debug.Log($"[AtlasWebLoader.DownloadAndAddBatchAsync] ===== START BATCH DOWNLOAD ===== Total items: {totalCount}");
            
            var downloadFailures = new List<string>();
            var addFailures = new List<string>();

            // Download all textures (allow partial failures)
            Debug.Log($"[AtlasWebLoader.DownloadAndAddBatchAsync] Creating {totalCount} download tasks...");
            var downloadTasks = new List<Task<(string name, Texture2D texture)>>();

            foreach (var kvp in urlsWithNames)
            {
                downloadTasks.Add(DownloadTextureAsync(kvp.Key, kvp.Value, cancellationToken));
            }

            Debug.Log($"[AtlasWebLoader.DownloadAndAddBatchAsync] Waiting for all downloads to complete...");
            var downloadedTextures = await Task.WhenAll(downloadTasks);
            Debug.Log($"[AtlasWebLoader.DownloadAndAddBatchAsync] All download tasks completed");

            // Build batch dictionary (only successful downloads)
            Debug.Log($"[AtlasWebLoader.DownloadAndAddBatchAsync] Building texture batch from downloaded textures...");
            var textureBatch = new Dictionary<string, Texture2D>();
            foreach (var (name, texture) in downloadedTextures)
            {
                if (texture != null)
                {
                    textureBatch[name] = texture;
                    Debug.Log($"[AtlasWebLoader.DownloadAndAddBatchAsync] Added '{name}' to batch - {texture.width}x{texture.height}, format: {texture.format}");
                }
                else
                {
                    downloadFailures.Add(name);
                    Debug.LogWarning($"[AtlasWebLoader.DownloadAndAddBatchAsync] Failed to download '{name}'");
                }
            }

            Debug.Log($"[AtlasWebLoader.DownloadAndAddBatchAsync] Texture batch built: {textureBatch.Count} successful, {downloadFailures.Count} failed");
            
            var results = new Dictionary<string, Sprite>();

            // Only proceed with batch add if we have any successful downloads
            if (textureBatch.Count > 0)
            {
                // Add all to atlas in one batch (more efficient)
                // ✅ THREAD SAFETY: Lock atlas modifications to prevent concurrent writes
                Debug.Log($"[AtlasWebLoader.DownloadAndAddBatchAsync] Calling atlas.AddBatch with {textureBatch.Count} textures...");
                Debug.Log($"[AtlasWebLoader.DownloadAndAddBatchAsync] Memory before AddBatch: {System.GC.GetTotalMemory(false) / (1024 * 1024)}MB");

                Dictionary<string, AtlasEntry> entries;
                lock (_atlasLock)
                {
                    entries = versions != null ? _atlas.AddBatch(textureBatch, versions) : _atlas.AddBatch(textureBatch);
                }

                Debug.Log($"[AtlasWebLoader.DownloadAndAddBatchAsync] AddBatch completed, returned {entries.Count} entries");
                Debug.Log($"[AtlasWebLoader.DownloadAndAddBatchAsync] Memory after AddBatch: {System.GC.GetTotalMemory(false) / (1024 * 1024)}MB");

                // Create sprites from successful entries
                Debug.Log($"[AtlasWebLoader.DownloadAndAddBatchAsync] Creating sprites from entries...");
                foreach (var kvp in entries)
                {
                    if (kvp.Value != null && kvp.Value.IsValid)
                    {
                        Debug.Log($"[AtlasWebLoader.DownloadAndAddBatchAsync] Creating sprite for '{kvp.Key}'...");
                        var sprite = kvp.Value.CreateSprite();
                        
                        // Verify sprite is valid before adding to results
                        if (sprite != null && sprite.texture != null)
                        {
                            results[kvp.Key] = sprite;
                            Debug.Log($"[AtlasWebLoader.DownloadAndAddBatchAsync] ✓ Sprite created successfully for '{kvp.Key}' - Texture: {sprite.texture.name}");
                        }
                        else
                        {
                            addFailures.Add(kvp.Key);
                            Debug.LogWarning($"[AtlasWebLoader.DownloadAndAddBatchAsync] Sprite invalid for '{kvp.Key}' - sprite: {sprite != null}, texture: {sprite?.texture != null}");
                        }
                    }
                    else
                    {
                        addFailures.Add(kvp.Key);
                        Debug.LogWarning($"[AtlasWebLoader.DownloadAndAddBatchAsync] Entry invalid for '{kvp.Key}'");
                    }
                }

                Debug.Log($"[AtlasWebLoader.DownloadAndAddBatchAsync] Sprites created: {results.Count}. Now cleaning up downloaded textures...");
                
                // Cleanup downloaded textures
                int cleanupCount = 0;
                foreach (var kvp in textureBatch)
                {
                    if (kvp.Value != null)
                    {
                        Debug.Log($"[AtlasWebLoader.DownloadAndAddBatchAsync] Destroying downloaded texture '{kvp.Key}'");
                        UnityEngine.Object.Destroy(kvp.Value);
                        cleanupCount++;
                    }
                }
                Debug.Log($"[AtlasWebLoader.DownloadAndAddBatchAsync] Cleanup complete: destroyed {cleanupCount} textures");
                Debug.Log($"[AtlasWebLoader.DownloadAndAddBatchAsync] Memory after cleanup: {System.GC.GetTotalMemory(false) / (1024 * 1024)}MB");
            }

            // Report summary
            var successCount = results.Count;
            var failureCount = totalCount - successCount;

            if (failureCount > 0)
            {
                Debug.LogWarning($"[AtlasWebLoader.DownloadAndAddBatchAsync] ===== BATCH COMPLETE (PARTIAL) ===== Success: {successCount}/{totalCount}, Failed: {failureCount}");
                if (downloadFailures.Count > 0)
                {
                    Debug.LogWarning($"[AtlasWebLoader.DownloadAndAddBatchAsync] Download failures: {string.Join(", ", downloadFailures)}");
                }
                if (addFailures.Count > 0)
                {
                    Debug.LogWarning($"[AtlasWebLoader.DownloadAndAddBatchAsync] Add failures: {string.Join(", ", addFailures)}");
                }
            }
            else
            {
                Debug.Log($"[AtlasWebLoader.DownloadAndAddBatchAsync] ===== BATCH COMPLETE (SUCCESS) ===== All {successCount} sprites added successfully");
            }

            return results;
        }

        /// <summary>
        /// Download multiple images as a batch but do NOT add them to the atlas.
        /// Returns a dictionary of downloaded textures that MUST be destroyed by the caller.
        /// </summary>
        /// <param name="urlsWithNames">Dictionary of URLs to sprite names</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>Dictionary mapping sprite names to their downloaded textures</returns>
        public async Task<Dictionary<string, Texture2D>> LoadBatchAsync(
            Dictionary<string, string> urlsWithNames, 
            CancellationToken cancellationToken = default)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(AtlasWebLoader));
            }

            // Download all textures
            var downloadTasks = new List<Task<(string name, Texture2D texture)>>();

            foreach (var kvp in urlsWithNames)
            {
                downloadTasks.Add(DownloadTextureAsync(kvp.Key, kvp.Value, cancellationToken));
            }

            var downloadedResults = await Task.WhenAll(downloadTasks);

            // Build result dictionary
            var textureDict = new Dictionary<string, Texture2D>();
            foreach (var (name, texture) in downloadedResults)
            {
                if (texture != null)
                {
                    textureDict[name] = texture;
                }
            }

            return textureDict;
        }

        private async Task<(string url, Sprite sprite)> GetSpriteWithUrlAsync(string url, CancellationToken cancellationToken)
        {
            var sprite = await GetSpriteAsync(url, null, cancellationToken);
            return (url, sprite);
        }

        private async Task<(string name, Texture2D texture)> DownloadTextureAsync(
            string url, 
            string name, 
            CancellationToken cancellationToken)
        {
            Debug.Log($"[AtlasWebLoader.DownloadTextureAsync] START - name: '{name}', url: {url}");
            Texture2D downloadedTexture = null;
            
            try
            {
                Debug.Log($"[AtlasWebLoader.DownloadTextureAsync] Creating web request for '{name}'");
                using (var request = UnityWebRequestTexture.GetTexture(url))
                {
                    Debug.Log($"[AtlasWebLoader.DownloadTextureAsync] Sending web request for '{name}'");
                    var operation = request.SendWebRequest();

                    // Wait for download to complete
                    while (!operation.isDone && !cancellationToken.IsCancellationRequested)
                    {
                        await Task.Yield();
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        Debug.LogWarning($"[AtlasWebLoader.DownloadTextureAsync] Request cancelled for '{name}'");
                        request.Abort();
                        return (name, null);
                    }

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        Debug.Log($"[AtlasWebLoader.DownloadTextureAsync] Download SUCCESS for '{name}', getting content...");
                        downloadedTexture = DownloadHandlerTexture.GetContent(request);
                        
                        if (downloadedTexture == null)
                        {
                            Debug.LogError($"[AtlasWebLoader.DownloadTextureAsync] DownloadHandlerTexture.GetContent returned NULL for '{name}'");
                            return (name, null);
                        }
                        
                        downloadedTexture.name = name;
                        Debug.Log($"[AtlasWebLoader.DownloadTextureAsync] Downloaded texture '{name}' - Size: {downloadedTexture.width}x{downloadedTexture.height}, Format: {downloadedTexture.format}, Memory: {(downloadedTexture.width * downloadedTexture.height * 4) / 1024}KB");

#if UNITY_IOS
                        // ✅ iOS CRITICAL FIX: Convert ARGB32 to RGBA32 to avoid Metal SIMD crash
                        // DownloadHandlerTexture may return ARGB32 textures for PNG images
                        // Metal's RemapSIMDWithPermute crashes when converting ARGB->RGBA during Apply()
                        if (downloadedTexture.format == TextureFormat.ARGB32)
                        {
                            Debug.Log($"[AtlasWebLoader.DownloadTextureAsync] iOS: ARGB32 detected for '{name}', converting to RGBA32...");

                            var pixels = downloadedTexture.GetPixels32();
                            var rgbaTexture = new Texture2D(downloadedTexture.width, downloadedTexture.height, TextureFormat.RGBA32, false);
                            rgbaTexture.name = name;
                            rgbaTexture.filterMode = downloadedTexture.filterMode;
                            rgbaTexture.wrapMode = downloadedTexture.wrapMode;
                            rgbaTexture.SetPixels32(pixels);
                            rgbaTexture.Apply(false, false);

                            // Destroy old texture and use converted one
                            UnityEngine.Object.Destroy(downloadedTexture);
                            downloadedTexture = rgbaTexture;

                            Debug.Log($"[AtlasWebLoader.DownloadTextureAsync] iOS: Conversion complete. New format: {downloadedTexture.format}");
                        }
#endif

                        // Return texture - caller is responsible for cleanup
                        Debug.Log($"[AtlasWebLoader.DownloadTextureAsync] Transferring texture ownership for '{name}' to caller");
                        var result = (name, downloadedTexture);
                        downloadedTexture = null; // Transfer ownership to caller
                        return result;
                    }
                    else
                    {
                        Debug.LogWarning($"[AtlasWebLoader.DownloadTextureAsync] Download FAILED for '{name}' from {url}: {request.error}");
                        return (name, null);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AtlasWebLoader.DownloadTextureAsync] EXCEPTION for '{name}': {ex.GetType().Name} - {ex.Message}\nStackTrace: {ex.StackTrace}");
                
                // Clean up texture on exception since we won't return it to caller
                if (downloadedTexture != null)
                {
                    Debug.LogWarning($"[AtlasWebLoader.DownloadTextureAsync] Destroying texture due to exception for '{name}'");
                    UnityEngine.Object.Destroy(downloadedTexture);
                }
                
                return (name, null);
            }
        }

        private async Task ProcessDownloadAsync(LoadRequest request, CancellationToken cancellationToken)
        {
            Texture2D downloadedTexture = null;
            
            try
            {
                // Download texture
                using (var webRequest = UnityWebRequestTexture.GetTexture(request.Url))
                {
                    var operation = webRequest.SendWebRequest();

                    // Wait for download to complete (non-blocking)
                    while (!operation.isDone && !cancellationToken.IsCancellationRequested)
                    {
                        await Task.Yield();
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        webRequest.Abort();
                        request.SetFailed("Cancelled");
                        OnDownloadFailed?.Invoke(request.Url, "Cancelled");
                        return;
                    }

                    if (webRequest.result == UnityWebRequest.Result.Success)
                    {
                        downloadedTexture = DownloadHandlerTexture.GetContent(webRequest);
                        downloadedTexture.name = request.SpriteName;

#if UNITY_IOS
                        // ✅ iOS CRITICAL FIX: Convert ARGB32 to RGBA32 to avoid Metal SIMD crash
                        // DownloadHandlerTexture may return ARGB32 textures for PNG images
                        // Metal's RemapSIMDWithPermute crashes when converting ARGB->RGBA during Apply()
                        if (downloadedTexture.format == TextureFormat.ARGB32)
                        {
                            Debug.Log($"[AtlasWebLoader] iOS: Converting downloaded texture '{request.SpriteName}' from ARGB32 to RGBA32...");

                            var pixels = downloadedTexture.GetPixels32();
                            var rgbaTexture = new Texture2D(downloadedTexture.width, downloadedTexture.height, TextureFormat.RGBA32, false);
                            rgbaTexture.name = request.SpriteName;
                            rgbaTexture.filterMode = downloadedTexture.filterMode;
                            rgbaTexture.wrapMode = downloadedTexture.wrapMode;
                            rgbaTexture.SetPixels32(pixels);
                            rgbaTexture.Apply(false, false);

                            // Destroy old texture and use converted one
                            UnityEngine.Object.Destroy(downloadedTexture);
                            downloadedTexture = rgbaTexture;

                            Debug.Log($"[AtlasWebLoader] iOS: Conversion complete. New format: {downloadedTexture.format}");
                        }
#endif

                        // Add to atlas
                        // ✅ THREAD SAFETY: Lock atlas modifications to prevent concurrent writes
                        AddResult result;
                        AtlasEntry entry;
                        lock (_atlasLock)
                        {
                            (result, entry) = _atlas.Add(request.SpriteName, downloadedTexture);
                        }

                        if (result == AddResult.Success && entry != null && entry.IsValid)
                        {
                            var sprite = entry.CreateSprite();
                            
                            // Verify sprite is valid
                            if (sprite != null && sprite.texture != null)
                            {
                                request.SetSuccess(sprite);
                                OnSpriteLoaded?.Invoke(request.Url, sprite);
#if UNITY_EDITOR
                                Debug.Log($"[AtlasWebLoader] ✓ Created sprite '{sprite.name}' - Texture: {sprite.texture.name}, Size: {sprite.rect.width}x{sprite.rect.height}");
#endif
                                // ✅ MEMORY FIX: Destroy texture AFTER successful atlas add
                                UnityEngine.Object.Destroy(downloadedTexture);
                                downloadedTexture = null;
                            }
                            else
                            {
                                request.SetFailed("Created sprite is invalid (null texture)");
                                OnDownloadFailed?.Invoke(request.Url, "Invalid sprite");
#if UNITY_EDITOR
                                Debug.LogWarning($"[AtlasWebLoader] Created sprite is invalid - sprite: {sprite != null}, texture: {sprite?.texture != null}");
#endif
                                // Clean up texture on failure
                                if (downloadedTexture != null)
                                {
                                    UnityEngine.Object.Destroy(downloadedTexture);
                                    downloadedTexture = null;
                                }
                            }
                        }
                        else
                        {
                            request.SetFailed($"Failed to add to atlas: {result}");
                            OnDownloadFailed?.Invoke(request.Url, result.ToString());
                            // Clean up texture on failure
                            if (downloadedTexture != null)
                            {
                                UnityEngine.Object.Destroy(downloadedTexture);
                                downloadedTexture = null;
                            }
                        }
                    }
                    else
                    {
                        request.SetFailed(webRequest.error);
                        OnDownloadFailed?.Invoke(request.Url, webRequest.error);
                    }
                }
            }
            catch (Exception ex)
            {
                request.SetFailed(ex.Message);
                OnDownloadFailed?.Invoke(request.Url, ex.Message);
                // Clean up texture on exception
                if (downloadedTexture != null)
                {
                    UnityEngine.Object.Destroy(downloadedTexture);
                    downloadedTexture = null;
                }
            }
            finally
            {
                // Clean up any remaining texture (safety net)
                if (downloadedTexture != null)
                {
                    Debug.LogWarning($"[AtlasWebLoader] Finally block cleanup for '{request.SpriteName}' - texture should have been cleaned up earlier");
                    UnityEngine.Object.Destroy(downloadedTexture);
                }
                
                // Remove from active requests
                lock (_activeRequests)
                {
                    _activeRequests.Remove(request.Url);
                    _activeDownloads--;

                    // Start next pending download if any
                    if (_pendingQueue.Count > 0)
                    {
                        var nextRequest = _pendingQueue.Dequeue();
                        _activeDownloads++;
                        _ = ProcessDownloadAsync(nextRequest, cancellationToken);
                    }
                }
            }
        }

        private string GetNameFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                var filename = System.IO.Path.GetFileNameWithoutExtension(uri.AbsolutePath);
                return string.IsNullOrEmpty(filename) ? $"Web_{url.GetHashCode():X}" : filename;
            }
            catch
            {
                return $"Web_{url.GetHashCode():X}";
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            // Cancel all pending requests
            lock (_activeRequests)
            {
                foreach (var request in _activeRequests.Values)
                {
                    request.SetFailed("Loader disposed");
                }
                _activeRequests.Clear();
                _pendingQueue.Clear();
            }
        }

        /// <summary>
        /// Internal class to track load requests and handle request deduplication.
        /// </summary>
        private class LoadRequest
        {
            public string Url { get; }
            public string SpriteName { get; }

            private readonly TaskCompletionSource<Sprite> _completionSource;
            private bool _isCompleted;

            public LoadRequest(string url, string spriteName)
            {
                Url = url;
                SpriteName = spriteName;
                _completionSource = new TaskCompletionSource<Sprite>();
                _isCompleted = false;
            }

            public async Task<Sprite> WaitForCompletionAsync(CancellationToken cancellationToken)
            {
                // Register cancellation
                using (cancellationToken.Register(() => _completionSource.TrySetCanceled()))
                {
                    return await _completionSource.Task;
                }
            }

            public void SetSuccess(Sprite sprite)
            {
                if (!_isCompleted)
                {
                    _isCompleted = true;
                    _completionSource.TrySetResult(sprite);
                }
            }

            public void SetFailed(string error)
            {
                if (!_isCompleted)
                {
                    _isCompleted = true;
                    _completionSource.TrySetResult(null);
                }
            }
        }
    }
}
