using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;

namespace RuntimeAtlasPacker.Editor
{
    /// <summary>
    /// Main editor window for debugging and managing runtime atlases.
    /// </summary>
    public class AtlasDebugWindow : EditorWindow
    {
        private Vector2 _scrollPosition;
        private Vector2 _atlasListScroll;
        private Vector2 _entryListScroll;
        private Vector2 _rendererListScroll;
        
        private RuntimeAtlas _selectedAtlas;
        private AtlasEntry _selectedEntry;
        private string _selectedAtlasName;
        
        private int _selectedTab;
        private readonly string[] _tabNames = { "Atlases", "Renderers", "Statistics", "Tools" };
        
        private Texture2D _draggedTexture;
        private bool _showAtlasPreview = true;
        private float _previewZoom = 1f;
        private Vector2 _previewScroll;
        
        private SearchField _searchField;
        private string _searchString = "";
        
        private bool _autoRefresh = true;
        private double _lastRefreshTime;
        private const double RefreshInterval = 0.5;
        
        // Cached data
        private List<AtlasInfo> _atlasInfoCache = new();
        private List<RendererInfo> _rendererInfoCache = new();
        private GlobalStats _globalStats;
        
        [MenuItem("Window/Runtime Atlas Packer/Debug Window")]
        public static void ShowWindow()
        {
            var window = GetWindow<AtlasDebugWindow>();
            window.titleContent = new GUIContent("Atlas Debug", EditorGUIUtility.IconContent("d_PreTextureMipMapHigh").image);
            window.minSize = new Vector2(600, 400);
            window.Show();
        }

        private void OnEnable()
        {
            _searchField = new SearchField();
            RefreshData();
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode || state == PlayModeStateChange.EnteredEditMode)
            {
                RefreshData();
            }
        }

        private void OnEditorUpdate()
        {
            if (_autoRefresh && EditorApplication.timeSinceStartup - _lastRefreshTime > RefreshInterval)
            {
                RefreshData();
                Repaint();
            }
        }

        private void RefreshData()
        {
            _lastRefreshTime = EditorApplication.timeSinceStartup;
            
            _atlasInfoCache.Clear();
            _rendererInfoCache.Clear();
            
            // Get all atlases via reflection (accessing private static fields)
            var atlases = GetAllAtlases();
            foreach (var kvp in atlases)
            {
                var info = new AtlasInfo
                {
                    Name = kvp.Key,
                    Atlas = kvp.Value,
                    EntryCount = kvp.Value.EntryCount,
                    Size = new Vector2Int(kvp.Value.Width, kvp.Value.Height),
                    FillRatio = kvp.Value.FillRatio,
                    MemoryUsage = CalculateMemoryUsage(kvp.Value)
                };
                _atlasInfoCache.Add(info);
            }
            
            // Get all renderers
            RefreshRendererCache();
            
            // Calculate global stats
            CalculateGlobalStats();
        }

        private Dictionary<string, RuntimeAtlas> GetAllAtlases()
        {
            var result = new Dictionary<string, RuntimeAtlas>();
            
            try
            {
                var type = typeof(AtlasPacker);
                
                // Get default atlas
                var defaultField = type.GetField("_defaultAtlas", BindingFlags.NonPublic | BindingFlags.Static);
                if (defaultField != null)
                {
                    var defaultAtlas = defaultField.GetValue(null) as RuntimeAtlas;
                    if (defaultAtlas != null)
                    {
                        result["[Default]"] = defaultAtlas;
                    }
                }
                
                // Get named atlases
                var namedField = type.GetField("_namedAtlases", BindingFlags.NonPublic | BindingFlags.Static);
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
            catch (Exception e)
            {
                Debug.LogWarning($"AtlasDebugWindow: Failed to get atlases via reflection: {e.Message}");
            }
            
            return result;
        }

        private void RefreshRendererCache()
        {
            _rendererInfoCache.Clear();
            
            // Find all AtlasSpriteRenderer components
            var spriteRenderers = FindObjectsByType<AtlasSpriteRenderer>(FindObjectsSortMode.None);
            foreach (var r in spriteRenderers)
            {
                _rendererInfoCache.Add(new RendererInfo
                {
                    GameObject = r.gameObject,
                    ComponentType = "AtlasSpriteRenderer",
                    Component = r,
                    HasEntry = r.HasEntry,
                    AtlasName = r.Atlas != null ? GetAtlasName(r.Atlas) : "None",
                    EntryId = r.Entry?.Id ?? -1
                });
            }
            
            // Find all AtlasImage components
            var images = FindObjectsByType<AtlasImage>(FindObjectsSortMode.None);
            foreach (var img in images)
            {
                _rendererInfoCache.Add(new RendererInfo
                {
                    GameObject = img.gameObject,
                    ComponentType = "AtlasImage",
                    Component = img,
                    HasEntry = img.HasEntry,
                    AtlasName = img.Atlas != null ? GetAtlasName(img.Atlas) : "None",
                    EntryId = img.Entry?.Id ?? -1
                });
            }
            
            // Find all AtlasRawImage components
            var rawImages = FindObjectsByType<AtlasRawImage>(FindObjectsSortMode.None);
            foreach (var raw in rawImages)
            {
                _rendererInfoCache.Add(new RendererInfo
                {
                    GameObject = raw.gameObject,
                    ComponentType = "AtlasRawImage",
                    Component = raw,
                    HasEntry = raw.HasEntry,
                    AtlasName = raw.Atlas != null ? GetAtlasName(raw.Atlas) : "None",
                    EntryId = raw.Entry?.Id ?? -1
                });
            }
            
            // Find legacy components
            var atlasSprites = FindObjectsByType<AtlasSprite>(FindObjectsSortMode.None);
            foreach (var s in atlasSprites)
            {
                _rendererInfoCache.Add(new RendererInfo
                {
                    GameObject = s.gameObject,
                    ComponentType = "AtlasSprite (Legacy)",
                    Component = s,
                    HasEntry = s.IsValid,
                    AtlasName = s.Entry?.Atlas != null ? GetAtlasName(s.Entry.Atlas) : "None",
                    EntryId = s.Entry?.Id ?? -1
                });
            }
            
            var atlasMaterials = FindObjectsByType<AtlasMaterial>(FindObjectsSortMode.None);
            foreach (var m in atlasMaterials)
            {
                _rendererInfoCache.Add(new RendererInfo
                {
                    GameObject = m.gameObject,
                    ComponentType = "AtlasMaterial (Legacy)",
                    Component = m,
                    HasEntry = m.Entry != null && m.Entry.IsValid,
                    AtlasName = m.Entry?.Atlas != null ? GetAtlasName(m.Entry.Atlas) : "None",
                    EntryId = m.Entry?.Id ?? -1
                });
            }
        }

        private string GetAtlasName(RuntimeAtlas atlas)
        {
            foreach (var info in _atlasInfoCache)
            {
                if (info.Atlas == atlas)
                    return info.Name;
            }
            return "Unknown";
        }

        private long CalculateMemoryUsage(RuntimeAtlas atlas)
        {
            if (atlas?.Texture == null) return 0;
            
            var tex = atlas.Texture;
            int bpp = 4; // Assume RGBA32
            
            switch (atlas.Settings.Format)
            {
                case TextureFormat.RGB24:
                    bpp = 3;
                    break;
                case TextureFormat.RGBA32:
                case TextureFormat.ARGB32:
                    bpp = 4;
                    break;
                case TextureFormat.RGB565:
                case TextureFormat.RGBA4444:
                    bpp = 2;
                    break;
            }
            
            long size = (long)tex.width * tex.height * bpp;
            
            if (atlas.Settings.GenerateMipMaps)
            {
                size = (long)(size * 1.33f); // Mipmaps add ~33%
            }
            
            return size;
        }

        private void CalculateGlobalStats()
        {
            _globalStats = new GlobalStats
            {
                TotalAtlases = _atlasInfoCache.Count,
                TotalEntries = _atlasInfoCache.Sum(a => a.EntryCount),
                TotalRenderers = _rendererInfoCache.Count,
                ActiveRenderers = _rendererInfoCache.Count(r => r.HasEntry),
                TotalMemoryBytes = _atlasInfoCache.Sum(a => a.MemoryUsage),
                AverageFillRatio = _atlasInfoCache.Count > 0 
                    ? _atlasInfoCache.Average(a => a.FillRatio) 
                    : 0f
            };
        }

        private void OnGUI()
        {
            DrawToolbar();
            
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabNames, GUILayout.Height(25));
            
            EditorGUILayout.Space(5);
            
            switch (_selectedTab)
            {
                case 0:
                    DrawAtlasesTab();
                    break;
                case 1:
                    DrawRenderersTab();
                    break;
                case 2:
                    DrawStatisticsTab();
                    break;
                case 3:
                    DrawToolsTab();
                    break;
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                RefreshData();
            }
            
            _autoRefresh = GUILayout.Toggle(_autoRefresh, "Auto", EditorStyles.toolbarButton, GUILayout.Width(45));
            
            GUILayout.FlexibleSpace();
            
            _searchString = _searchField.OnToolbarGUI(_searchString, GUILayout.Width(200));
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAtlasesTab()
        {
            EditorGUILayout.BeginHorizontal();
            
            // Left panel - Atlas list
            EditorGUILayout.BeginVertical(GUILayout.Width(250));
            DrawAtlasList();
            EditorGUILayout.EndVertical();
            
            // Splitter
            GUILayout.Box("", GUILayout.Width(2), GUILayout.ExpandHeight(true));
            
            // Right panel - Atlas details
            EditorGUILayout.BeginVertical();
            if (_selectedAtlas != null)
            {
                DrawAtlasDetails();
            }
            else
            {
                EditorGUILayout.HelpBox("Select an atlas from the list to view details.", MessageType.Info);
            }
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAtlasList()
        {
            EditorGUILayout.LabelField("Atlases", EditorStyles.boldLabel);
            
            if (_atlasInfoCache.Count == 0)
            {
                EditorGUILayout.HelpBox("No atlases found. Atlases are created at runtime.", MessageType.Info);
                return;
            }
            
            _atlasListScroll = EditorGUILayout.BeginScrollView(_atlasListScroll);
            
            foreach (var info in _atlasInfoCache)
            {
                if (!string.IsNullOrEmpty(_searchString) && 
                    !info.Name.ToLower().Contains(_searchString.ToLower()))
                    continue;
                
                bool isSelected = _selectedAtlas == info.Atlas;
                
                EditorGUILayout.BeginHorizontal(isSelected ? "SelectionRect" : "box");
                
                // Status indicator
                var statusColor = info.FillRatio > 0.9f ? Color.red : 
                                  info.FillRatio > 0.7f ? Color.yellow : Color.green;
                var oldColor = GUI.color;
                GUI.color = statusColor;
                GUILayout.Label("●", GUILayout.Width(15));
                GUI.color = oldColor;
                
                EditorGUILayout.BeginVertical();
                
                if (GUILayout.Button(info.Name, EditorStyles.label))
                {
                    _selectedAtlas = info.Atlas;
                    _selectedAtlasName = info.Name;
                    _selectedEntry = null;
                }
                
                EditorGUILayout.LabelField($"{info.Size.x}x{info.Size.y} | {info.EntryCount} entries | {info.FillRatio:P0}", 
                    EditorStyles.miniLabel);
                
                EditorGUILayout.EndVertical();
                
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.EndScrollView();
        }

        private void DrawAtlasDetails()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Atlas: {_selectedAtlasName}", EditorStyles.boldLabel);
            
            if (GUILayout.Button("Export PNG", GUILayout.Width(80)))
            {
                ExportAtlasToPNG(_selectedAtlas, _selectedAtlasName);
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);
            
            // Info panel
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Info", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Size: {_selectedAtlas.Width}x{_selectedAtlas.Height}");
            EditorGUILayout.LabelField($"Entries: {_selectedAtlas.EntryCount}");
            EditorGUILayout.LabelField($"Fill Ratio: {_selectedAtlas.FillRatio:P1}");
            EditorGUILayout.LabelField($"Memory: {FormatBytes(CalculateMemoryUsage(_selectedAtlas))}");
            EditorGUILayout.LabelField($"Version: {_selectedAtlas.Version}");
            EditorGUILayout.LabelField($"Format: {_selectedAtlas.Settings.Format}");
            EditorGUILayout.LabelField($"Algorithm: {_selectedAtlas.Settings.Algorithm}");
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(5);
            
            // Preview toggle
            _showAtlasPreview = EditorGUILayout.Foldout(_showAtlasPreview, "Atlas Preview", true);
            
            if (_showAtlasPreview && _selectedAtlas.Texture != null)
            {
                DrawAtlasPreview();
            }
            
            EditorGUILayout.Space(5);
            
            // Entry list
            EditorGUILayout.LabelField("Entries", EditorStyles.boldLabel);
            DrawEntryList();
            
            EditorGUILayout.Space(5);
            
            // Actions
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("Repack Atlas"))
            {
                _selectedAtlas.Repack();
                RefreshData();
            }
            
            GUI.enabled = _selectedEntry != null;
            if (GUILayout.Button("Remove Selected Entry"))
            {
                if (_selectedEntry != null && EditorUtility.DisplayDialog("Remove Entry",
                    $"Remove entry {_selectedEntry.Id} from atlas?", "Remove", "Cancel"))
                {
                    _selectedAtlas.Remove(_selectedEntry);
                    _selectedEntry = null;
                    RefreshData();
                }
            }
            GUI.enabled = true;
            
            EditorGUILayout.EndHorizontal();
            
            // Drag and drop area
            DrawTextureDropArea();
        }

        private void DrawAtlasPreview()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Zoom:", GUILayout.Width(40));
            _previewZoom = EditorGUILayout.Slider(_previewZoom, 0.1f, 2f, GUILayout.Width(150));
            if (GUILayout.Button("1:1", GUILayout.Width(35)))
            {
                _previewZoom = 1f;
            }
            EditorGUILayout.EndHorizontal();
            
            float previewSize = Mathf.Min(300, position.width - 300) * _previewZoom;
            
            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, 
                GUILayout.Height(Mathf.Min(previewSize + 20, 320)));
            
            var rect = GUILayoutUtility.GetRect(previewSize, previewSize);
            
            if (_selectedAtlas.Texture != null)
            {
                EditorGUI.DrawPreviewTexture(rect, _selectedAtlas.Texture, null, ScaleMode.ScaleToFit);
                
                // Draw entry highlight
                if (_selectedEntry != null && _selectedEntry.IsValid)
                {
                    var uvRect = _selectedEntry.UV;
                    var highlightRect = new Rect(
                        rect.x + uvRect.x * rect.width,
                        rect.y + (1 - uvRect.y - uvRect.height) * rect.height,
                        uvRect.width * rect.width,
                        uvRect.height * rect.height
                    );
                    
                    Handles.DrawSolidRectangleWithOutline(highlightRect, 
                        new Color(1, 1, 0, 0.2f), Color.yellow);
                }
            }
            
            EditorGUILayout.EndScrollView();
        }

        private void DrawEntryList()
        {
            _entryListScroll = EditorGUILayout.BeginScrollView(_entryListScroll, GUILayout.Height(150));
            
            var entries = _selectedAtlas.GetAllEntries().ToList();
            
            foreach (var entry in entries)
            {
                if (!string.IsNullOrEmpty(_searchString) && 
                    !entry.Id.ToString().Contains(_searchString))
                    continue;
                
                bool isSelected = _selectedEntry == entry;
                
                EditorGUILayout.BeginHorizontal(isSelected ? "SelectionRect" : "box");
                
                if (GUILayout.Button($"Entry {entry.Id}", EditorStyles.label, GUILayout.Width(80)))
                {
                    _selectedEntry = entry;
                }
                
                EditorGUILayout.LabelField($"{entry.Width}x{entry.Height}", GUILayout.Width(70));
                EditorGUILayout.LabelField($"UV: ({entry.UV.x:F2}, {entry.UV.y:F2})", GUILayout.Width(100));
                
                // Find renderers using this entry
                int rendererCount = _rendererInfoCache.Count(r => r.EntryId == entry.Id);
                EditorGUILayout.LabelField($"{rendererCount} renderer(s)", GUILayout.Width(80));
                
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.EndScrollView();
        }

        private void DrawTextureDropArea()
        {
            EditorGUILayout.Space(10);
            
            var dropArea = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Drop Texture Here to Add to Atlas", EditorStyles.helpBox);
            
            var evt = Event.current;
            switch (evt.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (!dropArea.Contains(evt.mousePosition))
                        break;
                    
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    
                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        
                        foreach (var obj in DragAndDrop.objectReferences)
                        {
                            if (obj is Texture2D texture)
                            {
                                try
                                {
                                    // Make texture readable temporarily if needed
                                    var path = AssetDatabase.GetAssetPath(texture);
                                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                                    bool wasReadable = importer?.isReadable ?? true;
                                    
                                    if (!wasReadable && importer != null)
                                    {
                                        importer.isReadable = true;
                                        importer.SaveAndReimport();
                                    }
                                    
                                    var entry = _selectedAtlas.Add(texture);
                                    Debug.Log($"Added texture to atlas: {texture.name} (Entry ID: {entry.Id})");
                                    
                                    if (!wasReadable && importer != null)
                                    {
                                        importer.isReadable = false;
                                        importer.SaveAndReimport();
                                    }
                                    
                                    RefreshData();
                                }
                                catch (Exception e)
                                {
                                    Debug.LogError($"Failed to add texture: {e.Message}");
                                }
                            }
                        }
                    }
                    evt.Use();
                    break;
            }
        }

        private void DrawRenderersTab()
        {
            EditorGUILayout.LabelField($"Atlas Renderers ({_rendererInfoCache.Count})", EditorStyles.boldLabel);
            
            if (_rendererInfoCache.Count == 0)
            {
                EditorGUILayout.HelpBox("No atlas renderers found in the scene.", MessageType.Info);
                return;
            }
            
            // Filter options
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Filter:", GUILayout.Width(40));
            
            if (GUILayout.Toggle(_searchString == "", "All", EditorStyles.miniButtonLeft))
                _searchString = "";
            if (GUILayout.Toggle(_searchString == "active", "Active", EditorStyles.miniButtonMid))
                _searchString = "active";
            if (GUILayout.Toggle(_searchString == "inactive", "Inactive", EditorStyles.miniButtonRight))
                _searchString = "inactive";
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);
            
            // Header
            EditorGUILayout.BeginHorizontal("box");
            EditorGUILayout.LabelField("GameObject", EditorStyles.boldLabel, GUILayout.Width(150));
            EditorGUILayout.LabelField("Component", EditorStyles.boldLabel, GUILayout.Width(150));
            EditorGUILayout.LabelField("Atlas", EditorStyles.boldLabel, GUILayout.Width(100));
            EditorGUILayout.LabelField("Entry ID", EditorStyles.boldLabel, GUILayout.Width(60));
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();
            
            _rendererListScroll = EditorGUILayout.BeginScrollView(_rendererListScroll);
            
            foreach (var info in _rendererInfoCache)
            {
                // Apply filter
                if (_searchString == "active" && !info.HasEntry) continue;
                if (_searchString == "inactive" && info.HasEntry) continue;
                
                EditorGUILayout.BeginHorizontal("box");
                
                // GameObject name (clickable)
                if (GUILayout.Button(info.GameObject.name, EditorStyles.label, GUILayout.Width(150)))
                {
                    Selection.activeGameObject = info.GameObject;
                    EditorGUIUtility.PingObject(info.GameObject);
                }
                
                EditorGUILayout.LabelField(info.ComponentType, GUILayout.Width(150));
                EditorGUILayout.LabelField(info.AtlasName, GUILayout.Width(100));
                EditorGUILayout.LabelField(info.EntryId >= 0 ? info.EntryId.ToString() : "-", GUILayout.Width(60));
                
                // Status indicator
                var statusColor = info.HasEntry ? Color.green : Color.gray;
                var oldColor = GUI.color;
                GUI.color = statusColor;
                EditorGUILayout.LabelField(info.HasEntry ? "●" : "○", GUILayout.Width(60));
                GUI.color = oldColor;
                
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.EndScrollView();
        }

        private void DrawStatisticsTab()
        {
            EditorGUILayout.LabelField("Global Statistics", EditorStyles.boldLabel);
            
            EditorGUILayout.Space(10);
            
            // Overview cards
            EditorGUILayout.BeginHorizontal();
            DrawStatCard("Atlases", _globalStats.TotalAtlases.ToString(), EditorGUIUtility.IconContent("d_PreTextureMipMapHigh"));
            DrawStatCard("Entries", _globalStats.TotalEntries.ToString(), EditorGUIUtility.IconContent("d_RectTool"));
            DrawStatCard("Renderers", $"{_globalStats.ActiveRenderers}/{_globalStats.TotalRenderers}", EditorGUIUtility.IconContent("d_SpriteRenderer Icon"));
            DrawStatCard("Memory", FormatBytes(_globalStats.TotalMemoryBytes), EditorGUIUtility.IconContent("d_MemoryProfiler"));
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(20);
            
            // Per-atlas breakdown
            EditorGUILayout.LabelField("Per-Atlas Breakdown", EditorStyles.boldLabel);
            
            if (_atlasInfoCache.Count == 0)
            {
                EditorGUILayout.HelpBox("No atlases to display.", MessageType.Info);
                return;
            }
            
            // Header
            EditorGUILayout.BeginHorizontal("box");
            EditorGUILayout.LabelField("Atlas", EditorStyles.boldLabel, GUILayout.Width(120));
            EditorGUILayout.LabelField("Size", EditorStyles.boldLabel, GUILayout.Width(100));
            EditorGUILayout.LabelField("Entries", EditorStyles.boldLabel, GUILayout.Width(60));
            EditorGUILayout.LabelField("Fill", EditorStyles.boldLabel, GUILayout.Width(60));
            EditorGUILayout.LabelField("Memory", EditorStyles.boldLabel, GUILayout.Width(80));
            EditorGUILayout.LabelField("Fill Ratio", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
            
            foreach (var info in _atlasInfoCache)
            {
                EditorGUILayout.BeginHorizontal("box");
                
                EditorGUILayout.LabelField(info.Name, GUILayout.Width(120));
                EditorGUILayout.LabelField($"{info.Size.x}x{info.Size.y}", GUILayout.Width(100));
                EditorGUILayout.LabelField(info.EntryCount.ToString(), GUILayout.Width(60));
                EditorGUILayout.LabelField($"{info.FillRatio:P0}", GUILayout.Width(60));
                EditorGUILayout.LabelField(FormatBytes(info.MemoryUsage), GUILayout.Width(80));
                
                // Progress bar for fill ratio
                var rect = GUILayoutUtility.GetRect(100, 18, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(rect, info.FillRatio, "");
                
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.Space(20);
            
            // Renderer type breakdown
            EditorGUILayout.LabelField("Renderer Types", EditorStyles.boldLabel);
            
            var typeCounts = _rendererInfoCache
                .GroupBy(r => r.ComponentType)
                .Select(g => new { Type = g.Key, Count = g.Count(), Active = g.Count(r => r.HasEntry) })
                .ToList();
            
            foreach (var tc in typeCounts)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(tc.Type, GUILayout.Width(200));
                EditorGUILayout.LabelField($"{tc.Active}/{tc.Count} active", GUILayout.Width(100));
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawStatCard(string label, string value, GUIContent icon)
        {
            EditorGUILayout.BeginVertical("box", GUILayout.Width(120), GUILayout.Height(60));
            
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
            
            EditorGUILayout.EndVertical();
        }

        private void DrawToolsTab()
        {
            EditorGUILayout.LabelField("Atlas Tools", EditorStyles.boldLabel);
            
            EditorGUILayout.Space(10);
            
            // Create new atlas
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Create Named Atlas", EditorStyles.boldLabel);
            
            _newAtlasName = EditorGUILayout.TextField("Name:", _newAtlasName);
            _newAtlasSize = EditorGUILayout.IntPopup("Initial Size:", _newAtlasSize, 
                new[] { "256", "512", "1024", "2048", "4096" }, 
                new[] { 256, 512, 1024, 2048, 4096 });
            
            if (GUILayout.Button("Create Atlas") && !string.IsNullOrEmpty(_newAtlasName))
            {
                var settings = AtlasSettings.Default;
                settings.InitialSize = _newAtlasSize;
                AtlasPacker.GetOrCreate(_newAtlasName, settings);
                RefreshData();
                Debug.Log($"Created atlas: {_newAtlasName}");
            }
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(10);
            
            // Bulk operations
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Bulk Operations", EditorStyles.boldLabel);
            
            if (GUILayout.Button("Repack All Atlases"))
            {
                foreach (var info in _atlasInfoCache)
                {
                    info.Atlas.Repack();
                }
                RefreshData();
                Debug.Log("Repacked all atlases");
            }
            
            EditorGUILayout.Space(5);
            
            if (GUILayout.Button("Export All Atlases"))
            {
                var folder = EditorUtility.SaveFolderPanel("Export Atlases", "", "");
                if (!string.IsNullOrEmpty(folder))
                {
                    foreach (var info in _atlasInfoCache)
                    {
                        var path = $"{folder}/{info.Name.Replace("[", "").Replace("]", "")}_atlas.png";
                        ExportAtlasToPNG(info.Atlas, info.Name, path);
                    }
                    Debug.Log($"Exported {_atlasInfoCache.Count} atlases to {folder}");
                }
            }
            
            EditorGUILayout.Space(5);
            
            GUI.color = Color.red;
            if (GUILayout.Button("Dispose All Atlases") && 
                EditorUtility.DisplayDialog("Dispose All", "This will dispose all runtime atlases. Are you sure?", "Yes", "No"))
            {
                AtlasPacker.DisposeAll();
                RefreshData();
                _selectedAtlas = null;
                Debug.Log("Disposed all atlases");
            }
            GUI.color = Color.white;
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(10);
            
            // Scene tools
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Scene Tools", EditorStyles.boldLabel);
            
            if (GUILayout.Button("Select All Atlas Renderers"))
            {
                var objects = _rendererInfoCache.Select(r => r.GameObject).ToArray();
                Selection.objects = objects;
            }
            
            if (GUILayout.Button("Select Active Renderers Only"))
            {
                var objects = _rendererInfoCache.Where(r => r.HasEntry).Select(r => r.GameObject).ToArray();
                Selection.objects = objects;
            }
            
            if (GUILayout.Button("Select Inactive Renderers Only"))
            {
                var objects = _rendererInfoCache.Where(r => !r.HasEntry).Select(r => r.GameObject).ToArray();
                Selection.objects = objects;
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private string _newAtlasName = "NewAtlas";
        private int _newAtlasSize = 1024;

        private void ExportAtlasToPNG(RuntimeAtlas atlas, string name, string path = null)
        {
            if (atlas?.Texture == null) return;
            
            if (string.IsNullOrEmpty(path))
            {
                path = EditorUtility.SaveFilePanel("Export Atlas", "", 
                    $"{name.Replace("[", "").Replace("]", "")}_atlas", "png");
            }
            
            if (string.IsNullOrEmpty(path)) return;
            
            // Create readable copy
            var rt = RenderTexture.GetTemporary(atlas.Width, atlas.Height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(atlas.Texture, rt);
            
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            
            var tex = new Texture2D(atlas.Width, atlas.Height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, atlas.Width, atlas.Height), 0, 0);
            tex.Apply();
            
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            
            var bytes = tex.EncodeToPNG();
            System.IO.File.WriteAllBytes(path, bytes);
            
            DestroyImmediate(tex);
            
            Debug.Log($"Exported atlas to: {path}");
            AssetDatabase.Refresh();
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            
            return $"{size:0.##} {sizes[order]}";
        }

        private struct AtlasInfo
        {
            public string Name;
            public RuntimeAtlas Atlas;
            public int EntryCount;
            public Vector2Int Size;
            public float FillRatio;
            public long MemoryUsage;
        }

        private struct RendererInfo
        {
            public GameObject GameObject;
            public string ComponentType;
            public Component Component;
            public bool HasEntry;
            public string AtlasName;
            public int EntryId;
        }

        private struct GlobalStats
        {
            public int TotalAtlases;
            public int TotalEntries;
            public int TotalRenderers;
            public int ActiveRenderers;
            public long TotalMemoryBytes;
            public float AverageFillRatio;
        }
    }
}
