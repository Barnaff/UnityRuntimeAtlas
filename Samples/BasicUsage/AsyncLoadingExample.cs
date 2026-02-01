using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using RuntimeAtlasPacker;

namespace RuntimeAtlasPacker.Samples
{
    /// <summary>
    /// Example demonstrating async texture loading with progress tracking.
    /// Shows loading screens, progress bars, and cancellation.
    /// </summary>
    public class AsyncLoadingExample : MonoBehaviour
    {
        [Header("Loading UI")]
        public GameObject loadingPanel;
        public Slider progressBar;
        public Text progressText;
        public Text statusText;
        public Button cancelButton;
        
        [Header("Content")]
        public Transform contentContainer;
        public int imagesToLoad = 20;
        public int imageSize = 128;

        private RuntimeAtlas _atlas;
        private List<AtlasEntry> _entries = new();
        private List<GameObject> _spawnedObjects = new();
        private CancellationTokenSource _cts;
        private bool _isLoading;

        private void Start()
        {
            _atlas = new RuntimeAtlas(new AtlasSettings
            {
                InitialSize = 512,
                MaxSize = 4096,
                Padding = 2,
                Format = TextureFormat.RGBA32,
                FilterMode = FilterMode.Bilinear,
                GenerateMipMaps = false,
                Readable = false,
                GrowthStrategy = GrowthStrategy.Double,
                Algorithm = PackingAlgorithm.MaxRects
            });

            if (cancelButton != null)
            {
                cancelButton.onClick.AddListener(CancelLoading);
            }

            // Start loading automatically
            _ = StartLoading();
        }

        public async Task StartLoading()
        {
            if (_isLoading) return;
            _isLoading = true;

            _cts = new CancellationTokenSource();
            
            ShowLoadingUI(true);
            UpdateStatus("Preparing to load...");
            UpdateProgress(0, imagesToLoad);

            try
            {
                await LoadImagesWithProgress(_cts.Token);
                
                if (!_cts.Token.IsCancellationRequested)
                {
                    UpdateStatus("Creating display...");
                    await Task.Yield(); // Let UI update
                    CreateDisplay();
                    UpdateStatus("Complete!");
                }
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Loading cancelled");
                Debug.Log("Loading was cancelled by user");
            }
            catch (Exception e)
            {
                UpdateStatus($"Error: {e.Message}");
                Debug.LogError($"Loading failed: {e}");
            }
            finally
            {
                await Task.Delay(500); // Show final status briefly
                ShowLoadingUI(false);
                _isLoading = false;
            }
        }

        private async Task LoadImagesWithProgress(CancellationToken ct)
        {
            var loadedTextures = new List<Texture2D>();

            for (int i = 0; i < imagesToLoad; i++)
            {
                ct.ThrowIfCancellationRequested();

                UpdateStatus($"Downloading image {i + 1}/{imagesToLoad}...");
                UpdateProgress(i, imagesToLoad);

                // Download image
                string url = $"https://picsum.photos/{imageSize}/{imageSize}?random={i + UnityEngine.Random.Range(1000, 9999)}";
                var texture = await DownloadTextureAsync(url, ct);

                if (texture != null)
                {
                    loadedTextures.Add(texture);
                }
                else
                {
                    Debug.LogWarning($"Failed to download image {i + 1}");
                }

                // Small delay to prevent rate limiting
                await Task.Delay(100, ct);
            }

            ct.ThrowIfCancellationRequested();

            // Pack all textures
            UpdateStatus("Packing textures into atlas...");
            UpdateProgress(imagesToLoad - 1, imagesToLoad);

            if (loadedTextures.Count > 0)
            {
                // Use synchronous batch packing (it's fast)
                var entries = _atlas.AddBatch(loadedTextures.ToArray());
                _entries.AddRange(entries);

                // Cleanup source textures
                foreach (var tex in loadedTextures)
                {
                    Destroy(tex);
                }
            }

            UpdateProgress(imagesToLoad, imagesToLoad);
            Debug.Log($"Loaded {_entries.Count} images. Atlas: {_atlas.Width}x{_atlas.Height}");
        }

        private async Task<Texture2D> DownloadTextureAsync(string url, CancellationToken ct)
        {
            try
            {
                // ✅ IMPROVED: Use UnityWebRequest with DownloadHandlerBuffer for better texture lifecycle control
                var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET);
                request.downloadHandler = new DownloadHandlerBuffer();
                
                try
                {
                    var operation = request.SendWebRequest();

                    while (!operation.isDone)
                    {
                        ct.ThrowIfCancellationRequested();
                        await Task.Yield();
                    }

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        // Get raw image bytes
                        byte[] imageData = request.downloadHandler.data;
                        
                        if (imageData != null && imageData.Length > 0)
                        {
                            // Create texture and load image data
                            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                            
                            if (texture.LoadImage(imageData))
                            {
                                return texture;
                            }
                            else
                            {
                                Destroy(texture);
                                Debug.LogWarning("Failed to load image data");
                            }
                        }
                        else
                        {
                            Debug.LogWarning("Downloaded data is empty");
                        }
                    }
                }
                finally
                {
                    request?.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Download error: {e.Message}");
            }

            return null;
        }

        private void CreateDisplay()
        {
            if (contentContainer == null || _entries.Count == 0) return;

            int columns = Mathf.CeilToInt(Mathf.Sqrt(_entries.Count));
            float spacing = 1.5f;

            for (int i = 0; i < _entries.Count; i++)
            {
                int row = i / columns;
                int col = i % columns;

                var go = new GameObject($"Image_{i}");
                go.transform.SetParent(contentContainer);
                go.transform.localPosition = new Vector3(
                    (col - columns / 2f) * spacing,
                    -(row - _entries.Count / columns / 2f) * spacing,
                    0
                );

                var renderer = go.AddComponent<AtlasSpriteRenderer>();
                renderer.PixelsPerUnit = imageSize;
                renderer.SetEntry(_entries[i]);

                _spawnedObjects.Add(go);
            }
        }

        public void CancelLoading()
        {
            _cts?.Cancel();
        }

        private void ShowLoadingUI(bool show)
        {
            if (loadingPanel != null)
                loadingPanel.SetActive(show);
        }

        private void UpdateProgress(int current, int total)
        {
            if (progressBar != null)
                progressBar.value = (float)current / total;
            
            if (progressText != null)
                progressText.text = $"{current} / {total}";
        }

        private void UpdateStatus(string status)
        {
            if (statusText != null)
                statusText.text = status;
        }

        /// <summary>
        /// Reload all content.
        /// </summary>
        public async Task Reload()
        {
            // Clear existing
            foreach (var go in _spawnedObjects)
            {
                if (go != null) Destroy(go);
            }
            _spawnedObjects.Clear();

            foreach (var entry in _entries)
            {
                entry.Remove();
            }
            _entries.Clear();

            // Reload
            await StartLoading();
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _atlas?.Dispose();
        }
    }

    /// <summary>
    /// Example of loading textures from Resources folder asynchronously.
    /// </summary>
    public class ResourcesAsyncLoading : MonoBehaviour
    {
        [Header("Settings")]
        public string resourcePath = "";
        public bool loadOnStart = true;

        [Header("Events")]
        public UnityEngine.Events.UnityEvent<float> onProgress;
        public UnityEngine.Events.UnityEvent<AtlasEntry[]> onComplete;
        public UnityEngine.Events.UnityEvent<string> onError;

        private RuntimeAtlas _atlas;
        private List<AtlasEntry> _entries = new();

        private async void Start()
        {
            _atlas = new RuntimeAtlas();

            if (loadOnStart)
            {
                await LoadFromResources();
            }
        }

        public async Task<AtlasEntry[]> LoadFromResources()
        {
            try
            {
                onProgress?.Invoke(0);

                // Load all textures from Resources
                var resourceRequest = Resources.LoadAsync<Texture2D>(resourcePath);
                
                // For loading all textures, we need to use LoadAll synchronously
                // But we can still do the packing asynchronously
                var textures = Resources.LoadAll<Texture2D>(resourcePath);

                if (textures.Length == 0)
                {
                    onError?.Invoke("No textures found in Resources");
                    return Array.Empty<AtlasEntry>();
                }

                onProgress?.Invoke(0.5f);

                // Pack synchronously (texture packing is fast)
                var entries = _atlas.AddBatch(textures);
                _entries.AddRange(entries);

                onProgress?.Invoke(1f);
                onComplete?.Invoke(entries);

                Debug.Log($"Loaded {entries.Length} textures from Resources. Atlas: {_atlas.Width}x{_atlas.Height}");
                return entries;
            }
            catch (Exception e)
            {
                onError?.Invoke(e.Message);
                return Array.Empty<AtlasEntry>();
            }
        }

        public AtlasEntry GetEntry(int index)
        {
            return index >= 0 && index < _entries.Count ? _entries[index] : null;
        }

        public RuntimeAtlas Atlas => _atlas;

        private void OnDestroy()
        {
            _atlas?.Dispose();
        }
    }

    /// <summary>
    /// Example showing streaming texture loading for large collections.
    /// </summary>
    public class StreamingTextureLoader : MonoBehaviour
    {
        [Header("Settings")]
        public int batchSize = 5;
        public float delayBetweenBatches = 0.1f;

        private RuntimeAtlas _atlas;
        private Queue<Texture2D> _pendingTextures = new();
        private bool _isProcessing;

        public event Action<AtlasEntry> OnEntryAdded;
        public event Action OnBatchComplete;

        private void Awake()
        {
            _atlas = new RuntimeAtlas(new AtlasSettings
            {
                InitialSize = 1024,
                MaxSize = 4096,
                Padding = 2,
                Format = TextureFormat.RGBA32,
                FilterMode = FilterMode.Bilinear,
                GenerateMipMaps = false,
                Readable = false,
                Algorithm = PackingAlgorithm.MaxRects,
                GrowthStrategy = GrowthStrategy.Double
            });
        }

        /// <summary>
        /// Queue a texture for streaming load.
        /// </summary>
        public void QueueTexture(Texture2D texture)
        {
            _pendingTextures.Enqueue(texture);
            
            if (!_isProcessing)
            {
                _ = ProcessQueue();
            }
        }

        /// <summary>
        /// Queue multiple textures.
        /// </summary>
        public void QueueTextures(IEnumerable<Texture2D> textures)
        {
            foreach (var tex in textures)
            {
                _pendingTextures.Enqueue(tex);
            }

            if (!_isProcessing)
            {
                _ = ProcessQueue();
            }
        }

        private async Task ProcessQueue()
        {
            _isProcessing = true;

            while (_pendingTextures.Count > 0)
            {
                // Process batch
                var batch = new List<Texture2D>();
                
                for (int i = 0; i < batchSize && _pendingTextures.Count > 0; i++)
                {
                    batch.Add(_pendingTextures.Dequeue());
                }

                // Add to atlas
                var entries = _atlas.AddBatch(batch.ToArray());
                
                foreach (var entry in entries)
                {
                    OnEntryAdded?.Invoke(entry);
                }

                OnBatchComplete?.Invoke();

                // Yield to prevent frame drops
                if (delayBetweenBatches > 0)
                {
                    await Task.Delay((int)(delayBetweenBatches * 1000));
                }
                else
                {
                    await Task.Yield();
                }
            }

            _isProcessing = false;
        }

        public RuntimeAtlas Atlas => _atlas;
        public int PendingCount => _pendingTextures.Count;
        public bool IsProcessing => _isProcessing;

        private void OnDestroy()
        {
            _atlas?.Dispose();
        }
    }
}
