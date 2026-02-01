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
        private readonly string[] _tabNames = { "Atlases", "Renderers", "Statistics", "Tools", "Textures", "Saved Atlases" };
        private Vector2 _texturesTabScroll;
        
        // Saved Atlases tab
        private List<SavedAtlasInfo> _savedAtlasesCache = new();
        private SavedAtlasInfo _selectedSavedAtlas;
        private Vector2 _savedAtlasListScroll;
        private Vector2 _savedAtlasDetailsScroll;
        private List<string> _playModeSavedPaths = new(); // Track atlases saved during play mode
        private int _selectedSavedAtlasPageIndex = 0; // For page browsing in saved atlases
        
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
            
            // Clean up loaded saved atlas textures
            foreach (var info in _savedAtlasesCache)
            {
                if (info.LoadedTextures != null)
                {
                    foreach (var tex in info.LoadedTextures)
                    {
                        if (tex != null)
                        {
                            DestroyImmediate(tex);
                        }
                    }
                }
            }
            _savedAtlasesCache.Clear();
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
            // Track save operations during play mode
            if (EditorApplication.isPlaying && 
                (data.OperationType == "SaveAtlas" || data.OperationType == "SaveAtlasAsync"))
            {
                // Extract file path from details
                var filePath = data.Details;
                if (!string.IsNullOrEmpty(filePath) && !_playModeSavedPaths.Contains(filePath))
                {
                    _playModeSavedPaths.Add(filePath);
                    Debug.Log($"[AtlasDebugWindow] Tracked saved atlas: {filePath}");
                }
            }
            
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
                    MemoryUsage = CalculateMemoryUsage(kvp.Value),
                    SourceFilePath = kvp.Value.SourceFilePath
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
                // Get all atlases from global registry (RuntimeAtlas instances)
                var allAtlases = RuntimeAtlas.GetAllAtlases();
                if (allAtlases != null)
                {
                    Debug.Log($"[AtlasDebugWindow] Found {allAtlases.Count} atlases in global registry");
                    foreach (var atlas in allAtlases)
                    {
                        if (atlas != null)
                        {
                            var name = atlas.DebugName ?? $"Atlas_{result.Count}";
                            result[name] = atlas;
                            bool hasTexture = atlas.Texture != null;
                            Debug.Log($"[AtlasDebugWindow] Found atlas '{name}': {(hasTexture ? atlas.Width.ToString() : "?")}x{(hasTexture ? atlas.Height.ToString() : "?")}, {atlas.EntryCount} entries");
                        }
                    }
                }
                
                // Get atlases from AtlasPacker using public API (no reflection needed)
                var managedAtlases = AtlasPacker.GetAllManagedAtlases();
                if (managedAtlases != null)
                {
                    Debug.Log($"[AtlasDebugWindow] Found {managedAtlases.Count} managed atlases from AtlasPacker");
                    foreach (var kvp in managedAtlases)
                    {
                        if (kvp.Value != null && !result.ContainsValue(kvp.Value))
                        {
                            result[kvp.Key] = kvp.Value;
                            Debug.Log($"[AtlasDebugWindow] Found managed atlas '{kvp.Key}'");
                        }
                    }
                }
                
                Debug.Log($"[AtlasDebugWindow] Total atlases found: {result.Count}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[AtlasDebugWindow] Failed to get atlases: {e.Message}\n{e.StackTrace}");
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
            if (atlas == null)
                return "None";
                
            // Check cache first
            foreach (var info in _atlasInfoCache)
            {
                if (info.Atlas == atlas)
                    return info.Name;
            }
            
            // Use DebugName if available
            if (!string.IsNullOrEmpty(atlas.DebugName))
                return atlas.DebugName;
            
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
                case 5:
                    DrawSavedAtlasesTab();
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
            
            GUILayout.FlexibleSpace();
            
            // Show in Finder/Explorer button (only if loaded from disk)
            var selectedInfo = _atlasInfoCache.FirstOrDefault(info => info.Atlas == _selectedAtlas);
            if (!string.IsNullOrEmpty(selectedInfo.SourceFilePath))
            {
                var buttonText = Application.platform == RuntimePlatform.OSXEditor ? "Show in Finder" : "Show in Explorer";
                if (GUILayout.Button(buttonText, GUILayout.Width(120)))
                {
                    ShowAtlasInFileExplorer(selectedInfo.SourceFilePath);
                }
            }
            
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
            
            // Show source file path if loaded from disk
            if (!string.IsNullOrEmpty(_selectedAtlas.SourceFilePath))
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Source:", EditorStyles.boldLabel);
                
                var directory = System.IO.Path.GetDirectoryName(_selectedAtlas.SourceFilePath);
                var fileName = System.IO.Path.GetFileNameWithoutExtension(_selectedAtlas.SourceFilePath);
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("📁 Loaded from disk", GUILayout.Width(120));
                EditorGUILayout.SelectableLabel(fileName, GUILayout.Height(16));
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(15);
                EditorGUILayout.SelectableLabel(directory, EditorStyles.miniLabel, GUILayout.Height(14));
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.Space(5);
            }
            
            // Sprite cache info
            var cacheMemory = _selectedAtlas.GetCachedSpriteMemoryUsage();
            var cacheEnabled = _selectedAtlas.Settings.EnableSpriteCache;
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Sprite Cache: {(cacheEnabled ? "Enabled" : "Disabled")}");
            if (cacheEnabled && cacheMemory > 0)
            {
                GUI.color = Color.yellow;
                EditorGUILayout.LabelField($"({FormatBytes(cacheMemory)})", EditorStyles.miniLabel);
                GUI.color = oldColor;
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.LabelField($"Version: {_selectedAtlas.Version}");
            EditorGUILayout.LabelField($"Format: {_selectedAtlas.Settings.Format}");
            EditorGUILayout.LabelField($"Algorithm: {_selectedAtlas.Settings.Algorithm}");
            
            // Show Readable and UseRenderTextures settings
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Readable:", GUILayout.Width(100));
            var readableColor = _selectedAtlas.Settings.Readable ? Color.green : new Color(1f, 0.5f, 0f); // Orange for non-readable
            GUI.color = readableColor;
            EditorGUILayout.LabelField(_selectedAtlas.Settings.Readable ? "Yes" : "No (Memory Optimized)");
            GUI.color = oldColor;
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Use RenderTextures:", GUILayout.Width(100));
            var rtColor = _selectedAtlas.Settings.UseRenderTextures ? Color.green : Color.cyan;
            GUI.color = rtColor;
            EditorGUILayout.LabelField(_selectedAtlas.Settings.UseRenderTextures ? "Yes (GPU)" : "No (CPU)");
            GUI.color = oldColor;
            EditorGUILayout.EndHorizontal();
            
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
            
            if (GUILayout.Button("Clear Sprite Cache"))
            {
                _selectedAtlas.ClearAllSpriteCaches();
                RefreshData();
                Debug.Log($"[AtlasDebug] Cleared sprite cache for atlas: {_selectedAtlas.Texture.name}");
            }
            
            GUI.enabled = _selectedEntry != null;
            if (GUILayout.Button("Remove Selected Entry"))
            {
                if (_selectedEntry != null && EditorUtility.DisplayDialog("Remove Entry",
                    $"Remove entry '{_selectedEntry.Name}' (ID: {_selectedEntry.Id}) from atlas?", "Remove", "Cancel"))
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
                    !entry.Id.ToString().Contains(_searchString) && 
                    !entry.Name.Contains(_searchString, StringComparison.OrdinalIgnoreCase))
                    continue;
                
                bool isSelected = _selectedEntry == entry;
                
                EditorGUILayout.BeginHorizontal(isSelected ? "SelectionRect" : "box");
                
                // Show Entry ID (key)
                if (GUILayout.Button($"ID: {entry.Id}", EditorStyles.label, GUILayout.Width(60)))
                {
                    _selectedEntry = entry;
                }
                
                // Show Sprite Name
                EditorGUILayout.LabelField($"Name: {entry.Name}", GUILayout.Width(150));
                
                EditorGUILayout.LabelField($"{entry.Width}x{entry.Height}", GUILayout.Width(70));
                EditorGUILayout.LabelField($"UV: ({entry.UV.x:F2}, {entry.UV.y:F2})", GUILayout.Width(100));
                
                // Show cached sprite count if any
                if (entry.HasCachedSprite)
                {
                    var oldColor = GUI.color;
                    GUI.color = Color.yellow;
                    EditorGUILayout.LabelField($"[cached]", GUILayout.Width(70));
                    GUI.color = oldColor;
                }
                
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
            DrawStatCard("Memory", FormatBytes(_globalStats.TotalMemoryBytes), EditorGUIUtility.IconContent("d_Profiler.Memory"));
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

        private void ShowAtlasInFileExplorer(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                Debug.LogWarning("[AtlasDebugWindow] No source file path available for this atlas");
                return;
            }

            // Get the directory containing the atlas files
            var directory = System.IO.Path.GetDirectoryName(filePath);
            var jsonFile = filePath + ".json";

            // Check if file exists
            if (!System.IO.File.Exists(jsonFile))
            {
                Debug.LogWarning($"[AtlasDebugWindow] Atlas file not found: {jsonFile}");
                EditorUtility.DisplayDialog("File Not Found", 
                    $"The atlas file could not be found:\n{jsonFile}\n\nIt may have been moved or deleted.", 
                    "OK");
                return;
            }

            // Open file location
            try
            {
                EditorUtility.RevealInFinder(jsonFile);
                Debug.Log($"[AtlasDebugWindow] Opened atlas location: {jsonFile}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AtlasDebugWindow] Failed to open file location: {ex.Message}");
            }
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
            public string SourceFilePath;
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

        // ============================
        // Saved Atlases Tab
        // ============================

        private struct SavedAtlasInfo
        {
            public string FilePath;
            public string DisplayName;
            public AtlasSerializationData Data;
            public List<Texture2D> LoadedTextures;
            public bool IsLoaded;
            public long FileSize;
            public DateTime LastModified;
            public int PageCount;
            public int EntryCount;

            public bool Equals(SavedAtlasInfo other)
            {
                return FilePath == other.FilePath;
            }
        }

        private void DrawSavedAtlasesTab()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Saved Atlases Inspector", EditorStyles.boldLabel);
            
            if (GUILayout.Button("Refresh", GUILayout.Width(80)))
            {
                RefreshSavedAtlases();
            }
            
            if (GUILayout.Button("Browse...", GUILayout.Width(80)))
            {
                BrowseForSavedAtlas();
            }
            
            if (GUILayout.Button("Clear List", GUILayout.Width(80)))
            {
                ClearSavedAtlasesList();
            }
            
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // Help box in Edit mode
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "This tab allows you to inspect saved atlases from disk in Edit Mode.\n\n" +
                    "• Click 'Browse...' to load an atlas from disk\n" +
                    "• Atlases saved during Play mode are automatically tracked\n" +
                    "• View atlas pages, sprite rects, and metadata",
                    MessageType.Info
                );
                EditorGUILayout.Space(5);
            }

            // Show play mode saved atlases info
            if (_playModeSavedPaths.Count > 0)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField($"Atlases Saved During Play Mode: {_playModeSavedPaths.Count}", EditorStyles.boldLabel);
                
                if (GUILayout.Button("Load All Play Mode Atlases"))
                {
                    foreach (var path in _playModeSavedPaths)
                    {
                        LoadSavedAtlas(path);
                    }
                }
                
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);
            }

            if (_savedAtlasesCache.Count == 0)
            {
                EditorGUILayout.Space(20);
                EditorGUILayout.HelpBox("No saved atlases loaded. Click 'Browse...' to load an atlas file.", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            
            // Left panel - Saved atlas list
            EditorGUILayout.BeginVertical(GUILayout.Width(250));
            DrawSavedAtlasList();
            EditorGUILayout.EndVertical();
            
            // Splitter
            GUILayout.Box("", GUILayout.Width(2), GUILayout.ExpandHeight(true));
            
            // Right panel - Saved atlas details
            _savedAtlasDetailsScroll = EditorGUILayout.BeginScrollView(_savedAtlasDetailsScroll);
            if (_selectedSavedAtlas.IsLoaded)
            {
                DrawSavedAtlasDetails();
            }
            else
            {
                EditorGUILayout.HelpBox("Select a saved atlas from the list to view details.", MessageType.Info);
            }
            EditorGUILayout.EndScrollView();
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSavedAtlasList()
        {
            EditorGUILayout.LabelField($"Saved Atlases ({_savedAtlasesCache.Count})", EditorStyles.boldLabel);
            
            _savedAtlasListScroll = EditorGUILayout.BeginScrollView(_savedAtlasListScroll);
            
            foreach (var info in _savedAtlasesCache)
            {
                bool isSelected = _selectedSavedAtlas.FilePath == info.FilePath;
                
                var cellStyle = new GUIStyle(isSelected ? "SelectionRect" : "box");
                cellStyle.alignment = TextAnchor.MiddleLeft;
                cellStyle.padding = new RectOffset(5, 5, 5, 5);
                
                if (GUILayout.Button("", cellStyle, GUILayout.Height(50), GUILayout.ExpandWidth(true)))
                {
                    _selectedSavedAtlas = info;
                    _selectedSavedAtlasPageIndex = 0; // Reset to first page when selecting new atlas
                }
                
                var lastRect = GUILayoutUtility.GetLastRect();
                
                // Icon
                var icon = EditorGUIUtility.IconContent("d_PreTextureMipMapHigh");
                var iconRect = new Rect(lastRect.x + 5, lastRect.y + lastRect.height / 2 - 10, 20, 20);
                GUI.Label(iconRect, icon);
                
                // Name
                var nameRect = new Rect(lastRect.x + 30, lastRect.y + 5, lastRect.width - 35, 16);
                GUI.Label(nameRect, info.DisplayName, EditorStyles.boldLabel);
                
                // Details line 1
                var details1Rect = new Rect(lastRect.x + 30, lastRect.y + 22, lastRect.width - 35, 12);
                GUI.Label(details1Rect, $"{info.PageCount} page(s), {info.EntryCount} entries", EditorStyles.miniLabel);
                
                // Details line 2
                var details2Rect = new Rect(lastRect.x + 30, lastRect.y + 34, lastRect.width - 35, 12);
                GUI.Label(details2Rect, $"{FormatBytes(info.FileSize)} - {info.LastModified:yyyy-MM-dd HH:mm}", EditorStyles.miniLabel);
            }
            
            EditorGUILayout.EndScrollView();
        }

        private void DrawSavedAtlasDetails()
        {
            var info = _selectedSavedAtlas;
            
            // Header with actions
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Atlas: {info.DisplayName}", EditorStyles.boldLabel);
            
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button("Show in Explorer", GUILayout.Width(120)))
            {
                ShowAtlasInFileExplorer(info.FilePath);
            }
            
            if (GUILayout.Button("Unload", GUILayout.Width(60)))
            {
                UnloadSavedAtlas(info);
                return;
            }
            
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // File info
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("File Information", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Path: {info.FilePath}");
            EditorGUILayout.LabelField($"Size: {FormatBytes(info.FileSize)}");
            EditorGUILayout.LabelField($"Modified: {info.LastModified:yyyy-MM-dd HH:mm:ss}");
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // Atlas info
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Atlas Information", EditorStyles.boldLabel);
            
            if (info.Data != null)
            {
                EditorGUILayout.LabelField($"Pages: {info.Data.Pages?.Count ?? 0}");
                EditorGUILayout.LabelField($"Entries: {info.Data.Entries?.Count ?? 0}");
                EditorGUILayout.LabelField($"Version: {info.Data.Version}");
                
                if (info.Data.Settings != null)
                {
                    EditorGUILayout.Space(3);
                    EditorGUILayout.LabelField("Settings:", EditorStyles.boldLabel);
                    EditorGUI.indentLevel++;
                    EditorGUILayout.LabelField($"Initial Size: {info.Data.Settings.InitialSize}");
                    EditorGUILayout.LabelField($"Max Size: {info.Data.Settings.MaxSize}");
                    EditorGUILayout.LabelField($"Max Pages: {(info.Data.Settings.MaxPageCount == -1 ? "Unlimited" : info.Data.Settings.MaxPageCount.ToString())}");
                    EditorGUILayout.LabelField($"Padding: {info.Data.Settings.Padding}");
                    EditorGUILayout.LabelField($"Format: {info.Data.Settings.Format}");
                    EditorGUILayout.LabelField($"Algorithm: {info.Data.Settings.Algorithm}");
                    EditorGUILayout.LabelField($"Readable: {info.Data.Settings.Readable}");
                    EditorGUILayout.LabelField($"Use RenderTextures: {info.Data.Settings.UseRenderTextures}");
                    EditorGUILayout.LabelField($"Mipmaps: {info.Data.Settings.GenerateMipMaps}");
                    EditorGUI.indentLevel--;
                }
            }
            
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // Page previews
            if (info.LoadedTextures != null && info.LoadedTextures.Count > 0)
            {
                EditorGUILayout.LabelField("Atlas Pages", EditorStyles.boldLabel);
                
                int pageCount = info.LoadedTextures.Count;
                
                // Ensure selected page is valid
                if (_selectedSavedAtlasPageIndex >= pageCount)
                    _selectedSavedAtlasPageIndex = 0;
                
                // Page navigation controls
                if (pageCount > 1)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Page:", GUILayout.Width(40));
                    
                    // Previous button
                    GUI.enabled = _selectedSavedAtlasPageIndex > 0;
                    if (GUILayout.Button("◄", GUILayout.Width(30)))
                    {
                        _selectedSavedAtlasPageIndex--;
                    }
                    GUI.enabled = true;
                    
                    // Page slider
                    int newPageIndex = EditorGUILayout.IntSlider(_selectedSavedAtlasPageIndex, 0, pageCount - 1);
                    if (newPageIndex != _selectedSavedAtlasPageIndex)
                    {
                        _selectedSavedAtlasPageIndex = newPageIndex;
                    }
                    
                    // Next button
                    GUI.enabled = _selectedSavedAtlasPageIndex < pageCount - 1;
                    if (GUILayout.Button("►", GUILayout.Width(30)))
                    {
                        _selectedSavedAtlasPageIndex++;
                    }
                    GUI.enabled = true;
                    
                    EditorGUILayout.LabelField($"of {pageCount - 1}", GUILayout.Width(50));
                    EditorGUILayout.EndHorizontal();
                    
                    EditorGUILayout.Space(5);
                }
                else
                {
                    _selectedSavedAtlasPageIndex = 0;
                }
                
                // Display current page
                int i = _selectedSavedAtlasPageIndex;
                if (i < info.LoadedTextures.Count)
                {
                    var texture = info.LoadedTextures[i];
                    if (texture != null)
                    {
                        EditorGUILayout.BeginVertical("box");
                        
                        // Page info
                        EditorGUILayout.LabelField($"Page {i} - {texture.width}x{texture.height}", EditorStyles.boldLabel);
                        
                        if (info.Data.Pages != null && i < info.Data.Pages.Count)
                        {
                            var pageData = info.Data.Pages[i];
                            EditorGUILayout.LabelField($"Dimensions: {pageData.Width}x{pageData.Height}");
                            
                            if (pageData.PackerState != null)
                            {
                                EditorGUILayout.LabelField($"Algorithm: {pageData.PackerState.Algorithm}");
                                EditorGUILayout.LabelField($"Used Rects: {pageData.PackerState.UsedRects?.Count ?? 0}");
                            }
                        }
                        
                        EditorGUILayout.Space(5);
                        
                        // Preview
                        float maxWidth = position.width - 300;
                        float aspect = (float)texture.height / texture.width;
                        float previewWidth = Mathf.Min(maxWidth, texture.width);
                        float previewHeight = previewWidth * aspect;
                        
                        var rect = GUILayoutUtility.GetRect(previewWidth, previewHeight);
                        EditorGUI.DrawPreviewTexture(rect, texture, null, ScaleMode.ScaleToFit);
                        
                        // Calculate actual texture display rect (accounting for ScaleToFit letterboxing)
                        float textureAspect = (float)texture.width / texture.height;
                        float rectAspect = rect.width / rect.height;
                        Rect textureRect;
                        
                        if (textureAspect > rectAspect)
                        {
                            // Texture is wider - letterbox top/bottom
                            float displayHeight = rect.width / textureAspect;
                            float yOffset = (rect.height - displayHeight) * 0.5f;
                            textureRect = new Rect(rect.x, rect.y + yOffset, rect.width, displayHeight);
                        }
                        else
                        {
                            // Texture is taller - letterbox left/right
                            float displayWidth = rect.height * textureAspect;
                            float xOffset = (rect.width - displayWidth) * 0.5f;
                            textureRect = new Rect(rect.x + xOffset, rect.y, displayWidth, rect.height);
                        }
                        
                        // Draw sprite rects on top
                        if (info.Data.Entries != null)
                        {
                            var entriesOnPage = info.Data.Entries.Where(e => e.TextureIndex == i).ToList();
                            
                            foreach (var entry in entriesOnPage)
                            {
                                var uvRect = new Rect(entry.UVRect.X, entry.UVRect.Y, entry.UVRect.Width, entry.UVRect.Height);
                                var entryRect = new Rect(
                                    textureRect.x + uvRect.x * textureRect.width,
                                    textureRect.y + (1 - uvRect.y - uvRect.height) * textureRect.height,
                                    uvRect.width * textureRect.width,
                                    uvRect.height * textureRect.height
                                );
                                
                                Handles.DrawSolidRectangleWithOutline(entryRect, 
                                    new Color(0, 1, 0, 0.1f), new Color(0, 1, 0, 0.5f));
                            }
                            
                            // Position sprite count label on actual texture rect
                            EditorGUI.LabelField(new Rect(textureRect.x + 5, textureRect.y + 5, 200, 20),
                                $"Page {i}: {entriesOnPage.Count} sprites",
                                EditorStyles.whiteBoldLabel);
                        }
                        
                        EditorGUILayout.EndVertical();
                    }
                }
            }

            EditorGUILayout.Space(5);

            // Entries list
            if (info.Data?.Entries != null && info.Data.Entries.Count > 0)
            {
                EditorGUILayout.LabelField($"Sprite Entries ({info.Data.Entries.Count})", EditorStyles.boldLabel);
                
                var foldoutKey = $"saved_atlas_entries_{info.FilePath}";
                var isExpanded = EditorPrefs.GetBool(foldoutKey, false);
                isExpanded = EditorGUILayout.Foldout(isExpanded, "Show All Entries", true);
                EditorPrefs.SetBool(foldoutKey, isExpanded);
                
                if (isExpanded)
                {
                    EditorGUILayout.BeginVertical("box");
                    
                    // Header
                    EditorGUILayout.BeginHorizontal("box");
                    EditorGUILayout.LabelField("ID", EditorStyles.boldLabel, GUILayout.Width(40));
                    EditorGUILayout.LabelField("Name", EditorStyles.boldLabel, GUILayout.Width(150));
                    EditorGUILayout.LabelField("Page", EditorStyles.boldLabel, GUILayout.Width(40));
                    EditorGUILayout.LabelField("Size", EditorStyles.boldLabel, GUILayout.Width(80));
                    EditorGUILayout.LabelField("Position", EditorStyles.boldLabel, GUILayout.Width(100));
                    EditorGUILayout.LabelField("UV", EditorStyles.boldLabel);
                    EditorGUILayout.EndHorizontal();
                    
                    // Entries
                    foreach (var entry in info.Data.Entries.OrderBy(e => e.TextureIndex).ThenBy(e => e.Id))
                    {
                        EditorGUILayout.BeginHorizontal("box");
                        EditorGUILayout.LabelField(entry.Id.ToString(), GUILayout.Width(40));
                        EditorGUILayout.LabelField(entry.Name ?? "-", GUILayout.Width(150));
                        EditorGUILayout.LabelField(entry.TextureIndex.ToString(), GUILayout.Width(40));
                        EditorGUILayout.LabelField($"{entry.PixelRect.Width}x{entry.PixelRect.Height}", GUILayout.Width(80));
                        EditorGUILayout.LabelField($"({entry.PixelRect.X}, {entry.PixelRect.Y})", GUILayout.Width(100));
                        EditorGUILayout.LabelField($"({entry.UVRect.X:F3}, {entry.UVRect.Y:F3}, {entry.UVRect.Width:F3}, {entry.UVRect.Height:F3})", EditorStyles.miniLabel);
                        EditorGUILayout.EndHorizontal();
                    }
                    
                    EditorGUILayout.EndVertical();
                }
            }
        }

        private void RefreshSavedAtlases()
        {
            // Keep existing atlases but refresh their data
            for (int i = _savedAtlasesCache.Count - 1; i >= 0; i--)
            {
                var info = _savedAtlasesCache[i];
                var jsonPath = info.FilePath + ".json";
                
                if (System.IO.File.Exists(jsonPath))
                {
                    // Refresh file info
                    var fileInfo = new System.IO.FileInfo(jsonPath);
                    var updatedInfo = info;
                    updatedInfo.LastModified = fileInfo.LastWriteTime;
                    updatedInfo.FileSize = fileInfo.Length;
                    
                    // Add page file sizes
                    for (int p = 0; p < info.PageCount; p++)
                    {
                        var pagePath = $"{info.FilePath}_page{p}.png";
                        if (System.IO.File.Exists(pagePath))
                        {
                            var pageFileInfo = new System.IO.FileInfo(pagePath);
                            updatedInfo.FileSize += pageFileInfo.Length;
                        }
                    }
                    
                    _savedAtlasesCache[i] = updatedInfo;
                }
                else
                {
                    // File no longer exists, remove from cache
                    UnloadSavedAtlas(info);
                    _savedAtlasesCache.RemoveAt(i);
                }
            }
            
            Repaint();
            Debug.Log($"[AtlasDebugWindow] Refreshed {_savedAtlasesCache.Count} saved atlases");
        }

        private void BrowseForSavedAtlas()
        {
            var path = EditorUtility.OpenFilePanel("Load Saved Atlas", Application.persistentDataPath, "json");
            if (string.IsNullOrEmpty(path)) return;
            
            // Remove .json extension if present
            if (path.EndsWith(".json"))
            {
                path = path.Substring(0, path.Length - 5);
            }
            
            LoadSavedAtlas(path);
        }

        private void LoadSavedAtlas(string filePath)
        {
            try
            {
                var jsonPath = filePath + ".json";
                
                if (!System.IO.File.Exists(jsonPath))
                {
                    Debug.LogError($"[AtlasDebugWindow] Atlas file not found: {jsonPath}");
                    EditorUtility.DisplayDialog("Error", $"Atlas file not found:\n{jsonPath}", "OK");
                    return;
                }
                
                // Check if already loaded
                if (_savedAtlasesCache.Any(a => a.FilePath == filePath))
                {
                    Debug.LogWarning($"[AtlasDebugWindow] Atlas already loaded: {filePath}");
                    _selectedSavedAtlas = _savedAtlasesCache.First(a => a.FilePath == filePath);
                    Repaint();
                    return;
                }
                
                // Load JSON data
                var json = System.IO.File.ReadAllText(jsonPath);
                var data = JsonUtility.FromJson<AtlasSerializationData>(json);
                
                if (data == null)
                {
                    Debug.LogError($"[AtlasDebugWindow] Failed to deserialize atlas data from: {jsonPath}");
                    return;
                }
                
                // Load textures
                var textures = new List<Texture2D>();
                for (int i = 0; i < (data.Pages?.Count ?? 0); i++)
                {
                    var texturePath = $"{filePath}_page{i}.png";
                    
                    if (!System.IO.File.Exists(texturePath))
                    {
                        Debug.LogWarning($"[AtlasDebugWindow] Texture page file not found: {texturePath}");
                        continue;
                    }
                    
                    var pngData = System.IO.File.ReadAllBytes(texturePath);
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    texture.LoadImage(pngData);
                    texture.name = $"{System.IO.Path.GetFileName(filePath)}_page{i}";
                    textures.Add(texture);
                }
                
                // Get file info
                var fileInfo = new System.IO.FileInfo(jsonPath);
                long totalSize = fileInfo.Length;
                
                for (int i = 0; i < textures.Count; i++)
                {
                    var pagePath = $"{filePath}_page{i}.png";
                    if (System.IO.File.Exists(pagePath))
                    {
                        var pageFileInfo = new System.IO.FileInfo(pagePath);
                        totalSize += pageFileInfo.Length;
                    }
                }
                
                var savedInfo = new SavedAtlasInfo
                {
                    FilePath = filePath,
                    DisplayName = System.IO.Path.GetFileName(filePath),
                    Data = data,
                    LoadedTextures = textures,
                    IsLoaded = true,
                    FileSize = totalSize,
                    LastModified = fileInfo.LastWriteTime,
                    PageCount = data.Pages?.Count ?? 0,
                    EntryCount = data.Entries?.Count ?? 0
                };
                
                _savedAtlasesCache.Add(savedInfo);
                _selectedSavedAtlas = savedInfo;
                
                Debug.Log($"[AtlasDebugWindow] Loaded saved atlas: {filePath} ({textures.Count} pages, {data.Entries?.Count ?? 0} entries)");
                Repaint();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AtlasDebugWindow] Failed to load saved atlas: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog("Error", $"Failed to load atlas:\n{ex.Message}", "OK");
            }
        }

        private void UnloadSavedAtlas(SavedAtlasInfo info)
        {
            // Clean up textures
            if (info.LoadedTextures != null)
            {
                foreach (var tex in info.LoadedTextures)
                {
                    if (tex != null)
                    {
                        DestroyImmediate(tex);
                    }
                }
            }
            
            _savedAtlasesCache.Remove(info);
            
            if (_selectedSavedAtlas.FilePath == info.FilePath)
            {
                _selectedSavedAtlas = default;
            }
            
            Debug.Log($"[AtlasDebugWindow] Unloaded saved atlas: {info.FilePath}");
            Repaint();
        }

        private void ClearSavedAtlasesList()
        {
            if (!EditorUtility.DisplayDialog("Clear Saved Atlases", 
                "This will unload all saved atlases from the inspector. The files on disk will not be deleted.", 
                "Clear", "Cancel"))
            {
                return;
            }
            
            foreach (var info in _savedAtlasesCache.ToList())
            {
                UnloadSavedAtlas(info);
            }
            
            _playModeSavedPaths.Clear();
            _savedAtlasesCache.Clear();
            _selectedSavedAtlas = default;
            
            Debug.Log("[AtlasDebugWindow] Cleared all saved atlases from inspector");
            Repaint();
        }
    }
}
