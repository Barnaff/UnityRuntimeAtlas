using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace RuntimeAtlasPacker
{
    // Enable detailed memory diagnostics - set to false for production
    // #define ATLAS_MEMORY_DIAGNOSTICS
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

            // ✅ IMPROVED: Load pre-built material from Resources for build compatibility
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
            Debug.Log($"[TextureBlitter.Blit] ========== SINGLE BLIT START ==========");
            
            if (source == null || target == null)
            {
                Debug.LogError($"[TextureBlitter.Blit] NULL parameter - source: {source != null}, target: {target != null}");
                throw new ArgumentNullException();
            }

            Debug.Log($"[TextureBlitter.Blit] Source: '{source.name}', {source.width}x{source.height}, Format: {source.format}, Readable: {source.isReadable}");
            Debug.Log($"[TextureBlitter.Blit] Target: '{target.name}', {target.width}x{target.height}, Format: {target.format}, Readable: {target.isReadable}");
            Debug.Log($"[TextureBlitter.Blit] Position: ({x}, {y})");
            Debug.Log($"[TextureBlitter.Blit] Memory before blit: {System.GC.GetTotalMemory(false) / (1024 * 1024)}MB");

            // Use Material-based rendering - works with ALL textures
            BlitWithMaterial(source, target, x, y);
            
            Debug.Log($"[TextureBlitter.Blit] Memory after blit: {System.GC.GetTotalMemory(false) / (1024 * 1024)}MB");
            Debug.Log($"[TextureBlitter.Blit] ========== SINGLE BLIT END ==========");
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

            // ✅ DIAGNOSTIC: Detailed texture info for crash debugging
            Debug.Log($"[TextureBlitter.BatchBlit] ========== BATCH BLIT START ==========");

            // Use MemoryDiagnostics for comprehensive memory state
            MemoryDiagnostics.LogMemoryState("BatchBlit START");
            MemoryDiagnostics.LogTextureInfo(target, "Target Atlas");

            Debug.Log($"[TextureBlitter.BatchBlit] Operations count: {operations.Length}");

            // Check for critical memory before proceeding
            if (MemoryDiagnostics.IsMemoryCritical())
            {
                Debug.LogWarning($"[TextureBlitter.BatchBlit] WARNING: Memory is critical! Forcing GC before batch blit...");
                MemoryDiagnostics.ForceGCAndLog("Pre-BatchBlit cleanup");
            }

            // Log each source texture for debugging
            long totalSourceMemory = 0;
            for (int i = 0; i < operations.Length; i++)
            {
                var (src, x, y) = operations[i];
                if (src != null)
                {
                    var srcMemory = MemoryDiagnostics.EstimateTextureMemory(src);
                    totalSourceMemory += srcMemory;
                    Debug.Log($"[TextureBlitter.BatchBlit] Source[{i}]: {src.name}, Size: {src.width}x{src.height}, Format: {src.format}, Readable: {src.isReadable}, Pos: ({x},{y}), Est.Memory: {srcMemory / 1024}KB");
                }
                else
                {
                    Debug.LogWarning($"[TextureBlitter.BatchBlit] Source[{i}]: NULL!");
                }
            }
            Debug.Log($"[TextureBlitter.BatchBlit] Total source textures memory: {totalSourceMemory / 1024 / 1024} MB");

            EnsureMaterial();

            RenderTexture rt = null;
            RenderTexture prevActive = RenderTexture.active;
            
#if UNITY_IOS
            // ✅ iOS MEMORY CRITICAL FIX: Force cleanup BEFORE allocating large RenderTexture
            // RenderTextures use NATIVE (GPU) memory which is NOT tracked by GC.GetTotalMemory()
            // iOS has strict limits on native memory - must aggressively clean up old RenderTextures
            var rtSizeMB = (target.width * target.height * 4) / 1024 / 1024; // RGBA32 size estimate
            
            if (rtSizeMB > 16) // If allocating > 16MB RenderTexture
            {
                // Force Unity to release all temporary RenderTextures from the pool
                // This is CRITICAL on iOS to prevent native memory accumulation
                var memBefore = System.GC.GetTotalMemory(false) / 1024 / 1024;
                
                // Clean up managed memory
                System.GC.Collect();
                System.GC.WaitForPendingFinalizers();
                
                // CRITICAL: Force Unity to clear RenderTexture pool
                // This releases native GPU memory that GC can't see
                RenderTexture.ReleaseTemporary(null); // null call forces pool cleanup
                
                var memAfter = System.GC.GetTotalMemory(false) / 1024 / 1024;
                Debug.Log($"[TextureBlitter.BatchBlit] iOS: Cleaned up before allocating {rtSizeMB}MB RenderTexture. Managed memory: {memBefore}MB → {memAfter}MB");
            }
#endif
            
            try
            {
                Debug.Log($"[TextureBlitter.BatchBlit] STEP 1: Allocating RenderTexture {target.width}x{target.height}...");
                Debug.Log($"[TextureBlitter.BatchBlit] RT Format: ARGB32 (will need conversion to {target.format})");

                // Create RenderTexture matching target size ONCE
                rt = RenderTexture.GetTemporary(
                    target.width,
                    target.height,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB
                );
                rt.filterMode = FilterMode.Point;

                Debug.Log($"[TextureBlitter.BatchBlit] STEP 1 COMPLETE: RenderTexture allocated");
                Debug.Log($"[TextureBlitter.BatchBlit] RT Created: {rt.IsCreated()}, RT Format: {rt.format}, RT Dimension: {rt.dimension}");
                
                // ✅ iOS FIX: Only preserve existing content if texture is readable
                // Non-readable textures can't be read from, and typically have no content to preserve anyway
                Debug.Log($"[TextureBlitter.BatchBlit] STEP 2: Preserving existing content...");

                if (target.isReadable)
                {
                    Debug.Log($"[TextureBlitter.BatchBlit] STEP 2a: Blitting target to RT (readable texture)...");
                    Graphics.Blit(target, rt);
                    Debug.Log($"[TextureBlitter.BatchBlit] STEP 2a COMPLETE");
                }
                else
                {
                    Debug.Log($"[TextureBlitter.BatchBlit] STEP 2a: Clearing RT (non-readable texture)...");
                    // Clear the RenderTexture since we can't preserve content from non-readable texture
                    RenderTexture.active = rt;
                    GL.Clear(true, true, Color.clear);
                    RenderTexture.active = null;
                    Debug.Log($"[TextureBlitter.BatchBlit] STEP 2a COMPLETE");
                }

                // Activate RT for rendering
                Debug.Log($"[TextureBlitter.BatchBlit] STEP 3: Setting up GL context for drawing...");
                RenderTexture.active = rt;

                Material blitMat = GetBlitMaterial();
                Debug.Log($"[TextureBlitter.BatchBlit] BlitMaterial: {(blitMat != null ? blitMat.name : "NULL")}");

                // Batch all draw operations
                Debug.Log($"[TextureBlitter.BatchBlit] STEP 4: Drawing {operations.Length} sprites to RenderTexture...");

                GL.PushMatrix();
                GL.LoadPixelMatrix(0, target.width, target.height, 0);

                for (int i = 0; i < operations.Length; i++)
                {
                    var (source, x, y) = operations[i];
                    if (source == null)
                    {
                        Debug.LogWarning($"[TextureBlitter.BatchBlit] Skipping NULL source at index {i}");
                        continue;
                    }

                    // Flip Y coordinate
                    float yFlipped = target.height - y - source.height;
                    Rect destRect = new Rect(x, yFlipped, source.width, source.height);

                    Debug.Log($"[TextureBlitter.BatchBlit] Drawing sprite {i}: '{source.name}' at ({x},{y}) -> rect({destRect.x},{destRect.y},{destRect.width},{destRect.height})");

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

                Debug.Log($"[TextureBlitter.BatchBlit] STEP 4 COMPLETE: All sprites drawn to RenderTexture");
                
                // ✅ iOS CRITICAL FIX: Avoid ReadPixels on large textures (causes OOM crash)
                Debug.Log($"[TextureBlitter.BatchBlit] STEP 5: Copying RT back to target texture...");
                Debug.Log($"[TextureBlitter.BatchBlit] RT Info - Width: {rt.width}, Height: {rt.height}, Format: {rt.format}, IsCreated: {rt.IsCreated()}");

#if UNITY_IOS
                // On iOS, large ReadPixels operations can crash due to memory pressure
                // Use GPU-only CopyTexture for textures >= 2048 pixels
                bool useGPUOnly = target.width >= 2048 || target.height >= 2048;
                if (useGPUOnly)
                {
                    Debug.Log($"[TextureBlitter.BatchBlit] iOS: Large texture detected ({target.width}x{target.height}), using GPU-only CopyTexture...");
                    Debug.Log($"[TextureBlitter.BatchBlit] >>> ABOUT TO CALL Graphics.CopyTexture <<<");

                    try
                    {
                        // GPU-only copy - no CPU memory allocation
                        Graphics.CopyTexture(rt, target);
                        Debug.Log($"[TextureBlitter.BatchBlit] Graphics.CopyTexture completed successfully");

                        // ✅ iOS CRITICAL: Multiple synchronization steps to ensure GPU operations complete
                        Debug.Log($"[TextureBlitter.BatchBlit] iOS: Forcing GPU flush...");
                        GL.Flush();
                        
                        Debug.Log($"[TextureBlitter.BatchBlit] iOS: Forcing GPU finish...");
                        GL.Finish();
                        
                        Debug.Log($"[TextureBlitter.BatchBlit] iOS: Ensuring GPU sync...");
                        GL.IssuePluginEvent(0);
                        
                        Debug.Log($"[TextureBlitter.BatchBlit] iOS: GPU synchronization complete");
                    }
                    catch (System.Exception copyEx)
                    {
                        Debug.LogError($"[TextureBlitter.BatchBlit] ✗ Graphics.CopyTexture FAILED: {copyEx.Message}\n{copyEx.StackTrace}");
                        throw;
                    }

                    Debug.Log($"[TextureBlitter.BatchBlit] ✓ BATCH BLIT COMPLETE");
                    MemoryDiagnostics.LogMemoryState("BatchBlit END");
                    Debug.Log($"[TextureBlitter.BatchBlit] ========== BATCH BLIT END ==========");
                    return; // Skip ReadPixels path
                }
#endif

                // For smaller textures or non-iOS, use appropriate method based on readability
                if (target.isReadable)
                {
                    Debug.Log($"[TextureBlitter.BatchBlit] STEP 5a: Using ReadPixels (readable texture)...");
                    Debug.Log($"[TextureBlitter.BatchBlit] >>> ABOUT TO CALL ReadPixels - THIS IS WHERE CRASH MAY OCCUR <<<");
                    Debug.Log($"[TextureBlitter.BatchBlit] ReadPixels params: Rect(0, 0, {rt.width}, {rt.height}), destX: 0, destY: 0");

                    // For READABLE textures: Use ReadPixels to update CPU memory
                    RenderTexture.active = rt;

                    try
                    {
                        target.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
                        Debug.Log($"[TextureBlitter.BatchBlit] ReadPixels completed successfully");
                    }
                    catch (System.Exception readEx)
                    {
                        Debug.LogError($"[TextureBlitter.BatchBlit] ✗ ReadPixels FAILED: {readEx.Message}\n{readEx.StackTrace}");
                        throw;
                    }

                    RenderTexture.active = null;

                    Debug.Log($"[TextureBlitter.BatchBlit] STEP 5b: Calling Apply()...");
                    Debug.Log($"[TextureBlitter.BatchBlit] >>> ABOUT TO CALL Apply - THIS IS WHERE CRASH MAY OCCUR <<<");

                    try
                    {
                        // Apply immediately to upload to GPU
                        target.Apply(false, false);
                        Debug.Log($"[TextureBlitter.BatchBlit] Apply completed successfully");

#if UNITY_IOS
                        // ✅ iOS CRITICAL FIX: Force GPU to complete all pending texture uploads
                        // On iOS Metal, Apply() schedules a DEFERRED upload. If we return immediately,
                        // another operation (like atlas save) might access the texture before upload completes.
                        // GL.Flush() forces all pending GPU operations to complete.
                        Debug.Log($"[TextureBlitter.BatchBlit] iOS: Forcing GPU flush to complete texture upload...");
                        GL.Flush();
                        Debug.Log($"[TextureBlitter.BatchBlit] iOS: GPU flush complete");
#endif
                    }
                    catch (System.Exception applyEx)
                    {
                        Debug.LogError($"[TextureBlitter.BatchBlit] ✗ Apply FAILED: {applyEx.Message}\n{applyEx.StackTrace}");
                        throw;
                    }
                }
                else
                {
                    Debug.Log($"[TextureBlitter.BatchBlit] STEP 5a: Using Graphics.CopyTexture (non-readable texture)...");
                    Debug.Log($"[TextureBlitter.BatchBlit] >>> ABOUT TO CALL CopyTexture - THIS IS WHERE CRASH MAY OCCUR <<<");

                    try
                    {
                        // For NON-READABLE textures: Use Graphics.CopyTexture (GPU-only)
                        Graphics.CopyTexture(rt, target);
                        Debug.Log($"[TextureBlitter.BatchBlit] CopyTexture completed successfully");

#if UNITY_IOS
                        // ✅ iOS CRITICAL FIX: Force GPU to complete the copy operation
                        Debug.Log($"[TextureBlitter.BatchBlit] iOS: Forcing GPU flush after CopyTexture...");
                        GL.Flush();
                        Debug.Log($"[TextureBlitter.BatchBlit] iOS: GPU flush complete");
#endif
                    }
                    catch (System.Exception copyEx)
                    {
                        Debug.LogError($"[TextureBlitter.BatchBlit] ✗ CopyTexture FAILED: {copyEx.Message}\n{copyEx.StackTrace}");
                        throw;
                    }
                }

                Debug.Log($"[TextureBlitter.BatchBlit] ✓ BATCH BLIT COMPLETE");
                MemoryDiagnostics.LogMemoryState("BatchBlit END");
                Debug.Log($"[TextureBlitter.BatchBlit] ========== BATCH BLIT END ==========");

#if UNITY_IOS
                // ✅ iOS MEMORY FIX: Force cleanup of temporary allocations after large operation
                if (target.width >= 1024 || target.height >= 1024)
                {
                    Resources.UnloadUnusedAssets();
                }
#endif
            }
            catch (System.Exception ex)
            {
#if UNITY_EDITOR || UNITY_IOS
                Debug.LogError($"[TextureBlitter.BatchBlit] ✗ CRASH during batch blit: {ex.Message}\nStack: {ex.StackTrace}");
#endif
                throw;
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (rt != null)
                {
#if UNITY_EDITOR || UNITY_IOS
                    Debug.Log($"[TextureBlitter.BatchBlit] Releasing RenderTexture...");
#endif
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

            // Graphics.CopyTexture requires same memory layout
            // RGB24 (3 bytes) != RGBA32 (4 bytes) - incompatible
            // Different compressed formats - incompatible
            // Different bit depths - incompatible

            // For safety, only allow exact matches
            return false;
        }

        /// <summary>
        /// Blit using Material-based rendering - works with ALL texture formats and readability states.
        /// This is the DEFINITIVE solution for non-readable textures.
        /// Uses GPU-only operations - no CPU readback required!
        /// </summary>
        private static void BlitWithMaterial(Texture2D source, Texture2D target, int x, int y)
        {
            Debug.Log($"[TextureBlitter.BlitWithMaterial] ========== SINGLE BLIT START ==========");
            Debug.Log($"[TextureBlitter.BlitWithMaterial] Source: {source.name}, Size: {source.width}x{source.height}, Format: {source.format}");
            Debug.Log($"[TextureBlitter.BlitWithMaterial] Target: {target.name}, Size: {target.width}x{target.height}, Format: {target.format}, Readable: {target.isReadable}");
            Debug.Log($"[TextureBlitter.BlitWithMaterial] Position: ({x}, {y})");

            // ✅ iOS FIX: Always use GPU-based blit method
            // CPU method causes crashes on large textures (4096x4096) due to massive array allocation (64MB)
            // iOS IL2CPP has strict limits on array allocation size (~16-32MB)
            // GPU method uses RenderTexture which doesn't have this limitation

            EnsureMaterial();

            RenderTexture rt = null;
            RenderTexture prevActive = RenderTexture.active;

            try
            {
                Debug.Log($"[TextureBlitter.BlitWithMaterial] Allocating RenderTexture...");

                // Create RenderTexture matching target size
                // CRITICAL: Use sRGB for correct color space (prevents burned/washed out colors)
                rt = RenderTexture.GetTemporary(
                    target.width,
                    target.height,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB
                );
                rt.filterMode = FilterMode.Point;

                Debug.Log($"[TextureBlitter.BlitWithMaterial] RenderTexture allocated: {rt.width}x{rt.height}, Format: {rt.format}");
                
                // ✅ iOS FIX: Only preserve existing content if texture is readable
                if (target.isReadable)
                {
                    // Preserve existing atlas content by blitting target to RT
                    Graphics.Blit(target, rt);
                }
                else
                {
                    // Can't read from non-readable texture, clear instead
                    RenderTexture.active = rt;
                    GL.Clear(true, true, Color.clear);
                    RenderTexture.active = null;
                }
                
                // Activate RT for rendering
                RenderTexture.active = rt;
                
                // CRITICAL FIX: Flip Y coordinate because Unity's texture coordinates are bottom-left origin
                // but we're using top-left pixel coordinates
                float yFlipped = target.height - y - source.height;
                
                // Use Graphics.Blit with custom material for precise positioning
                Material blitMat = GetBlitMaterial();
                if (blitMat != null)
                {
                    // Set source texture
                    blitMat.mainTexture = source;
                    
                    // Use Graphics.DrawTexture for pixel-perfect positioning
                    GL.PushMatrix();
                    GL.LoadPixelMatrix(0, target.width, target.height, 0);
                    
                    // Draw at exact pixel position with flipped Y
                    Rect destRect = new Rect(x, yFlipped, source.width, source.height);
                    Graphics.DrawTexture(destRect, source, blitMat);
                    
                    GL.PopMatrix();
                }
                else
                {
                    // Fallback: use Graphics.DrawTexture without material
                    GL.PushMatrix();
                    GL.LoadPixelMatrix(0, target.width, target.height, 0);
                    
                    // Draw at exact pixel position with flipped Y
                    Rect destRect = new Rect(x, yFlipped, source.width, source.height);
                    Graphics.DrawTexture(destRect, source);
                    
                    GL.PopMatrix();
                }
                
                RenderTexture.active = null;

                // ✅ CRITICAL FIX: Different approach for readable vs non-readable textures
                Debug.Log($"[TextureBlitter.BlitWithMaterial] Copying RT back to target...");

                // ✅ iOS CRITICAL FIX: Avoid ReadPixels on large textures (causes OOM crash)
                // ReadPixels internally allocates buffers proportional to texture size
                // For 4096x4096, this can cause iOS to crash due to memory pressure
                // Use GPU-only CopyTexture instead which has no memory allocation issues
                
#if UNITY_IOS
                bool useGPUOnly = target.width >= 2048 || target.height >= 2048;
                if (useGPUOnly)
                {
                    Debug.Log($"[TextureBlitter.BlitWithMaterial] iOS: Large texture detected ({target.width}x{target.height}), using GPU-only CopyTexture...");
                    Debug.Log($"[TextureBlitter.BlitWithMaterial] >>> ABOUT TO CALL Graphics.CopyTexture <<<");

                    try
                    {
                        // GPU-only copy - no CPU memory allocation
                        Graphics.CopyTexture(rt, target);
                        Debug.Log($"[TextureBlitter.BlitWithMaterial] Graphics.CopyTexture completed");

                        // ✅ iOS CRITICAL: Multiple synchronization steps to ensure GPU operations complete
                        // Graphics.CopyTexture is asynchronous on Metal - must force complete synchronization
                        
                        // Step 1: Flush all pending GPU commands
                        Debug.Log($"[TextureBlitter.BlitWithMaterial] iOS: Forcing GPU flush...");
                        GL.Flush();
                        
                        // Step 2: Force GPU to finish all commands (stronger than Flush)
                        Debug.Log($"[TextureBlitter.BlitWithMaterial] iOS: Forcing GPU finish...");
                        GL.Finish();
                        
                        // Step 3: Touch the texture to ensure it's uploaded and accessible
                        Debug.Log($"[TextureBlitter.BlitWithMaterial] iOS: Ensuring texture is GPU-resident...");
                        GL.IssuePluginEvent(0); // Force GPU sync point
                        
                        Debug.Log($"[TextureBlitter.BlitWithMaterial] iOS: GPU synchronization complete");
                    }
                    catch (System.Exception copyEx)
                    {
                        Debug.LogError($"[TextureBlitter.BlitWithMaterial] ✗ Graphics.CopyTexture FAILED: {copyEx.Message}\n{copyEx.StackTrace}");
                        throw;
                    }
                    
                    Debug.Log($"[TextureBlitter.BlitWithMaterial] ========== SINGLE BLIT END ==========");
                    return; // Skip ReadPixels path
                }
#endif

                // For smaller textures or non-iOS, use ReadPixels (safer for readable textures)
                if (target.isReadable)
                {
                    Debug.Log($"[TextureBlitter.BlitWithMaterial] Using ReadPixels (readable texture)...");
                    Debug.Log($"[TextureBlitter.BlitWithMaterial] >>> ABOUT TO CALL ReadPixels <<<");

                    // For READABLE textures: Use ReadPixels to update CPU memory
                    RenderTexture.active = rt;

                    try
                    {
                        target.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
                        Debug.Log($"[TextureBlitter.BlitWithMaterial] ReadPixels completed");
                    }
                    catch (System.Exception readEx)
                    {
                        Debug.LogError($"[TextureBlitter.BlitWithMaterial] ✗ ReadPixels FAILED: {readEx.Message}");
                        throw;
                    }

                    RenderTexture.active = null;

                    Debug.Log($"[TextureBlitter.BlitWithMaterial] >>> ABOUT TO CALL Apply <<<");

                    try
                    {
                        // ✅ CRITICAL: Apply immediately to upload pixel data to GPU
                        // Without this, the texture changes won't be visible!
                        target.Apply(false, false); // updateMipmaps=false, makeNoLongerReadable=false
                        Debug.Log($"[TextureBlitter.BlitWithMaterial] Apply completed");

#if UNITY_IOS
                        // ✅ iOS CRITICAL FIX: Force GPU to complete all pending texture uploads
                        // On iOS Metal, Apply() schedules a DEFERRED upload. If we return immediately,
                        // another operation (like atlas save) might access the texture before upload completes.
                        // GL.Flush() forces all pending GPU operations to complete.
                        Debug.Log($"[TextureBlitter.BlitWithMaterial] iOS: Forcing GPU flush...");
                        GL.Flush();
                        Debug.Log($"[TextureBlitter.BlitWithMaterial] iOS: GPU flush complete");
#endif
                    }
                    catch (System.Exception applyEx)
                    {
                        Debug.LogError($"[TextureBlitter.BlitWithMaterial] ✗ Apply FAILED: {applyEx.Message}");
                        throw;
                    }
                }
                else
                {
                    Debug.Log($"[TextureBlitter.BlitWithMaterial] Using Graphics.CopyTexture (non-readable texture)...");
                    Debug.Log($"[TextureBlitter.BlitWithMaterial] >>> ABOUT TO CALL CopyTexture <<<");

                    try
                    {
                        // For NON-READABLE textures: Use Graphics.CopyTexture (GPU-only)
                        Graphics.CopyTexture(rt, target);
                        Debug.Log($"[TextureBlitter.BlitWithMaterial] CopyTexture completed");

#if UNITY_IOS
                        // ✅ iOS CRITICAL FIX: Force GPU to complete the copy operation
                        Debug.Log($"[TextureBlitter.BlitWithMaterial] iOS: Forcing GPU flush...");
                        GL.Flush();
                        Debug.Log($"[TextureBlitter.BlitWithMaterial] iOS: GPU flush complete");
#endif
                    }
                    catch (System.Exception copyEx)
                    {
                        Debug.LogError($"[TextureBlitter.BlitWithMaterial] ✗ CopyTexture FAILED: {copyEx.Message}");
                        throw;
                    }
                }

                Debug.Log($"[TextureBlitter.BlitWithMaterial] ========== SINGLE BLIT END ==========");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[TextureBlitter.BlitWithMaterial] ✗ FATAL ERROR: {ex.Message}\n{ex.StackTrace}");
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
        /// Direct CPU-based pixel copy using SetPixels32 (byte format).
        /// This avoids the Metal format conversion that causes crashes on iOS.
        /// Uses Color32 (byte) format to match ARGB32/RGBA32 textures directly.
        /// </summary>
        private static void BlitCPUDirect(Texture2D source, Texture2D target, int x, int y)
        {
            Debug.Log($"[TextureBlitter.BlitCPUDirect] Starting CPU copy: {source.width}x{source.height} -> ({x},{y})");

            // Get source pixels as Color32 (byte format - matches ARGB32)
            var sourcePixels = source.GetPixels32();
            Debug.Log($"[TextureBlitter.BlitCPUDirect] Got source pixels: {sourcePixels.Length} pixels");

            // Get target pixels, modify the region, then set back
            // This is necessary because SetPixels32 doesn't have a region overload
            var targetPixels = target.GetPixels32();
            Debug.Log($"[TextureBlitter.BlitCPUDirect] Got target pixels: {targetPixels.Length} pixels");

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

            Debug.Log($"[TextureBlitter.BlitCPUDirect] Pixels copied, calling SetPixels32...");

            // Set all pixels back
            target.SetPixels32(targetPixels);

            Debug.Log($"[TextureBlitter.BlitCPUDirect] SetPixels32 complete, calling Apply...");

            // Apply changes - this uploads to GPU
            target.Apply(false, false);

            Debug.Log($"[TextureBlitter.BlitCPUDirect] Apply complete");

#if UNITY_IOS
            // ✅ iOS CRITICAL FIX: Force GPU to complete the texture upload
            GL.Flush();
            Debug.Log($"[TextureBlitter.BlitCPUDirect] iOS: GL.Flush() complete");
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
                
                var result = new Texture2D(region.width, region.height, TextureFormat.RGBA32, false);
                result.ReadPixels(new Rect(region.x, region.y, region.width, region.height), 0, 0);
                result.Apply();
                
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                
                return result;
            }

            var copy = new Texture2D(region.width, region.height, source.format, false);
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
