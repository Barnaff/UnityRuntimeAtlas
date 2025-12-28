using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace RuntimeAtlasPacker.Samples
{
    /// <summary>
    /// Example demonstrating save/load functionality for runtime atlases.
    /// Shows how to save an atlas to disk and load it back, then add new sprites.
    /// Auto-creates all required scene elements including Canvas, Camera, and EventSystem.
    /// Just drop this script into an empty scene and press Play!
    /// </summary>
    public class AtlasSaveLoadExample : MonoBehaviour
    {
        [Header("Example Options")]
        [SerializeField] private bool _autoRunExample = true;
        [SerializeField] private bool _showDebugLogs = true;
        [SerializeField] private float _stepDelay = 2f;
        
        [Header("Atlas Settings")]
        [SerializeField] private int _atlasSize = 1024;
        [SerializeField] private int _maxPages = 4;
        [SerializeField] private int _padding = 2;
        
        [Header("Random Images")]
        [SerializeField] private int _initialImageCount = 8;
        [SerializeField] private int _additionalImageCount = 4;
        [SerializeField] private int _imageSize = 128;
        [SerializeField] private int _imageSizeVariation = 50;
        
        [Header("UI Settings")]
        [SerializeField] private float _imageSize_UI = 150f;
        [SerializeField] private float _imageSpacing = 10f;
        [SerializeField] private int _imagesPerRow = 4;

        [Header("Save/Load")]
        [SerializeField] private string _savePath = "SavedAtlas";

        private RuntimeAtlas _atlas;
        private AtlasWebLoader _webLoader;
        private string _fullSavePath;
        private Transform _imageContainer;
        private Canvas _canvas;
        private ScrollRect _scrollRect;
        private Text _statusText;
        private CancellationTokenSource _cts;

        private void Start()
        {
            _fullSavePath = Path.Combine(Application.persistentDataPath, _savePath);
            LogDebug($"Save path: {_fullSavePath}");
            
            // Create scene elements
            CreateSceneElements();
            
            // Start example
            _cts = new CancellationTokenSource();
            
            if (_autoRunExample)
            {
                StartCoroutine(RunExample());
            }
            else
            {
                UpdateStatus("Ready. Use context menu to run steps manually.");
            }
        }

        private void CreateSceneElements()
        {
            // Create Camera if none exists
            if (Camera.main == null)
            {
                var cameraGO = new GameObject("Main Camera");
                var camera = cameraGO.AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
                cameraGO.transform.position = new Vector3(0, 0, -10);
                LogDebug("Created Main Camera");
            }

            // Create EventSystem if none exists
            if (FindObjectOfType<EventSystem>() == null)
            {
                var eventSystemGO = new GameObject("EventSystem");
                eventSystemGO.AddComponent<EventSystem>();
                eventSystemGO.AddComponent<StandaloneInputModule>();
                LogDebug("Created EventSystem");
            }

            // Create Canvas
            var canvasGO = new GameObject("AtlasSaveLoad_Canvas");
            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            // Create background panel
            var bgPanel = CreateUIPanel(canvasGO.transform, "Background");
            var bgImage = bgPanel.AddComponent<Image>();
            bgImage.color = new Color(0.15f, 0.15f, 0.15f, 1f);
            var bgRect = bgPanel.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            // Create status text at top
            var statusTextGO = new GameObject("StatusText");
            statusTextGO.transform.SetParent(canvasGO.transform, false);
            _statusText = statusTextGO.AddComponent<Text>();
            _statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _statusText.fontSize = 18;
            _statusText.alignment = TextAnchor.MiddleCenter;
            _statusText.color = Color.white;
            _statusText.text = "Atlas Save/Load Example";
            var statusRect = statusTextGO.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0, 1);
            statusRect.anchorMax = new Vector2(1, 1);
            statusRect.anchoredPosition = new Vector2(0, -30);
            statusRect.sizeDelta = new Vector2(-20, 50);

            // Create ScrollView
            var scrollViewGO = new GameObject("ScrollView");
            scrollViewGO.transform.SetParent(canvasGO.transform, false);
            var scrollViewRect = scrollViewGO.AddComponent<RectTransform>();
            scrollViewRect.anchorMin = new Vector2(0, 0);
            scrollViewRect.anchorMax = new Vector2(1, 1);
            scrollViewRect.offsetMin = new Vector2(10, 10);
            scrollViewRect.offsetMax = new Vector2(-10, -70);

            _scrollRect = scrollViewGO.AddComponent<ScrollRect>();
            var scrollImage = scrollViewGO.AddComponent<Image>();
            scrollImage.color = new Color(0.1f, 0.1f, 0.1f, 1f);

            // Create Viewport
            var viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(scrollViewGO.transform, false);
            var viewportRect = viewportGO.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            viewportGO.AddComponent<Mask>().showMaskGraphic = false;
            viewportGO.AddComponent<Image>();

            // Create Content
            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            var contentRect = contentGO.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0, 500);

            var gridLayout = contentGO.AddComponent<GridLayoutGroup>();
            gridLayout.cellSize = new Vector2(_imageSize_UI, _imageSize_UI);
            gridLayout.spacing = new Vector2(_imageSpacing, _imageSpacing);
            gridLayout.padding = new RectOffset(10, 10, 10, 10);
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = _imagesPerRow;
            gridLayout.childAlignment = TextAnchor.UpperLeft;

            var contentFitter = contentGO.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _imageContainer = contentGO.transform;
            _scrollRect.content = contentRect;
            _scrollRect.viewport = viewportRect;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;

            LogDebug("Scene elements created (Camera, EventSystem, Canvas, ScrollView)");
        }

        private GameObject CreateUIPanel(Transform parent, string name)
        {
            var panelGO = new GameObject(name);
            panelGO.transform.SetParent(parent, false);
            var rect = panelGO.AddComponent<RectTransform>();
            return panelGO;
        }

        private void UpdateStatus(string message)
        {
            if (_statusText != null)
            {
                _statusText.text = message;
            }
            LogDebug(message);
        }

        private void LogDebug(string message)
        {
            if (_showDebugLogs)
            {
                Debug.Log($"[AtlasSaveLoadExample] {message}");
            }
        }

        private IEnumerator RunExample()
        {
            // Step 1: Create atlas first
            UpdateStatus("Step 1: Creating empty atlas...");
            CreateEmptyAtlas();
            yield return new WaitForSeconds(_stepDelay);

            // Step 2: Download and add initial images using AtlasWebLoader
            UpdateStatus($"Step 2: Downloading and adding {_initialImageCount} images...");
            var downloadTask = DownloadAndAddImagesAsync(_initialImageCount);
            yield return new WaitUntil(() => downloadTask.IsCompleted);
            
            if (downloadTask.Exception != null)
            {
                UpdateStatus($"ERROR: {downloadTask.Exception.GetBaseException().Message}");
                yield break;
            }
            
            if (_atlas.EntryCount == 0)
            {
                UpdateStatus("ERROR: Failed to download any images!");
                yield break;
            }
            
            yield return new WaitForSeconds(_stepDelay);

            // Step 3: Display initial sprites
            UpdateStatus($"Step 3: Displaying {_atlas.EntryCount} sprites...");
            DisplayAllSprites();
            yield return new WaitForSeconds(_stepDelay);

            // Step 4: Save atlas to disk
            UpdateStatus("Step 4: Saving atlas to disk...");
            SaveAtlas();
            yield return new WaitForSeconds(_stepDelay);

            // Step 5: Dispose atlas and clear display
            UpdateStatus("Step 5: Clearing atlas...");
            DisposeAtlasAndClearDisplay();
            yield return new WaitForSeconds(_stepDelay);

            // Step 6: Load atlas from disk
            UpdateStatus("Step 6: Loading atlas from disk...");
            LoadAtlas();
            yield return new WaitForSeconds(_stepDelay);

            // Step 7: Display loaded sprites
            UpdateStatus($"Step 7: Displaying {_atlas.EntryCount} loaded sprites...");
            DisplayAllSprites();
            yield return new WaitForSeconds(_stepDelay);

            // Step 8: Download and add additional images
            UpdateStatus($"Step 8: Downloading {_additionalImageCount} more images...");
            var additionalTask = DownloadAndAddImagesAsync(_additionalImageCount);
            yield return new WaitUntil(() => additionalTask.IsCompleted);
            
            if (additionalTask.Exception != null)
            {
                UpdateStatus($"ERROR: {additionalTask.Exception.GetBaseException().Message}");
            }
            
            yield return new WaitForSeconds(_stepDelay);

            // Step 9: Display all sprites (original + new)
            UpdateStatus($"Step 9: Displaying all {_atlas.EntryCount} sprites...");
            DisplayAllSprites();
            yield return new WaitForSeconds(_stepDelay);

            // Step 10: Save updated atlas
            UpdateStatus("Step 10: Saving updated atlas...");
            SaveAtlas();
            yield return new WaitForSeconds(_stepDelay);

            UpdateStatus($"✓ Complete! {_atlas.EntryCount} entries, {_atlas.PageCount} page(s)");
        }

        private async Task DownloadAndAddImagesAsync(int count)
        {
            LogDebug($"Downloading and adding {count} random images using AtlasWebLoader...");

            // Generate URLs with names
            var urlsWithNames = new Dictionary<string, string>();
            for (var i = 0; i < count; i++)
            {
                var size = _imageSize + UnityEngine.Random.Range(-_imageSizeVariation, _imageSizeVariation + 1);
                size = Mathf.Clamp(size, 64, 512);
                var randomIndex = UnityEngine.Random.Range(1, 99999);
                var url = $"https://picsum.photos/{size}/{size}?random={randomIndex}";
                var imageName = $"RandomImage_{i}_{size}x{size}";
                
                urlsWithNames[url] = imageName;
            }

            // Use AtlasWebLoader to download and add all images in batch
            try
            {
                var entries = await _atlas.DownloadAndAddBatchAsync(urlsWithNames, maxConcurrentDownloads: 4, _cts.Token);
                
                var successCount = entries.Count;
                LogDebug($"✓ Successfully downloaded and added {successCount}/{count} images");
                UpdateStatus($"Downloaded and added {successCount}/{count} images");
            }
            catch (Exception ex)
            {
                LogDebug($"✗ Error downloading images: {ex.Message}");
                UpdateStatus($"Error: {ex.Message}");
            }
        }

        private void CreateEmptyAtlas()
        {
            var settings = new AtlasSettings
            {
                InitialSize = _atlasSize,
                MaxSize = _atlasSize,
                MaxPageCount = _maxPages,
                Padding = _padding,
                Format = TextureFormat.RGBA32,
                FilterMode = FilterMode.Bilinear,
                GenerateMipMaps = false,
                Readable = true,
                GrowthStrategy = GrowthStrategy.Double,
                Algorithm = PackingAlgorithm.MaxRects,
                RepackOnAdd = false,
                EnableSpriteCache = true
            };

            _atlas = new RuntimeAtlas(settings);
            _atlas.DebugName = "SaveLoadExample_Atlas";

            LogDebug("Created empty atlas");
        }

        private void SaveAtlas()
        {
            if (_atlas == null)
            {
                Debug.LogError("[AtlasSaveLoadExample] No atlas to save!");
                return;
            }

            var success = _atlas.Save(_fullSavePath);
            if (success)
            {
                Debug.Log($"[AtlasSaveLoadExample] ✓ Atlas saved successfully to: {_fullSavePath}");
                Debug.Log($"[AtlasSaveLoadExample]   - {_atlas.EntryCount} entries");
                Debug.Log($"[AtlasSaveLoadExample]   - {_atlas.PageCount} page(s)");
            }
            else
            {
                Debug.LogError("[AtlasSaveLoadExample] ✗ Failed to save atlas!");
            }
        }

        private void LoadAtlas()
        {
            _atlas = RuntimeAtlas.Load(_fullSavePath);
            
            if (_atlas != null)
            {
                Debug.Log($"[AtlasSaveLoadExample] ✓ Atlas loaded successfully from: {_fullSavePath}");
                Debug.Log($"[AtlasSaveLoadExample]   - {_atlas.EntryCount} entries");
                Debug.Log($"[AtlasSaveLoadExample]   - {_atlas.PageCount} page(s)");
            }
            else
            {
                Debug.LogError("[AtlasSaveLoadExample] ✗ Failed to load atlas!");
            }
        }

        private void DisplayAllSprites()
        {
            // Clear existing sprites
            ClearDisplay();

            if (_atlas == null || _imageContainer == null)
            {
                return;
            }

            var entries = _atlas.GetAllEntries();
            var index = 0;

            foreach (var entry in entries)
            {
                if (!entry.IsValid)
                {
                    continue;
                }

                var sprite = entry.CreateSprite(100f);
                if (sprite == null)
                {
                    continue;
                }

                // Create UI Image
                var imageGO = new GameObject($"Image_{entry.Name}");
                imageGO.transform.SetParent(_imageContainer, false);
                
                var image = imageGO.AddComponent<Image>();
                image.sprite = sprite;
                image.preserveAspect = true;

                var rectTransform = imageGO.GetComponent<RectTransform>();
                rectTransform.sizeDelta = new Vector2(_imageSize_UI, _imageSize_UI);

                index++;
            }

            LogDebug($"Displayed {index} sprites");
        }

        private void ClearDisplay()
        {
            if (_imageContainer == null)
            {
                return;
            }

            var childCount = _imageContainer.childCount;
            for (var i = childCount - 1; i >= 0; i--)
            {
                var child = _imageContainer.GetChild(i);
                if (Application.isPlaying)
                {
                    Destroy(child.gameObject);
                }
                else
                {
                    DestroyImmediate(child.gameObject);
                }
            }
        }

        private void DisposeAtlasAndClearDisplay()
        {
            ClearDisplay();
            
            if (_atlas != null)
            {
                _atlas.Dispose();
                _atlas = null;
                LogDebug("Atlas disposed");
            }
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            
            _webLoader?.Dispose();
            
            if (_atlas != null)
            {
                _atlas.Dispose();
                _atlas = null;
            }
            
            // Canvas will be destroyed automatically with the GameObject
        }

        // Editor buttons for testing
#if UNITY_EDITOR
        [ContextMenu("Create Atlas and Download Images")]
        private void EditorDownloadAndCreate()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("This feature only works in Play Mode");
                return;
            }
            
            StartCoroutine(EditorRunDownload());
        }

        private IEnumerator EditorRunDownload()
        {
            CreateEmptyAtlas();
            var task = DownloadAndAddImagesAsync(_initialImageCount);
            yield return new WaitUntil(() => task.IsCompleted);
            DisplayAllSprites();
        }

        [ContextMenu("Save Current Atlas")]
        private void EditorSave()
        {
            SaveAtlas();
        }

        [ContextMenu("Load Atlas")]
        private void EditorLoad()
        {
            DisposeAtlasAndClearDisplay();
            LoadAtlas();
            DisplayAllSprites();
        }


        [ContextMenu("Clear Display")]
        private void EditorClear()
        {
            ClearDisplay();
        }
#endif
    }
}

