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
        private readonly string[] _tabNames = { "Atlases", "Renderers", "Statistics", "Tools", "Textures" };
        private Vector2 _texturesTabScroll;
        
        private Texture2D _draggedTexture;
        private bool _showAtlasPreview = true;
        private float _previewZoom = 1f;
        private Vector2 _previewScroll;
        private int _selectedPageIndex = 0; // For multi-page preview
        
        private SearchField _searchField;
        private string _searchString = "";
        
        private bool _autoRefresh = true;
        private double _lastRefreshTime;
        private const double RefreshInterval = 0.5;
        
        // Cached data
        private List<AtlasInfo> _atlasInfoCache = new();
        private List<RendererInfo> _rendererInfoCache = new();
        private GlobalStats _globalStats;
        
        // Tools tab fields
        private string _newAtlasName = "";
        private int _newAtlasSize = 1024;
        
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
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            RuntimeAtlasProfiler.OnOperationLogged += OnAtlasOperation;
            
            // Immediate refresh
            RefreshData();
            
            Debug.Log("[AtlasDebugWindow] Enabled and connected to runtime atlases");
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            RuntimeAtlasProfiler.OnOperationLogged -= OnAtlasOperation;
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                // Clear and refresh when entering play mode
                _atlasInfoCache.Clear();
                _rendererInfoCache.Clear();
                _selectedAtlas = null;
                _selectedEntry = null;
                RefreshData();
                Repaint();
                Debug.Log("[AtlasDebugWindow] Play mode entered - data refreshed");
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                // Clear cache when returning to edit mode
                _atlasInfoCache.Clear();
                _rendererInfoCache.Clear();
                _selectedAtlas = null;
                _selectedEntry = null;
                RefreshData();
                Repaint();
                Debug.Log("[AtlasDebugWindow] Edit mode entered - data cleared and refreshed");
            }
            else if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.ExitingEditMode)
            {
                // Clear references to prevent null refs
                _selectedAtlas = null;
                _selectedEntry = null;
            }
        }

        private void OnAtlasOperation(ProfileData data)
        {
            // Any atlas operation triggers refresh
            if (EditorApplication.isPlaying)
            {
                RefreshData();
                Repaint();
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
            
            // Also collect atlases from active renderers (fallback method)
            RefreshRendererCache();
            CollectAtlasesFromRenderers(atlases);
            
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
            
            // Calculate global stats
            CalculateGlobalStats();
        }
        
        private void CollectAtlasesFromRenderers(Dictionary<string, RuntimeAtlas> atlases)
        {
            // Collect unique atlases from all renderers
            var foundAtlases = new HashSet<RuntimeAtlas>();
            
            foreach (var rendererInfo in _rendererInfoCache)
            {
                RuntimeAtlas atlas = null;
                
                if (rendererInfo.Component is AtlasSpriteRenderer spriteRenderer)
                {
                    atlas = spriteRenderer.Atlas;
                }
                else if (rendererInfo.Component is AtlasImage image)
                {
                    atlas = image.Atlas;
                }
                else if (rendererInfo.Component is AtlasRawImage rawImage)
                {
                    atlas = rawImage.Atlas;
                }
                
                if (atlas != null && !foundAtlases.Contains(atlas))
                {
                    foundAtlases.Add(atlas);
                    
                    // Check if we already have this atlas
                    bool alreadyExists = atlases.Values.Any(a => a == atlas);
                    if (!alreadyExists)
                    {
                        string atlasName = GetAtlasName(atlas);
                        atlases[atlasName] = atlas;
                        Debug.Log($"[AtlasDebugWindow] Found atlas from renderer: {atlasName}");
                    }
                }
            }
        }

        private Dictionary<string, RuntimeAtlas> GetAllAtlases()
        {
            var result = new Dictionary<string, RuntimeAtlas>();
            
            if (!EditorApplication.isPlaying)
                return result;
            
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
                        // Safely check if texture exists before accessing properties that might throw
                        bool hasTexture = defaultAtlas.Texture != null;
                        Debug.Log($"[AtlasDebugWindow] Found default atlas: {(hasTexture ? defaultAtlas.Width.ToString() : "?")}x{(hasTexture ? defaultAtlas.Height.ToString() : "?")}, {defaultAtlas.EntryCount} entries, Texture={(hasTexture ? "Yes" : "No")}");
                    }
                    else
                    {
                        Debug.Log("[AtlasDebugWindow] Default atlas field found but value is null");
                    }
                }
                else
                {
                    Debug.LogWarning("[AtlasDebugWindow] Could not find _defaultAtlas field");
                }
                
                // Get named atlases
                var namedField = type.GetField("_namedAtlases", BindingFlags.NonPublic | BindingFlags.Static);
                if (namedField != null)
                {
                    var namedAtlases = namedField.GetValue(null) as Dictionary<string, RuntimeAtlas>;
                    if (namedAtlases != null)
                    {
                        Debug.Log($"[AtlasDebugWindow] Found {namedAtlases.Count} named atlases");
                        foreach (var kvp in namedAtlases)
                        {
                            if (kvp.Value != null)
                            {
                                result[kvp.Key] = kvp.Value;
                                bool hasTexture = kvp.Value.Texture != null;
                                Debug.Log($"[AtlasDebugWindow] Found named atlas '{kvp.Key}': {(hasTexture ? kvp.Value.Width.ToString() : "?")}x{(hasTexture ? kvp.Value.Height.ToString() : "?")}, {kvp.Value.EntryCount} entries");
                            }
                        }
                    }
                    else
                    {
                        Debug.Log("[AtlasDebugWindow] Named atlases field found but value is null");
                    }
                }
                else
                {
                    Debug.LogWarning("[AtlasDebugWindow] Could not find _namedAtlases field");
                }
                
                Debug.Log($"[AtlasDebugWindow] Total atlases found: {result.Count}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[AtlasDebugWindow] Failed to get atlases via reflection: {e.Message}\n{e.StackTrace}");
            }
            
            return result;
        }

        private void RefreshRendererCache()
        {
            _rendererInfoCache.Clear();
            
            if (!EditorApplication.isPlaying)
                return;
            
            // Find all AtlasSpriteRenderer components
            var spriteRenderers = FindObjectsByType<AtlasSpriteRenderer>(FindObjectsSortMode.None);
            foreach (var r in spriteRenderers)
            {
                // Skip if GameObject is destroyed
                if (r == null || r.gameObject == null)
                    continue;
                    
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
                // Skip if GameObject is destroyed
                if (img == null || img.gameObject == null)
                    continue;
                    
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
                // Skip if GameObject is destroyed
                if (raw == null || raw.gameObject == null)
                    continue;
                    
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
            if (atlas == null) return 0;
            
            int pageCount = atlas.PageCount;
            if (pageCount == 0) return 0;
            
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
            
            long totalSize = 0;
            
            // Calculate size for each page
            for (int i = 0; i < pageCount; i++)
            {
                var tex = atlas.GetTexture(i);
                if (tex != null)
                {
                    long pageSize = (long)tex.width * tex.height * bpp;
                    
                    if (atlas.Settings.GenerateMipMaps)
                    {
                        pageSize = (long)(pageSize * 1.33f); // Mipmaps add ~33%
                    }
                    
                    totalSize += pageSize;
                }
            }
            
            return totalSize;
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
                case 4:
                    DrawTexturesTab();
                    break;
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            // Status indicator
            var statusColor = EditorApplication.isPlaying ? Color.green : Color.gray;
            var oldBg = GUI.backgroundColor;
            GUI.backgroundColor = statusColor;
            GUILayout.Label(
                EditorApplication.isPlaying 
                    ? $"● PLAY ({_atlasInfoCache.Count} atlases)" 
                    : "○ EDIT MODE",
                EditorStyles.toolbarButton,
                GUILayout.Width(150)
            );
            GUI.backgroundColor = oldBg;
            
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
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.Space(50);
                EditorGUILayout.HelpBox(
                    "Atlas debugging requires Play Mode.\n\n" +
                    "Press Play to see runtime atlases.",
                    MessageType.Info
                );
                return;
            }
            
            if (_atlasInfoCache.Count == 0)
            {
                EditorGUILayout.Space(50);
                EditorGUILayout.HelpBox(
                    "No runtime atlases detected.\n\n" +
                    "Create atlases using AtlasPacker to see them here.\n" +
                    "The window auto-refreshes every 0.5 seconds.",
                    MessageType.Warning
                );
                return;
            }
            
            EditorGUILayout.BeginHorizontal();
            
            // Left panel - Atlas list
            EditorGUILayout.BeginVertical(GUILayout.Width(250));
            DrawAtlasList();
            EditorGUILayout.EndVertical();
            
            // Splitter
            GUILayout.Box("", GUILayout.Width(2), GUILayout.ExpandHeight(true));
            
            // Right panel - Atlas details (scrollable)
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (_selectedAtlas != null)
            {
                DrawAtlasDetails();
            }
            else
            {
                EditorGUILayout.HelpBox("Select an atlas from the list to view details.", MessageType.Info);
            }
            EditorGUILayout.EndScrollView();
            
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
                
                // Make entire cell clickable
                var cellStyle = new GUIStyle(isSelected ? "SelectionRect" : "box");
                cellStyle.alignment = TextAnchor.MiddleLeft;
                cellStyle.padding = new RectOffset(5, 5, 5, 5);
                
                if (GUILayout.Button("", cellStyle, GUILayout.Height(40), GUILayout.ExpandWidth(true)))
                {
                    _selectedAtlas = info.Atlas;
                    _selectedAtlasName = info.Name;
                    _selectedEntry = null;
                }
                
                // Draw content on top of button
                var lastRect = GUILayoutUtility.GetLastRect();
                
                // Status indicator
                var statusColor = info.FillRatio > 0.9f ? Color.red : 
                                  info.FillRatio > 0.7f ? Color.yellow : Color.green;
                var indicatorRect = new Rect(lastRect.x + 5, lastRect.y + lastRect.height / 2 - 5, 10, 10);
                var oldColor = GUI.color;
                GUI.color = statusColor;
                GUI.Label(indicatorRect, "●");
                GUI.color = oldColor;
                
                // Name
                var nameRect = new Rect(lastRect.x + 25, lastRect.y + 5, lastRect.width - 30, 16);
                GUI.Label(nameRect, info.Name, EditorStyles.boldLabel);
                
                // Details
                var detailsRect = new Rect(lastRect.x + 25, lastRect.y + 22, lastRect.width - 30, 14);
                GUI.Label(detailsRect, $"{info.Size.x}x{info.Size.y} | {info.EntryCount} entries | {info.FillRatio:P0}", EditorStyles.miniLabel);
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
            
            // Page info with color coding
            int pageCount = _selectedAtlas.PageCount;
            int maxPages = _selectedAtlas.Settings.MaxPageCount;
            string pageInfo = maxPages == -1 
                ? $"{pageCount} (unlimited)" 
                : $"{pageCount} / {maxPages}";
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Pages:", GUILayout.Width(100));
            
            var oldColor = GUI.color;
            if (maxPages != -1 && pageCount >= maxPages)
                GUI.color = Color.red; // At limit
            else if (maxPages != -1 && pageCount >= maxPages * 0.8f)
                GUI.color = Color.yellow; // Near limit
            else
                GUI.color = Color.green; // OK
                
            EditorGUILayout.LabelField(pageInfo);
            GUI.color = oldColor;
            EditorGUILayout.EndHorizontal();
            
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
            // Page selector for multi-page atlases
            int pageCount = _selectedAtlas.PageCount;
            if (pageCount > 1)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Page:", GUILayout.Width(40));
                
                // Ensure selected page is valid
                if (_selectedPageIndex >= pageCount)
                    _selectedPageIndex = 0;
                
                // Page navigation
                GUI.enabled = _selectedPageIndex > 0;
                if (GUILayout.Button("◄", GUILayout.Width(30)))
                {
                    _selectedPageIndex--;
                }
                GUI.enabled = true;
                
                _selectedPageIndex = EditorGUILayout.IntSlider(_selectedPageIndex, 0, pageCount - 1);
                
                GUI.enabled = _selectedPageIndex < pageCount - 1;
                if (GUILayout.Button("►", GUILayout.Width(30)))
                {
                    _selectedPageIndex++;
                }
                GUI.enabled = true;
                
                EditorGUILayout.LabelField($"of {pageCount - 1}", GUILayout.Width(50));
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                _selectedPageIndex = 0; // Reset for single-page atlases
            }
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Zoom:", GUILayout.Width(40));
            _previewZoom = EditorGUILayout.Slider(_previewZoom, 0.1f, 2f);
            if (GUILayout.Button("1:1", GUILayout.Width(35)))
            {
                _previewZoom = 1f;
            }
            EditorGUILayout.EndHorizontal();
            
            // Get the texture for the selected page
            var pageTexture = _selectedAtlas.GetTexture(_selectedPageIndex);
            if (pageTexture == null)
            {
                EditorGUILayout.HelpBox($"Page {_selectedPageIndex} texture is null.", MessageType.Warning);
                return;
            }
            
            // Calculate available width (window width - left panel - splitter - margins)
            float availableWidth = position.width - 250 - 20 - 40; // 250=list, 20=splitter+margins, 40=scrollbar buffer
            float previewSize = availableWidth * _previewZoom;
            
            // Calculate aspect ratio
            float aspect = (float)pageTexture.height / pageTexture.width;
            float previewHeight = previewSize * aspect;
            
            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, 
                GUILayout.MaxHeight(Mathf.Min(previewHeight + 20, 500)));
            
            var rect = GUILayoutUtility.GetRect(previewSize, previewHeight, GUILayout.ExpandWidth(false));
            
            EditorGUI.DrawPreviewTexture(rect, pageTexture, null, ScaleMode.ScaleToFit);
            
            // Draw entry highlights for entries on this page
            if (_selectedEntry != null && _selectedEntry.IsValid && _selectedEntry.TextureIndex == _selectedPageIndex)
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
            
            // Draw all entries on this page with semi-transparent outlines
            var entries = _selectedAtlas.GetAllEntries().Where(e => e.TextureIndex == _selectedPageIndex).ToList();
            foreach (var entry in entries)
            {
                if (entry == _selectedEntry) continue; // Already drawn above
                
                var uvRect = entry.UV;
                var entryRect = new Rect(
                    rect.x + uvRect.x * rect.width,
                    rect.y + (1 - uvRect.y - uvRect.height) * rect.height,
                    uvRect.width * rect.width,
                    uvRect.height * rect.height
                );
                
                Handles.DrawSolidRectangleWithOutline(entryRect, 
                    new Color(0, 1, 0, 0.05f), new Color(0, 1, 0, 0.3f));
            }
            
            // Show entry count for this page
            EditorGUI.LabelField(new Rect(rect.x + 5, rect.y + 5, 200, 20),
                $"Page {_selectedPageIndex}: {entries.Count} textures",
                EditorStyles.whiteBoldLabel);
            
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
                                    
                                    var (result, entry) = _selectedAtlas.Add(texture);
                                    if (result == AddResult.Success && entry != null)
                                    {
                                        Debug.Log($"Added texture to atlas: {texture.name} (Entry ID: {entry.Id})");
                                    }
                                    else
                                    {
                                        Debug.LogWarning($"Failed to add texture '{texture.name}' to atlas: {result}");
                                    }
                                    
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
                // Skip if GameObject is destroyed
                if (info.GameObject == null)
                    continue;
                
                // Apply filter
                if (_searchString == "active" && !info.HasEntry) continue;
                if (_searchString == "inactive" && info.HasEntry) continue;
                
                EditorGUILayout.BeginHorizontal("box");
                
                // GameObject name (clickable)
                string objectName = "< Destroyed >";
                try
                {
                    objectName = info.GameObject.name;
                }
                catch
                {
                    // GameObject was destroyed
                    EditorGUILayout.EndHorizontal();
                    continue;
                }
                
                if (GUILayout.Button(objectName, EditorStyles.label, GUILayout.Width(150)))
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
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Repack All Atlases"))
            {
                RepackAllAtlases();
            }
            
            if (GUILayout.Button("Clear All Atlases", GUILayout.Width(150)))
            {
                if (EditorUtility.DisplayDialog("Clear All Atlases", 
                    "This will dispose and clear all runtime atlases. This action cannot be undone.", 
                    "Clear All", "Cancel"))
                {
                    int atlasCount = AtlasPacker.GetActiveAtlasCount();
                    AtlasPacker.ClearAllAtlases();
                    RefreshData();
                    Debug.Log($"[AtlasDebugWindow] Manually cleared {atlasCount} atlases");
                }
            }
            EditorGUILayout.EndHorizontal();
            
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
        
        private void RepackAllAtlases()
        {
            Debug.Log("[AtlasDebugWindow] Starting repack of all atlases...");
            
            foreach (var info in _atlasInfoCache)
            {
                if (info.Atlas != null && info.Atlas.EntryCount > 0)
                {
                    try
                    {
                        info.Atlas.Repack();
                        Debug.Log($"[AtlasDebugWindow] Repacked atlas: {info.Name}");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[AtlasDebugWindow] Failed to repack atlas {info.Name}: {ex.Message}");
                    }
                }
            }
            
            RefreshData();
            Debug.Log("[AtlasDebugWindow] Repack complete");
        }

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

        private struct TextureEntryInfo
        {
            public int EntryId;
            public string Name;
            public Vector2Int Size;
            public RectInt PixelRect;
            public Rect UV;
            public int TexturePageIndex;
            public List<SpriteConnectionInfo> ConnectedSprites;
            public long EstimatedMemoryBytes;
        }

        private struct SpriteConnectionInfo
        {
            public GameObject GameObject;
            public string ComponentType;
            public Component Component;
            public bool IsActive;
        }

        private void DrawTexturesTab()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.Space(50);
                EditorGUILayout.HelpBox(
                    "Texture view requires Play Mode.\n\nPress Play to see texture details.",
                    MessageType.Info
                );
                return;
            }

            if (_atlasInfoCache.Count == 0)
            {
                EditorGUILayout.Space(50);
                EditorGUILayout.HelpBox(
                    "No runtime atlases found.\n\nCreate atlases using AtlasPacker to see texture details.",
                    MessageType.Warning
                );
                return;
            }

            EditorGUILayout.BeginHorizontal();
            
            // Left panel - Atlas selector
            EditorGUILayout.BeginVertical(GUILayout.Width(200));
            EditorGUILayout.LabelField("Select Atlas:", EditorStyles.boldLabel);
            
            _atlasListScroll = EditorGUILayout.BeginScrollView(_atlasListScroll);
            foreach (var info in _atlasInfoCache)
            {
                bool isSelected = _selectedAtlas == info.Atlas;
                var style = new GUIStyle(isSelected ? "SelectionRect" : "box");
                style.alignment = TextAnchor.MiddleLeft;
                style.padding = new RectOffset(5, 5, 5, 5);
                
                if (GUILayout.Button("", style, GUILayout.Height(30)))
                {
                    _selectedAtlas = info.Atlas;
                    _selectedAtlasName = info.Name;
                }
                
                var lastRect = GUILayoutUtility.GetLastRect();
                GUI.Label(new Rect(lastRect.x + 5, lastRect.y + 5, lastRect.width - 10, 20), 
                    $"{info.Name} ({info.EntryCount})", EditorStyles.label);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            
            // Splitter
            GUILayout.Box("", GUILayout.Width(2), GUILayout.ExpandHeight(true));
            
            // Right panel - Texture details
            _texturesTabScroll = EditorGUILayout.BeginScrollView(_texturesTabScroll);
            
            if (_selectedAtlas != null)
            {
                DrawAtlasTextureDetails();
            }
            else
            {
                EditorGUILayout.HelpBox("Select an atlas from the list to view texture details.", MessageType.Info);
            }
            
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAtlasTextureDetails()
        {
            // Atlas Header
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"Atlas: {_selectedAtlasName}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Size: {_selectedAtlas.Width}x{_selectedAtlas.Height}");
            
            // Page info
            int pageCount = _selectedAtlas.PageCount;
            int maxPages = _selectedAtlas.Settings.MaxPageCount;
            string pageInfo = maxPages == -1 
                ? $"Pages: {pageCount} (unlimited)" 
                : $"Pages: {pageCount} / {maxPages}";
            EditorGUILayout.LabelField(pageInfo);
            
            EditorGUILayout.LabelField($"Total Textures: {_selectedAtlas.EntryCount}");
            EditorGUILayout.LabelField($"Fill Ratio: {_selectedAtlas.FillRatio:P1}");
            EditorGUILayout.LabelField($"Memory: {FormatBytes(CalculateMemoryUsage(_selectedAtlas))}");
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(10);
            
            // Get all entries and build texture info
            var entries = _selectedAtlas.GetAllEntries().ToList();
            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox("No textures in this atlas.", MessageType.Info);
                return;
            }

            // Build texture info with sprite connections
            var textureInfos = new List<TextureEntryInfo>();
            foreach (var entry in entries)
            {
                var connectedSprites = FindConnectedSprites(entry);
                
                var textureInfo = new TextureEntryInfo
                {
                    EntryId = entry.Id,
                    Name = entry.Name ?? $"Entry_{entry.Id}",
                    Size = new Vector2Int(entry.Width, entry.Height),
                    PixelRect = entry.Rect,
                    UV = entry.UV,
                    TexturePageIndex = entry.TextureIndex,
                    ConnectedSprites = connectedSprites,
                    EstimatedMemoryBytes = entry.Width * entry.Height * 4 // RGBA32
                };
                
                textureInfos.Add(textureInfo);
            }

            // Sort by size (largest first)
            textureInfos = textureInfos.OrderByDescending(t => t.Size.x * t.Size.y).ToList();

            // Summary stats
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Texture Summary", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            
            DrawStatBox("Total Textures", textureInfos.Count.ToString(), EditorGUIUtility.IconContent("d_PreTextureMipMapHigh"));
            DrawStatBox("With Sprites", textureInfos.Count(t => t.ConnectedSprites.Count > 0).ToString(), EditorGUIUtility.IconContent("d_Prefab Icon"));
            DrawStatBox("Orphaned", textureInfos.Count(t => t.ConnectedSprites.Count == 0).ToString(), EditorGUIUtility.IconContent("d_console.warnicon.sml"));
            DrawStatBox("Total Sprites", textureInfos.Sum(t => t.ConnectedSprites.Count).ToString(), EditorGUIUtility.IconContent("d_Grid.Default"));
            
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(10);

            // Texture list with details
            EditorGUILayout.LabelField("Texture Details", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Draw each texture entry
            foreach (var textureInfo in textureInfos)
            {
                DrawTextureEntryDetail(textureInfo);
            }
        }

        private void DrawTextureEntryDetail(TextureEntryInfo textureInfo)
        {
            EditorGUILayout.BeginVertical("box");
            
            // Header with texture name and ID
            EditorGUILayout.BeginHorizontal();
            
            var iconContent = textureInfo.ConnectedSprites.Count > 0 
                ? EditorGUIUtility.IconContent("d_PreTextureMipMapHigh") 
                : EditorGUIUtility.IconContent("d_console.warnicon.sml");
            GUILayout.Label(iconContent, GUILayout.Width(20), GUILayout.Height(20));
            
            EditorGUILayout.LabelField($"{textureInfo.Name} (ID: {textureInfo.EntryId})", EditorStyles.boldLabel);
            
            // Size indicator
            var sizeColor = textureInfo.Size.x * textureInfo.Size.y > 128 * 128 ? Color.yellow : Color.green;
            var oldColor = GUI.color;
            GUI.color = sizeColor;
            GUILayout.Label($"{textureInfo.Size.x}x{textureInfo.Size.y}", EditorStyles.miniLabel, GUILayout.Width(80));
            GUI.color = oldColor;
            
            EditorGUILayout.EndHorizontal();
            
            // Texture details
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Position: ({textureInfo.PixelRect.x}, {textureInfo.PixelRect.y})", GUILayout.Width(150));
            EditorGUILayout.LabelField($"Page: {textureInfo.TexturePageIndex}", GUILayout.Width(80));
            EditorGUILayout.LabelField($"Memory: {FormatBytes(textureInfo.EstimatedMemoryBytes)}", GUILayout.Width(100));
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"UV: ({textureInfo.UV.x:F3}, {textureInfo.UV.y:F3}, {textureInfo.UV.width:F3}, {textureInfo.UV.height:F3})", GUILayout.Width(300));
            EditorGUILayout.EndHorizontal();
            
            // Connected sprites section
            if (textureInfo.ConnectedSprites.Count > 0)
            {
                EditorGUILayout.Space(5);
                var foldoutRect = EditorGUILayout.GetControlRect();
                var foldoutKey = $"texture_sprites_{textureInfo.EntryId}";
                var isExpanded = EditorPrefs.GetBool(foldoutKey, false);
                isExpanded = EditorGUI.Foldout(foldoutRect, isExpanded, 
                    $"Connected Sprites ({textureInfo.ConnectedSprites.Count})", true);
                EditorPrefs.SetBool(foldoutKey, isExpanded);
                
                if (isExpanded)
                {
                    EditorGUI.indentLevel++;
                    
                    foreach (var sprite in textureInfo.ConnectedSprites)
                    {
                        DrawSpriteConnection(sprite);
                    }
                    
                    EditorGUI.indentLevel--;
                }
            }
            else
            {
                EditorGUILayout.Space(3);
                GUI.color = new Color(1f, 0.8f, 0f);
                EditorGUILayout.LabelField("⚠ No sprites connected (orphaned texture)", EditorStyles.miniLabel);
                GUI.color = Color.white;
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);
        }

        private void DrawSpriteConnection(SpriteConnectionInfo sprite)
        {
            EditorGUILayout.BeginHorizontal("box");
            
            // Active indicator
            var statusColor = sprite.IsActive ? Color.green : Color.gray;
            var oldColor = GUI.color;
            GUI.color = statusColor;
            GUILayout.Label("●", GUILayout.Width(15));
            GUI.color = oldColor;
            
            // Component type icon
            var icon = sprite.ComponentType switch
            {
                "AtlasSpriteRenderer" => EditorGUIUtility.IconContent("d_SpriteRenderer Icon"),
                "AtlasImage" => EditorGUIUtility.IconContent("d_Image Icon"),
                "AtlasRawImage" => EditorGUIUtility.IconContent("d_RawImage Icon"),
                _ => EditorGUIUtility.IconContent("d_GameObject Icon")
            };
            GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(16));
            
            // GameObject name (clickable)
            if (GUILayout.Button(sprite.GameObject.name, EditorStyles.linkLabel))
            {
                Selection.activeGameObject = sprite.GameObject;
                EditorGUIUtility.PingObject(sprite.GameObject);
            }
            
            // Component type
            EditorGUILayout.LabelField($"({sprite.ComponentType})", EditorStyles.miniLabel, GUILayout.Width(150));
            
            // Hierarchy path
            var path = GetGameObjectPath(sprite.GameObject);
            EditorGUILayout.LabelField(path, EditorStyles.miniLabel);
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatBox(string label, string value, GUIContent icon)
        {
            EditorGUILayout.BeginVertical("box", GUILayout.Width(120), GUILayout.Height(50));
            
            EditorGUILayout.BeginHorizontal();
            if (icon != null)
            {
                GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));
            }
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
            
            EditorGUILayout.EndVertical();
        }

        private List<SpriteConnectionInfo> FindConnectedSprites(AtlasEntry entry)
        {
            var connections = new List<SpriteConnectionInfo>();
            
            foreach (var rendererInfo in _rendererInfoCache)
            {
                if (rendererInfo.EntryId == entry.Id)
                {
                    connections.Add(new SpriteConnectionInfo
                    {
                        GameObject = rendererInfo.GameObject,
                        ComponentType = rendererInfo.ComponentType,
                        Component = rendererInfo.Component,
                        IsActive = rendererInfo.HasEntry && rendererInfo.GameObject.activeInHierarchy
                    });
                }
            }
            
            return connections;
        }

        private string GetGameObjectPath(GameObject go)
        {
            if (go == null) return "";
            
            var path = go.name;
            var parent = go.transform.parent;
            
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            
            return path;
        }
    }
}
