using UnityEngine;
using UnityEditor;

namespace RuntimeAtlasPacker.Editor
{
    /// <summary>
    /// Custom inspector for AtlasSpriteRenderer component.
    /// </summary>
    [CustomEditor(typeof(AtlasSpriteRenderer))]
    [CanEditMultipleObjects]
    public class AtlasSpriteRendererInspector : UnityEditor.Editor
    {
        private SerializedProperty _pixelsPerUnit;
        private SerializedProperty _pivot;
        private SerializedProperty _autoPackOnAssign;
        private SerializedProperty _targetAtlasName;
        
        private bool _showDebugInfo = true;
        private Texture2D _textureToAdd;

        private void OnEnable()
        {
            _pixelsPerUnit = serializedObject.FindProperty("_pixelsPerUnit");
            _pivot = serializedObject.FindProperty("_pivot");
            _autoPackOnAssign = serializedObject.FindProperty("_autoPackOnAssign");
            _targetAtlasName = serializedObject.FindProperty("_targetAtlasName");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            
            var renderer = target as AtlasSpriteRenderer;
            
            EditorGUILayout.LabelField("Atlas Sprite Renderer", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            // Settings
            EditorGUILayout.PropertyField(_pixelsPerUnit);
            EditorGUILayout.PropertyField(_pivot);
            EditorGUILayout.PropertyField(_autoPackOnAssign, new GUIContent("Auto Pack"));
            EditorGUILayout.PropertyField(_targetAtlasName, new GUIContent("Target Atlas"));
            
            EditorGUILayout.Space(10);
            
            // Status
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            
            bool hasEntry = renderer.HasEntry;
            EditorGUILayout.LabelField("Has Entry:", hasEntry ? "Yes" : "No");
            
            if (hasEntry)
            {
                EditorGUILayout.LabelField("Sprite Name:", renderer.Entry.Name);
                EditorGUILayout.LabelField("Entry ID:", renderer.Entry.Id.ToString());
                EditorGUILayout.LabelField("Size:", $"{renderer.Entry.Width}x{renderer.Entry.Height}");
                EditorGUILayout.LabelField("UV:", renderer.Entry.UV.ToString("F3"));
                
                if (renderer.Atlas != null)
                {
                    EditorGUILayout.LabelField("Atlas Size:", $"{renderer.Atlas.Width}x{renderer.Atlas.Height}");
                    EditorGUILayout.LabelField("Atlas Fill:", $"{renderer.Atlas.FillRatio:P1}");
                }
            }
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(10);
            
            // Actions
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            _textureToAdd = EditorGUILayout.ObjectField("Texture:", _textureToAdd, typeof(Texture2D), false) as Texture2D;
            
            GUI.enabled = _textureToAdd != null && Application.isPlaying;
            if (GUILayout.Button("Set", GUILayout.Width(50)))
            {
                renderer.SetTexture(_textureToAdd);
                _textureToAdd = null;
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Texture assignment only works in Play Mode.", MessageType.Info);
            }
            
            EditorGUILayout.Space(5);
            
            EditorGUILayout.BeginHorizontal();
            
            GUI.enabled = hasEntry && Application.isPlaying;
            if (GUILayout.Button("Clear"))
            {
                renderer.Clear();
            }
            
            if (GUILayout.Button("Remove from Atlas"))
            {
                renderer.RemoveFromAtlas();
            }
            
            if (GUILayout.Button("Force Refresh"))
            {
                renderer.ForceRefresh();
            }
            GUI.enabled = true;
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(10);
            
            // Debug info
            _showDebugInfo = EditorGUILayout.Foldout(_showDebugInfo, "Debug Info", true);
            if (_showDebugInfo && hasEntry && renderer.Entry.Texture != null)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Atlas Preview", EditorStyles.boldLabel);
                
                // Show atlas texture preview with entry highlighted
                var atlasTexture = renderer.Entry.Texture;
                var entry = renderer.Entry;
                
                // Calculate preview size while maintaining aspect ratio
                float maxWidth = EditorGUIUtility.currentViewWidth - 40;
                float atlasAspect = (float)atlasTexture.height / atlasTexture.width;
                float previewWidth = Mathf.Min(maxWidth, 400);
                float previewHeight = previewWidth * atlasAspect;
                
                // Get rect for preview
                var previewRect = GUILayoutUtility.GetRect(previewWidth, previewHeight);
                
                // Draw atlas texture (ScaleToFit maintains aspect ratio and may add letterboxing)
                EditorGUI.DrawPreviewTexture(previewRect, atlasTexture, null, ScaleMode.ScaleToFit);
                
                // Calculate actual texture display area (accounting for letterboxing)
                float displayAspect = previewRect.width / previewRect.height;
                Rect textureRect;
                
                if (displayAspect > atlasAspect)
                {
                    // Letterboxed on sides
                    float displayWidth = previewRect.height * atlasAspect;
                    float offsetX = (previewRect.width - displayWidth) * 0.5f;
                    textureRect = new Rect(
                        previewRect.x + offsetX,
                        previewRect.y,
                        displayWidth,
                        previewRect.height
                    );
                }
                else
                {
                    // Letterboxed on top/bottom
                    float displayHeight = previewRect.width / atlasAspect;
                    float offsetY = (previewRect.height - displayHeight) * 0.5f;
                    textureRect = new Rect(
                        previewRect.x,
                        previewRect.y + offsetY,
                        previewRect.width,
                        displayHeight
                    );
                }
                
                // Get UV coordinates from entry
                var uvRect = entry.UV;
                
                // Convert UV coordinates to screen coordinates
                // UV origin is bottom-left, but screen origin is top-left
                // So we need to flip Y: screenY = (1 - uvY - uvHeight)
                var highlightRect = new Rect(
                    textureRect.x + uvRect.x * textureRect.width,
                    textureRect.y + (1 - uvRect.y - uvRect.height) * textureRect.height,
                    uvRect.width * textureRect.width,
                    uvRect.height * textureRect.height
                );
                
                // Draw yellow highlight with outline
                Handles.DrawSolidRectangleWithOutline(
                    highlightRect,
                    new Color(1, 1, 0, 0.25f),  // Yellow semi-transparent fill
                    Color.yellow                  // Yellow outline
                );
                
                // Draw debug info
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField($"Sprite Position: ({entry.Rect.x}, {entry.Rect.y})", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"Sprite Size: {entry.Rect.width}x{entry.Rect.height}", EditorStyles.miniLabel);
            }
            
            serializedObject.ApplyModifiedProperties();
        }
    }

    /// <summary>
    /// Custom inspector for AtlasImage component.
    /// </summary>
    [CustomEditor(typeof(AtlasImage))]
    [CanEditMultipleObjects]
    public class AtlasImageInspector : UnityEditor.Editor
    {
        private SerializedProperty _pixelsPerUnit;
        private SerializedProperty _preserveAspect;
        private SerializedProperty _autoPackOnAssign;
        private SerializedProperty _targetAtlasName;
        private SerializedProperty _imageType;
        private SerializedProperty _fillCenter;
        
        private bool _showDebugInfo = true;
        private Texture2D _textureToAdd;

        private void OnEnable()
        {
            _pixelsPerUnit = serializedObject.FindProperty("_pixelsPerUnit");
            _preserveAspect = serializedObject.FindProperty("_preserveAspect");
            _autoPackOnAssign = serializedObject.FindProperty("_autoPackOnAssign");
            _targetAtlasName = serializedObject.FindProperty("_targetAtlasName");
            _imageType = serializedObject.FindProperty("_imageType");
            _fillCenter = serializedObject.FindProperty("_fillCenter");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            
            var image = target as AtlasImage;
            
            EditorGUILayout.LabelField("Atlas Image", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            // Settings
            EditorGUILayout.PropertyField(_imageType, new GUIContent("Image Type"));
            EditorGUILayout.PropertyField(_pixelsPerUnit);
            EditorGUILayout.PropertyField(_preserveAspect);
            
            if (_imageType.enumValueIndex == (int)AtlasImage.ImageType.Sliced)
            {
                EditorGUILayout.PropertyField(_fillCenter);
            }
            
            EditorGUILayout.Space(5);
            EditorGUILayout.PropertyField(_autoPackOnAssign, new GUIContent("Auto Pack"));
            EditorGUILayout.PropertyField(_targetAtlasName, new GUIContent("Target Atlas"));
            
            EditorGUILayout.Space(10);
            
            // Status
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            
            bool hasEntry = image.HasEntry;
            EditorGUILayout.LabelField("Has Entry:", hasEntry ? "Yes" : "No");
            
            if (hasEntry)
            {
                EditorGUILayout.LabelField("Entry ID:", image.Entry.Id.ToString());
                EditorGUILayout.LabelField("Size:", $"{image.Entry.Width}x{image.Entry.Height}");
                
                if (image.Atlas != null)
                {
                    EditorGUILayout.LabelField("Atlas Size:", $"{image.Atlas.Width}x{image.Atlas.Height}");
                }
            }
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(10);
            
            // Actions
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            _textureToAdd = EditorGUILayout.ObjectField("Texture:", _textureToAdd, typeof(Texture2D), false) as Texture2D;
            
            GUI.enabled = _textureToAdd != null && Application.isPlaying;
            if (GUILayout.Button("Set", GUILayout.Width(50)))
            {
                image.SetTexture(_textureToAdd);
                _textureToAdd = null;
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Texture assignment only works in Play Mode.", MessageType.Info);
            }
            
            EditorGUILayout.Space(5);
            
            EditorGUILayout.BeginHorizontal();
            
            GUI.enabled = Application.isPlaying;
            if (GUILayout.Button("Set Native Size"))
            {
                image.SetNativeSize();
            }
            
            GUI.enabled = hasEntry && Application.isPlaying;
            if (GUILayout.Button("Clear"))
            {
                image.Clear();
            }
            
            if (GUILayout.Button("Force Refresh"))
            {
                image.ForceRefresh();
            }
            GUI.enabled = true;
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
            
            // Debug preview
            _showDebugInfo = EditorGUILayout.Foldout(_showDebugInfo, "Atlas Preview", true);
            if (_showDebugInfo && hasEntry && image.Entry.Texture != null)
            {
                EditorGUILayout.Space(5);
                
                var atlasTexture = image.Entry.Texture;
                var entry = image.Entry;
                
                // Calculate preview size while maintaining aspect ratio
                float maxWidth = EditorGUIUtility.currentViewWidth - 40;
                float atlasAspect = (float)atlasTexture.height / atlasTexture.width;
                float previewWidth = Mathf.Min(maxWidth, 400);
                float previewHeight = previewWidth * atlasAspect;
                
                // Get rect for preview
                var previewRect = GUILayoutUtility.GetRect(previewWidth, previewHeight);
                
                // Draw atlas texture
                EditorGUI.DrawPreviewTexture(previewRect, atlasTexture, null, ScaleMode.ScaleToFit);
                
                // Calculate actual texture display area (accounting for letterboxing)
                float displayAspect = previewRect.width / previewRect.height;
                Rect textureRect;
                
                if (displayAspect > atlasAspect)
                {
                    // Letterboxed on sides
                    float displayWidth = previewRect.height * atlasAspect;
                    float offsetX = (previewRect.width - displayWidth) * 0.5f;
                    textureRect = new Rect(
                        previewRect.x + offsetX,
                        previewRect.y,
                        displayWidth,
                        previewRect.height
                    );
                }
                else
                {
                    // Letterboxed on top/bottom
                    float displayHeight = previewRect.width / atlasAspect;
                    float offsetY = (previewRect.height - displayHeight) * 0.5f;
                    textureRect = new Rect(
                        previewRect.x,
                        previewRect.y + offsetY,
                        previewRect.width,
                        displayHeight
                    );
                }
                
                // Get UV coordinates and convert to screen space
                var uvRect = entry.UV;
                var highlightRect = new Rect(
                    textureRect.x + uvRect.x * textureRect.width,
                    textureRect.y + (1 - uvRect.y - uvRect.height) * textureRect.height,
                    uvRect.width * textureRect.width,
                    uvRect.height * textureRect.height
                );
                
                // Draw green highlight
                Handles.DrawSolidRectangleWithOutline(
                    highlightRect,
                    new Color(0, 1, 0, 0.25f),  // Green semi-transparent fill
                    Color.green                  // Green outline
                );
                
                // Debug info
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField($"Sprite Position: ({entry.Rect.x}, {entry.Rect.y})", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"Sprite Size: {entry.Rect.width}x{entry.Rect.height}", EditorStyles.miniLabel);
            }
            
            serializedObject.ApplyModifiedProperties();
        }
    }

    /// <summary>
    /// Custom inspector for AtlasRawImage component.
    /// </summary>
    [CustomEditor(typeof(AtlasRawImage))]
    [CanEditMultipleObjects]
    public class AtlasRawImageInspector : UnityEditor.Editor
    {
        private SerializedProperty _preserveAspect;
        private SerializedProperty _autoPackOnAssign;
        private SerializedProperty _targetAtlasName;
        
        private Texture2D _textureToAdd;

        private void OnEnable()
        {
            _preserveAspect = serializedObject.FindProperty("_preserveAspect");
            _autoPackOnAssign = serializedObject.FindProperty("_autoPackOnAssign");
            _targetAtlasName = serializedObject.FindProperty("_targetAtlasName");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            
            var rawImage = target as AtlasRawImage;
            
            EditorGUILayout.LabelField("Atlas Raw Image", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            // Settings
            EditorGUILayout.PropertyField(_preserveAspect);
            EditorGUILayout.PropertyField(_autoPackOnAssign, new GUIContent("Auto Pack"));
            EditorGUILayout.PropertyField(_targetAtlasName, new GUIContent("Target Atlas"));
            
            EditorGUILayout.Space(10);
            
            // Status
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            
            bool hasEntry = rawImage.HasEntry;
            EditorGUILayout.LabelField("Has Entry:", hasEntry ? "Yes" : "No");
            
            if (hasEntry)
            {
                EditorGUILayout.LabelField("Entry ID:", rawImage.Entry.Id.ToString());
                EditorGUILayout.LabelField("Size:", $"{rawImage.Entry.Width}x{rawImage.Entry.Height}");
                EditorGUILayout.LabelField("UV:", rawImage.Entry.UV.ToString());
            }
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(10);
            
            // Actions
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            _textureToAdd = EditorGUILayout.ObjectField("Texture:", _textureToAdd, typeof(Texture2D), false) as Texture2D;
            
            GUI.enabled = _textureToAdd != null && Application.isPlaying;
            if (GUILayout.Button("Set", GUILayout.Width(50)))
            {
                rawImage.SetTexture(_textureToAdd);
                _textureToAdd = null;
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Texture assignment only works in Play Mode.", MessageType.Info);
            }
            
            EditorGUILayout.Space(5);
            
            EditorGUILayout.BeginHorizontal();
            
            GUI.enabled = Application.isPlaying;
            if (GUILayout.Button("Set Native Size"))
            {
                rawImage.SetNativeSize();
            }
            
            GUI.enabled = hasEntry && Application.isPlaying;
            if (GUILayout.Button("Clear"))
            {
                rawImage.Clear();
            }
            GUI.enabled = true;
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
            
            serializedObject.ApplyModifiedProperties();
        }
    }

    /// <summary>
    /// Custom inspector for legacy AtlasSprite component.
    /// </summary>
    [CustomEditor(typeof(AtlasSprite))]
    public class AtlasSpriteInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var sprite = target as AtlasSprite;
            
            EditorGUILayout.LabelField("Atlas Sprite (Legacy)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("This is a legacy component. Consider using AtlasSpriteRenderer instead.", MessageType.Info);
            
            EditorGUILayout.Space(5);
            
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Is Valid:", sprite.IsValid ? "Yes" : "No");
            
            if (sprite.IsValid && sprite.Entry != null)
            {
                EditorGUILayout.LabelField("Entry ID:", sprite.Entry.Id.ToString());
                EditorGUILayout.LabelField("Size:", $"{sprite.Entry.Width}x{sprite.Entry.Height}");
            }
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(5);
            
            GUI.enabled = sprite.IsValid && Application.isPlaying;
            if (GUILayout.Button("Unbind"))
            {
                sprite.Unbind();
            }
            GUI.enabled = true;
        }
    }

    /// <summary>
    /// Custom inspector for legacy AtlasMaterial component.
    /// </summary>
    [CustomEditor(typeof(AtlasMaterial))]
    public class AtlasMaterialInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var mat = target as AtlasMaterial;
            
            EditorGUILayout.LabelField("Atlas Material (Legacy)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("This is a legacy component for custom shader integration.", MessageType.Info);
            
            DrawDefaultInspector();
            
            EditorGUILayout.Space(5);
            
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            
            bool hasEntry = mat.Entry != null && mat.Entry.IsValid;
            EditorGUILayout.LabelField("Has Entry:", hasEntry ? "Yes" : "No");
            
            if (hasEntry)
            {
                EditorGUILayout.LabelField("Entry ID:", mat.Entry.Id.ToString());
            }
            
            EditorGUILayout.EndVertical();
            
            GUI.enabled = hasEntry && Application.isPlaying;
            if (GUILayout.Button("Unbind"))
            {
                mat.Unbind();
            }
            GUI.enabled = true;
        }
    }
}
