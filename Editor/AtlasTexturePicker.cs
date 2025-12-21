using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace RuntimeAtlasPacker.Editor
{
    /// <summary>
    /// Quick texture picker window for adding textures to atlases.
    /// </summary>
    public class AtlasTexturePicker : EditorWindow
    {
        private Vector2 _scrollPosition;
        private List<Texture2D> _selectedTextures = new();
        private RuntimeAtlas _targetAtlas;
        private string _targetAtlasName = "[Default]";
        private string _searchFilter = "";
        private float _previewSize = 64f;
        private bool _showOnlySelected = false;
        
        private List<Texture2D> _allTextures = new();
        private List<Texture2D> _filteredTextures = new();

        [MenuItem("Window/Runtime Atlas Packer/Texture Picker")]
        public static void ShowWindow()
        {
            var window = GetWindow<AtlasTexturePicker>();
            window.titleContent = new GUIContent("Atlas Texture Picker", EditorGUIUtility.IconContent("Texture Icon").image);
            window.minSize = new Vector2(400, 300);
            window.Show();
        }

        public static void ShowWindow(RuntimeAtlas targetAtlas, string atlasName)
        {
            var window = GetWindow<AtlasTexturePicker>();
            window._targetAtlas = targetAtlas;
            window._targetAtlasName = atlasName;
            window.titleContent = new GUIContent("Atlas Texture Picker");
            window.Show();
        }

        private void OnEnable()
        {
            RefreshTextureList();
        }

        private void RefreshTextureList()
        {
            _allTextures.Clear();
            
            var guids = AssetDatabase.FindAssets("t:Texture2D");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null)
                {
                    _allTextures.Add(texture);
                }
            }
            
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (string.IsNullOrEmpty(_searchFilter))
            {
                _filteredTextures = _showOnlySelected 
                    ? _selectedTextures.ToList() 
                    : _allTextures.ToList();
            }
            else
            {
                var filter = _searchFilter.ToLower();
                var source = _showOnlySelected ? _selectedTextures : _allTextures;
                _filteredTextures = source
                    .Where(t => t.name.ToLower().Contains(filter))
                    .ToList();
            }
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawTextureGrid();
            DrawFooter();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                RefreshTextureList();
            }
            
            EditorGUI.BeginChangeCheck();
            _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(150));
            if (EditorGUI.EndChangeCheck())
            {
                ApplyFilter();
            }
            
            EditorGUI.BeginChangeCheck();
            _showOnlySelected = GUILayout.Toggle(_showOnlySelected, "Selected Only", EditorStyles.toolbarButton, GUILayout.Width(90));
            if (EditorGUI.EndChangeCheck())
            {
                ApplyFilter();
            }
            
            GUILayout.FlexibleSpace();
            
            EditorGUILayout.LabelField("Size:", GUILayout.Width(35));
            _previewSize = EditorGUILayout.Slider(_previewSize, 32, 128, GUILayout.Width(100));
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTextureGrid()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            
            if (_filteredTextures.Count == 0)
            {
                EditorGUILayout.HelpBox("No textures found. Try adjusting your search filter.", MessageType.Info);
            }
            else
            {
                int columns = Mathf.Max(1, (int)((position.width - 20) / (_previewSize + 10)));
                int rows = Mathf.CeilToInt((float)_filteredTextures.Count / columns);
                
                for (int row = 0; row < rows; row++)
                {
                    EditorGUILayout.BeginHorizontal();
                    
                    for (int col = 0; col < columns; col++)
                    {
                        int index = row * columns + col;
                        if (index >= _filteredTextures.Count) break;
                        
                        var texture = _filteredTextures[index];
                        DrawTextureItem(texture);
                    }
                    
                    EditorGUILayout.EndHorizontal();
                }
            }
            
            EditorGUILayout.EndScrollView();
        }

        private void DrawTextureItem(Texture2D texture)
        {
            bool isSelected = _selectedTextures.Contains(texture);
            
            EditorGUILayout.BeginVertical(isSelected ? "SelectionRect" : "box", 
                GUILayout.Width(_previewSize + 6), GUILayout.Height(_previewSize + 22));
            
            // Texture preview
            var rect = GUILayoutUtility.GetRect(_previewSize, _previewSize);
            EditorGUI.DrawPreviewTexture(rect, texture, null, ScaleMode.ScaleToFit);
            
            // Selection toggle
            if (GUI.Button(rect, "", GUIStyle.none))
            {
                if (isSelected)
                    _selectedTextures.Remove(texture);
                else
                    _selectedTextures.Add(texture);
            }
            
            // Checkmark overlay
            if (isSelected)
            {
                var checkRect = new Rect(rect.xMax - 20, rect.y, 20, 20);
                GUI.Label(checkRect, "✓", new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, normal = { textColor = Color.green } });
            }
            
            // Name
            EditorGUILayout.LabelField(texture.name, EditorStyles.centeredGreyMiniLabel, GUILayout.Width(_previewSize));
            
            EditorGUILayout.EndVertical();
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(5);
            
            EditorGUILayout.BeginHorizontal("box");
            
            EditorGUILayout.LabelField($"Selected: {_selectedTextures.Count}", GUILayout.Width(100));
            
            // Target atlas selection
            EditorGUILayout.LabelField("Target Atlas:", GUILayout.Width(80));
            
            if (GUILayout.Button(_targetAtlasName, EditorStyles.popup, GUILayout.Width(120)))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("[Default]"), _targetAtlasName == "[Default]", () => SelectAtlas("[Default]", null));
                
                // Add named atlases from AtlasPacker via reflection
                var atlases = GetAllAtlases();
                foreach (var kvp in atlases)
                {
                    var name = kvp.Key;
                    var atlas = kvp.Value;
                    menu.AddItem(new GUIContent(name), _targetAtlasName == name, () => SelectAtlas(name, atlas));
                }
                
                menu.ShowAsContext();
            }
            
            GUILayout.FlexibleSpace();
            
            // Actions
            GUI.enabled = _selectedTextures.Count > 0;
            
            if (GUILayout.Button("Clear Selection", GUILayout.Width(100)))
            {
                _selectedTextures.Clear();
                ApplyFilter();
            }
            
            GUI.enabled = _selectedTextures.Count > 0 && Application.isPlaying;
            
            if (GUILayout.Button($"Add {_selectedTextures.Count} to Atlas", GUILayout.Width(130)))
            {
                AddSelectedToAtlas();
            }
            
            GUI.enabled = true;
            
            EditorGUILayout.EndHorizontal();
            
            if (!Application.isPlaying && _selectedTextures.Count > 0)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to add textures to the atlas.", MessageType.Info);
            }
        }

        private void SelectAtlas(string name, RuntimeAtlas atlas)
        {
            _targetAtlasName = name;
            _targetAtlas = atlas;
        }

        private Dictionary<string, RuntimeAtlas> GetAllAtlases()
        {
            var result = new Dictionary<string, RuntimeAtlas>();
            
            try
            {
                var type = typeof(AtlasPacker);
                var namedField = type.GetField("_namedAtlases", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                
                if (namedField != null)
                {
                    var namedAtlases = namedField.GetValue(null) as Dictionary<string, RuntimeAtlas>;
                    if (namedAtlases != null)
                    {
                        foreach (var kvp in namedAtlases)
                        {
                            result[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch { }
            
            return result;
        }

        private void AddSelectedToAtlas()
        {
            if (_selectedTextures.Count == 0) return;
            
            RuntimeAtlas targetAtlas = _targetAtlas ?? AtlasPacker.Default;
            
            int successCount = 0;
            var errors = new List<string>();
            
            foreach (var texture in _selectedTextures)
            {
                try
                {
                    // Make texture readable temporarily
                    var path = AssetDatabase.GetAssetPath(texture);
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    bool wasReadable = importer?.isReadable ?? true;
                    
                    if (!wasReadable && importer != null)
                    {
                        importer.isReadable = true;
                        importer.SaveAndReimport();
                    }
                    
                    var (result, entry) = targetAtlas.Add(texture);
                    if (result == AddResult.Success && entry != null)
                    {
                        successCount++;
                    }
                    else
                    {
                        Debug.LogWarning($"Failed to add texture '{texture.name}': {result}");
                    }
                    
                    if (!wasReadable && importer != null)
                    {
                        importer.isReadable = false;
                        importer.SaveAndReimport();
                    }
                }
                catch (Exception e)
                {
                    errors.Add($"{texture.name}: {e.Message}");
                }
            }
            
            if (errors.Count > 0)
            {
                Debug.LogWarning($"Failed to add {errors.Count} texture(s) to atlas:\n" + string.Join("\n", errors));
            }
            
            Debug.Log($"Added {successCount} texture(s) to atlas '{_targetAtlasName}'");
            
            _selectedTextures.Clear();
            ApplyFilter();
        }
    }

    /// <summary>
    /// Batch texture import and atlas creation wizard.
    /// </summary>
    public class AtlasBatchImportWizard : ScriptableWizard
    {
        [Header("Source")]
        public string folderPath = "Assets/Sprites";
        public bool includeSubfolders = true;
        public string nameFilter = "";
        
        [Header("Atlas Settings")]
        public string atlasName = "BatchAtlas";
        public int initialSize = 1024;
        public int maxSize = 4096;
        public int padding = 2;
        public PackingAlgorithm algorithm = PackingAlgorithm.MaxRects;
        
        [Header("Options")]
        public bool makeTexturesReadable = true;
        public bool restoreReadableState = true;

        private List<Texture2D> _foundTextures = new();

        [MenuItem("Window/Runtime Atlas Packer/Batch Import Wizard")]
        public static void ShowWizard()
        {
            DisplayWizard<AtlasBatchImportWizard>("Batch Import to Atlas", "Create Atlas", "Find Textures");
        }

        private void OnWizardCreate()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Error", "Enter Play Mode to create atlases.", "OK");
                return;
            }
            
            if (_foundTextures.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "No textures found. Click 'Find Textures' first.", "OK");
                return;
            }
            
            var settings = new AtlasSettings
            {
                InitialSize = initialSize,
                MaxSize = maxSize,
                Padding = padding,
                Algorithm = algorithm,
                Format = TextureFormat.RGBA32,
                FilterMode = FilterMode.Bilinear,
                GrowthStrategy = GrowthStrategy.Double,
                GenerateMipMaps = false,
                Readable = false
            };
            
            var atlas = AtlasPacker.GetOrCreate(atlasName, settings);
            
            int successCount = 0;
            var readableStates = new Dictionary<string, bool>();
            
            EditorUtility.DisplayProgressBar("Creating Atlas", "Adding textures...", 0);
            
            try
            {
                for (int i = 0; i < _foundTextures.Count; i++)
                {
                    var texture = _foundTextures[i];
                    EditorUtility.DisplayProgressBar("Creating Atlas", $"Adding {texture.name}...", (float)i / _foundTextures.Count);
                    
                    var path = AssetDatabase.GetAssetPath(texture);
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    
                    if (makeTexturesReadable && importer != null && !importer.isReadable)
                    {
                        readableStates[path] = false;
                        importer.isReadable = true;
                        importer.SaveAndReimport();
                        
                        // Reload texture
                        texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    }
                    
                    try
                    {
                        var (result, entry) = atlas.Add(texture);
                        if (result == AddResult.Success && entry != null)
                        {
                            successCount++;
                        }
                        else
                        {
                            Debug.LogWarning($"Failed to add {texture.name}: {result}");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Failed to add {texture.name}: {e.Message}");
                    }
                }
                
                // Restore readable states
                if (restoreReadableState)
                {
                    foreach (var kvp in readableStates)
                    {
                        var importer = AssetImporter.GetAtPath(kvp.Key) as TextureImporter;
                        if (importer != null)
                        {
                            importer.isReadable = kvp.Value;
                            importer.SaveAndReimport();
                        }
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            
            Debug.Log($"Created atlas '{atlasName}' with {successCount} textures. Size: {atlas.Width}x{atlas.Height}, Fill: {atlas.FillRatio:P1}");
            
            // Open debug window
            AtlasDebugWindow.ShowWindow();
        }

        private void OnWizardOtherButton()
        {
            FindTextures();
        }

        private void FindTextures()
        {
            _foundTextures.Clear();
            
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                EditorUtility.DisplayDialog("Error", $"Folder not found: {folderPath}", "OK");
                return;
            }
            
            var searchOption = includeSubfolders ? "t:Texture2D" : "t:Texture2D";
            var guids = AssetDatabase.FindAssets(searchOption, new[] { folderPath });
            
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                
                // Check subfolder constraint
                if (!includeSubfolders && System.IO.Path.GetDirectoryName(path).Replace("\\", "/") != folderPath)
                    continue;
                
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null) continue;
                
                // Apply name filter
                if (!string.IsNullOrEmpty(nameFilter) && !texture.name.ToLower().Contains(nameFilter.ToLower()))
                    continue;
                
                _foundTextures.Add(texture);
            }
            
            helpString = $"Found {_foundTextures.Count} texture(s) in {folderPath}";
            
            // Estimate atlas size
            if (_foundTextures.Count > 0)
            {
                int recommendedSize = AtlasBatchProcessor.CalculateMinimumSize(_foundTextures.ToArray(), padding, maxSize);
                helpString += $"\nRecommended atlas size: {recommendedSize}x{recommendedSize}";
            }
        }

        protected override bool DrawWizardGUI()
        {
            var changed = base.DrawWizardGUI();
            
            EditorGUILayout.Space(10);
            
            if (_foundTextures.Count > 0)
            {
                EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
                EditorGUILayout.BeginVertical("box");
                
                EditorGUILayout.LabelField($"Textures: {_foundTextures.Count}");
                
                // Show first few textures
                int showCount = Mathf.Min(5, _foundTextures.Count);
                for (int i = 0; i < showCount; i++)
                {
                    EditorGUILayout.LabelField($"  • {_foundTextures[i].name} ({_foundTextures[i].width}x{_foundTextures[i].height})");
                }
                
                if (_foundTextures.Count > showCount)
                {
                    EditorGUILayout.LabelField($"  ... and {_foundTextures.Count - showCount} more");
                }
                
                EditorGUILayout.EndVertical();
            }
            
            return changed;
        }
    }
}
