using System.Collections.Generic;
using UnityEngine;
using RuntimeAtlasPacker;

namespace RuntimeAtlasPacker.Samples
{
    /// <summary>
    /// Example showing dynamic sprite spawning using atlas textures.
    /// Demonstrates efficient sprite instancing with shared atlas.
    /// </summary>
    public class DynamicSpritesExample : MonoBehaviour
    {
        [Header("Textures")]
        public Texture2D[] spriteTextures;
        
        [Header("Spawn Settings")]
        public int initialSpawnCount = 50;
        public float spawnRadius = 10f;
        public float moveSpeed = 2f;
        
        [Header("Performance")]
        public bool useBatching = true;
        public float pixelsPerUnit = 100f;

        private RuntimeAtlas _atlas;
        private AtlasEntry[] _entries;
        private List<SpawnedSprite> _sprites = new();
        
        private struct SpawnedSprite
        {
            public GameObject GameObject;
            public AtlasSpriteRenderer Renderer;
            public Vector3 Velocity;
        }

        private void Start()
        {
            // Create optimized atlas
            _atlas = new RuntimeAtlas(new AtlasSettings
            {
                InitialSize = 512,
                MaxSize = 2048,
                Padding = 1,
                Format = TextureFormat.RGBA32,
                Algorithm = PackingAlgorithm.MaxRects,
                FilterMode = FilterMode.Point, // Pixel art friendly
                GenerateMipMaps = false
            });

            // Load textures if not assigned
            if (spriteTextures == null || spriteTextures.Length == 0)
            {
                spriteTextures = Resources.LoadAll<Texture2D>("");
            }

            if (spriteTextures.Length == 0)
            {
                Debug.LogWarning("No textures found. Creating colored placeholders.");
                spriteTextures = CreatePlaceholderTextures(5);
            }

            // Pack all textures
            _entries = _atlas.AddBatch(spriteTextures);
            Debug.Log($"Created sprite atlas: {_atlas.Width}x{_atlas.Height}, {_entries.Length} sprites");

            // Spawn initial sprites
            for (int i = 0; i < initialSpawnCount; i++)
            {
                SpawnRandomSprite();
            }
        }

        private Texture2D[] CreatePlaceholderTextures(int count)
        {
            var textures = new Texture2D[count];
            var colors = new[] { Color.red, Color.green, Color.blue, Color.yellow, Color.cyan };
            
            for (int i = 0; i < count; i++)
            {
                var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                var pixels = new Color[32 * 32];
                
                for (int p = 0; p < pixels.Length; p++)
                {
                    pixels[p] = colors[i % colors.Length];
                }
                
                tex.SetPixels(pixels);
                tex.Apply();
                tex.name = $"Placeholder_{i}";
                textures[i] = tex;
            }
            
            return textures;
        }

        private void Update()
        {
            // Spawn with space key
            if (Input.GetKeyDown(KeyCode.Space))
            {
                for (int i = 0; i < 10; i++)
                {
                    SpawnRandomSprite();
                }
            }

            // Clear with C key
            if (Input.GetKeyDown(KeyCode.C))
            {
                ClearAllSprites();
            }

            // Move sprites
            UpdateSprites();
        }

        private void SpawnRandomSprite()
        {
            // Random position within radius
            Vector2 randomPos = Random.insideUnitCircle * spawnRadius;
            Vector3 position = transform.position + new Vector3(randomPos.x, randomPos.y, 0);
            
            // Random sprite from atlas
            var entry = _entries[Random.Range(0, _entries.Length)];
            
            // Create sprite
            var go = new GameObject($"Sprite_{_sprites.Count}");
            go.transform.position = position;
            
            var renderer = go.AddComponent<AtlasSpriteRenderer>();
            renderer.PixelsPerUnit = pixelsPerUnit;
            renderer.SetEntry(entry);
            
            // Random velocity
            Vector3 velocity = new Vector3(
                Random.Range(-1f, 1f),
                Random.Range(-1f, 1f),
                0
            ).normalized * moveSpeed;

            _sprites.Add(new SpawnedSprite
            {
                GameObject = go,
                Renderer = renderer,
                Velocity = velocity
            });
        }

        /// <summary>
        /// Spawn a sprite with a specific entry.
        /// </summary>
        public AtlasSpriteRenderer SpawnSprite(AtlasEntry entry, Vector3 position)
        {
            var go = new GameObject($"Sprite_{_sprites.Count}");
            go.transform.position = position;
            
            var renderer = go.AddComponent<AtlasSpriteRenderer>();
            renderer.PixelsPerUnit = pixelsPerUnit;
            renderer.SetEntry(entry);
            
            _sprites.Add(new SpawnedSprite
            {
                GameObject = go,
                Renderer = renderer,
                Velocity = Vector3.zero
            });

            return renderer;
        }

        /// <summary>
        /// Spawn a sprite with a texture (auto-packs if needed).
        /// </summary>
        public AtlasSpriteRenderer SpawnSprite(Texture2D texture, Vector3 position)
        {
            var go = new GameObject($"Sprite_{_sprites.Count}");
            go.transform.position = position;
            
            var renderer = go.AddComponent<AtlasSpriteRenderer>();
            renderer.PixelsPerUnit = pixelsPerUnit;
            renderer.TargetAtlasName = ""; // Uses default
            renderer.SetTexture(texture); // Auto-packs
            
            _sprites.Add(new SpawnedSprite
            {
                GameObject = go,
                Renderer = renderer,
                Velocity = Vector3.zero
            });

            return renderer;
        }

        private void UpdateSprites()
        {
            float dt = Time.deltaTime;
            
            for (int i = _sprites.Count - 1; i >= 0; i--)
            {
                var sprite = _sprites[i];
                
                if (sprite.GameObject == null)
                {
                    _sprites.RemoveAt(i);
                    continue;
                }

                // Move
                sprite.GameObject.transform.position += sprite.Velocity * dt;
                
                // Bounce at boundaries
                Vector3 pos = sprite.GameObject.transform.position - transform.position;
                if (Mathf.Abs(pos.x) > spawnRadius || Mathf.Abs(pos.y) > spawnRadius)
                {
                    var newSprite = sprite;
                    newSprite.Velocity = -sprite.Velocity;
                    _sprites[i] = newSprite;
                }
            }
        }

        public void ClearAllSprites()
        {
            foreach (var sprite in _sprites)
            {
                if (sprite.GameObject != null)
                {
                    Destroy(sprite.GameObject);
                }
            }
            _sprites.Clear();
        }

        private void OnDestroy()
        {
            ClearAllSprites();
            _atlas?.Dispose();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 100));
            GUILayout.Label($"Sprites: {_sprites.Count}");
            GUILayout.Label($"Atlas: {_atlas?.Width}x{_atlas?.Height}");
            GUILayout.Label($"Fill: {_atlas?.FillRatio:P1}");
            GUILayout.Label("Press SPACE to spawn, C to clear");
            GUILayout.EndArea();
        }
    }

    /// <summary>
    /// Example of spawning sprites along a path using atlas entries.
    /// </summary>
    public class PathSpritesExample : MonoBehaviour
    {
        public Texture2D pathTexture;
        public int spriteCount = 20;
        public float pathRadius = 5f;
        public float rotationSpeed = 30f;

        private RuntimeAtlas _atlas;
        private AtlasEntry _entry;
        private List<AtlasSpriteRenderer> _sprites = new();

        private void Start()
        {
            if (pathTexture == null)
            {
                Debug.LogWarning("Assign a path texture");
                return;
            }

            _atlas = new RuntimeAtlas();
            var (result, entry) = _atlas.Add(pathTexture);
            if (result != AddResult.Success || entry == null)
            {
                Debug.LogWarning($"Failed to add path texture to atlas: {result}");
                return;
            }
            _entry = entry;

            // Create sprites along circular path
            for (int i = 0; i < spriteCount; i++)
            {
                float angle = (float)i / spriteCount * 360f;
                Vector3 pos = GetPositionOnPath(angle);
                
                var go = new GameObject($"PathSprite_{i}");
                go.transform.SetParent(transform);
                go.transform.localPosition = pos;
                
                var renderer = go.AddComponent<AtlasSpriteRenderer>();
                renderer.SetEntry(_entry);
                
                _sprites.Add(renderer);
            }
        }

        private void Update()
        {
            // Rotate sprites along path
            float rotation = rotationSpeed * Time.deltaTime;
            
            for (int i = 0; i < _sprites.Count; i++)
            {
                float baseAngle = (float)i / spriteCount * 360f;
                float currentAngle = baseAngle + Time.time * rotationSpeed;
                
                _sprites[i].transform.localPosition = GetPositionOnPath(currentAngle);
            }
        }

        private Vector3 GetPositionOnPath(float angle)
        {
            float rad = angle * Mathf.Deg2Rad;
            return new Vector3(
                Mathf.Cos(rad) * pathRadius,
                Mathf.Sin(rad) * pathRadius,
                0
            );
        }

        private void OnDestroy()
        {
            _atlas?.Dispose();
        }
    }
}
