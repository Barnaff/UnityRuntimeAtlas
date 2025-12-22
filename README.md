# Runtime Atlas Packer

High-performance runtime texture atlas packing for Unity with dynamic updates, Burst compilation, and zero-allocation design.

[![Unity Version](https://img.shields.io/badge/Unity-2021.3%2B-blue.svg)](https://unity3d.com/get-unity/download)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## ✨ Features

- **⚡ Fast Packing**: Burst-compiled MaxRects and Skyline algorithms
- **🎯 Memory Efficient**: Native containers, no GC allocations during packing
- **🔄 Dynamic Updates**: Add/remove sprites at runtime without breaking references
- **📈 Auto-Growth**: Atlas automatically grows when full (configurable)
- **⏱️ Async Support**: Full async/await API for background packing
- **🔗 Auto-Updating References**: `AtlasEntry` objects automatically update when UV coordinates change
- **🚀 GPU Accelerated**: Uses `Graphics.CopyTexture` when available
- **🎨 Simple API**: One-liner for common operations
- **🖼️ UI Integration**: Built-in components for SpriteRenderer, UI Image, and RawImage
- **🛠️ Comprehensive Tools**: Debug window, profiler, memory analyzer, and more

## 📦 Installation

### Via Git URL (Recommended)

1. Open **Window > Package Manager**
2. Click the **+** button
3. Select **Add package from git URL**
4. Paste: `https://github.com/yourusername/runtime-atlas-packer.git`

### Via Package Manager (Local)

1. Clone this repository
2. Open **Window > Package Manager**
3. Click the **+** button
4. Select **Add package from disk**
5. Navigate to the cloned folder and select `package.json`

### Via manifest.json

Add to your `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.gamedev.runtimeatlaspacker": "https://github.com/yourusername/runtime-atlas-packer.git"
  }
}
```

### Manual Installation

Copy the entire `RuntimeAtlasPacker` folder to your project's `Packages` directory.

## Quick Start

### Basic Usage

```csharp
using RuntimeAtlasPacker;
using UnityEngine;

public class Example : MonoBehaviour
{
    public Texture2D[] sprites;
    public SpriteRenderer targetRenderer;

    void Start()
    {
        // Simple one-liner
        var entry = AtlasPacker.Pack(sprites[0]);
        entry.ApplyTo(targetRenderer);
    }
}
```

### Creating an Atlas

```csharp
// Use default settings
var atlas = new RuntimeAtlas();

// Or use presets
var mobileAtlas = new RuntimeAtlas(AtlasSettings.Mobile);
var hqAtlas = new RuntimeAtlas(AtlasSettings.HighQuality);

// Or customize
var customAtlas = new RuntimeAtlas(new AtlasSettings
{
    InitialSize = 1024,
    MaxSize = 4096,
    Padding = 2,
    Format = TextureFormat.RGBA32,
    Algorithm = PackingAlgorithm.MaxRects,
    GrowthStrategy = GrowthStrategy.Double
});
```

### Adding Sprites

```csharp
// Single texture
AtlasEntry entry = atlas.Add(myTexture);

// Batch add (more efficient)
AtlasEntry[] entries = atlas.AddBatch(textureArray);

// Async
AtlasEntry entry = await atlas.AddAsync(myTexture);
AtlasEntry[] entries = await atlas.AddBatchAsync(textureArray);
```

### Using Atlas Entries

The `AtlasEntry` is a reference that automatically updates when the atlas changes:

```csharp
AtlasEntry entry = atlas.Add(texture);

// Get UV coordinates
Rect uv = entry.UV;
Vector4 uvVector = entry.GetUVVector4();

// Create a Unity Sprite
Sprite sprite = entry.CreateSprite(pixelsPerUnit: 100f);

// Apply to renderers
entry.ApplyTo(spriteRenderer);
entry.ApplyTo(uiImage);
entry.ApplyTo(rawImage);

// Listen for changes
entry.OnUVChanged += (e) => {
    Debug.Log($"Entry {e.Id} UV changed to {e.UV}");
};
```

### Auto-Updating Components

The package provides self-contained components that handle everything internally - no extra components needed:

```csharp
// === AtlasSpriteRenderer (replaces SpriteRenderer) ===
// Just add and assign - it handles atlas binding automatically

var renderer = gameObject.AddComponent<AtlasSpriteRenderer>();

// Option 1: Set from atlas entry
renderer.SetEntry(entry);

// Option 2: Set from texture (auto-packs into atlas)
renderer.SetTexture(myTexture);

// Option 3: Async loading
await renderer.SetTextureAsync(myTexture);

// Configure
renderer.PixelsPerUnit = 100f;
renderer.Pivot = new Vector2(0.5f, 0.5f);
renderer.TargetAtlasName = "MyAtlas"; // optional, uses default if empty


// === AtlasImage (UI - replaces Image) ===
// Full-featured UI component with slicing, tiling support

var image = gameObject.AddComponent<AtlasImage>();
image.SetEntry(entry);
// or
image.SetTexture(myTexture);

image.Type = AtlasImage.ImageType.Sliced; // Simple, Sliced, Tiled, Filled
image.PreserveAspect = true;


// === AtlasRawImage (UI - most performant) ===
// Lightweight, no Sprite creation, uses UV directly

var rawImage = gameObject.AddComponent<AtlasRawImage>();
rawImage.SetEntry(entry);
// or
rawImage.SetTexture(myTexture);
```

All components automatically update when:
- Atlas is resized
- Entry UV coordinates change
- New sprites are added to the atlas

### Extension Methods

```csharp
// Quick setup via extensions
gameObject.AddAtlasSpriteRenderer(entry);
gameObject.AddAtlasSpriteRenderer(texture); // auto-packs

gameObject.AddAtlasImage(entry);
gameObject.AddAtlasImage(texture);

gameObject.AddAtlasRawImage(entry);
gameObject.AddAtlasRawImage(texture);

// Pack and bind in one call
myTexture.PackAndBind(atlasSpriteRenderer);
myTexture.PackAndBind(atlasImage);
myTexture.PackAndBind(atlasRawImage);
```

### Legacy Components (Manual Binding)

For more control, use the separate binding components:

```csharp
// AtlasSprite - Separate component for SpriteRenderer binding
var atlasSprite = gameObject.AddComponent<AtlasSprite>();
atlasSprite.Bind(entry, pixelsPerUnit: 100f);

// AtlasMaterial - For custom shaders and mesh renderers
var atlasMat = gameObject.AddComponent<AtlasMaterial>();
atlasMat.Bind(entry);
```

### Removing Sprites

```csharp
// Remove by entry
atlas.Remove(entry);

// Or use the entry directly
entry.Remove();

// Remove from integrated components
atlasSpriteRenderer.RemoveFromAtlas();
atlasImage.RemoveFromAtlas();

// Repack to reclaim fragmented space
atlas.Repack();
```

### Named Atlases

```csharp
// Get or create named atlases
var uiAtlas = AtlasPacker.GetOrCreate("UI");
var effectsAtlas = AtlasPacker.GetOrCreate("Effects", AtlasSettings.Mobile);

// Pack into named atlas
AtlasPacker.Pack("UI", texture);

// Dispose
AtlasPacker.DisposeAtlas("UI");
AtlasPacker.DisposeAll();
```

## Advanced Usage

### Batch Processing with Jobs

```csharp
using RuntimeAtlasPacker;

// Analyze batch before packing
var stats = AtlasBatchProcessor.AnalyzeBatch(textures, atlasSize: 2048);
Debug.Log(stats); // Shows fill ratio, recommended size, etc.

// Check if textures will fit
bool willFit = AtlasBatchProcessor.WillFit(textures, 1024, 1024);

// Calculate minimum size needed
int minSize = AtlasBatchProcessor.CalculateMinimumSize(textures);

// High-performance batch pack
var entries = AtlasBatchProcessor.PackBatch(atlas, textures);
```

### Custom Shader Integration

```csharp
// Manual property block update
MaterialPropertyBlock block = new MaterialPropertyBlock();
renderer.GetPropertyBlock(block);
block.SetTexture("_MainTex", entry.Texture);
block.SetVector("_MainTex_ST", entry.GetUVVector4());
renderer.SetPropertyBlock(block);

// Or use the extension
entry.ApplyTo(meshRenderer, "_MainTex", "_MainTex_ST");

// Get quad UVs for mesh
Vector2[] uvs = entry.GetQuadUVs();
mesh.uv = uvs;
```

### Event Handling

```csharp
// Atlas events
atlas.OnAtlasResized += (a) => {
    Debug.Log($"Atlas resized to {a.Width}x{a.Height}");
    // Update any external references to the texture
};

atlas.OnEntryUpdated += (a, e) => {
    Debug.Log($"Entry {e.Id} updated");
};

// Entry events
entry.OnUVChanged += (e) => {
    // Update mesh UVs, material properties, etc.
};
```

### Memory Management

```csharp
// Manual apply after batching (more efficient)
for (int i = 0; i < 100; i++)
{
    atlas.Add(textures[i]);
}
atlas.Apply(); // Apply all at once

// Dispose when done
atlas.Dispose();

// Or for static API
AtlasPacker.DisposeAll();
```

## Performance Tips

1. **Use Batch Operations**: `AddBatch()` is significantly faster than adding one at a time
2. **Sort by Size**: The batch processor automatically sorts by area for optimal packing
3. **Choose the Right Algorithm**: 
   - `MaxRects`: Better packing quality, slightly slower
   - `Skyline`: Faster, good for real-time updates
4. **Pre-calculate Size**: Use `AtlasBatchProcessor.CalculateMinimumSize()` to avoid growth
5. **Limit Removals**: Frequent remove/add cycles cause fragmentation. Call `Repack()` periodically
6. **Use Async for Large Operations**: Prevents frame drops
7. **Disable Readable**: Set `Readable = false` for textures you don't need CPU access to

## Settings Reference

| Setting | Default | Description |
|---------|---------|-------------|
| `InitialSize` | 1024 | Starting atlas dimension |
| `MaxSize` | 4096 | Maximum atlas dimension |
| `Padding` | 2 | Pixels between sprites |
| `Format` | RGBA32 | Texture format |
| `FilterMode` | Bilinear | Texture filtering |
| `GenerateMipMaps` | false | Enable mipmaps |
| `Readable` | false | CPU read access (uses more memory) |
| `GrowthStrategy` | Double | How atlas grows when full |
| `Algorithm` | MaxRects | Packing algorithm |

## 🛠️ Editor Tools

The package includes comprehensive editor tools for debugging and managing atlases at runtime.

### Atlas Debug Window
**Window > Runtime Atlas Packer > Debug Window**

Real-time atlas inspection and management:

- **Atlases Tab**: View all active atlases with statistics (size, fill ratio, entry count, memory usage)
- **Renderers Tab**: See all atlas renderers in the scene and their bound entries
- **Statistics Tab**: Global memory and performance statistics across all atlases
- **Tools Tab**: Create/manage atlases, repack, and bulk operations
- **Textures Tab**: Browse all entries in selected atlas with UV coordinates

**Key Features**:
- 🔍 Live preview of atlas texture with entry highlighting
- ➕ Drag-and-drop textures to add to atlas
- 💾 Export atlases as PNG files
- 🔄 Repack atlases to reclaim fragmented space
- 📊 Real-time statistics with auto-refresh

**Important**: Atlases created with `AtlasPacker.GetOrCreate()` or `AtlasPacker.Pack()` will automatically appear in the Debug Window. Direct instantiation with `new RuntimeAtlas()` won't be tracked.

### Profiler Window
**Window > Runtime Atlas Packer > Profiler**

- Track all atlas operations (add, remove, resize, repack)
- View operation duration in milliseconds
- Filter by operation type
- Identify performance bottlenecks

### Memory Analyzer
**Window > Runtime Atlas Packer > Memory Analyzer**

- View memory usage per atlas
- See texture memory vs overhead breakdown
- Get optimization recommendations
- Identify underutilized atlases

### Texture Picker
**Window > Runtime Atlas Packer > Texture Picker**

- Browse project textures with preview
- Select multiple textures to add to atlas
- Filter by name
- Quick add to any atlas

### Batch Import Wizard
**Window > Runtime Atlas Packer > Batch Import Wizard**

- Import entire folders of textures
- Configure atlas settings
- Automatic size estimation
- Handles texture readability automatically

### Scene View Tools

- **Toggle Gizmos**: Window > Runtime Atlas Packer > Toggle Gizmos
- Shows bounds and entry info for all atlas renderers in scene view
- Scene view overlay panel for quick access

### Custom Inspectors

All atlas components (AtlasSpriteRenderer, AtlasImage, AtlasRawImage) have custom inspectors showing:
- Current entry status
- Atlas information
- Quick actions (set texture, clear, refresh)
- Atlas texture preview with entry highlight

### Preferences

Access via Edit > Preferences > Runtime Atlas Packer:
- Enable/disable scene gizmos
- Auto-refresh settings
- Profiler toggle

## Requirements

- Unity 2021.3 or newer
- Collections 1.4.0+
- Mathematics 1.2.6+
- Burst 1.6.0+ (optional, for improved performance)

## 🐛 Troubleshooting

### Atlas Not Visible in Debug Window

If your atlases don't appear in the Atlas Debug Window:

**Solution**: Use `AtlasPacker.GetOrCreate()` or `AtlasPacker.Pack()` instead of creating atlases directly with `new RuntimeAtlas()`.

```csharp
// ✅ Good - Atlas will be visible in Debug Window
var atlas = AtlasPacker.GetOrCreate("MyAtlas", settings);

// ❌ Not recommended - Won't appear in Debug Window
var atlas = new RuntimeAtlas(settings);
```

The Debug Window tracks atlases registered through the `AtlasPacker` static API.

### Burst Compilation Errors

If you encounter errors like `"Burst failed to compile the function pointer"` during atlas generation:

1. **Quick Fix**: Add `DISABLE_BURST_COMPILATION` to **Project Settings > Player > Scripting Define Symbols**
2. **Alternative**: The package automatically falls back to non-Burst implementations (you'll see a warning but it will still work)
3. **Details**: See [BURST_COMPILATION.md](BURST_COMPILATION.md) for more information

### Common Issues

- **Textures not packing**: Ensure textures are marked as readable in import settings
- **Poor packing efficiency**: Use `AtlasBatchProcessor.AnalyzeBatch()` for optimization recommendations
- **Atlas too large**: Adjust `InitialSize` and `MaxSize` in atlas settings
- **Icon loading errors**: Package is compatible with Unity 2021.3+, some editor icons may vary by version

## 📝 License

MIT License - Free for commercial and personal use. See [LICENSE.md](LICENSE.md) for details.
