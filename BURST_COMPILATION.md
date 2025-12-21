# Burst Compilation Configuration

## Overview

This package supports Burst compilation for improved performance, but it's **optional**. The package will work perfectly fine without Burst, just slightly slower for large batch operations.

## Burst Compilation Errors

If you're experiencing Burst compilation errors like:
```
Burst failed to compile the function pointer
System.TypeInitializationException: The type initializer for 'Try_000000BC$BurstDirectCall' threw an exception
```

You have several options:

### Option 1: Disable Burst (Recommended for Development)

Add this scripting define symbol to your project:
1. Go to **Edit > Project Settings > Player > Other Settings**
2. Find **Scripting Define Symbols**
3. Add: `DISABLE_BURST_COMPILATION`
4. Click Apply

### Option 2: Enable Burst Properly

If you want to use Burst for better performance:
1. Go to **Edit > Project Settings > Burst AOT Settings**
2. Enable **Enable Burst Compilation**
3. Make sure Burst package version is compatible with your Unity version
4. Restart Unity

### Option 3: Let the Package Handle It

The package automatically falls back to non-Burst implementations if Burst compilation fails. You'll see a warning in the console but the atlas will still work correctly.

## Performance Impact

- **With Burst**: Batch packing of 1000 textures ~50-100ms
- **Without Burst**: Batch packing of 1000 textures ~200-500ms
- **Regular Add()**: Not affected, uses native MaxRects algorithm

For most use cases (< 100 textures per frame), the difference is negligible.

## Compatibility

- Unity 2021.3+ (with or without Burst)
- Burst 1.6.0+ (optional)
- All platforms supported
