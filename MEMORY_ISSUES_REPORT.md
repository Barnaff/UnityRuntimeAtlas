# Memory Issues Report - UnityRuntimeAtlas

## Executive Summary

This report identifies critical memory issues found in the UnityRuntimeAtlas package, particularly in batch operations and download functionality. Several memory leaks have already been fixed, but **one critical bug remains** that causes crashes during batch operations.

---

## Critical Issues

### 1. **CRITICAL BUG: Double Processing in AtlasBatchProcessor.PackBatchWithJobs**

**File:** `Runtime/Utils/AtlasBatchProcessor.cs`
**Lines:** 116-142
**Severity:** CRITICAL - Causes crashes and memory issues

#### The Problem

The `PackBatchWithJobs` method has a fundamental logic error that causes textures to be processed twice:

```csharp
// Lines 116-142
// Apply to atlas
for (int i = 0; i < count; i++)
{
    var r = sortedResults[i];
    var contentRect = new RectInt(...);

    // FIRST: Manual blit
    TextureBlitter.Blit(textures[i], atlas.Texture, contentRect.x, contentRect.y);

    var uvRect = new Rect(...);

    // SECOND: Calls atlas.Add which tries to pack and blit AGAIN!
    var (result, entry) = atlas.Add(textures[i]);  // ← BUG!
    entries[i] = entry;
}

atlas.Apply();
```

#### What Happens

1. **Jobs calculate packing positions** (lines 63-88)
2. **Manual texture blitting** to atlas (line 127)
3. **Call atlas.Add()** which:
   - Tries to pack the texture AGAIN (ignoring job results)
   - Blits the texture AGAIN (overwriting previous blit)
   - May try to Apply() the texture (depending on atlas settings)
   - Creates atlas entry

#### Consequences

- **Memory crashes** from excessive texture operations
- **Texture corruption** from double blitting
- **Wasted CPU/GPU cycles** from redundant operations
- **Incorrect UV coordinates** (job results are ignored)
- **Potential race conditions** in multi-threaded scenarios

#### Recommended Fix

The method should either:
- **Option A:** Use jobs for packing calculations only, then create entries manually without calling Add()
- **Option B:** Don't use jobs at all - just call `atlas.AddBatch()` which handles everything correctly

The correct pattern (already used elsewhere) is:
```csharp
// Just use the atlas's built-in batch add
return atlas.AddBatch(textures);
```

---

## Medium Issues

### 2. **Memory Leak Risk: Temp Textures in AsyncBatchPackingExample**

**File:** `Samples/BasicUsage/AsyncBatchPackingExample.cs`
**Lines:** 114-132, 186-196
**Severity:** MEDIUM

#### The Problem

Generated test textures are stored in `_generatedTextures` list and only cleaned up in `OnDestroy()`. If the atlas copies texture data during packing, these original textures waste memory for the component's lifetime.

```csharp
// Line 114-132: Generates textures and stores them
private IEnumerator GenerateTestTextures()
{
    _generatedTextures.Clear();
    for (int i = 0; i < textureCount; i++)
    {
        var texture = GenerateRandomTexture(...);
        _generatedTextures.Add(texture);  // Held until OnDestroy
    }
}

// Line 85-98: Uses textures
var textures = _generatedTextures.ToArray();
yield return StartCoroutine(
    AtlasPacker.PackBatchAsync(textures, ...)
);
// Textures still in _generatedTextures list!
```

#### Recommended Fix

Destroy textures immediately after they're added to the atlas:
```csharp
yield return StartCoroutine(
    AtlasPacker.PackBatchAsync(textures, ...)
);

// Clean up immediately after packing
foreach (var tex in _generatedTextures)
{
    if (tex != null) Destroy(tex);
}
_generatedTextures.Clear();
```

---

### 3. **Potential Memory Leak: NativeArray in BatchPackJob**

**File:** `Runtime/Jobs/PackingJobs.cs`
**Lines:** 84, 126
**Severity:** LOW (already handled but worth noting)

#### The Issue

`BatchPackJob.PlaceRect()` creates a `NativeList<int4>` with `Allocator.Temp`:

```csharp
private void PlaceRect(int4 rect)
{
    var newRects = new NativeList<int4>(4, Allocator.Temp);  // Line 84

    // ... processing ...

    newRects.Dispose();  // Line 126 - Good!
}
```

**Status:** ✅ This is correctly disposed. The Allocator.Temp also provides automatic cleanup, but explicit disposal is good practice.

---

## Already Fixed Issues (Good!)

The following memory leaks have already been addressed with fixes:

### ✅ Fixed: Downloaded Texture Cleanup in AtlasWebLoader

**Files:** `Runtime/Core/AtlasWebLoader.cs`

1. **Line 332-338:** DownloadTextureAsync cleans up texture on error
```csharp
finally
{
    // MEMORY LEAK FIX: Clean up texture if we still own it
    if (downloadedTexture != null)
    {
        UnityEngine.Object.Destroy(downloadedTexture);
    }
}
```

2. **Line 415-420:** ProcessDownloadAsync cleans up texture
```csharp
finally
{
    // MEMORY LEAK FIX: Always cleanup downloaded texture
    if (downloadedTexture != null)
    {
        UnityEngine.Object.Destroy(downloadedTexture);
    }
}
```

3. **Line 242-249:** DownloadAndAddBatchAsync cleans up textures after batch add
```csharp
// Cleanup downloaded textures
foreach (var texture in textureBatch.Values)
{
    if (texture != null)
    {
        UnityEngine.Object.Destroy(texture);
    }
}
```

### ✅ Fixed: Old Texture Cleanup in RuntimeAtlas

**File:** `Runtime/Core/RuntimeAtlas.cs`

1. **Line 1367-1375:** TryGrowPage destroys old texture before replacing
```csharp
// MEMORY LEAK FIX: Destroy old texture BEFORE replacing reference
if (Application.isPlaying)
{
    UnityEngine.Object.Destroy(oldTexture);
}
else
{
    UnityEngine.Object.DestroyImmediate(oldTexture);
}
```

2. **Line 1409-1420:** TryGrowPage cleans up new texture on failure
```csharp
// MEMORY LEAK FIX: Clean up new texture if creation failed
if (newTexture != null)
{
    if (Application.isPlaying)
    {
        UnityEngine.Object.Destroy(newTexture);
    }
    else
    {
        UnityEngine.Object.DestroyImmediate(newTexture);
    }
}
```

---

## Best Practices & Documentation

### Correct Usage Pattern

The codebase documentation (lines 184-204 in RuntimeAtlas.cs) correctly explains the memory management contract:

```csharp
/// <summary>
/// Add a texture to the atlas.
/// <para>⚠️ <b>IMPORTANT</b>: This method COPIES the texture data into the atlas.</para>
/// <para>If you created or downloaded the input texture, you MUST destroy it after
/// calling this method to avoid memory leaks.</para>
/// <para>Textures loaded from Resources or assigned in the Inspector should NOT be destroyed.</para>
/// </summary>
```

### Example of Correct Usage

The `WebDownloadExample.cs` demonstrates the correct pattern:

```csharp
// Line 189-207
private async Task AddNewImage()
{
    var texture = await DownloadImageAsync(url, _cts.Token);
    if (texture == null) return;

    var entry = AtlasPacker.Pack(texture);

    Destroy(texture);  // ✅ CORRECT: Destroy after adding to atlas

    // Use entry...
}
```

---

## Memory Allocation Analysis

### Current Memory Flow

1. **Download Phase:**
   - Texture downloaded via UnityWebRequest
   - Stored in temporary variable
   - ✅ Cleaned up after use (already fixed)

2. **Batch Add Phase:**
   - Multiple textures stored in array/dictionary
   - Passed to atlas.AddBatch()
   - ✅ Cleaned up after batch (already fixed)
   - ❌ **BUT:** AtlasBatchProcessor.PackBatchWithJobs processes textures twice!

3. **Atlas Storage:**
   - Texture data copied to atlas texture(s)
   - Original textures can be destroyed
   - Atlas owns the texture data

### Memory Efficiency Improvements

The batch operations are designed for efficiency:

```csharp
// Runtime/Core/RuntimeAtlas.cs, line 805-820
// ✅ OPTIMIZATION: Apply only modified pages instead of all pages
if (successCount > 0)
{
    foreach (var pageIndex in modifiedPages)
    {
        if (pageIndex >= 0 && pageIndex < _textures.Count)
        {
            _textures[pageIndex].Apply(false, false);
        }
    }
}
```

This optimization is bypassed by the bug in AtlasBatchProcessor.PackBatchWithJobs!

---

## Recommendations

### Immediate Actions (Priority 1)

1. **FIX AtlasBatchProcessor.PackBatchWithJobs**
   - Remove the `atlas.Add()` call
   - Create entries manually or use atlas.AddBatch()
   - This will fix the crashes

### High Priority (Priority 2)

2. **Fix AsyncBatchPackingExample memory leak**
   - Destroy textures immediately after packing
   - Clear _generatedTextures list

### Verification Steps

3. **Test batch operations under load**
   - Download and add 100+ images in batch
   - Monitor memory usage
   - Verify no crashes or texture corruption

4. **Add memory profiling**
   - Use Unity Profiler to track texture memory
   - Verify textures are destroyed after atlas add
   - Check for lingering references

---

## Technical Details

### Unity Texture Memory

- Texture2D objects are Unity engine objects, not regular C# objects
- Must use `Object.Destroy()` or `Object.DestroyImmediate()` to free memory
- Garbage collector cannot free Unity engine object memory
- Texture data lives in GPU/CPU memory (depending on settings)

### NativeArray/Jobs Memory

- NativeArray with Allocator.TempJob must be disposed
- Using statements ensure disposal on scope exit
- Allocator.Temp is automatically freed at end of frame
- Jobs must complete before disposing their data

### Atlas Memory Model

- Atlas copies texture pixel data into its own Texture2D
- Original textures are not referenced after Add/AddBatch
- Caller is responsible for cleanup of input textures
- This is by design for flexibility (some textures shouldn't be destroyed)

---

## Conclusion

The UnityRuntimeAtlas package has **one critical bug** that causes crashes in batch operations (AtlasBatchProcessor.PackBatchWithJobs). Several memory leaks have already been properly fixed.

The memory management architecture is sound - the issue is a logic error in the batch processing that causes double-processing of textures. Fixing this single issue should resolve the reported memory crashes.

The documentation correctly explains the memory contract, and most example code follows best practices. Some examples could be improved to destroy temporary textures immediately after use.

---

**Report Date:** 2026-01-09
**Reviewed Files:** 8 core files, 2 example files
**Critical Issues:** 1
**Medium Issues:** 2
**Fixed Issues:** 5
