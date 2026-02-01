using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using RuntimeAtlasPacker;
using System.Collections;
using System.Collections.Generic;
using System;

/// <summary>
/// Example demonstrating downloading random cat images from URL,
/// adding them to a RuntimeAtlas with random names,
/// and displaying them via buttons on a UI canvas.
/// </summary>
public class SimpleNamedAtlasExample : MonoBehaviour
{
    [Header("Download Settings")]
    [Tooltip("Number of random cat images to download")]
    [SerializeField] private int imageCount = 6;
    
    [Tooltip("Size of each image to download")]
    [SerializeField] private int imageSize = 256;
    
    [Header("UI Setup (Optional - will auto-create if empty)")]
    public Canvas targetCanvas;
    public Transform buttonsParent;
    public Transform displayArea;
    
    [Header("UI Prefabs (Optional)")]
    public Button buttonPrefab;
    
    private RuntimeAtlas atlas;
    private Dictionary<string, Texture2D> downloadedImages = new Dictionary<string, Texture2D>();
    private Image currentDisplayImage;
    
    void Start()
    {
        Debug.Log("=== Random Cat Image Atlas Example ===");
        
        // Setup UI
        SetupUI();
        
        // Create atlas
        CreateAtlas();
        
        // Start downloading images
        StartCoroutine(DownloadAndAddImagesToAtlas());
    }
    
    /// <summary>
    /// Setup the UI canvas and layout
    /// </summary>
    void SetupUI()
    {
        // Create canvas if not assigned
        if (targetCanvas == null)
        {
            GameObject canvasObj = new GameObject("CatAtlas_Canvas");
            targetCanvas = canvasObj.AddComponent<Canvas>();
            targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            
            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            
            canvasObj.AddComponent<GraphicRaycaster>();
        }
        
        // Create buttons panel
        if (buttonsParent == null)
        {
            GameObject buttonsPanel = new GameObject("ButtonsPanel");
            buttonsPanel.transform.SetParent(targetCanvas.transform, false);
            
            RectTransform buttonsPanelRect = buttonsPanel.AddComponent<RectTransform>();
            buttonsPanelRect.anchorMin = new Vector2(0, 0.5f);
            buttonsPanelRect.anchorMax = new Vector2(0, 0.5f);
            buttonsPanelRect.pivot = new Vector2(0, 0.5f);
            buttonsPanelRect.anchoredPosition = new Vector2(20, 0);
            buttonsPanelRect.sizeDelta = new Vector2(200, 600);
            
            // Add vertical layout group for buttons
            VerticalLayoutGroup layoutGroup = buttonsPanel.AddComponent<VerticalLayoutGroup>();
            layoutGroup.spacing = 10;
            layoutGroup.padding = new RectOffset(10, 10, 10, 10);
            layoutGroup.childControlHeight = false;
            layoutGroup.childControlWidth = true;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.childForceExpandWidth = true;
            
            buttonsParent = buttonsPanel.transform;
        }
        
        // Create display area
        if (displayArea == null)
        {
            GameObject displayPanel = new GameObject("DisplayPanel");
            displayPanel.transform.SetParent(targetCanvas.transform, false);
            
            RectTransform displayPanelRect = displayPanel.AddComponent<RectTransform>();
            displayPanelRect.anchorMin = new Vector2(0.5f, 0.5f);
            displayPanelRect.anchorMax = new Vector2(0.5f, 0.5f);
            displayPanelRect.pivot = new Vector2(0.5f, 0.5f);
            displayPanelRect.anchoredPosition = Vector2.zero;
            displayPanelRect.sizeDelta = new Vector2(600, 600);
            
            displayArea = displayPanel.transform;
            
            // Add background
            Image bgImage = displayPanel.AddComponent<Image>();
            bgImage.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            
            // Create image for displaying selected cat
            GameObject imageObj = new GameObject("CatImage");
            imageObj.transform.SetParent(displayArea, false);
            
            RectTransform imageRect = imageObj.AddComponent<RectTransform>();
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = new Vector2(10, 10);
            imageRect.offsetMax = new Vector2(-10, -10);
            
            currentDisplayImage = imageObj.AddComponent<Image>();
            currentDisplayImage.preserveAspect = true;
        }
        
        Debug.Log("✓ UI setup complete");
    }
    
    /// <summary>
    /// Create the RuntimeAtlas
    /// </summary>
    void CreateAtlas()
    {
        var settings = new AtlasSettings
        {
            InitialSize = 1024,
            MaxSize = 2048,
            Padding = 2,
            Format = TextureFormat.RGBA32,
            FilterMode = FilterMode.Bilinear,
            Algorithm = PackingAlgorithm.MaxRects
        };
        
        // Use AtlasPacker to create a named atlas so it shows up in the debugger window
        atlas = AtlasPacker.GetOrCreate("CatAtlas", settings);
        Debug.Log($"✓ Atlas created: {atlas.Width}x{atlas.Height}");
    }
    
    /// <summary>
    /// Download random cat images and add them to the atlas
    /// </summary>
    IEnumerator DownloadAndAddImagesToAtlas()
    {
        Debug.Log($"\n=== Downloading {imageCount} Random Cat Images ===");
        
        for (int i = 0; i < imageCount; i++)
        {
            // Generate random GUID for unique image
            string guid = Guid.NewGuid().ToString();
            string imageName = $"cat_{i + 1}";
            string url = $"https://api.images.cat/{imageSize}/{imageSize}/{guid}";
            
            Debug.Log($"Downloading {imageName} from: {url}");
            
            // ✅ IMPROVED: Download image using UnityWebRequest with DownloadHandlerBuffer
            UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET);
            request.downloadHandler = new DownloadHandlerBuffer();
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                // Get raw image bytes
                byte[] imageData = request.downloadHandler.data;
                
                if (imageData != null && imageData.Length > 0)
                {
                    // Create texture and load image data
                    Texture2D downloadedTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    downloadedTexture.name = imageName;
                    
                    if (downloadedTexture.LoadImage(imageData))
                    {
                        // Store the downloaded texture
                        downloadedImages[imageName] = downloadedTexture;
                        
                        // Add to atlas with name
                        var (result, entry) = atlas.Add(imageName, downloadedTexture);
                        
                        if (result == AddResult.Success)
                        {
                            Debug.Log($"✓ Added '{imageName}' to atlas (Entry ID: {entry.Id})");
                            
                            // Create button for this image
                            CreateButtonForImage(imageName);
                        }
                        else
                        {
                            Debug.LogError($"✗ Failed to add '{imageName}' to atlas: {result}");
                        }
                    }
                    else
                    {
                        Debug.LogError($"✗ Failed to load image data for '{imageName}'");
                        Destroy(downloadedTexture);
                    }
                }
                else
                {
                    Debug.LogError($"✗ Downloaded data is empty for '{imageName}'");
                }
            }
            else
            {
                Debug.LogError($"✗ Failed to download '{imageName}': {request.error}");
            }
            
            request.Dispose();
            
            // Small delay between downloads
            yield return new WaitForSeconds(0.2f);
        }
        
        Debug.Log($"\n✓ Download complete! Added {downloadedImages.Count} images to atlas");
        Debug.Log($"✓ Atlas fill ratio: {atlas.FillRatio:P1}");
        Debug.Log($"✓ Atlas entries: {atlas.EntryCount}");
        
        // Select first image by default
        if (downloadedImages.Count > 0)
        {
            DisplayImageFromAtlas("cat_1");
        }
    }
    
    /// <summary>
    /// Create a button to display a specific cat image
    /// </summary>
    void CreateButtonForImage(string imageName)
    {
        GameObject buttonObj;
        Button button;
        
        if (buttonPrefab != null)
        {
            // Use prefab if provided
            buttonObj = Instantiate(buttonPrefab.gameObject, buttonsParent);
            button = buttonObj.GetComponent<Button>();
        }
        else
        {
            // Create button from scratch
            buttonObj = new GameObject($"Button_{imageName}");
            buttonObj.transform.SetParent(buttonsParent, false);
            
            RectTransform buttonRect = buttonObj.AddComponent<RectTransform>();
            buttonRect.sizeDelta = new Vector2(180, 40);
            
            Image buttonImage = buttonObj.AddComponent<Image>();
            buttonImage.color = new Color(0.3f, 0.6f, 0.9f, 1f);
            
            button = buttonObj.AddComponent<Button>();
            
            // Add text
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(buttonObj.transform, false);
            
            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            
            Text buttonText = textObj.AddComponent<Text>();
            buttonText.text = imageName.Replace("_", " ").ToUpper();
            buttonText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            buttonText.fontSize = 16;
            buttonText.alignment = TextAnchor.MiddleCenter;
            buttonText.color = Color.white;
        }
        
        // Set button click handler
        string capturedName = imageName; // Capture for closure
        button.onClick.AddListener(() => DisplayImageFromAtlas(capturedName));
        
        Debug.Log($"✓ Created button for '{imageName}'");
    }
    
    /// <summary>
    /// Display a cat image from the atlas by name
    /// </summary>
    void DisplayImageFromAtlas(string imageName)
    {
        Debug.Log($"\n=== Displaying '{imageName}' from Atlas ===");
        
        // Check if the atlas contains this name
        if (!atlas.ContainsName(imageName))
        {
            Debug.LogWarning($"⚠ Atlas does not contain '{imageName}'");
            return;
        }
        
        // Get sprite from atlas by name
        Sprite sprite = atlas.GetSprite(imageName, pixelsPerUnit: 100f);
        
        if (sprite != null)
        {
            currentDisplayImage.sprite = sprite;
            Debug.Log($"✓ Displayed '{imageName}' from atlas");
            Debug.Log($"  - Sprite size: {sprite.rect.width}x{sprite.rect.height}");
            Debug.Log($"  - UV rect: {sprite.textureRect}");
        }
        else
        {
            Debug.LogError($"✗ Failed to create sprite for '{imageName}'");
        }
    }
    
    /// <summary>
    /// Get atlas statistics
    /// </summary>
    public void ShowAtlasStats()
    {
        if (atlas == null)
        {
            Debug.Log("Atlas not initialized");
            return;
        }
        
        string stats = $"\n=== Atlas Statistics ===\n" +
                      $"Size: {atlas.Width}x{atlas.Height}\n" +
                      $"Entries: {atlas.EntryCount}\n" +
                      $"Fill Ratio: {atlas.FillRatio:P1}\n" +
                      $"Pages: {atlas.PageCount}\n" +
                      $"Version: {atlas.Version}";
        
        Debug.Log(stats);
    }
    
    /// <summary>
    /// Public method to check if an image exists in the atlas
    /// </summary>
    public bool HasImage(string imageName)
    {
        return atlas != null && atlas.ContainsName(imageName);
    }
    
    /// <summary>
    /// Public method to get a sprite by name
    /// </summary>
    public Sprite GetSprite(string imageName)
    {
        if (atlas == null)
        {
            Debug.LogError("Atlas not initialized!");
            return null;
        }
        
        return atlas.GetSprite(imageName);
    }
    
    void OnDestroy()
    {
        // Note: The atlas is managed by AtlasPacker, so we don't dispose it here
        // It will be automatically cleaned up when the application quits
        
        // Clean up downloaded textures
        foreach (var kvp in downloadedImages)
        {
            if (kvp.Value != null)
            {
                Destroy(kvp.Value);
            }
        }
        downloadedImages.Clear();
    }
}

