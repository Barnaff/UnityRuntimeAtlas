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
                EditorGUILayout.LabelField("Entry ID:", renderer.Entry.Id.ToString());
                EditorGUILayout.LabelField("Size:", $"{renderer.Entry.Width}x{renderer.Entry.Height}");
                EditorGUILayout.LabelField("UV:", renderer.Entry.UV.ToString());
                
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
                // Show atlas texture preview with entry highlighted
                var atlasTexture = renderer.Entry.Texture;
                float maxSize = EditorGUIUtility.currentViewWidth - 40;
                float aspect = (float)atlasTexture.height / atlasTexture.width;
                float width = Mathf.Min(maxSize, 300);
                float height = width * aspect;
                
                var rect = GUILayoutUtility.GetRect(width, height);
                EditorGUI.DrawPreviewTexture(rect, atlasTexture, null, ScaleMode.ScaleToFit);
                
                // Draw entry rect
                var uvRect = renderer.Entry.UV;
                var highlightRect = new Rect(
                    rect.x + uvRect.x * rect.width,
                    rect.y + (1 - uvRect.y - uvRect.height) * rect.height,
                    uvRect.width * rect.width,
                    uvRect.height * rect.height
                );
                
                Handles.DrawSolidRectangleWithOutline(highlightRect, 
                    new Color(1, 1, 0, 0.3f), Color.yellow);
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
                var atlasTexture = image.Entry.Texture;
                float maxSize = EditorGUIUtility.currentViewWidth - 40;
                float width = Mathf.Min(maxSize, 200);
                float height = width;
                
                var rect = GUILayoutUtility.GetRect(width, height);
                EditorGUI.DrawPreviewTexture(rect, atlasTexture, null, ScaleMode.ScaleToFit);
                
                var uvRect = image.Entry.UV;
                var highlightRect = new Rect(
                    rect.x + uvRect.x * rect.width,
                    rect.y + (1 - uvRect.y - uvRect.height) * rect.height,
                    uvRect.width * rect.width,
                    uvRect.height * rect.height
                );
                
                Handles.DrawSolidRectangleWithOutline(highlightRect, 
                    new Color(0, 1, 0, 0.3f), Color.green);
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
