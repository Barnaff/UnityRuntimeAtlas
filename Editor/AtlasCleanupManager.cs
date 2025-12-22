using UnityEditor;
using UnityEngine;

namespace RuntimeAtlasPacker.Editor
{
    /// <summary>
    /// Manages atlas cleanup between Unity editor play mode runs.
    /// Prevents memory leaks and null reference exceptions in the editor.
    /// </summary>
    [InitializeOnLoad]
    public static class AtlasCleanupManager
    {
        static AtlasCleanupManager()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting += OnEditorQuitting;
            
            Debug.Log("[AtlasCleanupManager] Initialized - will cleanup atlases between play mode runs");
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingPlayMode:
                    CleanupAtlases("Exiting Play Mode");
                    break;
                    
                case PlayModeStateChange.EnteredEditMode:
                    CleanupAtlases("Entered Edit Mode");
                    break;
                    
                case PlayModeStateChange.ExitingEditMode:
                    // Optional: Clear before entering play mode too
                    CleanupAtlases("Exiting Edit Mode");
                    break;
            }
        }

        private static void OnEditorQuitting()
        {
            CleanupAtlases("Editor Quitting");
        }

        private static void CleanupAtlases(string reason)
        {
            try
            {
                int atlasCount = AtlasPacker.GetActiveAtlasCount();
                if (atlasCount > 0)
                {
                    Debug.Log($"[AtlasCleanupManager] {reason} - Cleaning up {atlasCount} atlases");
                    
                    // Log atlas names for debugging
                    string[] atlasNames = AtlasPacker.GetActiveAtlasNames();
                    if (atlasNames.Length > 0)
                    {
                        Debug.Log($"[AtlasCleanupManager] Atlas names: {string.Join(", ", atlasNames)}");
                    }
                    
                    AtlasPacker.ClearAllAtlases();
                    
                    // Force garbage collection to help free memory
                    System.GC.Collect();
                    
                    Debug.Log($"[AtlasCleanupManager] Cleanup complete - {atlasCount} atlases disposed");
                }
                else
                {
                    Debug.Log($"[AtlasCleanupManager] {reason} - No atlases to cleanup");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AtlasCleanupManager] Error during cleanup: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Manually trigger atlas cleanup (for debugging or testing).
        /// </summary>
        [MenuItem("Window/Runtime Atlas Packer/Force Cleanup Atlases")]
        public static void ForceCleanup()
        {
            CleanupAtlases("Manual Force Cleanup");
        }
    }
}
