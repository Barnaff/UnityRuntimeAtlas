using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// GPU-accelerated texture blitting operations.
    /// Falls back to CPU when GPU blit is not available.
    /// </summary>
    public static class TextureBlitter
    {
        private static Material _blitMaterial;
        private static bool _materialInitialized;

        private static void EnsureMaterial()
        {
            if (_materialInitialized) return;
            _materialInitialized = true;

            // Load pre-built material from Resources for build compatibility
            _blitMaterial = Resources.Load<Material>("AtlasBlitMaterial");

            if (_blitMaterial == null)
            {
                // Fallback: Try to find the shader and create material
                var shader = Shader.Find("Hidden/RuntimeAtlasPacker/Blit");
                if (shader == null)
                {
                    // Last resort fallback to Unity's built-in blit shader
                    shader = Shader.Find("Hidden/BlitCopy");
                }

                if (shader != null)
                {
                    _blitMaterial = new Material(shader);
                    _blitMaterial.hideFlags = HideFlags.HideAndDontSave;
                }
            }
        }

        /// <summary>
        /// Blit a source texture to a target texture at the specified position.
        /// Works with both readable and non-readable textures.
        /// </summary>
        public static void Blit(Texture2D source, Texture2D target, int x, int y)
        {
            if (source == null || target == null)
            {
                Debug.LogError($"[TextureBlitter.Blit] NULL parameter - source: {source != null}, target: {target != null}");
                throw new ArgumentNullException();
            }

#if UNITY_EDITOR
            Debug.Log($"[TextureBlitter.Blit] Source: '{source.name}', {source.width}x{source.height}, Format: {source.format}, Readable: {source.isReadable}");
            Debug.Log($"[TextureBlitter.Blit] Target: '{target.name}', {target.width}x{target.height}, Format: {target.format}, Readable: {target.isReadable}");
            Debug.Log($"[TextureBlitter.Blit] Position: ({x}, {y})");
#endif

            // Use Material-based rendering - works with ALL textures
            BlitWithMaterial(source, target, x, y);
        }

        /// <summary>
        /// Batch blit multiple textures to a target texture.
        /// Much more efficient than calling Blit multiple times.
        /// </summary>
        /// <param name="operations">Array of (source, x, y) tuples</param>
        /// <param name="target">Target texture to blit to</param>
        public static void BatchBlit(Texture2D target, params (Texture2D source, int x, int y)[] operations)
        {
            if (target == null || operations == null || operations.Length == 0)
                return;

#if UNITY_EDITOR
            Debug.Log($"[TextureBlitter.BatchBlit] Target: {target.name}, {target.width}x{target.height}, Format: {target.format}, Readable: {target.isReadable}");
            Debug.Log($"[TextureBlitter.BatchBlit] Operations count: {operations.Length}");
#endif

            EnsureMaterial();

            RenderTexture rt = null;
            RenderTexture prevActive = RenderTexture.active;

            try
            {
                // Create RenderTexture matching target size
                rt = RenderTexture.GetTemporary(
                    target.width,
                    target.height,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB
                );
                rt.filterMode = FilterMode.Point;

                // BUG FIX: Preserve existing atlas content via GPU blit.
                // Graphics.Blit works with BOTH readable and non-readable textures
                // because it samples the texture via a shader (GPU operation).
                // Previously, non-readable textures were cleared with GL.Clear,
                // which destroyed all previously-added sprites in the atlas.
                Graphics.Blit(target, rt);

                // Activate RT for rendering
                RenderTexture.active = rt;

                Material blitMat = GetBlitMaterial();

                // Batch all draw operations
                GL.PushMatrix();
                GL.LoadPixelMatrix(0, target.width, target.height, 0);

                for (int i = 0; i < operations.Length; i++)
                {
                    var (source, x, y) = operations[i];
                    if (source == null)
                    {
#if UNITY_EDITOR
                        Debug.LogWarning($"[TextureBlitter.BatchBlit] Skipping NULL source at index {i}");
#endif
                        continue;
                    }

                    // Flip Y coordinate
                    float yFlipped = target.height - y - source.height;
                    Rect destRect = new Rect(x, yFlipped, source.width, source.height);

                    if (blitMat != null)
                    {
                        blitMat.mainTexture = source;
                        Graphics.DrawTexture(destRect, source, blitMat);
                    }
                    else
                    {
                        Graphics.DrawTexture(destRect, source);
                    }
                }

                GL.PopMatrix();
                RenderTexture.active = null;

                // Copy RT back to target texture
                CopyRenderTextureToTexture(rt, target);

#if UNITY_EDITOR
                Debug.Log($"[TextureBlitter.BatchBlit] Batch blit complete ({operations.Length} operations)");
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TextureBlitter.BatchBlit] CRASH during batch blit: {ex.Message}\nStack: {ex.StackTrace}");
                throw;
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (rt != null)
                {
                    RenderTexture.ReleaseTemporary(rt);
                    rt = null;
                }
            }
        }

        /// <summary>
        /// Check if two texture formats are compatible for Graphics.CopyTexture.
        /// Graphics.CopyTexture requires exact format match or compatible formats.
        /// </summary>
        private static bool AreFormatsCompatibleForGPUCopy(TextureFormat sourceFormat, TextureFormat targetFormat)
        {
            // Exact match is always compatible
            if (sourceFormat == targetFormat)
                return true;

            // For safety, only allow exact matches
            return false;
        }

        /// <summary>
        /// Safely copy a RenderTexture back to a Texture2D, handling format differences
        /// and readable/non-readable textures correctly on all platforms including iOS Metal.
        /// </summary>
        private static void CopyRenderTextureToTexture(RenderTexture rt, Texture2D target)
        {
            if (target.isReadable)
            {
                // READABLE textures: Use ReadPixels which safely handles format conversion
                // from RenderTexture (ARGB32) to any Texture2D format.
                RenderTexture.active = rt;
                target.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
                RenderTexture.active = null;
                target.Apply(false, false);

#if UNITY_IOS
                // iOS Metal: Apply() schedules a DEFERRED upload. GL.Flush() ensures
                // the upload completes before any subsequent operations.
                GL.Flush();
#endif
            }
            else
            {
                // NON-READABLE textures: Cannot use ReadPixels.
                // Graphics.CopyTexture requires compatible formats between RT and Texture2D.
                // On iOS Metal, RenderTextureFormat.ARGB32 may map to a different internal
                // pixel format than TextureFormat.ARGB32, causing deferred crashes in BlitterRemap.
                //
                // SAFE APPROACH: Use a temporary readable Texture2D as an intermediate.
                // ReadPixels handles format conversion safely, then CopyTexture between
                // two Texture2Ds with the same TextureFormat is guaranteed compatible.
#if UNITY_IOS
                var temp = new Texture2D(rt.width, rt.height, target.format, false);
                try
                {
                    RenderTexture.active = rt;
                    temp.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
                    RenderTexture.active = null;
                    temp.Apply(false, false);

                    Graphics.CopyTexture(temp, target);
                    GL.Flush();
                }
                finally
                {
                    RenderTexture.active = null;
                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(temp);
                    else
                        UnityEngine.Object.DestroyImmediate(temp);
                }
#else
                // On non-iOS platforms, CopyTexture between RT and Texture2D is generally safe
                Graphics.CopyTexture(rt, target);
#endif
            }
        }

        /// <summary>
        /// Blit using Material-based rendering - works with ALL texture formats and readability states.
        /// This is the DEFINITIVE solution for non-readable textures.
        /// Uses GPU-only operations - no CPU readback required!
        /// </summary>
        private static void BlitWithMaterial(Texture2D source, Texture2D target, int x, int y)
        {
#if UNITY_IOS
            // iOS: Use direct Texture2D.SetPixels() when both textures are readable
            // to avoid RenderTexture format conversion issues entirely.
            if (source.isReadable && target.isReadable)
            {
                try
                {
                    BlitDirectPixelCopy(source, target, x, y);
                    return;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[TextureBlitter.BlitWithMaterial] iOS: Direct pixel copy failed: {ex.Message}");
                    // Fall through to GPU method
                }
            }
#endif

            EnsureMaterial();

            RenderTexture rt = null;
            RenderTexture prevActive = RenderTexture.active;

            try
            {
                // Create RenderTexture matching target size
                rt = RenderTexture.GetTemporary(
                    target.width,
                    target.height,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB
                );
                rt.filterMode = FilterMode.Point;

                // BUG FIX: Preserve existing atlas content via GPU blit.
                // Graphics.Blit works with BOTH readable and non-readable textures
                // because it samples the texture via a shader (GPU operation).
                // Previously, non-readable textures were cleared with GL.Clear,
                // which destroyed all previously-added sprites in the atlas.
                Graphics.Blit(target, rt);

                // Activate RT for rendering
                RenderTexture.active = rt;

                // Flip Y coordinate because Unity's texture coordinates are bottom-left origin
                // but we're using top-left pixel coordinates
                float yFlipped = target.height - y - source.height;

                // Use Graphics.DrawTexture for pixel-perfect positioning
                Material blitMat = GetBlitMaterial();

                GL.PushMatrix();
                GL.LoadPixelMatrix(0, target.width, target.height, 0);

                Rect destRect = new Rect(x, yFlipped, source.width, source.height);
                if (blitMat != null)
                {
                    blitMat.mainTexture = source;
                    Graphics.DrawTexture(destRect, source, blitMat);
                }
                else
                {
                    Graphics.DrawTexture(destRect, source);
                }

                GL.PopMatrix();
                RenderTexture.active = null;

                // Copy RT back to target texture (handles format conversion safely)
                CopyRenderTextureToTexture(rt, target);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TextureBlitter.BlitWithMaterial] FATAL ERROR: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (rt != null)
                {
                    RenderTexture.ReleaseTemporary(rt);
                    rt = null;
                }
            }
        }

        /// <summary>
        /// Get or create the blit material.
        /// </summary>
        private static Material GetBlitMaterial()
        {
            EnsureMaterial();

            // If no custom material, create a simple one
            if (_blitMaterial == null)
            {
                // Try to find Unity's built-in blit shader
                Shader shader = Shader.Find("Hidden/BlitCopy");
                if (shader == null)
                {
                    shader = Shader.Find("UI/Default");
                }

                if (shader != null)
                {
                    _blitMaterial = new Material(shader);
                    _blitMaterial.hideFlags = HideFlags.HideAndDontSave;
                }
            }

            return _blitMaterial;
        }

        /// <summary>
        /// Blit a source texture to a RenderTexture at the specified position.
        /// </summary>
        public static void BlitToRenderTexture(Texture2D source, RenderTexture target, RectInt destRect)
        {
            EnsureMaterial();

            var prev = RenderTexture.active;
            RenderTexture.active = target;

            GL.PushMatrix();
            GL.LoadPixelMatrix(0, target.width, target.height, 0);

            var srcRect = new Rect(0, 0, 1, 1);
            var dstRect = new Rect(destRect.x, destRect.y, destRect.width, destRect.height);

            if (_blitMaterial != null)
            {
                _blitMaterial.mainTexture = source;
                _blitMaterial.SetPass(0);
            }

            GL.Begin(GL.QUADS);
            GL.TexCoord2(srcRect.xMin, srcRect.yMin);
            GL.Vertex3(dstRect.xMin, dstRect.yMax, 0);
            GL.TexCoord2(srcRect.xMax, srcRect.yMin);
            GL.Vertex3(dstRect.xMax, dstRect.yMax, 0);
            GL.TexCoord2(srcRect.xMax, srcRect.yMax);
            GL.Vertex3(dstRect.xMax, dstRect.yMin, 0);
            GL.TexCoord2(srcRect.xMin, srcRect.yMax);
            GL.Vertex3(dstRect.xMin, dstRect.yMin, 0);
            GL.End();

            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        /// <summary>
        /// Direct CPU-based pixel copy using GetPixels/SetPixels with region.
        /// More efficient than BlitCPUDirect as it only reads/writes affected regions.
        /// Handles format conversion automatically.
        /// </summary>
        private static void BlitDirectPixelCopy(Texture2D source, Texture2D target, int x, int y)
        {
            // Get source pixels - Unity handles format conversion automatically
            var sourcePixels = source.GetPixels();

            // SetPixels with region - only modifies the target region, very efficient
            target.SetPixels(x, y, source.width, source.height, sourcePixels);

            target.Apply(false, false);
        }

        /// <summary>
        /// Direct CPU-based pixel copy using SetPixels32 (byte format).
        /// This avoids the Metal format conversion that causes crashes on iOS.
        /// Uses Color32 (byte) format to match ARGB32/RGBA32 textures directly.
        /// </summary>
        private static void BlitCPUDirect(Texture2D source, Texture2D target, int x, int y)
        {
            // Get source pixels as Color32 (byte format - matches ARGB32)
            var sourcePixels = source.GetPixels32();

            // Get target pixels, modify the region, then set back
            // This is necessary because SetPixels32 doesn't have a region overload
            var targetPixels = target.GetPixels32();

            // Copy source pixels into the correct position in target
            int srcWidth = source.width;
            int srcHeight = source.height;
            int tgtWidth = target.width;

            for (int sy = 0; sy < srcHeight; sy++)
            {
                int srcRow = sy * srcWidth;
                int tgtRow = (y + sy) * tgtWidth + x;

                for (int sx = 0; sx < srcWidth; sx++)
                {
                    targetPixels[tgtRow + sx] = sourcePixels[srcRow + sx];
                }
            }

            // Set all pixels back
            target.SetPixels32(targetPixels);

            // Apply changes - this uploads to GPU
            target.Apply(false, false);

#if UNITY_IOS
            GL.Flush();
#endif
        }

        /// <summary>
        /// CPU-based texture blit (fallback when GPU is not available).
        /// WARNING: This allocates large arrays for both source and target textures.
        /// Use BlitCPUDirect instead for better memory efficiency.
        /// </summary>
        public static void BlitCPU(Texture2D source, Texture2D target, int x, int y)
        {
            var sourcePixels = source.GetPixels32();
            var targetPixels = target.GetPixels32();

            int srcWidth = source.width;
            int srcHeight = source.height;
            int tgtWidth = target.width;

            for (int sy = 0; sy < srcHeight; sy++)
            {
                int ty = y + sy;
                if (ty < 0 || ty >= target.height) continue;

                for (int sx = 0; sx < srcWidth; sx++)
                {
                    int tx = x + sx;
                    if (tx < 0 || tx >= tgtWidth) continue;

                    int srcIndex = sy * srcWidth + sx;
                    int tgtIndex = ty * tgtWidth + tx;
                    targetPixels[tgtIndex] = sourcePixels[srcIndex];
                }
            }

            target.SetPixels32(targetPixels);
            target.Apply(false, false);
        }

        /// <summary>
        /// Clear a region of a texture to transparent.
        /// </summary>
        public static void ClearRegion(Texture2D texture, RectInt region)
        {
            if (!texture.isReadable)
            {
                Debug.LogWarning("TextureBlitter: Cannot clear region - texture not readable");
                return;
            }

            var pixels = texture.GetPixels32();
            var clear = new Color32(0, 0, 0, 0);

            int width = texture.width;

            for (int y = region.y; y < region.y + region.height && y < texture.height; y++)
            {
                for (int x = region.x; x < region.x + region.width && x < width; x++)
                {
                    pixels[y * width + x] = clear;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
        }

        /// <summary>
        /// Copy a texture region to a new texture.
        /// </summary>
        public static Texture2D CopyRegion(Texture2D source, RectInt region)
        {
            if (!source.isReadable)
            {
                // Use RenderTexture workaround
                var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(source, rt);

                var prev = RenderTexture.active;
                RenderTexture.active = rt;

                var result = new Texture2D(region.width, region.height, AtlasSettings.DefaultFormat, false);
                result.ReadPixels(new Rect(region.x, region.y, region.width, region.height), 0, 0);
                result.Apply();

                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);

                return result;
            }

            var copy = new Texture2D(region.width, region.height, AtlasSettings.DefaultFormat, false);
            var sourcePixels = source.GetPixels(region.x, region.y, region.width, region.height);
            copy.SetPixels(sourcePixels);
            copy.Apply();

            return copy;
        }

        /// <summary>
        /// Create a resized copy of a texture (useful for atlas growth).
        /// Preserves existing content in the top-left corner.
        /// </summary>
        public static Texture2D CreateResized(Texture2D source, int newWidth, int newHeight, TextureFormat format, bool mipChain = false)
        {
            var newTexture = new Texture2D(newWidth, newHeight, format, mipChain);
            newTexture.filterMode = source.filterMode;
            newTexture.wrapMode = source.wrapMode;

            // Clear to transparent
            var clearPixels = new Color32[newWidth * newHeight];
            newTexture.SetPixels32(clearPixels);
            newTexture.Apply(false, false);

            // Copy existing content using the Blit method which handles format conversion
            Blit(source, newTexture, 0, 0);

            return newTexture;
        }
    }
}
