# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.3] - 2025-12-27

### Added
- **Dictionary-based AddBatch method**: New `AddBatch(Dictionary<string, Texture2D>)` method for batch adding textures with named keys
- Named sprite retrieval: Easily retrieve sprites by their keys using `GetSprite(string name)`
- Automatic sprite replacement: Entries with the same key are automatically replaced when re-added
- Atlas Debug Window now shows sprite names and entry IDs in the entry list for better identification
- Enhanced search functionality in Atlas Debug Window to search by both entry ID and sprite name (case-insensitive)
- BatchWithKeysExample sample script demonstrating dictionary-based batch operations
- Comprehensive documentation for batch add with keys feature
- Quick reference guide for batch operations

### Changed
- **Refactored AddBatch(Texture2D[])**: Now uses dictionary-based implementation internally to eliminate code duplication (~80 lines removed)
- Improved batch packing performance with single `Apply()` call at the end
- Atlas Debug Window entry list now displays both sprite name and entry ID for better clarity
- Remove Entry dialog now shows both sprite name and entry ID
- Updated sprite cache to only cache default sprites (100 PPU, center pivot, no border) for better memory management

### Fixed
- Fixed memory leaks from previous atlas instances remaining in Atlas Debug Window
- Fixed null reference errors when atlases are cleared between runs
- Fixed frame drops when adding multiple images by optimizing batch operations
- Fixed errors when atlas reaches page limit - now only shows errors if max page count is reached
- Improved smoothness of multi-image atlas operations

### Improved
- Atlas Debug Window now shows all active textures of an atlas with detailed stats
- Added page count display and individual page texture viewing in debug window
- Better organization and display of sprite entries in debug window
- Optimized batch processing for better performance

## [1.0.2] - 2025-12-22

### Fixed
- Fixed atlas not appearing in Atlas Debug Window when created directly
- Fixed icon loading error: replaced 'd_MemoryProfiler' with 'd_Profiler.Memory' for Unity version compatibility
- SimpleNamedAtlasExample now uses `AtlasPacker.GetOrCreate()` to properly register atlases with the debugger

### Changed
- Sample atlases are now registered with AtlasPacker for better debugging visibility
- Improved Atlas Debug Window compatibility across Unity versions

## [1.0.1] - 2024-12-21

### Fixed
- Fixed Burst compilation errors that occurred on some platforms/configurations
- Made Burst compilation optional with automatic fallback to non-Burst implementations
- Added try-catch error handling in AtlasBatchProcessor for better reliability
- Jobs now work with or without Burst enabled
- Fixed Graphics.CopyTexture format incompatibility errors (RGB8 vs RGBA8)
- Added automatic format conversion via RenderTexture when needed
- Improved texture blitting to handle all format combinations
- Fixed "Texture is not readable" error when using RenderTexture fallback
- BlitViaRenderTexture now uses Graphics.CopyTexture to avoid ReadPixels requirement

### Added
- BURST_COMPILATION.md documentation for troubleshooting Burst issues
- Automatic fallback mechanisms when Jobs/Burst fail
- Troubleshooting section in README

### Changed
- Burst compilation is now optional (was required)
- Updated package requirements to reflect optional Burst dependency
- Improved error messages when job-based packing fails

## [1.0.0] - 2024-01-01

### Added
- Initial release
- RuntimeAtlas core class with dynamic packing support
- AtlasEntry auto-updating references
- MaxRects packing algorithm (best quality)
- Skyline packing algorithm (fastest)
- Async API support (AddAsync, AddBatchAsync)
- Auto-growth when atlas is full
- GPU-accelerated texture blitting
- Burst-compiled batch packing jobs
- AtlasSpriteRenderer component for SpriteRenderer integration
- AtlasImage and AtlasRawImage components for UI integration
- Extension methods for easy integration
- Static AtlasPacker API for quick usage
- Named atlas management
- AtlasBatchProcessor for high-performance operations
- Atlas Debug Window for runtime inspection and debugging
- Comprehensive documentation and examples
- MIT License
