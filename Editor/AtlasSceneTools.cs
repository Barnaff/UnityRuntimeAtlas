using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine.UIElements;

namespace RuntimeAtlasPacker.Editor
{
    /// <summary>
    /// Draws gizmos for atlas renderers in scene view.
    /// </summary>
    [InitializeOnLoad]
    public static class AtlasGizmos
    {
        private static bool _showGizmos = false;
        private static bool _showBounds = true;
        private static bool _showInfo = true;
        private static Color _activeColor = new Color(0, 1, 0, 0.5f);
        private static Color _inactiveColor = new Color(1, 0, 0, 0.5f);
        
        public static bool ShowGizmos
        {
            get => _showGizmos;
            set
            {
                _showGizmos = value;
                SceneView.RepaintAll();
            }
        }
        
        public static bool ShowBounds
        {
            get => _showBounds;
            set
            {
                _showBounds = value;
                SceneView.RepaintAll();
            }
        }
        
        public static bool ShowInfo
        {
            get => _showInfo;
            set
            {
                _showInfo = value;
                SceneView.RepaintAll();
            }
        }

        static AtlasGizmos()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!_showGizmos) return;
            
            // Draw gizmos for AtlasSpriteRenderers
            var spriteRenderers = Object.FindObjectsByType<AtlasSpriteRenderer>(FindObjectsSortMode.None);
            foreach (var renderer in spriteRenderers)
            {
                DrawRendererGizmo(renderer.gameObject, renderer.HasEntry, 
                    renderer.Entry?.Id ?? -1, renderer.Entry?.Width ?? 0, renderer.Entry?.Height ?? 0);
            }
            
            // Draw gizmos for AtlasImages
            var images = Object.FindObjectsByType<AtlasImage>(FindObjectsSortMode.None);
            foreach (var image in images)
            {
                DrawUIGizmo(image.gameObject, image.HasEntry,
                    image.Entry?.Id ?? -1, image.Entry?.Width ?? 0, image.Entry?.Height ?? 0);
            }
            
            // Draw gizmos for AtlasRawImages
            var rawImages = Object.FindObjectsByType<AtlasRawImage>(FindObjectsSortMode.None);
            foreach (var rawImage in rawImages)
            {
                DrawUIGizmo(rawImage.gameObject, rawImage.HasEntry,
                    rawImage.Entry?.Id ?? -1, rawImage.Entry?.Width ?? 0, rawImage.Entry?.Height ?? 0);
            }
        }

        private static void DrawRendererGizmo(GameObject go, bool hasEntry, int entryId, int width, int height)
        {
            var spriteRenderer = go.GetComponent<SpriteRenderer>();
            if (spriteRenderer == null) return;
            
            var color = hasEntry ? _activeColor : _inactiveColor;
            Handles.color = color;
            
            if (_showBounds)
            {
                var bounds = spriteRenderer.bounds;
                Handles.DrawWireCube(bounds.center, bounds.size);
            }
            
            if (_showInfo)
            {
                var style = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = color },
                    fontSize = 10
                };
                
                var pos = go.transform.position;
                var labelPos = HandleUtility.WorldToGUIPoint(pos);
                
                Handles.BeginGUI();
                var rect = new Rect(labelPos.x - 50, labelPos.y - 30, 100, 20);
                
                string label = hasEntry 
                    ? $"ID:{entryId} ({width}x{height})" 
                    : "No Entry";
                
                GUI.Label(rect, label, style);
                Handles.EndGUI();
            }
        }

        private static void DrawUIGizmo(GameObject go, bool hasEntry, int entryId, int width, int height)
        {
            var rectTransform = go.GetComponent<RectTransform>();
            if (rectTransform == null) return;
            
            var color = hasEntry ? _activeColor : _inactiveColor;
            Handles.color = color;
            
            if (_showBounds)
            {
                var corners = new Vector3[4];
                rectTransform.GetWorldCorners(corners);
                
                Handles.DrawLine(corners[0], corners[1]);
                Handles.DrawLine(corners[1], corners[2]);
                Handles.DrawLine(corners[2], corners[3]);
                Handles.DrawLine(corners[3], corners[0]);
            }
            
            if (_showInfo)
            {
                var style = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = color },
                    fontSize = 10
                };
                
                var center = rectTransform.position;
                var labelPos = HandleUtility.WorldToGUIPoint(center);
                
                Handles.BeginGUI();
                var rect = new Rect(labelPos.x - 50, labelPos.y - 30, 100, 20);
                
                string label = hasEntry 
                    ? $"ID:{entryId} ({width}x{height})" 
                    : "No Entry";
                
                GUI.Label(rect, label, style);
                Handles.EndGUI();
            }
        }
    }

    /// <summary>
    /// Scene view overlay panel for atlas controls.
    /// </summary>
    [Overlay(typeof(SceneView), "Atlas Debug", true)]
    public class AtlasSceneOverlay : Overlay, ITransientOverlay
    {
        public bool visible => true;
        
        private Label _statsLabel;
        private Toggle _showGizmosToggle;
        private Toggle _showBoundsToggle;
        private Toggle _showInfoToggle;

        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            root.style.width = 200;
            root.style.paddingTop = 5;
            root.style.paddingBottom = 5;
            root.style.paddingLeft = 5;
            root.style.paddingRight = 5;
            
            // Title
            var title = new Label("Atlas Debug");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 5;
            root.Add(title);
            
            // Stats label
            _statsLabel = new Label();
            _statsLabel.style.marginBottom = 5;
            root.Add(_statsLabel);
            
            // Toggles
            _showGizmosToggle = new Toggle("Show Gizmos");
            _showGizmosToggle.value = AtlasGizmos.ShowGizmos;
            _showGizmosToggle.RegisterValueChangedCallback(evt => AtlasGizmos.ShowGizmos = evt.newValue);
            root.Add(_showGizmosToggle);
            
            _showBoundsToggle = new Toggle("Show Bounds");
            _showBoundsToggle.value = AtlasGizmos.ShowBounds;
            _showBoundsToggle.RegisterValueChangedCallback(evt => AtlasGizmos.ShowBounds = evt.newValue);
            root.Add(_showBoundsToggle);
            
            _showInfoToggle = new Toggle("Show Info");
            _showInfoToggle.value = AtlasGizmos.ShowInfo;
            _showInfoToggle.RegisterValueChangedCallback(evt => AtlasGizmos.ShowInfo = evt.newValue);
            root.Add(_showInfoToggle);
            
            // Buttons
            var buttonContainer = new VisualElement();
            buttonContainer.style.flexDirection = FlexDirection.Row;
            buttonContainer.style.marginTop = 5;
            
            var debugButton = new Button(() => AtlasDebugWindow.ShowWindow());
            debugButton.text = "Debug Window";
            buttonContainer.Add(debugButton);
            
            var profilerButton = new Button(() => AtlasProfilerWindow.ShowWindow());
            profilerButton.text = "Profiler";
            buttonContainer.Add(profilerButton);
            
            root.Add(buttonContainer);
            
            // Schedule updates
            root.schedule.Execute(UpdateStats).Every(500);
            
            return root;
        }

        private void UpdateStats()
        {
            if (_statsLabel == null) return;
            
            int spriteCount = Object.FindObjectsByType<AtlasSpriteRenderer>(FindObjectsSortMode.None).Length;
            int imageCount = Object.FindObjectsByType<AtlasImage>(FindObjectsSortMode.None).Length;
            int rawImageCount = Object.FindObjectsByType<AtlasRawImage>(FindObjectsSortMode.None).Length;
            
            int activeSprites = Object.FindObjectsByType<AtlasSpriteRenderer>(FindObjectsSortMode.None).Count(r => r.HasEntry);
            int activeImages = Object.FindObjectsByType<AtlasImage>(FindObjectsSortMode.None).Count(i => i.HasEntry);
            int activeRawImages = Object.FindObjectsByType<AtlasRawImage>(FindObjectsSortMode.None).Count(r => r.HasEntry);
            
            int total = spriteCount + imageCount + rawImageCount;
            int active = activeSprites + activeImages + activeRawImages;
            
            _statsLabel.text = $"Renderers: {active}/{total}\n" +
                              $"Sprites: {activeSprites}/{spriteCount}\n" +
                              $"Images: {activeImages}/{imageCount}\n" +
                              $"RawImages: {activeRawImages}/{rawImageCount}";
        }
    }

    /// <summary>
    /// Handles menu and preferences for atlas gizmos.
    /// </summary>
    public static class AtlasGizmoSettings
    {
        private const string ShowGizmosKey = "RuntimeAtlasPacker_ShowGizmos";
        private const string ShowBoundsKey = "RuntimeAtlasPacker_ShowBounds";
        private const string ShowInfoKey = "RuntimeAtlasPacker_ShowInfo";

        [InitializeOnLoadMethod]
        private static void LoadSettings()
        {
            AtlasGizmos.ShowGizmos = EditorPrefs.GetBool(ShowGizmosKey, false);
            AtlasGizmos.ShowBounds = EditorPrefs.GetBool(ShowBoundsKey, true);
            AtlasGizmos.ShowInfo = EditorPrefs.GetBool(ShowInfoKey, true);
        }

        [MenuItem("Window/Runtime Atlas Packer/Toggle Gizmos")]
        private static void ToggleGizmos()
        {
            AtlasGizmos.ShowGizmos = !AtlasGizmos.ShowGizmos;
            EditorPrefs.SetBool(ShowGizmosKey, AtlasGizmos.ShowGizmos);
        }

        [MenuItem("Window/Runtime Atlas Packer/Toggle Gizmos", true)]
        private static bool ToggleGizmosValidate()
        {
            Menu.SetChecked("Window/Runtime Atlas Packer/Toggle Gizmos", AtlasGizmos.ShowGizmos);
            return true;
        }
    }
}
