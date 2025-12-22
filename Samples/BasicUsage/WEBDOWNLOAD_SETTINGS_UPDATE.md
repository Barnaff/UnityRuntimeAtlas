# WebDownloadExample Settings Update

## Changes Made

The `WebDownloadExample.cs` has been updated to properly expose and apply atlas settings through the Unity Inspector.

### New Inspector Fields

All atlas configuration options are now exposed in the Inspector under the **"Atlas Settings"** header:

```csharp
[Header("Atlas Settings")]
[Tooltip("Maximum atlas texture size")]
public int maxAtlasSize = 2048;

[Tooltip("Initial atlas texture size")]
public int initialAtlasSize = 512;

[Tooltip("Maximum number of atlas pages (-1 = unlimited, 0 = single page only, >0 = specific limit)")]
public int maxPageCount = -1;

[Tooltip("Padding between textures in pixels")]
public int padding = 2;

[Tooltip("Use a named atlas instead of default")]
public bool useNamedAtlas = true;

[Tooltip("Name of the atlas to use (only if useNamedAtlas is true)")]
public string atlasName = "WebDownloadAtlas";
```

### Settings Application

The settings are now properly applied when creating or getting the atlas:

**Before:**
```csharp
// Settings were created but not applied to any atlas
var settings = AtlasSettings.Default;
settings.MaxSize = maxAtlasSize;
// ... but then the default atlas was used without these settings
```

**After:**
```csharp
// Settings are configured
var settings = AtlasSettings.Default;
settings.MaxSize = maxAtlasSize;
settings.InitialSize = Mathf.Clamp(initialAtlasSize, 256, maxAtlasSize);
settings.MaxPageCount = maxPageCount;
settings.Padding = padding;

// Settings are applied to a named atlas
if (useNamedAtlas && !string.IsNullOrEmpty(atlasName))
{
    _activeAtlasName = atlasName;
    var atlas = AtlasPacker.GetOrCreate(atlasName, settings);
    // Atlas is created with custom settings!
}
```

### Named Atlas Support

The example now supports using either:
- **Named Atlas** (default): Creates a dedicated atlas with custom settings
- **Default Atlas**: Uses the shared default atlas (settings may not be customizable if already created)

This is controlled by the `useNamedAtlas` checkbox in the Inspector.

### Multi-Page Support

The example now fully supports the new multi-page features:

- **MaxPageCount** configuration
- Tracks overflow atlases correctly for both named and default atlases
- Shows accurate atlas counts in debug logs
- Respects page limits (will log warnings if page limit is reached)

## Inspector Configuration

### Typical Settings for Different Use Cases

#### Mobile (Memory Constrained)
```
Max Atlas Size: 1024
Initial Atlas Size: 512
Max Page Count: 3
Padding: 2
Use Named Atlas: true
Atlas Name: "MobileWebAtlas"
```

#### Desktop (Flexible)
```
Max Atlas Size: 2048
Initial Atlas Size: 1024
Max Page Count: -1 (unlimited)
Padding: 2
Use Named Atlas: true
Atlas Name: "WebDownloadAtlas"
```

#### Single Page Only
```
Max Atlas Size: 2048
Initial Atlas Size: 1024
Max Page Count: 0 (single page)
Padding: 1
Use Named Atlas: true
Atlas Name: "SinglePageAtlas"
```

#### Limited Pages
```
Max Atlas Size: 2048
Initial Atlas Size: 512
Max Page Count: 5
Padding: 2
Use Named Atlas: true
Atlas Name: "LimitedAtlas"
```

## Usage Example

1. **Add the component** to a GameObject in your scene
2. **Configure in Inspector**:
   - Set Max Atlas Size (1024, 2048, 4096)
   - Set Initial Atlas Size (starts smaller, grows as needed)
   - Set Max Page Count (-1 for unlimited, 0 for single page, or specific limit)
   - Set Padding (space between textures)
   - Enable "Use Named Atlas" (recommended)
   - Set Atlas Name (e.g., "WebDownloadAtlas")
3. **Assign UI references**:
   - UI Container (parent for spawned images)
   - UI Image Prefab (template with AtlasImage component)
4. **Play** and watch images download and pack into the atlas

## Debug Logging

The example now provides detailed logging:

```
[WebDownload] Created/using named atlas 'WebDownloadAtlas' with MaxSize: 2048, MaxPages: unlimited, Padding: 2
[WebDownload] Added image 1 - Total atlases: 1, Total images: 1
[WebDownload] Added image 5 - Total atlases: 2, Total images: 5  // Created overflow atlas
[WebDownload] Batch complete! Total atlases: 2, Total images: 10
```

## Benefits

✅ **Configurable in Inspector** - No code changes needed  
✅ **Proper settings application** - Settings actually affect the atlas  
✅ **Named atlas support** - Isolated from default atlas  
✅ **Multi-page aware** - Works with new page limit features  
✅ **Memory control** - Limit atlas size and page count  
✅ **Better debugging** - Clear logging of atlas configuration  

## Migration from Old Code

If you were using the old version:

**Old behavior:**
- Settings were created but not used
- Always used default atlas
- No page limit control

**New behavior:**
- Settings are applied to named atlas
- Can choose named or default atlas
- Full page limit control
- Better memory management

No breaking changes - existing scenes will continue to work with default values.

## Important Notes

### Default Atlas Limitation

If using the default atlas (`useNamedAtlas = false`):
- Settings cannot be changed after the default atlas is created
- This is because the default atlas is a singleton
- **Recommendation**: Always use named atlases for custom settings

### Page Limits

When `maxPageCount` is reached:
- New textures will fail to pack
- Warning logged: "Failed to pack texture - atlas may be at page limit!"
- Consider increasing page limit or using texture pooling

### Memory Usage

Calculate approximate memory:
```
Memory = PageCount × (MaxSize²) × 4 bytes × (1.33 if mipmaps)

Example: 3 pages × 2048² × 4 = 48 MB
With mipmaps: 48 MB × 1.33 = 64 MB
```

Set `maxPageCount` accordingly based on your target platform's memory budget.

