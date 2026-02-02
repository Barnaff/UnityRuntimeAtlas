# Atlas URL Manager - Quick Reference

## Setup (3 Steps)

1. **Create GameObject** → Add `AtlasUrlManagerExample` component
2. **Assign URL File** → Drag your `.txt` file to "Url List File" field
3. **Press Play** → UI creates automatically!

## Buttons

| Button | Action | Count | Method |
|--------|--------|-------|--------|
| 🔵 **Batch (10)** | Download random URLs | 10 | Batch API |
| 🔵 **Single (3-5)** | Download random URLs | 3-5 | Single API |
| 🔵 **Save** | Save atlas to disk | - | Async Save |
| 🔵 **Unload** | Clear atlas & images | - | Dispose |
| 🔵 **Load** | Load saved atlas | - | Async Load |

## URL File Format

```text
# Comments start with #
https://example.com/image1.jpg
https://example.com/image2.png

# Empty lines ignored
https://example.com/image3.jpg
```

## Key Features

✅ **Auto-Initialization** - Atlas created automatically when needed (NEW!)
✅ **Random Selection** - Never downloads the same URL twice
✅ **Usage Tracking** - Tracks used URLs with HashSet
✅ **Batch Downloads** - 4 concurrent downloads (faster)
✅ **Single Downloads** - One at a time (memory-efficient)
✅ **Persistent Storage** - Save/Load with full metadata
✅ **Auto UI Creation** - Canvas, buttons, scroll view all automatic
✅ **Responsive Grid** - Configurable columns and spacing
✅ **Status Feedback** - Real-time status messages

## Status Messages

```
✅ Ready! Loaded 400 URLs. Used: 0
🔧 Atlas auto-created
⏳ Downloading 10 images (batch)...
✅ Added 10 images (batch). Total: 10, Used URLs: 10/400
✅ Atlas saved! 10 entries, 1 page(s)
✅ Atlas loaded! 10 entries, 1 page(s)
⚠️ No unused URLs available!
❌ No saved atlas found!
```

## Settings

### Atlas Settings
- **Atlas Size**: 2048 (texture dimensions)
- **Max Pages**: 10 (max texture pages)
- **Padding**: 2 (pixels between images)

### UI Settings
- **Thumbnail Size**: 120 (thumbnail width/height)
- **Spacing**: 10 (gap between thumbnails)
- **Columns**: 5 (thumbnails per row)

## File Locations

**Save Path**: `Application.persistentDataPath/AtlasUrlManager/`

```
AtlasUrlManager/
  ├── AtlasUrlManager.json          # Metadata
  ├── AtlasUrlManager_page0.png     # Page 0 texture
  └── AtlasUrlManager_page1.png     # Page 1 texture (if needed)
```

## Common Workflows

### Download & Save
```
1. Click "Batch (10)"   → Downloads 10 images
2. Click "Single (3-5)" → Downloads 3-5 more
3. Click "Save"         → Saves to disk
```

### Load Previous Session
```
1. Press Play
2. Click "Load"         → Restores saved atlas
3. All images reappear
```

### Clear & Start Fresh
```
1. Click "Unload"       → Clears everything
2. Click "Batch (10)"   → Download new images
```

## API Methods Used

```csharp
// Batch download (fast)
await _atlas.DownloadAndAddBatchAsync(urlsWithNames, ...);

// Single download
await _atlas.DownloadAndAddAsync(url, key, version, ...);

// Save
await AtlasPersistence.SaveAtlasAsync(_atlas, path);

// Load
_atlas = await AtlasPersistence.LoadAtlasAsync(path);

// Display
var entries = _atlas.GetAllEntries().ToList();
var sprite = entry.CreateSprite();
```

## Tips

💡 **Start with 10-20 URLs** for initial testing
💡 **Save after downloading** to preserve progress
💡 **Watch Console logs** for detailed progress
💡 **Test URLs in browser** before adding to file
💡 **Use comments in URL file** to organize sections

## Troubleshooting

| Issue | Solution |
|-------|----------|
| No images downloading | Check URL file is assigned and URLs are valid |
| "No unused URLs" | All URLs used - add more or restart scene |
| "No saved atlas" | Haven't saved yet - download images first |
| Buttons not working | Check Console for errors |

## Code Reference

**Main Class**: `AtlasUrlManagerExample.cs`
**Location**: `Assets/Packages/UnityRuntimeAtlas/Samples/BasicUsage/`
**Guide**: `ATLAS_URL_MANAGER_EXAMPLE_GUIDE.md`

Perfect for learning batch downloads, save/load, and UI creation! 🚀

