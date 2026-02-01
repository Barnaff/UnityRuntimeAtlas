using UnityEngine;
using UnityEditor;

namespace RuntimeAtlasPacker.Editor
{
    /// <summary>
    /// Editor utility to ensure the AtlasBlit material references the correct shader.
    /// Run this if the material shows as pink or has missing shader reference.
    /// </summary>
    public static class AtlasBlitMaterialSetup
    {
        [MenuItem("Tools/Runtime Atlas/Setup Blit Material")]
        public static void SetupBlitMaterial()
        {
            // Find the shader
            var shader = Shader.Find("Hidden/RuntimeAtlasPacker/Blit");
            if (shader == null)
            {
                Debug.LogError("[AtlasBlitMaterialSetup] Shader 'Hidden/RuntimeAtlasPacker/Blit' not found! " +
                              "Make sure AtlasBlit.shader exists in Runtime/Shaders/");
                return;
            }

            // Load the material
            var material = Resources.Load<Material>("AtlasBlitMaterial");
            if (material == null)
            {
                Debug.LogError("[AtlasBlitMaterialSetup] Material 'AtlasBlitMaterial' not found in Resources folder! " +
                              "Make sure AtlasBlitMaterial.mat exists in Runtime/Resources/");
                return;
            }

            // Assign shader to material
            material.shader = shader;
            
            // Mark as dirty and save
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[AtlasBlitMaterialSetup] ✅ Successfully setup AtlasBlitMaterial with shader!");
        }

        [MenuItem("Tools/Runtime Atlas/Verify Blit Material")]
        public static void VerifyBlitMaterial()
        {
            // Check shader
            var shader = Shader.Find("Hidden/RuntimeAtlasPacker/Blit");
            var shaderStatus = shader != null ? "✅ Found" : "❌ Missing";
            Debug.Log($"[AtlasBlitMaterialSetup] Shader: {shaderStatus}");

            // Check material
            var material = Resources.Load<Material>("AtlasBlitMaterial");
            if (material == null)
            {
                Debug.LogError("[AtlasBlitMaterialSetup] Material: ❌ Missing");
                return;
            }

            Debug.Log($"[AtlasBlitMaterialSetup] Material: ✅ Found");
            
            // Check material's shader
            if (material.shader == null)
            {
                Debug.LogError("[AtlasBlitMaterialSetup] Material shader: ❌ Not assigned");
            }
            else if (material.shader.name == "Hidden/RuntimeAtlasPacker/Blit")
            {
                Debug.Log("[AtlasBlitMaterialSetup] Material shader: ✅ Correct");
            }
            else
            {
                Debug.LogWarning($"[AtlasBlitMaterialSetup] Material shader: ⚠️ Wrong shader assigned: {material.shader.name}");
            }

            Debug.Log("[AtlasBlitMaterialSetup] Verification complete!");
        }

        /// <summary>
        /// Automatically run setup when Unity loads (only in editor)
        /// </summary>
        [InitializeOnLoadMethod]
        private static void AutoSetup()
        {
            // Small delay to ensure assets are loaded
            EditorApplication.delayCall += () =>
            {
                var material = Resources.Load<Material>("AtlasBlitMaterial");
                if (material != null && material.shader == null)
                {
                    Debug.LogWarning("[AtlasBlitMaterialSetup] AtlasBlitMaterial has no shader assigned. Auto-fixing...");
                    SetupBlitMaterial();
                }
            };
        }
    }
}

