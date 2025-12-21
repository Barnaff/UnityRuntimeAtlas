using System.Collections.Generic;
using UnityEngine;
using RuntimeAtlasPacker;

namespace RuntimeAtlasPacker.Samples
{
    /// <summary>
    /// Example demonstrating object pooling with atlas sprites.
    /// Efficient for bullet hell, particle effects, collectibles, etc.
    /// </summary>
    public class ObjectPoolingExample : MonoBehaviour
    {
        [Header("Pool Settings")]
        public int initialPoolSize = 100;
        public int maxPoolSize = 500;
        
        [Header("Textures")]
        public Texture2D bulletTexture;
        public Texture2D coinTexture;
        public Texture2D particleTexture;

        [Header("Spawn Settings")]
        public float bulletSpeed = 10f;
        public float coinRotationSpeed = 180f;
        public float particleLifetime = 2f;

        private RuntimeAtlas _atlas;
        private AtlasEntry _bulletEntry;
        private AtlasEntry _coinEntry;
        private AtlasEntry _particleEntry;

        private SpritePool _bulletPool;
        private SpritePool _coinPool;
        private SpritePool _particlePool;

        private List<ActiveBullet> _activeBullets = new();
        private List<ActiveCoin> _activeCoin = new();
        private List<ActiveParticle> _activeParticles = new();

        private void Start()
        {
            // Create shared atlas for all pooled objects
            _atlas = new RuntimeAtlas(new AtlasSettings
            {
                InitialSize = 256,
                MaxSize = 1024,
                Padding = 1,
                Format = TextureFormat.RGBA32,
                FilterMode = FilterMode.Point
            });

            // Pack textures or create placeholders
            _bulletEntry = PackOrCreatePlaceholder(bulletTexture, Color.yellow, "Bullet");
            _coinEntry = PackOrCreatePlaceholder(coinTexture, Color.yellow, "Coin");
            _particleEntry = PackOrCreatePlaceholder(particleTexture, Color.white, "Particle");

            // Create pools
            _bulletPool = new SpritePool("Bullet", _bulletEntry, initialPoolSize, maxPoolSize, transform);
            _coinPool = new SpritePool("Coin", _coinEntry, initialPoolSize / 2, maxPoolSize / 2, transform);
            _particlePool = new SpritePool("Particle", _particleEntry, initialPoolSize, maxPoolSize, transform);

            Debug.Log($"Pools initialized. Atlas: {_atlas.Width}x{_atlas.Height}");
        }

        private AtlasEntry PackOrCreatePlaceholder(Texture2D texture, Color color, string name)
        {
            if (texture != null)
            {
                var (addResult, atlasEntry) = _atlas.Add(texture);
                return atlasEntry; // May be null if failed
            }

            // Create colored placeholder
            var placeholder = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            var pixels = new Color[16 * 16];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            placeholder.SetPixels(pixels);
            placeholder.Apply();
            placeholder.name = name;
            
            var (result, entry) = _atlas.Add(placeholder);
            return entry; // May be null if failed
        }

        private void Update()
        {
            HandleInput();
            UpdateBullets();
            UpdateCoins();
            UpdateParticles();
        }

        private void HandleInput()
        {
            // Fire bullets with left click
            if (Input.GetMouseButton(0))
            {
                FireBullet();
            }

            // Spawn coins with right click
            if (Input.GetMouseButtonDown(1))
            {
                SpawnCoins(10);
            }

            // Spawn particles with middle click
            if (Input.GetMouseButtonDown(2))
            {
                SpawnParticles(20);
            }

            // Clear all with space
            if (Input.GetKeyDown(KeyCode.Space))
            {
                ReturnAllToPool();
            }
        }

        public void FireBullet()
        {
            var renderer = _bulletPool.Get();
            if (renderer == null) return;

            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mousePos.z = 0;

            Vector3 direction = (mousePos - transform.position).normalized;
            renderer.transform.position = transform.position;
            renderer.transform.rotation = Quaternion.LookRotation(Vector3.forward, direction);

            _activeBullets.Add(new ActiveBullet
            {
                Renderer = renderer,
                Velocity = direction * bulletSpeed
            });
        }

        public void SpawnCoins(int count)
        {
            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mousePos.z = 0;

            for (int i = 0; i < count; i++)
            {
                var renderer = _coinPool.Get();
                if (renderer == null) break;

                Vector3 offset = Random.insideUnitCircle * 2f;
                renderer.transform.position = mousePos + offset;

                _activeCoin.Add(new ActiveCoin
                {
                    Renderer = renderer,
                    RotationSpeed = coinRotationSpeed * Random.Range(0.5f, 1.5f)
                });
            }
        }

        public void SpawnParticles(int count)
        {
            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mousePos.z = 0;

            for (int i = 0; i < count; i++)
            {
                var renderer = _particlePool.Get();
                if (renderer == null) break;

                renderer.transform.position = mousePos;

                _activeParticles.Add(new ActiveParticle
                {
                    Renderer = renderer,
                    Velocity = Random.insideUnitCircle.normalized * Random.Range(1f, 5f),
                    Lifetime = particleLifetime,
                    TimeAlive = 0
                });
            }
        }

        private void UpdateBullets()
        {
            float dt = Time.deltaTime;
            
            for (int i = _activeBullets.Count - 1; i >= 0; i--)
            {
                var bullet = _activeBullets[i];
                bullet.Renderer.transform.position += bullet.Velocity * dt;

                // Return to pool if out of bounds
                if (bullet.Renderer.transform.position.magnitude > 20f)
                {
                    _bulletPool.Return(bullet.Renderer);
                    _activeBullets.RemoveAt(i);
                }
            }
        }

        private void UpdateCoins()
        {
            float dt = Time.deltaTime;
            
            for (int i = _activeCoin.Count - 1; i >= 0; i--)
            {
                var coin = _activeCoin[i];
                coin.Renderer.transform.Rotate(0, 0, coin.RotationSpeed * dt);
            }
        }

        private void UpdateParticles()
        {
            float dt = Time.deltaTime;
            
            for (int i = _activeParticles.Count - 1; i >= 0; i--)
            {
                var particle = _activeParticles[i];
                particle.Renderer.transform.position += (Vector3)particle.Velocity * dt;
                particle.TimeAlive += dt;

                // Fade out
                float alpha = 1f - (particle.TimeAlive / particle.Lifetime);
                var sr = particle.Renderer.Renderer;
                sr.color = new Color(1, 1, 1, alpha);

                // Update velocity (slow down)
                particle.Velocity *= 0.98f;
                _activeParticles[i] = particle;

                // Return to pool when expired
                if (particle.TimeAlive >= particle.Lifetime)
                {
                    sr.color = Color.white; // Reset color
                    _particlePool.Return(particle.Renderer);
                    _activeParticles.RemoveAt(i);
                }
            }
        }

        private void ReturnAllToPool()
        {
            foreach (var bullet in _activeBullets)
                _bulletPool.Return(bullet.Renderer);
            _activeBullets.Clear();

            foreach (var coin in _activeCoin)
                _coinPool.Return(coin.Renderer);
            _activeCoin.Clear();

            foreach (var particle in _activeParticles)
            {
                particle.Renderer.Renderer.color = Color.white;
                _particlePool.Return(particle.Renderer);
            }
            _activeParticles.Clear();
        }

        private void OnDestroy()
        {
            _atlas?.Dispose();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 150));
            GUILayout.Label("Object Pooling Example", GUI.skin.box);
            GUILayout.Label($"Bullets: {_activeBullets.Count} / {_bulletPool.TotalCount}");
            GUILayout.Label($"Coins: {_activeCoin.Count} / {_coinPool.TotalCount}");
            GUILayout.Label($"Particles: {_activeParticles.Count} / {_particlePool.TotalCount}");
            GUILayout.Label("");
            GUILayout.Label("Left Click: Fire bullets");
            GUILayout.Label("Right Click: Spawn coins");
            GUILayout.Label("Middle Click: Spawn particles");
            GUILayout.Label("Space: Clear all");
            GUILayout.EndArea();
        }

        private struct ActiveBullet
        {
            public AtlasSpriteRenderer Renderer;
            public Vector3 Velocity;
        }

        private struct ActiveCoin
        {
            public AtlasSpriteRenderer Renderer;
            public float RotationSpeed;
        }

        private struct ActiveParticle
        {
            public AtlasSpriteRenderer Renderer;
            public Vector2 Velocity;
            public float Lifetime;
            public float TimeAlive;
        }
    }

    /// <summary>
    /// Simple sprite pool for atlas sprites.
    /// </summary>
    public class SpritePool
    {
        private readonly string _name;
        private readonly AtlasEntry _entry;
        private readonly int _maxSize;
        private readonly Transform _parent;
        
        private readonly Queue<AtlasSpriteRenderer> _available = new();
        private readonly HashSet<AtlasSpriteRenderer> _active = new();

        public int AvailableCount => _available.Count;
        public int ActiveCount => _active.Count;
        public int TotalCount => _available.Count + _active.Count;

        public SpritePool(string name, AtlasEntry entry, int initialSize, int maxSize, Transform parent)
        {
            _name = name;
            _entry = entry;
            _maxSize = maxSize;
            _parent = parent;

            // Pre-warm pool
            for (int i = 0; i < initialSize; i++)
            {
                var renderer = CreateNew();
                renderer.gameObject.SetActive(false);
                _available.Enqueue(renderer);
            }
        }

        public AtlasSpriteRenderer Get()
        {
            AtlasSpriteRenderer renderer;

            if (_available.Count > 0)
            {
                renderer = _available.Dequeue();
            }
            else if (TotalCount < _maxSize)
            {
                renderer = CreateNew();
            }
            else
            {
                return null; // Pool exhausted
            }

            renderer.gameObject.SetActive(true);
            _active.Add(renderer);
            return renderer;
        }

        public void Return(AtlasSpriteRenderer renderer)
        {
            if (!_active.Contains(renderer)) return;

            _active.Remove(renderer);
            renderer.gameObject.SetActive(false);
            renderer.transform.localPosition = Vector3.zero;
            renderer.transform.localRotation = Quaternion.identity;
            renderer.transform.localScale = Vector3.one;
            
            _available.Enqueue(renderer);
        }

        private AtlasSpriteRenderer CreateNew()
        {
            var go = new GameObject($"{_name}_{TotalCount}");
            go.transform.SetParent(_parent);
            
            var renderer = go.AddComponent<AtlasSpriteRenderer>();
            renderer.SetEntry(_entry);
            
            return renderer;
        }

        public void Clear()
        {
            foreach (var renderer in _active)
            {
                if (renderer != null)
                    Object.Destroy(renderer.gameObject);
            }
            _active.Clear();

            while (_available.Count > 0)
            {
                var renderer = _available.Dequeue();
                if (renderer != null)
                    Object.Destroy(renderer.gameObject);
            }
        }
    }
}
