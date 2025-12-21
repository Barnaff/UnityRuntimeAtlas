# Changelog

All notable changes to this project will be documented in this file.

## [1.0.1] - 2024-12-21

### Fixed
- Fixed Burst compilation errors that occurred on some platforms/configurations
- Made Burst compilation optional with automatic fallback to non-Burst implementations
- Added try-catch error handling in AtlasBatchProcessor for better reliability
- Jobs now work with or without Burst enabled

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
- AtlasSprite component for SpriteRenderer
- AtlasMaterial component for custom shaders
- Extension methods for easy integration
- Static AtlasPacker API for quick usage
- Named atlas management
- AtlasBatchProcessor for high-performance operations
- Comprehensive documentation and examples
