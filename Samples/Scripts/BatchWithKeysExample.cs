using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using RuntimeAtlasPacker;

namespace RuntimeAtlasPacker.Samples
{
    /// <summary>
    /// Example demonstrating batch packing with named keys for easy sprite retrieval.
    /// </summary>
    public class BatchWithKeysExample : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private int textureCount = 20;
        [SerializeField] private int textureSize = 128;
        
        [Header("UI")]
        [SerializeField] private Text statusText;
        [SerializeField] private Button startButton;
        [SerializeField] private Transform spriteContainer;
        [SerializeField] private GameObject spriteRendererPrefab;
        
        [Header("Atlas")]
        [SerializeField] private RuntimeAtlas runtimeAtlas;
        
        private Dictionary<string, Texture2D> _textureDict = new Dictionary<string, Texture2D>();
        private Dictionary<string, AtlasEntry> _entryDict = new Dictionary<string, AtlasEntry>();

        private void Start()
        {
            if (runtimeAtlas == null)
            {
                // Create atlas with custom settings
                var settings = new AtlasSettings
                {
                    InitialSize = 1024,
                    MaxSize = 2048,
                    MaxPageCount = 5,
                    Padding = 2,
                    Format = AtlasSettings.DefaultFormat
                };
                runtimeAtlas = new RuntimeAtlas(settings);
            }

            if (startButton != null)
            {
                startButton.onClick.AddListener(OnStartButtonClicked);
            }
            
            UpdateStatus("Ready. Click 'Start' to batch pack textures with keys.");
        }

        private void OnStartButtonClicked()
        {
            StartCoroutine(RunBatchWithKeysExample());
        }

        private IEnumerator RunBatchWithKeysExample()
        {
            if (startButton != null) startButton.interactable = false;
            
            // Step 1: Generate test textures with meaningful keys
            UpdateStatus($"Generating {textureCount} test textures with keys...");
            GenerateTexturesWithKeys();
            yield return null;
            
            // Step 2: Pack textures using dictionary-based AddBatch
            UpdateStatus($"Packing {textureCount} textures with named keys...");
            var startTime = Time.realtimeSinceStartup;
            
            // Add batch with keys - this allows easy retrieval by key later
            _entryDict = runtimeAtlas.AddBatch(_textureDict);
            
            var elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
            
            // Step 3: Show results
            UpdateStatus($"Packed {_entryDict.Count}/{textureCount} textures in {elapsed:F2}ms");
            
            Debug.Log($"[BatchWithKeysExample] Successfully packed {_entryDict.Count} textures with keys:");
            foreach (var kvp in _entryDict)
            {
                Debug.Log($"  Key: '{kvp.Key}' -> Entry ID: {kvp.Value.Id}, Size: {kvp.Value.Width}x{kvp.Value.Height}");
            }
            
            yield return new WaitForSeconds(1f);
            
            // Step 4: Demonstrate sprite retrieval by key
            UpdateStatus("Demonstrating sprite retrieval by key...");
            yield return StartCoroutine(DisplaySpritesFromKeys());
            
            UpdateStatus($"Complete! Atlas pages: {runtimeAtlas.PageCount}, Entries: {runtimeAtlas.EntryCount}, Fill: {runtimeAtlas.FillRatio:P1}");
            
            if (startButton != null) startButton.interactable = true;
        }

        private void GenerateTexturesWithKeys()
        {
            _textureDict.Clear();
            
            // Generate textures with meaningful keys based on type
            var colors = new[] { Color.red, Color.green, Color.blue, Color.yellow, Color.cyan, Color.magenta };
            var prefixes = new[] { "icon", "sprite", "tile", "ui_element" };
            
            for (var i = 0; i < textureCount; i++)
            {
                var prefix = prefixes[i % prefixes.Length];
                var colorName = colors[i % colors.Length].ToString().ToLower();
                var key = $"{prefix}_{colorName}_{i}";
                
                // Create a test texture
                var size = Random.Range(textureSize / 2, textureSize);
                var texture = GenerateTestTexture(size, size, colors[i % colors.Length]);
                texture.name = key;
                
                _textureDict[key] = texture;
            }
            
            Debug.Log($"[BatchWithKeysExample] Generated {_textureDict.Count} textures with keys");
        }

        private Texture2D GenerateTestTexture(int width, int height, Color baseColor)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            
            var pixels = new Color32[width * height];
            for (var i = 0; i < pixels.Length; i++)
            {
                // Add some variation
                var variation = Random.Range(-0.2f, 0.2f);
                pixels[i] = new Color(
                    Mathf.Clamp01(baseColor.r + variation),
                    Mathf.Clamp01(baseColor.g + variation),
                    Mathf.Clamp01(baseColor.b + variation),
                    1f
                );
            }
            
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private IEnumerator DisplaySpritesFromKeys()
        {
            if (spriteContainer == null || spriteRendererPrefab == null)
            {
                Debug.LogWarning("[BatchWithKeysExample] Sprite container or prefab not set!");
                yield break;
            }
            
            // Clear existing sprites
            foreach (Transform child in spriteContainer)
            {
                Destroy(child.gameObject);
            }
            
            var displayCount = Mathf.Min(10, _entryDict.Count);
            var keys = new List<string>(_entryDict.Keys);
            
            for (var i = 0; i < displayCount; i++)
            {
                var key = keys[i];
                
                // Retrieve sprite by key - this is the key feature!
                var sprite = runtimeAtlas.GetSprite(key);
                if (sprite == null)
                {
                    Debug.LogWarning($"[BatchWithKeysExample] Failed to get sprite for key: {key}");
                    continue;
                }
                
                // Create sprite renderer
                var go = Instantiate(spriteRendererPrefab, spriteContainer);
                go.name = key;
                
                var sr = go.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    sr.sprite = sprite;
                }
                
                // Position in a grid
                var x = (i % 5) * 2f;
                var y = (i / 5) * -2f;
                go.transform.localPosition = new Vector3(x, y, 0);
                
                yield return new WaitForSeconds(0.1f);
            }
            
            Debug.Log($"[BatchWithKeysExample] Displayed {displayCount} sprites retrieved by their keys");
        }

        private void UpdateStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
            Debug.Log($"[BatchWithKeysExample] {message}");
        }

        private void OnDestroy()
        {
            // Clean up generated textures
            foreach (var texture in _textureDict.Values)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }
            _textureDict.Clear();
            
            // Dispose atlas if we created it
            if (runtimeAtlas != null)
            {
                runtimeAtlas.Dispose();
            }
        }
    }
}

