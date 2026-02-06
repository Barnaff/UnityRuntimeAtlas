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
        /// Uses batch add for better performance. Handles partial failures gracefully.
        /// </summary>
        /// <param name="urlsWithNames">Dictionary of URLs to sprite names</param>
        /// <param name="versions">Optional dictionary of name to version mappings</param>
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
            var downloadFailures = new List<string>();
            var addFailures = new List<string>();

            // Download all textures (allow partial failures)
            var downloadTasks = new List<Task<(string name, Texture2D texture)>>();

            foreach (var kvp in urlsWithNames)
            {
                downloadTasks.Add(DownloadTextureAsync(kvp.Key, kvp.Value, cancellationToken));
            }

            var downloadedTextures = await Task.WhenAll(downloadTasks);

            // Build batch dictionary (only successful downloads)
            var textureBatch = new Dictionary<string, Texture2D>();
            foreach (var (name, texture) in downloadedTextures)
            {
                if (texture != null)
                {
                    textureBatch[name] = texture;
                }
                else
                {
                    downloadFailures.Add(name);
#if UNITY_EDITOR
                    Debug.LogWarning($"[AtlasWebLoader] Failed to download '{name}'");
#endif
                }
            }

            var results = new Dictionary<string, Sprite>();

            // Only proceed with batch add if we have any successful downloads
            if (textureBatch.Count > 0)
            {
                // Add all to atlas in one batch (more efficient)
                Dictionary<string, AtlasEntry> entries;
                lock (_atlasLock)
                {
                    entries = versions != null ? _atlas.AddBatch(textureBatch, versions) : _atlas.AddBatch(textureBatch);
                }

                // Create sprites from successful entries
                foreach (var kvp in entries)
                {
                    if (kvp.Value != null && kvp.Value.IsValid)
                    {
                        var sprite = kvp.Value.CreateSprite();

                        // Verify sprite is valid before adding to results
                        if (sprite != null && sprite.texture != null)
                        {
                            results[kvp.Key] = sprite;
                        }
                        else
                        {
                            addFailures.Add(kvp.Key);
#if UNITY_EDITOR
                            Debug.LogWarning($"[AtlasWebLoader] Sprite invalid for '{kvp.Key}'");
#endif
                        }
                    }
                    else
                    {
                        addFailures.Add(kvp.Key);
#if UNITY_EDITOR
                        Debug.LogWarning($"[AtlasWebLoader] Entry invalid for '{kvp.Key}'");
#endif
                    }
                }

#if UNITY_IOS
                // iOS: Flush GPU before destroying textures.
                // Apply() queues GPU uploads asynchronously - destroying textures before
                // upload completes causes EXC_RESOURCE crash in UploadTextureData.
                GL.Flush();
#endif

                // Cleanup downloaded textures
                foreach (var kvp in textureBatch)
                {
                    if (kvp.Value != null)
                    {
                        UnityEngine.Object.Destroy(kvp.Value);
                    }
                }
            }

            // Report summary for partial failures
            var failureCount = totalCount - results.Count;
            if (failureCount > 0)
            {
                Debug.LogWarning($"[AtlasWebLoader] Batch complete: {results.Count}/{totalCount} succeeded");
#if UNITY_EDITOR
                if (downloadFailures.Count > 0)
                {
                    Debug.LogWarning($"[AtlasWebLoader] Download failures: {string.Join(", ", downloadFailures)}");
                }
                if (addFailures.Count > 0)
                {
                    Debug.LogWarning($"[AtlasWebLoader] Add failures: {string.Join(", ", addFailures)}");
                }
#endif
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
            Texture2D downloadedTexture = null;

            try
            {
                using (var request = UnityWebRequestTexture.GetTexture(url))
                {
                    var operation = request.SendWebRequest();

                    // Wait for download to complete
                    while (!operation.isDone && !cancellationToken.IsCancellationRequested)
                    {
                        await Task.Yield();
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        request.Abort();
                        return (name, null);
                    }

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        downloadedTexture = DownloadHandlerTexture.GetContent(request);

                        if (downloadedTexture == null)
                        {
                            Debug.LogError($"[AtlasWebLoader] DownloadHandlerTexture.GetContent returned NULL for '{name}'");
                            return (name, null);
                        }

                        downloadedTexture.name = name;

                        // Return texture - caller is responsible for cleanup
                        var result = (name, downloadedTexture);
                        downloadedTexture = null; // Transfer ownership to caller
                        return result;
                    }
                    else
                    {
                        Debug.LogWarning($"[AtlasWebLoader] Download failed for '{name}' from {url}: {request.error}");
                        return (name, null);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AtlasWebLoader] Exception downloading '{name}': {ex.GetType().Name} - {ex.Message}");

                // Clean up texture on exception since we won't return it to caller
                if (downloadedTexture != null)
                {
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

                        // Add to atlas
                        AddResultType result;
                        AtlasEntry entry;
                        lock (_atlasLock)
                        {
                            (result, entry) = _atlas.Add(request.SpriteName, downloadedTexture);
                        }

                        if (result == AddResultType.Success && entry != null && entry.IsValid)
                        {
                            var sprite = entry.CreateSprite();

                            // Verify sprite is valid
                            if (sprite != null && sprite.texture != null)
                            {
                                request.SetSuccess(sprite);
                                OnSpriteLoaded?.Invoke(request.Url, sprite);
#if UNITY_IOS
                                // iOS: Flush GPU before destroying texture
                                GL.Flush();
#endif
                                // Destroy texture AFTER successful atlas add
                                UnityEngine.Object.Destroy(downloadedTexture);
                                downloadedTexture = null;
                            }
                            else
                            {
                                request.SetFailed("Created sprite is invalid (null texture)");
                                OnDownloadFailed?.Invoke(request.Url, "Invalid sprite");
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
