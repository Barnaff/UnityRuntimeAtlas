using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using RuntimeAtlasPacker;

namespace RuntimeAtlasPacker.Samples
{
    /// <summary>
    /// Example demonstrating async batch packing to avoid frame drops when loading many textures.
    /// </summary>
    public class AsyncBatchPackingExample : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private int textureCount = 100;
        [SerializeField] private int textureSize = 128;
        [SerializeField] private int texturesPerFrame = 5;
        
        [Header("UI")]
        [SerializeField] private Text statusText;
        [SerializeField] private Slider progressBar;
        [SerializeField] private Button startButton;
        
        [Header("Performance Monitoring")]
        [SerializeField] private Text fpsText;
        [SerializeField] private bool showFPS = true;
        
        private List<Texture2D> _generatedTextures = new List<Texture2D>();
        private float _deltaTime;
        private bool _isProcessing;

        private void Start()
        {
            if (startButton != null)
            {
                startButton.onClick.AddListener(OnStartButtonClicked);
            }
            
            UpdateStatus("Ready. Click 'Start' to begin async batch packing.");
        }

        private void Update()
        {
            if (showFPS)
            {
                _deltaTime += (Time.unscaledDeltaTime - _deltaTime) * 0.1f;
                float fps = 1.0f / _deltaTime;
                
                if (fpsText != null)
                {
                    fpsText.text = $"FPS: {fps:F1}";
                    
                    // Color code based on performance
                    if (fps > 50) fpsText.color = Color.green;
                    else if (fps > 30) fpsText.color = Color.yellow;
                    else fpsText.color = Color.red;
                }
            }
        }

        private void OnStartButtonClicked()
        {
            if (_isProcessing)
            {
                Debug.LogWarning("[AsyncBatchPackingExample] Already processing!");
                return;
            }
            
            StartCoroutine(RunAsyncPackingExample());
        }

        private IEnumerator RunAsyncPackingExample()
        {
            _isProcessing = true;
            if (startButton != null) startButton.interactable = false;
            
            // Step 1: Generate test textures
            UpdateStatus($"Generating {textureCount} test textures...");
            yield return StartCoroutine(GenerateTestTextures());
            
            // Step 2: Pack textures using async method
            UpdateStatus($"Packing {textureCount} textures asynchronously ({texturesPerFrame} per frame)...");
            UpdateProgress(0f);
            
            var textures = _generatedTextures.ToArray();
            AtlasEntry[] results = null;
            
            yield return StartCoroutine(
                AtlasPacker.PackBatchAsync(
                    textures,
                    onProgress: (entries, progress) =>
                    {
                        UpdateProgress(progress);
                        UpdateStatus($"Packing... {entries.Length}/{textures.Length} textures ({progress:P0})");
                    },
                    texturesPerFrame: texturesPerFrame
                )
            );
            
            // Step 3: Show results
            int packedCount = AtlasPacker.Default.EntryCount;
            UpdateStatus($"Complete! Packed {packedCount} textures. FPS should remain stable.");
            UpdateProgress(1f);
            
            if (startButton != null) startButton.interactable = true;
            _isProcessing = false;
            
            // Show atlas info
            Debug.Log($"[AsyncBatchPackingExample] Final atlas size: {AtlasPacker.Default.Width}x{AtlasPacker.Default.Height}");
            Debug.Log($"[AsyncBatchPackingExample] Fill ratio: {AtlasPacker.Default.FillRatio:P1}");
            Debug.Log($"[AsyncBatchPackingExample] Active atlases: {AtlasPacker.GetActiveAtlasCount()}");
        }

        private IEnumerator GenerateTestTextures()
        {
            _generatedTextures.Clear();
            
            for (int i = 0; i < textureCount; i++)
            {
                var texture = GenerateRandomTexture(textureSize, textureSize, $"TestTexture_{i}");
                _generatedTextures.Add(texture);
                
                // Yield every 10 textures to keep UI responsive
                if (i % 10 == 0)
                {
                    UpdateProgress((float)i / textureCount);
                    yield return null;
                }
            }
            
            UpdateProgress(1f);
        }

        private Texture2D GenerateRandomTexture(int width, int height, string name)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.name = name;
            
            var pixels = new Color32[width * height];
            var color = new Color32(
                (byte)Random.Range(0, 256),
                (byte)Random.Range(0, 256),
                (byte)Random.Range(0, 256),
                255
            );
            
            // Create a simple pattern
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Add some variation
                    float brightness = 0.7f + 0.3f * Mathf.PerlinNoise(x * 0.1f, y * 0.1f);
                    pixels[y * width + x] = new Color32(
                        (byte)(color.r * brightness),
                        (byte)(color.g * brightness),
                        (byte)(color.b * brightness),
                        255
                    );
                }
            }
            
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            
            return texture;
        }

        private void UpdateStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
            Debug.Log($"[AsyncBatchPackingExample] {message}");
        }

        private void UpdateProgress(float progress)
        {
            if (progressBar != null)
            {
                progressBar.value = progress;
            }
        }

        private void OnDestroy()
        {
            // Clean up generated textures
            foreach (var texture in _generatedTextures)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }
            _generatedTextures.Clear();
        }

        // Editor buttons for testing
        [ContextMenu("Pack Synchronously (Will Lag!)")]
        private void PackSynchronously()
        {
            if (_generatedTextures.Count == 0)
            {
                Debug.LogWarning("No textures generated yet!");
                return;
            }
            
            Debug.Log($"[AsyncBatchPackingExample] Packing {_generatedTextures.Count} textures synchronously...");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            var entries = AtlasPacker.PackBatch(_generatedTextures.ToArray());
            
            sw.Stop();
            Debug.Log($"[AsyncBatchPackingExample] Sync pack complete in {sw.ElapsedMilliseconds}ms. Packed: {entries.Length}");
        }

        [ContextMenu("Clear Atlas")]
        private void ClearAtlas()
        {
            AtlasPacker.ClearAllAtlases();
            Debug.Log("[AsyncBatchPackingExample] Atlas cleared");
        }
    }
}

