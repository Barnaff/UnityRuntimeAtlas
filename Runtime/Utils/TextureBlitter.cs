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

        private static readonly string BlitShaderCode = @"
Shader ""Hidden/RuntimeAtlasPacker/Blit""
{
    Properties
    {
        _MainTex (""Texture"", 2D) = ""white"" {}
    }
    SubShader
    {
        Tags { ""RenderType""=""Opaque"" }
        LOD 100
        Cull Off ZWrite Off ZTest Always
        Blend One Zero

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv);
            }
            ENDCG
        }
    }
}";

        private static void EnsureMaterial()
        {
            if (_materialInitialized) return;
            _materialInitialized = true;

            var shader = Shader.Find("Hidden/RuntimeAtlasPacker/Blit");
            if (shader == null)
            {
                // Fallback to standard blit shader
                shader = Shader.Find("Hidden/BlitCopy");
            }
            
            if (shader != null)
            {
                _blitMaterial = new Material(shader);
                _blitMaterial.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        /// <summary>
        /// Blit a source texture to a target texture at the specified position.
        /// Uses GPU when available, falls back to CPU.
        /// </summary>
        public static void Blit(Texture2D source, Texture2D target, int x, int y)
        {
            if (source == null || target == null)
                throw new ArgumentNullException();

            if (SystemInfo.copyTextureSupport != CopyTextureSupport.None)
            {
                // Use GPU copy
                Graphics.CopyTexture(source, 0, 0, 0, 0, source.width, source.height,
                    target, 0, 0, x, y);
            }
            else if (source.isReadable && target.isReadable)
            {
                // CPU fallback
                BlitCPU(source, target, x, y);
            }
            else
            {
                Debug.LogWarning("TextureBlitter: Cannot blit - textures not readable and GPU copy not supported");
            }
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
        /// CPU-based texture blit (fallback when GPU is not available).
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

            // Copy existing content
            if (SystemInfo.copyTextureSupport != CopyTextureSupport.None)
            {
                Graphics.CopyTexture(source, 0, 0, 0, 0, source.width, source.height,
                    newTexture, 0, 0, 0, 0);
            }
            else if (source.isReadable)
            {
                BlitCPU(source, newTexture, 0, 0);
            }

            return newTexture;
        }
    }
}
