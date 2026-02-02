# Atlas URL Manager Example - Complete Guide

## Overview
`AtlasUrlManagerExample` is a comprehensive example that demonstrates URL-based image loading with full UI controls. It showcases random URL selection, batch/single downloads, save/load operations, and a scrollable image display.

## Features

### ✅ URL Management
- **Load URLs from TextAsset** - Provide a text file with image URLs
- **Random Selection** - Randomly picks unused URLs to avoid duplicates
- **Usage Tracking** - Maintains a list of used URLs
- **Smart Fallback** - Warns when all URLs are exhausted
- **Auto-Initialization** - Automatically creates atlas if not initialized (NEW!)

### ✅ Download Operations
- **Batch Download (10 images)** - Uses `DownloadAndAddBatchAsync` API
- **Single Download (3-5 images)** - Uses `DownloadAndAddAsync` API
- **Concurrent Downloads** - Batch mode downloads 4 images simultaneously

### ✅ Atlas Persistence
- **Save to Disk** - Saves atlas with all images and metadata
- **Unload & Clear** - Clears memory and UI display
- **Load from Disk** - Restores saved atlas with all images

### ✅ UI Display
- **Scrollable Grid** - Automatically arranged thumbnails
- **Responsive Layout** - Configurable columns and spacing
- **Visual Feedback** - Status messages for all operations
- **Button Controls** - 5 clear action buttons

## Setup Instructions

### 1. Create URL List File
Create a text file (`.txt`) with one URL per line:

```text
# Image URLs for Atlas Manager Example
https://picsum.photos/300/300?random=1
https://picsum.photos/300/300?random=2
https://picsum.photos/300/300?random=3
# Add more URLs...
```

**Format Rules:**
- One URL per line
- Lines starting with `#` are comments (ignored)
- Empty lines are ignored
- URLs must start with `http://` or `https://`

### 2. Add Component to Scene
1. Create an empty GameObject in your scene
2. Add the `AtlasUrlManagerExample` component
3. In the Inspector, assign your URL text file to the **Url List File** field

### 3. Configure Settings (Optional)

**Atlas Settings:**
- `Atlas Size` - Texture size (default: 2048)
- `Max Pages` - Maximum atlas pages (default: 10)
- `Padding` - Padding between images (default: 2)

**UI Settings:**
- `Thumbnail Size` - Size of each image thumbnail (default: 120)
- `Spacing` - Space between thumbnails (default: 10)
- `Columns` - Number of columns in grid (default: 5)

**Save Path:**
- `Save Path` - Folder name for saved atlas (default: "AtlasUrlManager")

### 4. Run the Scene
Press Play - the UI will be created automatically!

**Note**: The atlas is automatically created when you first click any download button. No manual setup needed!

## UI Controls

### 🔵 Batch (10)
Downloads 10 random unused URLs from your file using batch download.
- Uses `DownloadAndAddBatchAsync` API
- Downloads 4 images concurrently
- Faster for multiple images

### 🔵 Single (3-5)
Downloads 3-5 random unused URLs (random count) using single downloads.
- Uses `DownloadAndAddAsync` API
- Downloads images one by one
- Good for gradual loading

### 🔵 Save
Saves the current atlas to disk.
- Saves all images and metadata
- Creates `.json` file and `.png` files for each page
- Location: `Application.persistentDataPath/AtlasUrlManager/`

### 🔵 Unload
Unloads the atlas and clears all displayed images.
- Frees memory
- Clears UI display
- Atlas becomes null (can load again later)

### 🔵 Load
Loads a previously saved atlas from disk.
- Restores all images
- Rebuilds UI display
- Shows all previously saved images

## Status Bar
The status bar shows real-time information:
```
Ready! Loaded 100 URLs. Used: 0
🔧 Atlas auto-created
⏳ Downloading 10 images (batch)...
✅ Added 10 images (batch). Total: 10, Used URLs: 10/100
⚠️ No saved atlas found!
```

## Auto-Initialization (NEW!)

The example now **automatically creates an atlas** when you first click any download button. This means:
- ✅ No manual setup required
- ✅ Just assign your URL file and click buttons
- ✅ Atlas is created with optimal settings automatically
- ✅ You'll see "🔧 Atlas auto-created" in the status bar

This makes the example even more user-friendly - perfect for quick testing and learning!

## Example Workflow

### First Run - Download Images
1. **Start**: `Ready! Loaded 400 URLs. Used: 0`
2. **Click "Batch (10)"**: Downloads 10 images
   - Status: `✅ Added 10 images (batch). Total: 10, Used URLs: 10/400`
3. **Click "Single (3-5)"**: Downloads 3-5 more images
   - Status: `✅ Added 4 images (single). Total: 14, Used URLs: 14/400`
4. **Click "Save"**: Saves atlas to disk
   - Status: `✅ Atlas saved! 14 entries, 1 page(s)`

### Second Run - Load Saved Atlas
1. **Start**: `Ready! Loaded 400 URLs. Used: 0`
2. **Click "Load"**: Loads previously saved atlas
   - Status: `✅ Atlas loaded! 14 entries, 1 page(s)`
3. All 14 images appear in the scroll view
4. Can continue adding more images with Batch/Single

### Testing URL Exhaustion
1. Create a file with only 5 URLs
2. Click "Batch (10)"
3. Status: `✅ Added 5 images (batch). Total: 5, Used URLs: 5/5`
4. Click "Batch (10)" again
5. Status: `⚠️ No unused URLs available!`

## File Structure

After saving, files are created in `Application.persistentDataPath/AtlasUrlManager/`:
```
AtlasUrlManager.json           # Metadata (entries, UVs, etc.)
AtlasUrlManager_page0.png      # First page texture
AtlasUrlManager_page1.png      # Second page texture (if needed)
...
```

## Code Architecture

### Key Components

**URL Management:**
```csharp
LoadUrlsFromFile()           // Parse URLs from TextAsset
GetRandomUnusedUrls(count)   // Get random unused URLs
_availableUrls               // All valid URLs from file
_usedUrls                    // HashSet of already-used URLs
```

**Atlas Operations:**
```csharp
CreateEmptyAtlas()           // Initialize new atlas
DownloadBatchImages()        // Batch download 10 images
DownloadSingleImages()       // Single download 3-5 images
SaveAtlas()                  // Save to disk
UnloadAtlas()                // Clear and dispose
LoadAtlas()                  // Load from disk
```

**UI Management:**
```csharp
CreateUI()                   // Create all UI elements
DisplayAllImages()           // Show thumbnails in grid
CreateImageThumbnail()       // Create single thumbnail
ClearDisplayedImages()       // Remove all thumbnails
```

## Technical Details

### Random Selection Algorithm
```csharp
// Get unused URLs
var unusedUrls = _availableUrls.Where(url => !_usedUrls.Contains(url));

// Shuffle and take random
var selectedUrls = unusedUrls.OrderBy(x => random.Next()).Take(count);

// Mark as used
foreach (var url in selectedUrls)
    _usedUrls.Add(url);
```

### Thumbnail Creation
Each thumbnail has:
- **Background**: Dark gray panel
- **Image**: Scaled sprite from atlas (preserves aspect)
- **Border**: Blue outline for visual polish
- **Layout**: Automatic grid arrangement

### Button State Management
- Buttons are disabled during operations
- Prevents multiple simultaneous operations
- Re-enabled after completion or error

## Performance Notes

- **Batch downloads** are faster for multiple images (concurrent downloads)
- **Single downloads** use less memory during download
- **UI updates** happen after all downloads complete (no flicker)
- **Thumbnails** are created from atlas sprites (no extra memory)

## Troubleshooting

### No Images Downloading
- ✅ Check that URL list file is assigned
- ✅ Verify URLs are valid (start with http:// or https://)
- ✅ Check Console for error messages
- ✅ Test a URL in a web browser

### "No unused URLs available"
- This means all URLs have been used
- Either add more URLs to your file
- Or restart the scene to reset the used list

### Load Button Shows "No saved atlas found"
- You haven't saved an atlas yet
- Click "Batch" or "Single" to add images
- Then click "Save" before clicking "Load"

### Images Not Displaying
- Check Console for sprite creation errors
- Verify atlas has entries: `_atlas.EntryCount > 0`
- Make sure downloaded images are valid image files

## Example URL Files

### Small Test File (10 URLs)
```text
# Small test set
https://picsum.photos/200/200?random=1
https://picsum.photos/300/300?random=2
https://picsum.photos/250/250?random=3
https://picsum.photos/400/400?random=4
https://picsum.photos/350/350?random=5
https://picsum.photos/200/200?random=6
https://picsum.photos/300/300?random=7
https://picsum.photos/250/250?random=8
https://picsum.photos/400/400?random=9
https://picsum.photos/350/350?random=10
```

### Large Production File (100+ URLs)
Use the existing `SampleURLs.txt` file or create your own with hundreds of URLs for extensive testing.

## Best Practices

1. **Start Small** - Test with 10-20 URLs first
2. **Save Often** - Save your atlas after downloading to preserve progress
3. **Monitor Status** - Watch the status bar for feedback
4. **Check Console** - Detailed logs show download progress
5. **Use Valid URLs** - Test URLs in browser before adding to list

## API Usage Examples

This example demonstrates:
- ✅ `DownloadAndAddBatchAsync` - Efficient batch downloads
- ✅ `DownloadAndAddAsync` - Single image downloads
- ✅ `SaveAtlasAsync` - Persistent storage
- ✅ `LoadAtlasAsync` - Restoring saved data
- ✅ `GetAllEntries` - Retrieving atlas contents
- ✅ `CreateSprite` - Converting entries to sprites

Perfect for learning the RuntimeAtlas API! 🎉

