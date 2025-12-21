using UnityEngine;
using RuntimeAtlasPacker;

namespace RuntimeAtlasPacker.Samples
{
    /// <summary>
    /// Simple test to verify Memory Analyzer is working.
    /// Creates atlases and adds textures over time so you can see real-time tracking.
    /// </summary>
    public class MemoryAnalyzerTest : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("How often to add a new texture (seconds)")]
        public float addInterval = 1f;
        
        [Tooltip("Number of textures to create")]
        public int textureCount = 20;
        
        [Tooltip("Size of test textures")]
        public int textureSize = 256;
        
        private float _nextAddTime;
        private int _texturesAdded;
        
        private void Start()
        {
            Debug.Log("[MemoryAnalyzerTest] Starting test...");
            Debug.Log($"Will create {textureCount} textures of size {textureSize}x{textureSize}");
            Debug.Log("Open Window > Runtime Atlas Packer > Memory Analyzer to watch!");
            
            _nextAddTime = Time.time + addInterval;
        }
        
        private void Update()
        {
            if (_texturesAdded >= textureCount)
                return;
                
            if (Time.time >= _nextAddTime)
            {
                AddRandomTexture();
                _nextAddTime = Time.time + addInterval;
            }
        }
        
        private void AddRandomTexture()
        {
            _texturesAdded++;
            
            // Create a random colored texture
            var texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
            var color = Random.ColorHSV(0f, 1f, 0.8f, 1f, 0.8f, 1f);
            
            var pixels = texture.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            
            texture.name = $"TestTexture_{_texturesAdded}";
            
            // Pack into default atlas
            var entry = AtlasPacker.Pack(texture);
            
            Debug.Log($"[MemoryAnalyzerTest] Added texture {_texturesAdded}/{textureCount}: {texture.name} " +
                      $"(Entry ID: {entry.Id}, Atlas: {entry.Texture.width}x{entry.Texture.height})");
            
            if (_texturesAdded >= textureCount)
            {
                Debug.Log("[MemoryAnalyzerTest] Test complete! Check the Memory Analyzer window.");
            }
        }
    }
}

