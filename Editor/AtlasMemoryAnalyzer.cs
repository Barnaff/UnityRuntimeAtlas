using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;

namespace RuntimeAtlasPacker.Editor
{
    /// <summary>
    /// Memory analyzer for runtime atlases.
    /// </summary>
    public class AtlasMemoryAnalyzer : EditorWindow
    {
        private Vector2 _scrollPosition;
        private List<MemoryEntry> _memoryEntries = new();
        private long _totalMemory;
        private bool _autoRefresh = true;
        private double _lastRefreshTime;
        
        // Sort options
        private enum SortBy { Name, Size, Entries, FillRatio }
        private SortBy _sortBy = SortBy.Size;
        private bool _sortDescending = true;

        [MenuItem("Window/Runtime Atlas Packer/Memory Analyzer")]
        public static void ShowWindow()
        {
            var window = GetWindow<AtlasMemoryAnalyzer>();
            window.titleContent = new GUIContent("Atlas Memory", EditorGUIUtility.IconContent("d_MemoryProfiler").image);
            window.minSize = new Vector2(500, 300);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshData();
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (_autoRefresh && EditorApplication.timeSinceStartup - _lastRefreshTime > 1.0)
            {
                RefreshData();
                Repaint();
            }
        }

        private void RefreshData()
        {
            _lastRefreshTime = EditorApplication.timeSinceStartup;
            _memoryEntries.Clear();
            _totalMemory = 0;
            
            var atlases = GetAllAtlases();
            
            foreach (var kvp in atlases)
            {
                var atlas = kvp.Value;
                if (atlas?.Texture == null) continue;
                
                var entry = new MemoryEntry
                {
                    Name = kvp.Key,
                    Atlas = atlas,
                    TextureMemory = CalculateTextureMemory(atlas),
                    EntryCount = atlas.EntryCount,
                    FillRatio = atlas.FillRatio,
                    Width = atlas.Width,
                    Height = atlas.Height,
                    Format = atlas.Settings.Format
                };
                
                // Estimate overhead
                entry.OverheadMemory = EstimateOverhead(atlas);
                entry.TotalMemory = entry.TextureMemory + entry.OverheadMemory;
                
                _memoryEntries.Add(entry);
                _totalMemory += entry.TotalMemory;
            }
            
            ApplySort();
        }

        private Dictionary<string, RuntimeAtlas> GetAllAtlases()
        {
            var result = new Dictionary<string, RuntimeAtlas>();
            
            try
            {
                var type = typeof(AtlasPacker);
                
                var defaultField = type.GetField("_defaultAtlas", BindingFlags.NonPublic | BindingFlags.Static);
                if (defaultField != null)
                {
                    var defaultAtlas = defaultField.GetValue(null) as RuntimeAtlas;
                    if (defaultAtlas != null)
                        result["[Default]"] = defaultAtlas;
                }
                
                var namedField = type.GetField("_namedAtlases", BindingFlags.NonPublic | BindingFlags.Static);
                if (namedField != null)
                {
                    var namedAtlases = namedField.GetValue(null) as Dictionary<string, RuntimeAtlas>;
                    if (namedAtlases != null)
                    {
                        foreach (var kvp in namedAtlases)
                            result[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch { }
            
            return result;
        }

        private long CalculateTextureMemory(RuntimeAtlas atlas)
        {
            if (atlas?.Texture == null) return 0;
            
            int bpp = GetBytesPerPixel(atlas.Settings.Format);
            long size = (long)atlas.Width * atlas.Height * bpp;
            
            if (atlas.Settings.GenerateMipMaps)
                size = (long)(size * 1.33f);
            
            return size;
        }

        private int GetBytesPerPixel(TextureFormat format)
        {
            switch (format)
            {
                case TextureFormat.Alpha8:
                case TextureFormat.R8:
                    return 1;
                case TextureFormat.RGB565:
                case TextureFormat.RGBA4444:
                case TextureFormat.RG16:
                case TextureFormat.R16:
                    return 2;
                case TextureFormat.RGB24:
                    return 3;
                case TextureFormat.RGBA32:
                case TextureFormat.ARGB32:
                case TextureFormat.BGRA32:
                case TextureFormat.RGBAFloat:
                    return 4;
                default:
                    return 4;
            }
        }

        private long EstimateOverhead(RuntimeAtlas atlas)
        {
            // Rough estimate of native container and dictionary overhead
            long overhead = 0;
            
            // Dictionary entries
            overhead += atlas.EntryCount * 64; // Rough estimate per entry
            
            // Packing algorithm data
            overhead += atlas.Width * 4; // Free rects estimate
            
            return overhead;
        }

        private void ApplySort()
        {
            switch (_sortBy)
            {
                case SortBy.Name:
                    _memoryEntries = _sortDescending 
                        ? _memoryEntries.OrderByDescending(e => e.Name).ToList()
                        : _memoryEntries.OrderBy(e => e.Name).ToList();
                    break;
                case SortBy.Size:
                    _memoryEntries = _sortDescending 
                        ? _memoryEntries.OrderByDescending(e => e.TotalMemory).ToList()
                        : _memoryEntries.OrderBy(e => e.TotalMemory).ToList();
                    break;
                case SortBy.Entries:
                    _memoryEntries = _sortDescending 
                        ? _memoryEntries.OrderByDescending(e => e.EntryCount).ToList()
                        : _memoryEntries.OrderBy(e => e.EntryCount).ToList();
                    break;
                case SortBy.FillRatio:
                    _memoryEntries = _sortDescending 
                        ? _memoryEntries.OrderByDescending(e => e.FillRatio).ToList()
                        : _memoryEntries.OrderBy(e => e.FillRatio).ToList();
                    break;
            }
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawSummary();
            DrawMemoryTable();
            DrawRecommendations();
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
            
            EditorGUILayout.LabelField("Sort by:", GUILayout.Width(50));
            var newSort = (SortBy)EditorGUILayout.EnumPopup(_sortBy, EditorStyles.toolbarPopup, GUILayout.Width(80));
            if (newSort != _sortBy)
            {
                _sortBy = newSort;
                ApplySort();
            }
            
            if (GUILayout.Button(_sortDescending ? "▼" : "▲", EditorStyles.toolbarButton, GUILayout.Width(25)))
            {
                _sortDescending = !_sortDescending;
                ApplySort();
            }
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSummary()
        {
            EditorGUILayout.BeginHorizontal();
            
            DrawSummaryBox("Total Atlases", _memoryEntries.Count.ToString());
            DrawSummaryBox("Total Memory", FormatBytes(_totalMemory));
            DrawSummaryBox("Total Entries", _memoryEntries.Sum(e => e.EntryCount).ToString());
            DrawSummaryBox("Avg Fill", _memoryEntries.Count > 0 
                ? $"{_memoryEntries.Average(e => e.FillRatio):P0}" 
                : "-");
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSummaryBox(string label, string value)
        {
            EditorGUILayout.BeginVertical("box", GUILayout.Width(100));
            EditorGUILayout.LabelField(label, EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.LabelField(value, new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter });
            EditorGUILayout.EndVertical();
        }

        private void DrawMemoryTable()
        {
            // Header
            EditorGUILayout.BeginHorizontal("box");
            EditorGUILayout.LabelField("Atlas", EditorStyles.boldLabel, GUILayout.Width(120));
            EditorGUILayout.LabelField("Size", EditorStyles.boldLabel, GUILayout.Width(80));
            EditorGUILayout.LabelField("Entries", EditorStyles.boldLabel, GUILayout.Width(60));
            EditorGUILayout.LabelField("Fill", EditorStyles.boldLabel, GUILayout.Width(50));
            EditorGUILayout.LabelField("Texture Mem", EditorStyles.boldLabel, GUILayout.Width(80));
            EditorGUILayout.LabelField("Overhead", EditorStyles.boldLabel, GUILayout.Width(70));
            EditorGUILayout.LabelField("Total", EditorStyles.boldLabel, GUILayout.Width(80));
            EditorGUILayout.LabelField("", GUILayout.ExpandWidth(true)); // Memory bar
            EditorGUILayout.EndHorizontal();
            
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            
            foreach (var entry in _memoryEntries)
            {
                EditorGUILayout.BeginHorizontal("box");
                
                EditorGUILayout.LabelField(entry.Name, GUILayout.Width(120));
                EditorGUILayout.LabelField($"{entry.Width}x{entry.Height}", GUILayout.Width(80));
                EditorGUILayout.LabelField(entry.EntryCount.ToString(), GUILayout.Width(60));
                
                // Fill ratio with color
                var fillColor = entry.FillRatio > 0.8f ? Color.green : 
                               entry.FillRatio > 0.5f ? Color.yellow : Color.red;
                var oldColor = GUI.color;
                GUI.color = fillColor;
                EditorGUILayout.LabelField($"{entry.FillRatio:P0}", GUILayout.Width(50));
                GUI.color = oldColor;
                
                EditorGUILayout.LabelField(FormatBytes(entry.TextureMemory), GUILayout.Width(80));
                EditorGUILayout.LabelField(FormatBytes(entry.OverheadMemory), GUILayout.Width(70));
                EditorGUILayout.LabelField(FormatBytes(entry.TotalMemory), GUILayout.Width(80));
                
                // Memory proportion bar
                float proportion = _totalMemory > 0 ? (float)entry.TotalMemory / _totalMemory : 0;
                var barRect = GUILayoutUtility.GetRect(100, 16, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(barRect, proportion, $"{proportion:P0}");
                
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.EndScrollView();
        }

        private void DrawRecommendations()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Recommendations", EditorStyles.boldLabel);
            
            bool hasRecommendations = false;
            
            foreach (var entry in _memoryEntries)
            {
                // Low fill ratio warning
                if (entry.FillRatio < 0.5f && entry.EntryCount > 0)
                {
                    hasRecommendations = true;
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.HelpBox($"Atlas '{entry.Name}' has low fill ratio ({entry.FillRatio:P0}). Consider repacking or using a smaller initial size.", MessageType.Warning);
                    if (GUILayout.Button("Repack", GUILayout.Width(60), GUILayout.Height(38)))
                    {
                        entry.Atlas.Repack();
                        RefreshData();
                    }
                    EditorGUILayout.EndHorizontal();
                }
                
                // Large atlas warning
                if (entry.TextureMemory > 16 * 1024 * 1024) // > 16MB
                {
                    hasRecommendations = true;
                    EditorGUILayout.HelpBox($"Atlas '{entry.Name}' is using {FormatBytes(entry.TextureMemory)} of texture memory. Consider splitting into multiple atlases.", MessageType.Warning);
                }
                
                // Empty atlas
                if (entry.EntryCount == 0)
                {
                    hasRecommendations = true;
                    EditorGUILayout.HelpBox($"Atlas '{entry.Name}' has no entries. Consider disposing it.", MessageType.Info);
                }
            }
            
            if (!hasRecommendations)
            {
                EditorGUILayout.HelpBox("No optimization recommendations at this time.", MessageType.Info);
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

        private struct MemoryEntry
        {
            public string Name;
            public RuntimeAtlas Atlas;
            public long TextureMemory;
            public long OverheadMemory;
            public long TotalMemory;
            public int EntryCount;
            public float FillRatio;
            public int Width;
            public int Height;
            public TextureFormat Format;
        }
    }

    /// <summary>
    /// Preferences for the Runtime Atlas Packer editor tools.
    /// </summary>
    public static class AtlasPreferences
    {
        private const string GizmosEnabledKey = "RuntimeAtlasPacker_GizmosEnabled";
        private const string AutoRefreshKey = "RuntimeAtlasPacker_AutoRefresh";
        private const string ProfilerEnabledKey = "RuntimeAtlasPacker_ProfilerEnabled";

        public static bool GizmosEnabled
        {
            get => EditorPrefs.GetBool(GizmosEnabledKey, false);
            set => EditorPrefs.SetBool(GizmosEnabledKey, value);
        }

        public static bool AutoRefresh
        {
            get => EditorPrefs.GetBool(AutoRefreshKey, true);
            set => EditorPrefs.SetBool(AutoRefreshKey, value);
        }

        public static bool ProfilerEnabled
        {
            get => EditorPrefs.GetBool(ProfilerEnabledKey, true);
            set => EditorPrefs.SetBool(ProfilerEnabledKey, value);
        }

#if UNITY_2019_1_OR_NEWER
        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            var provider = new SettingsProvider("Preferences/Runtime Atlas Packer", SettingsScope.User)
            {
                label = "Runtime Atlas Packer",
                guiHandler = (searchContext) =>
                {
                    EditorGUILayout.Space(10);
                    
                    EditorGUILayout.LabelField("Editor Tools", EditorStyles.boldLabel);
                    GizmosEnabled = EditorGUILayout.Toggle("Show Scene Gizmos", GizmosEnabled);
                    AutoRefresh = EditorGUILayout.Toggle("Auto-Refresh Debug Windows", AutoRefresh);
                    ProfilerEnabled = EditorGUILayout.Toggle("Enable Profiler", ProfilerEnabled);
                    
                    EditorGUILayout.Space(20);
                    
                    EditorGUILayout.LabelField("Quick Actions", EditorStyles.boldLabel);
                    
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Open Debug Window"))
                        AtlasDebugWindow.ShowWindow();
                    if (GUILayout.Button("Open Profiler"))
                        AtlasProfilerWindow.ShowWindow();
                    if (GUILayout.Button("Open Memory Analyzer"))
                        AtlasMemoryAnalyzer.ShowWindow();
                    EditorGUILayout.EndHorizontal();
                    
                    EditorGUILayout.Space(10);
                    
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Open Texture Picker"))
                        AtlasTexturePicker.ShowWindow();
                    if (GUILayout.Button("Open Batch Import Wizard"))
                        AtlasBatchImportWizard.ShowWizard();
                    EditorGUILayout.EndHorizontal();
                },
                keywords = new HashSet<string>(new[] { "Atlas", "Texture", "Packer", "Runtime", "Sprite" })
            };

            return provider;
        }
#endif
    }
}
