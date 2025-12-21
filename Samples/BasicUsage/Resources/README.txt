# Sample Textures

Drop your texture files here to use with the examples.

## Recommended Setup

1. Add 5-10 PNG or JPG images to this folder
2. Name them sequentially (e.g., `sprite_01.png`, `sprite_02.png`, etc.)
3. Or use descriptive names like `player.png`, `enemy.png`, `item.png`

## Texture Requirements

- Format: PNG, JPG, TGA, or any Unity-supported format
- Size: Any size works, but 64x64 to 512x512 recommended for examples
- Read/Write: Examples will handle texture readability automatically

## Quick Test Textures

If you don't have textures handy, you can:

1. Use Unity's built-in textures
2. Download from the web using the `WebDownloadExample` script
3. Create simple colored squares in any image editor

## Example Texture Names Used in Scripts

The example scripts look for these textures:
- `sprite_01` through `sprite_10`
- `player`, `enemy`, `item`, `background`
- Any texture in this folder via `Resources.LoadAll<Texture2D>("")`
