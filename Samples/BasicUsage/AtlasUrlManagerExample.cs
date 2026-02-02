using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using RuntimeAtlasPacker;

namespace RuntimeAtlasPacker.Samples
{
    /// <summary>
    /// Example demonstrating URL-based image loading with UI controls.
    /// Features: Random URL selection, batch/single downloads, save/load, and scrollable display.
    /// </summary>
    public class AtlasUrlManagerExample : MonoBehaviour
    {
        [Header("URL Configuration")]
        [SerializeField] private TextAsset _urlListFile;
        
        [Header("Atlas Settings")]
        [SerializeField] private int _atlasSize = 2048;
        [SerializeField] private int _maxPages = 10;
        [SerializeField] private int _padding = 2;
        
        [Header("UI Settings")]
        [SerializeField] private float _thumbnailSize = 120f;
        [SerializeField] private float _spacing = 10f;
        [SerializeField] private int _columns = 5;
        
        [Header("Save Path")]
        [SerializeField] private string _savePath = "AtlasUrlManager";
        
        // Runtime variables
        private RuntimeAtlas _atlas;
        private List<string> _availableUrls = new List<string>();
        private HashSet<string> _usedUrls = new HashSet<string>();
        private CancellationTokenSource _cts;
        private string _fullSavePath;
        
        // UI Components
        private Canvas _canvas;
        private ScrollRect _scrollRect;
        private Transform _contentContainer;
        private Text _statusText;
        private Button _batchButton;
        private Button _singleButton;
        private Button _saveButton;
        private Button _unloadButton;
        private Button _loadButton;
        
        private void Start()
        {
            _cts = new CancellationTokenSource();
            _fullSavePath = Path.Combine(Application.persistentDataPath, _savePath);
            
            Debug.Log($"[AtlasUrlManager] Save path: {_fullSavePath}");
            Debug.Log($"[AtlasUrlManager] Will save as: {_fullSavePath}.json and {_fullSavePath}_pageX.png");
            
            // Load URLs from file
            LoadUrlsFromFile();
            
            // Create UI
            CreateUI();
            
            // Create empty atlas
            CreateEmptyAtlas();
            
            UpdateStatus($"Ready! Loaded {_availableUrls.Count} URLs. Used: {_usedUrls.Count}");
        }
        
        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _atlas?.Dispose();
        }
        
        #region URL Management
        
        private void LoadUrlsFromFile()
        {
            _availableUrls.Clear();
            
            if (_urlListFile == null || string.IsNullOrEmpty(_urlListFile.text))
            {
                Debug.LogWarning("[AtlasUrlManager] No URL list file provided!");
                return;
            }
            
            var lines = _urlListFile.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                
                // Skip comments and empty lines
                if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
                    continue;
                
                // Validate URL
                if (trimmedLine.StartsWith("http://") || trimmedLine.StartsWith("https://"))
                {
                    _availableUrls.Add(trimmedLine);
                }
            }
            
            Debug.Log($"[AtlasUrlManager] Loaded {_availableUrls.Count} valid URLs");
        }
        
        private List<string> GetRandomUnusedUrls(int count)
        {
            var unusedUrls = _availableUrls.Where(url => !_usedUrls.Contains(url)).ToList();
            
            if (unusedUrls.Count == 0)
            {
                Debug.LogWarning("[AtlasUrlManager] All URLs have been used!");
                return new List<string>();
            }
            
            // Shuffle and take random URLs
            var random = new System.Random();
            var selectedUrls = unusedUrls.OrderBy(x => random.Next()).Take(count).ToList();
            
            // Mark as used
            foreach (var url in selectedUrls)
            {
                _usedUrls.Add(url);
            }
            
            return selectedUrls;
        }
        
        #endregion
        
        #region Atlas Operations
        
        private void CreateEmptyAtlas()
        {
            var settings = new AtlasSettings
            {
                InitialSize = _atlasSize,
                MaxSize = _atlasSize,
                MaxPageCount = _maxPages,
                Padding = _padding,
                Format = AtlasSettings.DefaultFormat,
                FilterMode = FilterMode.Bilinear,
                GenerateMipMaps = false,
                Readable = true,
                GrowthStrategy = GrowthStrategy.Double,
                Algorithm = PackingAlgorithm.MaxRects,
                RepackOnAdd = false,
                EnableSpriteCache = true,
                UseRenderTextures = true
            };
            
            _atlas?.Dispose();
            _atlas = new RuntimeAtlas(settings);
            _atlas.DebugName = "AtlasUrlManager_Atlas";
            
            Debug.Log("[AtlasUrlManager] Created empty atlas");
        }
        
        private void EnsureAtlasInitialized()
        {
            if (_atlas == null)
            {
                Debug.Log("[AtlasUrlManager] Atlas not initialized, creating new atlas...");
                CreateEmptyAtlas();
                UpdateStatus("🔧 Atlas auto-created");
            }
        }
        
        private async void DownloadBatchImages()
        {
            // Ensure atlas is initialized
            EnsureAtlasInitialized();
            
            DisableButtons();
            UpdateStatus("⏳ Downloading 10 images (batch)...");
            
            try
            {
                var urls = GetRandomUnusedUrls(10);
                if (urls.Count == 0)
                {
                    UpdateStatus("⚠️ No unused URLs available!");
                    EnableButtons();
                    return;
                }
                
                var urlsWithNames = new Dictionary<string, string>();
                for (int i = 0; i < urls.Count; i++)
                {
                    var url = urls[i];
                    var imageName = $"Batch_{_usedUrls.Count - urls.Count + i}_{GetUrlFileName(url)}";
                    urlsWithNames[url] = imageName;
                }
                
                // Since batch API doesn't support callbacks, we'll download one by one for progressive display
                // This provides better visual feedback
                int successCount = 0;
                int totalCount = urlsWithNames.Count;
                
                foreach (var kvp in urlsWithNames)
                {
                    var url = kvp.Key;
                    var imageName = kvp.Value;
                    
                    UpdateStatus($"⏳ Downloading {successCount + 1}/{totalCount} images (batch)...");
                    
                    try
                    {
                        var entry = await _atlas.DownloadAndAddAsync(
                            url,
                            key: imageName,
                            version: 0,
                            cancellationToken: _cts.Token
                        );
                        
                        if (entry != null && entry.IsValid)
                        {
                            successCount++;
                            Debug.Log($"[AtlasUrlManager] Entry created: {entry.Name}, Texture: {entry.Texture?.name}, Valid: {entry.IsValid}");
                            
                            // Display image immediately after download
                            CreateImageThumbnail(entry);
                        }
                        else
                        {
                            Debug.LogWarning($"[AtlasUrlManager] Entry is null or invalid for {imageName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[AtlasUrlManager] Error downloading '{imageName}': {ex.Message}");
                    }
                }
                
                Debug.Log($"[AtlasUrlManager] ✓ Batch downloaded {successCount} images");
                
                UpdateStatus($"✅ Added {successCount} images (batch). Total: {_atlas.EntryCount}, Used URLs: {_usedUrls.Count}/{_availableUrls.Count}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AtlasUrlManager] Error in batch download: {ex.Message}");
                UpdateStatus($"❌ Batch download failed: {ex.Message}");
            }
            finally
            {
                EnableButtons();
            }
        }
        
        private async void DownloadSingleImages()
        {
            // Ensure atlas is initialized
            EnsureAtlasInitialized();
            
            DisableButtons();
            
            var count = UnityEngine.Random.Range(3, 6); // 3-5 images
            UpdateStatus($"⏳ Downloading {count} images (single)...");
            
            try
            {
                var urls = GetRandomUnusedUrls(count);
                if (urls.Count == 0)
                {
                    UpdateStatus("⚠️ No unused URLs available!");
                    EnableButtons();
                    return;
                }
                
                int successCount = 0;
                
                for (int i = 0; i < urls.Count; i++)
                {
                    var url = urls[i];
                    var imageName = $"Single_{_usedUrls.Count - urls.Count + i}_{GetUrlFileName(url)}";
                    
                    UpdateStatus($"⏳ Downloading {i + 1}/{urls.Count} images (single)...");
                    
                    try
                    {
                        var entry = await _atlas.DownloadAndAddAsync(
                            url,
                            key: imageName,
                            version: 0,
                            cancellationToken: _cts.Token
                        );
                        
                        if (entry != null && entry.IsValid)
                        {
                            successCount++;
                            
                            Debug.Log($"[AtlasUrlManager] Entry created: {entry.Name}, Texture: {entry.Texture?.name}, Valid: {entry.IsValid}");
                            
                            // Display image immediately after download
                            CreateImageThumbnail(entry);
                            
                            Debug.Log($"[AtlasUrlManager] ✓ Downloaded '{imageName}'");
                        }
                        else
                        {
                            Debug.LogWarning($"[AtlasUrlManager] Entry is null or invalid for {imageName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[AtlasUrlManager] Error downloading '{imageName}': {ex.Message}");
                    }
                }
                
                UpdateStatus($"✅ Added {successCount}/{urls.Count} images (single). Total: {_atlas.EntryCount}, Used URLs: {_usedUrls.Count}/{_availableUrls.Count}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AtlasUrlManager] Error in single download: {ex.Message}");
                UpdateStatus($"❌ Single download failed: {ex.Message}");
            }
            finally
            {
                EnableButtons();
            }
        }
        
        private async void SaveAtlas()
        {
            if (_atlas == null || _atlas.EntryCount == 0)
            {
                UpdateStatus("⚠️ No atlas to save!");
                return;
            }
            
            DisableButtons();
            UpdateStatus("⏳ Saving atlas...");
            
            try
            {
                Debug.Log($"[AtlasUrlManager] Saving atlas with {_atlas.EntryCount} entries to: {_fullSavePath}");
                Debug.Log($"[AtlasUrlManager] Atlas has {_atlas.PageCount} page(s)");
                
                // Log texture readability before save
                for (int i = 0; i < _atlas.PageCount; i++)
                {
                    var tex = _atlas.GetTexture(i);
                    Debug.Log($"[AtlasUrlManager]   - Page {i}: {tex?.width}x{tex?.height}, Readable: {tex?.isReadable}, Format: {tex?.format}");
                }
                
                var success = await AtlasPersistence.SaveAtlasAsync(_atlas, _fullSavePath);
                
                if (success)
                {
                    Debug.Log($"[AtlasUrlManager] ✓ SaveAtlasAsync returned success");
                    
                    // Verify files were actually created
                    var jsonPath = $"{_fullSavePath}.json";
                    var jsonExists = File.Exists(jsonPath);
                    Debug.Log($"[AtlasUrlManager]   - JSON file exists: {jsonExists} ({jsonPath})");
                    
                    if (jsonExists)
                    {
                        var jsonInfo = new FileInfo(jsonPath);
                        Debug.Log($"[AtlasUrlManager]   - JSON file size: {jsonInfo.Length} bytes");
                    }
                    
                    for (int i = 0; i < _atlas.PageCount; i++)
                    {
                        var pagePath = $"{_fullSavePath}_page{i}.png";
                        var pageExists = File.Exists(pagePath);
                        Debug.Log($"[AtlasUrlManager]   - Page {i} file exists: {pageExists} ({pagePath})");
                        
                        if (pageExists)
                        {
                            var fileInfo = new FileInfo(pagePath);
                            Debug.Log($"[AtlasUrlManager]   - Page {i} file size: {fileInfo.Length / 1024}KB");
                        }
                    }
                    
                    if (jsonExists)
                    {
                        UpdateStatus($"✅ Atlas saved! {_atlas.EntryCount} entries, {_atlas.PageCount} page(s)");
                    }
                    else
                    {
                        UpdateStatus("❌ Save reported success but files not found!");
                        Debug.LogError("[AtlasUrlManager] Save returned true but JSON file was not created!");
                    }
                }
                else
                {
                    UpdateStatus("❌ Failed to save atlas!");
                    Debug.LogError("[AtlasUrlManager] SaveAtlasAsync returned false");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AtlasUrlManager] Save error: {ex.Message}\n{ex.StackTrace}");
                UpdateStatus($"❌ Save failed: {ex.Message}");
            }
            finally
            {
                EnableButtons();
            }
        }
        
        private void UnloadAtlas()
        {
            DisableButtons();
            UpdateStatus("⏳ Unloading atlas...");
            
            // Clear displayed images
            ClearDisplayedImages();
            
            // Dispose atlas
            _atlas?.Dispose();
            _atlas = null;
            
            Debug.Log("[AtlasUrlManager] ✓ Atlas unloaded");
            UpdateStatus("✅ Atlas unloaded and images cleared");
            
            EnableButtons();
        }
        
        private async void LoadAtlas()
        {
            DisableButtons();
            UpdateStatus("⏳ Loading atlas...");
            
            try
            {
                // Check if the atlas JSON file exists
                var jsonPath = $"{_fullSavePath}.json";
                Debug.Log($"[AtlasUrlManager] Looking for atlas at: {jsonPath}");
                
                if (!File.Exists(jsonPath))
                {
                    UpdateStatus("⚠️ No saved atlas found!");
                    Debug.LogWarning($"[AtlasUrlManager] Atlas JSON file not found: {jsonPath}");
                    
                    // Check if any page files exist
                    bool foundAnyPages = false;
                    for (int i = 0; i < 10; i++)
                    {
                        var pagePath = $"{_fullSavePath}_page{i}.png";
                        if (File.Exists(pagePath))
                        {
                            Debug.LogWarning($"[AtlasUrlManager] Found orphaned page file: {pagePath}");
                            foundAnyPages = true;
                        }
                    }
                    
                    if (foundAnyPages)
                    {
                        Debug.LogError("[AtlasUrlManager] Found page files but no JSON! Save may have been incomplete.");
                    }
                    
                    EnableButtons();
                    return;
                }
                
                Debug.Log($"[AtlasUrlManager] JSON file exists, loading...");
                
                // Check page files before loading
                var jsonContent = File.ReadAllText(jsonPath);
                Debug.Log($"[AtlasUrlManager] JSON content length: {jsonContent.Length} bytes");
                
                // Dispose old atlas
                _atlas?.Dispose();
                
                // Load atlas (LoadAtlasAsync only takes filePath, creates atlas with saved settings)
                _atlas = await AtlasPersistence.LoadAtlasAsync(_fullSavePath);
                
                if (_atlas != null)
                {
                    _atlas.DebugName = "AtlasUrlManager_Atlas_Loaded";
                    
                    Debug.Log($"[AtlasUrlManager] ✓ Atlas loaded successfully");
                    Debug.Log($"[AtlasUrlManager]   - Entries: {_atlas.EntryCount}");
                    Debug.Log($"[AtlasUrlManager]   - Pages: {_atlas.PageCount}");
                    
                    // Log each page's details
                    for (int i = 0; i < _atlas.PageCount; i++)
                    {
                        var tex = _atlas.GetTexture(i);
                        Debug.Log($"[AtlasUrlManager]   - Page {i}: {tex?.name}, {tex?.width}x{tex?.height}, Readable: {tex?.isReadable}");
                    }
                    
                    if (_atlas.EntryCount > 0)
                    {
                        // Display loaded images
                        Debug.Log($"[AtlasUrlManager] Displaying {_atlas.EntryCount} loaded images...");
                        DisplayAllImages();
                        
                        UpdateStatus($"✅ Atlas loaded! {_atlas.EntryCount} entries, {_atlas.PageCount} page(s)");
                    }
                    else
                    {
                        Debug.LogWarning("[AtlasUrlManager] Atlas loaded but has 0 entries!");
                        UpdateStatus("⚠️ Atlas loaded but empty!");
                    }
                }
                else
                {
                    Debug.LogError("[AtlasUrlManager] LoadAtlasAsync returned null!");
                    UpdateStatus("❌ Failed to load atlas!");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AtlasUrlManager] Load error: {ex.Message}\n{ex.StackTrace}");
                UpdateStatus($"❌ Load failed: {ex.Message}");
            }
            finally
            {
                EnableButtons();
            }
        }
        
        #endregion
        
        #region UI Creation
        
        private void CreateUI()
        {
            // Create Canvas if needed
            if (FindObjectOfType<Canvas>() == null)
            {
                var canvasGO = new GameObject("Canvas");
                _canvas = canvasGO.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                
                // Add CanvasScaler for responsive UI across all resolutions
                var scaler = canvasGO.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080); // Base resolution
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f; // Balance between width and height
                scaler.referencePixelsPerUnit = 100;
                
                canvasGO.AddComponent<GraphicRaycaster>();
            }
            else
            {
                _canvas = FindObjectOfType<Canvas>();
            }
            
            // Create EventSystem if needed
            if (FindObjectOfType<EventSystem>() == null)
            {
                var eventSystemGO = new GameObject("EventSystem");
                eventSystemGO.AddComponent<EventSystem>();
                eventSystemGO.AddComponent<StandaloneInputModule>();
            }
            
            // Create main panel
            var mainPanel = CreatePanel("AtlasUrlManager_Panel", _canvas.transform);
            var mainRect = mainPanel.GetComponent<RectTransform>();
            mainRect.anchorMin = Vector2.zero;
            mainRect.anchorMax = Vector2.one;
            mainRect.offsetMin = Vector2.zero;
            mainRect.offsetMax = Vector2.zero;
            
            // Add background
            var bg = mainPanel.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 1f);
            
            // Create control panel at top
            CreateControlPanel(mainPanel.transform);
            
            // Create status text
            CreateStatusText(mainPanel.transform);
            
            // Create scroll view for images
            CreateScrollView(mainPanel.transform);
            
            Debug.Log("[AtlasUrlManager] UI created");
        }
        
        private void CreateControlPanel(Transform parent)
        {
            var panel = CreatePanel("ControlPanel", parent);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0, 120); // Increased height for mobile
            
            // Add background
            var bg = panel.AddComponent<Image>();
            bg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            
            // Create button container with layout group for responsive buttons
            var buttonContainer = new GameObject("ButtonContainer");
            buttonContainer.transform.SetParent(panel.transform, false);
            var containerRect = buttonContainer.AddComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0, 0);
            containerRect.anchorMax = new Vector2(1, 1);
            containerRect.offsetMin = new Vector2(10, 10);
            containerRect.offsetMax = new Vector2(-10, -10);
            
            // Add HorizontalLayoutGroup for responsive button layout
            var layoutGroup = buttonContainer.AddComponent<HorizontalLayoutGroup>();
            layoutGroup.spacing = 10f;
            layoutGroup.childAlignment = TextAnchor.MiddleCenter;
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = true;
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = true;
            layoutGroup.padding = new RectOffset(10, 10, 10, 10);
            
            // Create buttons - they will auto-size thanks to layout group
            _batchButton = CreateResponsiveButton("BatchButton", buttonContainer.transform, "Batch (10)");
            _batchButton.onClick.AddListener(DownloadBatchImages);
            
            _singleButton = CreateResponsiveButton("SingleButton", buttonContainer.transform, "Single (3-5)");
            _singleButton.onClick.AddListener(DownloadSingleImages);
            
            _saveButton = CreateResponsiveButton("SaveButton", buttonContainer.transform, "Save");
            _saveButton.onClick.AddListener(SaveAtlas);
            
            _unloadButton = CreateResponsiveButton("UnloadButton", buttonContainer.transform, "Unload");
            _unloadButton.onClick.AddListener(UnloadAtlas);
            
            _loadButton = CreateResponsiveButton("LoadButton", buttonContainer.transform, "Load");
            _loadButton.onClick.AddListener(LoadAtlas);
        }
        
        private void CreateStatusText(Transform parent)
        {
            var textGO = new GameObject("StatusText");
            textGO.transform.SetParent(parent, false);
            
            var rect = textGO.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = new Vector2(0, -130); // Adjusted for new control panel height
            rect.sizeDelta = new Vector2(-20, 50);
            
            _statusText = textGO.AddComponent<Text>();
            _statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _statusText.fontSize = 20; // Slightly larger for better readability
            _statusText.alignment = TextAnchor.MiddleCenter;
            _statusText.color = Color.white;
            _statusText.text = "Initializing...";
            _statusText.resizeTextForBestFit = true; // Auto-resize text to fit
            _statusText.resizeTextMinSize = 14;
            _statusText.resizeTextMaxSize = 20;
        }
        
        private void CreateScrollView(Transform parent)
        {
            var scrollViewGO = new GameObject("ScrollView");
            scrollViewGO.transform.SetParent(parent, false);
            
            var scrollRect = scrollViewGO.AddComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0, 0);
            scrollRect.anchorMax = new Vector2(1, 1);
            scrollRect.pivot = new Vector2(0.5f, 0.5f);
            scrollRect.offsetMin = new Vector2(10, 10);
            scrollRect.offsetMax = new Vector2(-10, -190); // Adjusted for new control panel and status text height
            
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
            
            viewportGO.AddComponent<Image>();
            viewportGO.AddComponent<Mask>().showMaskGraphic = false;
            
            // Create Content
            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            
            var contentRect = contentGO.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0, 0);
            
            // Calculate responsive column count based on screen width
            // This will be recalculated dynamically but start with a reasonable default
            int calculatedColumns = CalculateResponsiveColumns();
            
            var gridLayout = contentGO.AddComponent<GridLayoutGroup>();
            gridLayout.cellSize = new Vector2(_thumbnailSize, _thumbnailSize);
            gridLayout.spacing = new Vector2(_spacing, _spacing);
            gridLayout.padding = new RectOffset(10, 10, 10, 10);
            gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
            gridLayout.childAlignment = TextAnchor.UpperLeft;
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = calculatedColumns;
            
            var contentFitter = contentGO.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            
            _contentContainer = contentGO.transform;
            
            // Configure ScrollRect
            _scrollRect.content = contentRect;
            _scrollRect.viewport = viewportRect;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.scrollSensitivity = 20f;
            
            Debug.Log($"[AtlasUrlManager] ScrollView created. Content container: {_contentContainer?.name}, Columns: {calculatedColumns}");
        }
        
        /// <summary>
        /// Calculate responsive column count based on screen width
        /// </summary>
        private int CalculateResponsiveColumns()
        {
            // Get available width (subtract padding and margins)
            float availableWidth = Screen.width - 40f; // 20px padding on each side
            
            // Calculate how many thumbnails fit (thumbnail size + spacing)
            float columnWidth = _thumbnailSize + _spacing;
            int columns = Mathf.Max(1, Mathf.FloorToInt(availableWidth / columnWidth));
            
            // Clamp to reasonable range
            columns = Mathf.Clamp(columns, 1, 10);
            
            Debug.Log($"[AtlasUrlManager] Calculated {columns} columns for screen width {Screen.width}");
            return columns;
        }
        
        private string GetGameObjectPath(GameObject obj)
        {
            if (obj == null) return "null";
            
            string path = obj.name;
            Transform parent = obj.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }
        
        private GameObject CreatePanel(string name, Transform parent)
        {
            var panelGO = new GameObject(name);
            panelGO.transform.SetParent(parent, false);
            panelGO.AddComponent<RectTransform>();
            return panelGO;
        }
        
        private Button CreateButton(string name, Transform parent, string text, Vector2 position, Vector2 size)
        {
            var buttonGO = new GameObject(name);
            buttonGO.transform.SetParent(parent, false);
            
            var rect = buttonGO.AddComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            
            var image = buttonGO.AddComponent<Image>();
            image.color = new Color(0.3f, 0.5f, 0.8f, 1f);
            
            var button = buttonGO.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = new Color(0.3f, 0.5f, 0.8f, 1f);
            colors.highlightedColor = new Color(0.4f, 0.6f, 0.9f, 1f);
            colors.pressedColor = new Color(0.2f, 0.4f, 0.7f, 1f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            button.colors = colors;
            
            // Add text
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(buttonGO.transform, false);
            
            var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(5, 5); // Padding
            textRect.offsetMax = new Vector2(-5, -5); // Padding
            
            var textComponent = textGO.AddComponent<Text>();
            textComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            textComponent.fontSize = 18;
            textComponent.alignment = TextAnchor.MiddleCenter;
            textComponent.color = Color.white;
            textComponent.text = text;
            textComponent.resizeTextForBestFit = true; // Auto-resize for mobile
            textComponent.resizeTextMinSize = 10;
            textComponent.resizeTextMaxSize = 18;
            
            return button;
        }
        
        /// <summary>
        /// Create a responsive button that works with layout groups
        /// </summary>
        private Button CreateResponsiveButton(string name, Transform parent, string text)
        {
            var buttonGO = new GameObject(name);
            buttonGO.transform.SetParent(parent, false);
            
            // Let the layout group control size, just add LayoutElement for min size
            var layoutElement = buttonGO.AddComponent<LayoutElement>();
            layoutElement.minWidth = 80f; // Minimum width for mobile
            layoutElement.minHeight = 60f; // Minimum height for mobile
            layoutElement.preferredWidth = 150f; // Preferred width
            layoutElement.preferredHeight = 80f; // Preferred height
            layoutElement.flexibleWidth = 1f; // Allow growth
            
            var image = buttonGO.AddComponent<Image>();
            image.color = new Color(0.3f, 0.5f, 0.8f, 1f);
            
            var button = buttonGO.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = new Color(0.3f, 0.5f, 0.8f, 1f);
            colors.highlightedColor = new Color(0.4f, 0.6f, 0.9f, 1f);
            colors.pressedColor = new Color(0.2f, 0.4f, 0.7f, 1f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            button.colors = colors;
            
            // Add text
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(buttonGO.transform, false);
            
            var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(5, 5); // Padding
            textRect.offsetMax = new Vector2(-5, -5); // Padding
            
            var textComponent = textGO.AddComponent<Text>();
            textComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            textComponent.fontSize = 18;
            textComponent.alignment = TextAnchor.MiddleCenter;
            textComponent.color = Color.white;
            textComponent.text = text;
            textComponent.resizeTextForBestFit = true; // Auto-resize for mobile
            textComponent.resizeTextMinSize = 10;
            textComponent.resizeTextMaxSize = 18;
            
            return button;
        }
        
        #endregion
        
        #region Display Management
        
        private void DisplayAllImages()
        {
            if (_atlas == null || _contentContainer == null)
                return;
            
            // Clear existing
            ClearDisplayedImages();
            
            // Get all entries as list to avoid multiple enumeration
            var entries = _atlas.GetAllEntries().ToList();
            
            Debug.Log($"[AtlasUrlManager] Displaying {entries.Count} images");
            
            foreach (var entry in entries)
            {
                CreateImageThumbnail(entry);
            }
        }
        
        private void CreateImageThumbnail(AtlasEntry entry)
        {
            if (_contentContainer == null)
            {
                Debug.LogError("[AtlasUrlManager] Content container is null! Cannot create thumbnail.");
                return;
            }
            
            if (entry == null || !entry.IsValid)
            {
                Debug.LogWarning("[AtlasUrlManager] Entry is null or invalid!");
                return;
            }
            
            Debug.Log($"[AtlasUrlManager] Creating thumbnail for '{entry.Name}' - Texture: {entry.Texture?.name}, Size: {entry.Width}x{entry.Height}");
            
            // Create container
            var thumbnailGO = new GameObject($"Thumbnail_{entry.Name}");
            thumbnailGO.transform.SetParent(_contentContainer, false);
            
            var rect = thumbnailGO.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(_thumbnailSize, _thumbnailSize);
            
            // Add background
            var bg = thumbnailGO.AddComponent<Image>();
            bg.color = new Color(0.25f, 0.25f, 0.25f, 1f);
            
            // Create sprite
            var sprite = entry.CreateSprite();
            if (sprite == null)
            {
                Debug.LogError($"[AtlasUrlManager] Failed to create sprite for {entry.Name}. Texture: {entry.Texture?.name}, Rect: {entry.UV}");
                // Still show the background so we can see something was created
                return;
            }
            
            Debug.Log($"[AtlasUrlManager] ✓ Sprite created: {sprite.name}, Texture: {sprite.texture?.name}, Rect: {sprite.rect}");
            
            // Create image
            var imageGO = new GameObject("Image");
            imageGO.transform.SetParent(thumbnailGO.transform, false);
            
            var imageRect = imageGO.AddComponent<RectTransform>();
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = new Vector2(5, 5);
            imageRect.offsetMax = new Vector2(-5, -5);
            
            var image = imageGO.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            
            Debug.Log($"[AtlasUrlManager] ✓ Thumbnail created and parented to: {_contentContainer.name}");
            
            // Add border
            var borderGO = new GameObject("Border");
            borderGO.transform.SetParent(thumbnailGO.transform, false);
            
            var borderRect = borderGO.AddComponent<RectTransform>();
            borderRect.anchorMin = Vector2.zero;
            borderRect.anchorMax = Vector2.one;
            borderRect.offsetMin = Vector2.zero;
            borderRect.offsetMax = Vector2.zero;
            
            var border = borderGO.AddComponent<Outline>();
            border.effectColor = new Color(0.5f, 0.7f, 1f, 1f);
            border.effectDistance = new Vector2(2, -2);
        }
        
        private void ClearDisplayedImages()
        {
            if (_contentContainer == null)
                return;
            
            foreach (Transform child in _contentContainer)
            {
                Destroy(child.gameObject);
            }
        }
        
        #endregion
        
        #region Helpers
        
        private void UpdateStatus(string message)
        {
            if (_statusText != null)
            {
                _statusText.text = message;
            }
            Debug.Log($"[AtlasUrlManager] {message}");
        }
        
        private void DisableButtons()
        {
            if (_batchButton != null) _batchButton.interactable = false;
            if (_singleButton != null) _singleButton.interactable = false;
            if (_saveButton != null) _saveButton.interactable = false;
            if (_unloadButton != null) _unloadButton.interactable = false;
            if (_loadButton != null) _loadButton.interactable = false;
        }
        
        private void EnableButtons()
        {
            if (_batchButton != null) _batchButton.interactable = true;
            if (_singleButton != null) _singleButton.interactable = true;
            if (_saveButton != null) _saveButton.interactable = true;
            if (_unloadButton != null) _unloadButton.interactable = true;
            if (_loadButton != null) _loadButton.interactable = true;
        }
        
        private string GetUrlFileName(string url)
        {
            try
            {
                var uri = new Uri(url);
                var fileName = Path.GetFileName(uri.LocalPath);
                if (string.IsNullOrEmpty(fileName))
                {
                    return $"img_{url.GetHashCode()}";
                }
                return fileName;
            }
            catch
            {
                return $"img_{url.GetHashCode()}";
            }
        }
        
        #endregion
    }
}

