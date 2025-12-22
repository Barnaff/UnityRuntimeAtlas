# Runtime Atlas Packer

High-performance runtime texture atlas packing system for Unity with dynamic updates, multiple packing algorithms, multi-page support, and comprehensive debugging tools. Perfect for dynamic sprite atlasing at runtime with non-readable textures support.

## Features

✨ **Dynamic Texture Packing** - Add and remove textures at runtime with automatic atlas management  
🚀 **Multiple Packing Algorithms** - Skyline, MaxRects, Guillotine, and Shelf algorithms  
📄 **Multi-Page Support** - Automatically creates new texture pages when atlas is full  
🎯 **Non-Readable Texture Support** - Works with textures that don't have Read/Write enabled  
🔄 **Repack & Optimize** - Dynamically repack atlases to optimize space usage  
🎨 **UI Integration** - Components for SpriteRenderer, UI Image, and RawImage  
📊 **Profiling Tools** - Built-in memory and performance profilers  
🐛 **Debug Window** - Real-time atlas visualization and inspection  
⚡ **Async Operations** - Non-blocking texture packing with async/await support  
📦 **Batch Processing** - Efficiently pack multiple textures in a single operation

## Installation

### Via Unity Package Manager (Git URL)

1. Open Unity Package Manager (`Window > Package Manager`)
2. Click the `+` button and select `Add package from git URL...`
3. Enter: `https://github.com/Barnaff/UnityRuntimeAtlas.git`

### Via manifest.json

Add the following to your `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.barnaff.runtimeatlaspacker": "https://github.com/Barnaff/UnityRuntimeAtlas.git"
  }
}
```

## Quick Start

### Basic Usage

```csharp
using RuntimeAtlasPacker;
using UnityEngine;

public class AtlasExample : MonoBehaviour
{
    void Start()
    {
        // Create an atlas with default settings
        var atlas = new RuntimeAtlas();
        
        // Add a texture
        Texture2D myTexture = Resources.Load<Texture2D>("MyImage");
        var result = atlas.Add(myTexture);
        
        if (result.result == AddResult.Success)
        {
            // Create a sprite from the atlas entry
            Sprite sprite = result.entry.CreateSprite();
            GetComponent<SpriteRenderer>().sprite = sprite;
        }
    }
}
```

### Custom Atlas Settings

```csharp
using RuntimeAtlasPacker;

var settings = new AtlasSettings
{
    InitialSize = 512,
    MaxSize = 2048,
    Padding = 2,
    Algorithm = PackingAlgorithm.MaxRects,
    Format = TextureFormat.RGBA32,
    FilterMode = FilterMode.Bilinear,
    EnableRepack = true
};

var atlas = new RuntimeAtlas(settings);
```

## Packing Algorithms

The package includes four different packing algorithms, each with different characteristics:

### Skyline (Default)
- Good balance between speed and efficiency
- Works well with varied texture sizes
- Recommended for most use cases

```csharp
settings.Algorithm = PackingAlgorithm.Skyline;
```

### MaxRects
- Most space-efficient algorithm
- Slower than Skyline but produces tighter packs
- Best for maximizing atlas usage

```csharp
settings.Algorithm = PackingAlgorithm.MaxRects;
```

### Guillotine
- Fast packing with good efficiency
- Works well with similar-sized textures
- Good for performance-critical scenarios

```csharp
settings.Algorithm = PackingAlgorithm.Guillotine;
```

### Shelf
- Simple and fast
- Best for textures of similar heights
- Predictable packing pattern

```csharp
settings.Algorithm = PackingAlgorithm.Shelf;
```

## Multi-Page Atlases

When an atlas reaches its maximum size, it automatically creates new texture pages:

```csharp
var settings = new AtlasSettings
{
    InitialSize = 512,
    MaxSize = 1024,  // Max size per page
    EnableMultiPage = true  // Automatically enabled
};

var atlas = new RuntimeAtlas(settings);

// Add many textures - new pages are created automatically
foreach (var texture in largeTextureCollection)
{
    var result = atlas.Add(texture);
    
    switch (result.result)
    {
        case AddResult.Success:
            Debug.Log($"Added to page {result.entry.TextureIndex}");
            break;
        case AddResult.Failed:
            Debug.LogError("Failed to add texture");
            break;
    }
}

// Access specific pages
Debug.Log($"Total pages: {atlas.Textures.Count}");
```

## Batch Processing

For better performance when adding multiple textures:

```csharp
Texture2D[] textures = LoadMultipleTextures();

// Add all textures in a single batch
var results = atlas.AddBatch(textures);

foreach (var result in results)
{
    if (result.result == AddResult.Success)
    {
        Sprite sprite = result.entry.CreateSprite();
        // Use the sprite...
    }
}
```

## Async Operations

For non-blocking operations, use async methods:

```csharp
using System.Threading.Tasks;

async Task LoadAtlasAsync()
{
    var atlas = new RuntimeAtlas();
    
    // Load texture asynchronously
    Texture2D texture = await LoadTextureAsync();
    
    // Add to atlas asynchronously
    var result = await atlas.AddAsync(texture);
    
    if (result.result == AddResult.Success)
    {
        UpdateUI(result.entry.CreateSprite());
    }
}
```

## UI Components

### SpriteRenderer Component

```csharp
using RuntimeAtlasPacker;

public class MySpriteController : MonoBehaviour
{
    public AtlasSpriteRenderer atlasSpriteRenderer;
    
    async void Start()
    {
        Texture2D texture = await DownloadTexture("https://example.com/image.png");
        await atlasSpriteRenderer.SetTextureAsync(texture);
    }
}
```

### UI Image Component

```csharp
using RuntimeAtlasPacker;
using UnityEngine.UI;

public class MyUIController : MonoBehaviour
{
    public AtlasImage atlasImage;
    
    void UpdateImage(Texture2D newTexture)
    {
        atlasImage.SetTexture(newTexture);
    }
}
```

### RawImage Component

```csharp
using RuntimeAtlasPacker;

public class MyRawImageController : MonoBehaviour
{
    public AtlasRawImage atlasRawImage;
    
    void Start()
    {
        atlasRawImage.SetTexture(myTexture);
    }
}
```

## Named Atlases

Manage multiple atlases with the AtlasPacker system:

```csharp
using RuntimeAtlasPacker;

// Create named atlases
var characterAtlas = AtlasPacker.GetOrCreateAtlas("Characters");
var itemAtlas = AtlasPacker.GetOrCreateAtlas("Items");
var uiAtlas = AtlasPacker.GetOrCreateAtlas("UI");

// Add textures to specific atlases
characterAtlas.Add(playerTexture);
itemAtlas.Add(swordTexture);
uiAtlas.Add(buttonTexture);

// Access atlases by name later
var atlas = AtlasPacker.GetAtlas("Characters");
```

## Repack and Optimize

Dynamically repack atlases to optimize space after removing textures:

```csharp
var atlas = new RuntimeAtlas(new AtlasSettings 
{ 
    EnableRepack = true 
});

// Add textures
var entry1 = atlas.Add(texture1);
var entry2 = atlas.Add(texture2);
var entry3 = atlas.Add(texture3);

// Remove a texture
atlas.Remove(entry1.entry.Id);

// Repack to optimize space
atlas.Repack();
```

## Non-Readable Texture Support

The package automatically handles non-readable textures using GPU-based copying:

```csharp
// Works even if the texture doesn't have Read/Write enabled
Texture2D nonReadableTexture = Resources.Load<Texture2D>("MyImage");
var result = atlas.Add(nonReadableTexture);

// The package automatically:
// 1. Creates a temporary readable copy
// 2. Copies pixel data via GPU (Graphics.Blit with shader)
// 3. Cleans up temporary resources
```

## Debugging Tools

### Atlas Debug Window

Open via `Window > Runtime Atlas > Atlas Debug`

Features:
- Real-time atlas visualization
- Texture page browser
- Sprite entry inspection
- Renderer tracking
- Click on atlas list items to view details
- Automatic connection in Play mode

### Memory Analyzer

Open via `Window > Runtime Atlas > Memory Analyzer`

Features:
- Real-time memory tracking
- Allocation graphs
- GC allocation monitoring
- Per-atlas memory usage
- Normalized graph view

### Performance Profiler

Open via `Window > Runtime Atlas > Profiler`

Features:
- Active atlas count
- Entry count tracking
- Packing time graphs
- Memory usage over time
- Automatic profiling in Play mode

## Advanced Examples

### Web Image Downloader with Atlas Management

```csharp
using RuntimeAtlasPacker;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public class WebImageAtlas : MonoBehaviour
{
    private RuntimeAtlas _atlas;
    
    void Start()
    {
        _atlas = new RuntimeAtlas(new AtlasSettings
        {
            MaxSize = 1024,
            Padding = 2,
            Algorithm = PackingAlgorithm.MaxRects
        });
        
        StartCoroutine(DownloadAndAddImages());
    }
    
    IEnumerator DownloadAndAddImages()
    {
        string[] urls = {
            "https://picsum.photos/256",
            "https://picsum.photos/200",
            "https://picsum.photos/300"
        };
        
        foreach (var url in urls)
        {
            yield return DownloadAndAdd(url);
            yield return new WaitForSeconds(2f);
        }
    }
    
    IEnumerator DownloadAndAdd(string url)
    {
        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(url))
        {
            yield return www.SendWebRequest();
            
            if (www.result == UnityWebRequest.Result.Success)
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(www);
                var result = _atlas.Add(texture);
                
                if (result.result == AddResult.Success)
                {
                    CreateSpriteObject(result.entry.CreateSprite());
                }
            }
        }
    }
    
    void CreateSpriteObject(Sprite sprite)
    {
        GameObject obj = new GameObject("WebSprite");
        var sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        
        // Position in a grid
        int count = _atlas.EntryCount;
        obj.transform.position = new Vector3((count % 5) * 2, (count / 5) * 2, 0);
    }
}
```

### Dynamic Batch Loading

```csharp
using RuntimeAtlasPacker;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class BatchAtlasLoader : MonoBehaviour
{
    private RuntimeAtlas _atlas;
    
    async void Start()
    {
        _atlas = new RuntimeAtlas(new AtlasSettings
        {
            MaxSize = 2048,
            EnableRepack = true
        });
        
        await LoadBatchAsync();
    }
    
    async Task LoadBatchAsync()
    {
        // Load all textures from Resources
        Texture2D[] textures = Resources.LoadAll<Texture2D>("Sprites");
        
        Debug.Log($"Loading {textures.Length} textures...");
        
        // Add in batch for better performance
        var results = _atlas.AddBatch(textures);
        
        List<Sprite> sprites = new List<Sprite>();
        foreach (var result in results)
        {
            if (result.result == AddResult.Success)
            {
                sprites.Add(result.entry.CreateSprite());
            }
        }
        
        Debug.Log($"Successfully added {sprites.Count} sprites across {_atlas.Textures.Count} pages");
        
        // Use the sprites...
        DisplaySprites(sprites);
    }
    
    void DisplaySprites(List<Sprite> sprites)
    {
        int columns = 10;
        
        for (int i = 0; i < sprites.Count; i++)
        {
            GameObject obj = new GameObject($"Sprite_{i}");
            var sr = obj.AddComponent<SpriteRenderer>();
            sr.sprite = sprites[i];
            
            float x = (i % columns) * 1.5f;
            float y = -(i / columns) * 1.5f;
            obj.transform.position = new Vector3(x, y, 0);
        }
    }
}
```

### Atlas with Automatic Cleanup

```csharp
using RuntimeAtlasPacker;
using System.Collections.Generic;
using UnityEngine;

public class ManagedAtlas : MonoBehaviour
{
    private RuntimeAtlas _atlas;
    private Dictionary<string, int> _entryIds = new Dictionary<string, int>();
    
    void Start()
    {
        _atlas = new RuntimeAtlas(new AtlasSettings
        {
            EnableRepack = true,
            MaxSize = 1024
        });
    }
    
    public void AddImage(string id, Texture2D texture)
    {
        // Remove old image if exists
        if (_entryIds.ContainsKey(id))
        {
            RemoveImage(id);
        }
        
        var result = _atlas.Add(texture);
        if (result.result == AddResult.Success)
        {
            _entryIds[id] = result.entry.Id;
        }
    }
    
    public void RemoveImage(string id)
    {
        if (_entryIds.TryGetValue(id, out int entryId))
        {
            _atlas.Remove(entryId);
            _entryIds.Remove(id);
            
            // Repack to optimize space
            _atlas.Repack();
        }
    }
    
    public Sprite GetSprite(string id)
    {
        if (_entryIds.TryGetValue(id, out int entryId))
        {
            var entry = _atlas.GetEntry(entryId);
            if (entry != null)
            {
                return entry.CreateSprite();
            }
        }
        return null;
    }
    
    void OnDestroy()
    {
        _atlas?.Dispose();
    }
}
```

## API Reference

### RuntimeAtlas

```csharp
// Constructor
RuntimeAtlas(AtlasSettings settings = null)

// Properties
int Width { get; }
int Height { get; }
int EntryCount { get; }
List<Texture2D> Textures { get; }

// Methods
(AddResult result, AtlasEntry entry) Add(Texture2D texture)
Task<(AddResult result, AtlasEntry entry)> AddAsync(Texture2D texture)
List<(AddResult result, AtlasEntry entry)> AddBatch(Texture2D[] textures)
bool Remove(int entryId)
AtlasEntry GetEntry(int entryId)
void Repack()
void Clear()
void Dispose()
```

### AtlasSettings

```csharp
int InitialSize = 512          // Initial texture size
int MaxSize = 2048             // Maximum texture size per page
int Padding = 1                // Padding between sprites
PackingAlgorithm Algorithm     // Packing algorithm to use
TextureFormat Format           // Texture format
FilterMode FilterMode          // Texture filter mode
bool EnableRepack = false      // Enable dynamic repacking
```

### AtlasEntry

```csharp
int Id { get; }                // Unique entry ID
RectInt Rect { get; }          // Rectangle in atlas
int TextureIndex { get; }      // Page index for multi-page atlases
RuntimeAtlas Atlas { get; }    // Parent atlas reference

Sprite CreateSprite()          // Create sprite from entry
```

### AddResult Enum

```csharp
Success     // Texture added successfully
Failed      // Failed to add texture
Full        // Atlas is full (all pages exhausted)
```

## Performance Tips

1. **Use Batch Operations** - `AddBatch()` is more efficient than multiple `Add()` calls
2. **Choose the Right Algorithm** - MaxRects for space efficiency, Skyline for speed
3. **Set Appropriate Max Size** - Larger atlases = fewer pages but more memory per page
4. **Enable Repack Wisely** - Only enable if you frequently add/remove textures
5. **Use Async Methods** - For better frame rates during heavy loading
6. **Set Proper Padding** - Prevents texture bleeding (1-2 pixels usually sufficient)
7. **Monitor with Profilers** - Use built-in tools to track memory and performance

## Common Issues

### Textures Not Showing
- Check that textures are added successfully (`AddResult.Success`)
- Verify atlas has available space
- Ensure sprites are created from atlas entries

### Color Issues
- Verify texture format matches your needs (RGBA32 for transparency)
- Check that linear/sRGB settings match your rendering pipeline

### Performance Issues
- Use batch operations instead of individual adds
- Consider using a faster packing algorithm
- Reduce texture sizes before adding to atlas

### Memory Issues
- Monitor atlas count with Memory Analyzer
- Set appropriate MaxSize to limit memory per page
- Call Dispose() on atlases you no longer need

## Requirements

- Unity 2021.3 or higher
- Collections package (1.4.0+)
- Burst package (1.8.0+)
- Mathematics package (1.2.6+)

## License

MIT License - see [LICENSE.md](https://github.com/Barnaff/UnityRuntimeAtlas/blob/main/LICENSE.md)

## Support

- 📖 [Documentation](https://github.com/Barnaff/UnityRuntimeAtlas/#readme)
- 🐛 [Issue Tracker](https://github.com/Barnaff/UnityRuntimeAtlas/issues)
- 💬 [Discussions](https://github.com/Barnaff/UnityRuntimeAtlas/discussions)

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

---

Made with ❤️ by [Kobi Chariski](https://github.com/Barnaff/)

