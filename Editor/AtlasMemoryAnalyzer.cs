using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace RuntimeAtlasPacker.Editor
{
    public class AtlasMemoryAnalyzer : EditorWindow
    {
        private const int MAX_GRAPH_POINTS = 200;

        private readonly List<AtlasInfo> _atlases = new List<AtlasInfo>();
        private readonly List<GraphPoint> _graphData = new List<GraphPoint>();

        private Vector2 _scrollPos;
        private Vector2 _previewScrollPos;
        private bool _showGraph = true;
        private bool _showPreviews = true;
        private float _lastUpdateTime;
        private float _playSessionStartTime;

        private long _totalMemoryBytes;
        private int _totalAtlasCount;
        private int _totalTextureCount;

        [MenuItem("Window/Runtime Atlas Packer/Memory Analyzer")]
        public static void ShowWindow()
        {
            var window = GetWindow<AtlasMemoryAnalyzer>("Atlas Memory");
            window.minSize = new Vector2(700, 500);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            RuntimeAtlasProfiler.OnOperationLogged += OnProfilerEvent;

            if (EditorApplication.isPlaying)
            {
                _playSessionStartTime = (float)EditorApplication.timeSinceStartup;
                UpdateAtlasData();
            }
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            RuntimeAtlasProfiler.OnOperationLogged -= OnProfilerEvent;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                _graphData.Clear();
                _atlases.Clear();
                _playSessionStartTime = (float)EditorApplication.timeSinceStartup;
                UpdateAtlasData();
                Repaint();
            }
        }

        private void OnProfilerEvent(ProfileData data)
        {
            if (EditorApplication.isPlaying)
            {
                UpdateAtlasData();
                Repaint();
            }
        }

        private void OnEditorUpdate()
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }

            var currentTime = (float)EditorApplication.timeSinceStartup;
            if (currentTime - _lastUpdateTime > 0.5f)
            {
                _lastUpdateTime = currentTime;
                UpdateAtlasData();
                Repaint();
            }
        }

        private void UpdateAtlasData()
        {
            _atlases.Clear();
            _totalMemoryBytes = 0;
            _totalAtlasCount = 0;
            _totalTextureCount = 0;

            var atlasPackerType = typeof(AtlasPacker);

            // Get default atlas
            var defaultField = atlasPackerType.GetField("_defaultAtlas", BindingFlags.NonPublic | BindingFlags.Static);
            if (defaultField != null)
            {
                var defaultAtlas = defaultField.GetValue(null) as RuntimeAtlas;
                if (defaultAtlas != null && defaultAtlas.Texture != null)
                {
                    AddAtlasInfo("[Default]", defaultAtlas);
                }
            }

            // Get named atlases
            var namedField = atlasPackerType.GetField("_namedAtlases", BindingFlags.NonPublic | BindingFlags.Static);
            if (namedField != null)
            {
                var dict = namedField.GetValue(null) as Dictionary<string, RuntimeAtlas>;
                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        if (kvp.Value != null && kvp.Value.Texture != null)
                        {
                            AddAtlasInfo(kvp.Key, kvp.Value);
                        }
                    }
                }
            }

            // Add to graph
            var time = (float)EditorApplication.timeSinceStartup - _playSessionStartTime;
            var shouldAdd = _graphData.Count == 0 ||
                            _totalMemoryBytes != _graphData[_graphData.Count - 1].MemoryBytes ||
                            _totalAtlasCount != _graphData[_graphData.Count - 1].AtlasCount;

            if (shouldAdd)
            {
                _graphData.Add(new GraphPoint
                {
                    Time = time,
                    MemoryBytes = _totalMemoryBytes,
                    AtlasCount = _totalAtlasCount,
                    TextureCount = _totalTextureCount,
                    GcAllocated = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong(),
                    TotalAllocated = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong()
                });

                while (_graphData.Count > MAX_GRAPH_POINTS)
                {
                    _graphData.RemoveAt(0);
                }
            }
        }

        private void AddAtlasInfo(string name, RuntimeAtlas atlas)
        {
            var info = new AtlasInfo
            {
                Name = name,
                Atlas = atlas,
                Width = atlas.Width,
                Height = atlas.Height,
                Format = atlas.Settings.Format,
                EntryCount = atlas.EntryCount,
                FillRatio = atlas.FillRatio,
                Texture = atlas.Texture,
                PageCount = atlas.PageCount
            };

            var bpp = GetBytesPerPixel(info.Format);
            long bytes = 0;

            // Calculate memory for all pages
            for (var i = 0; i < atlas.PageCount; i++)
            {
                var pageTex = atlas.GetTexture(i);
                if (pageTex != null)
                {
                    var pageBytes = (long)pageTex.width * pageTex.height * bpp;
                    if (atlas.Settings.GenerateMipMaps)
                    {
                        pageBytes = (long)(pageBytes * 1.33f);
                    }
                    bytes += pageBytes;
                }
            }

            info.MemoryBytes = bytes;
            
            // Add sprite cache memory
            var cacheMemory = atlas.GetCachedSpriteMemoryUsage();
            info.CachedSpriteCount = atlas.GetTotalCachedSpriteCount();
            info.CachedSpriteMemoryBytes = cacheMemory;

            _atlases.Add(info);
            _totalMemoryBytes += bytes + cacheMemory;
            _totalAtlasCount++;
            _totalTextureCount += info.EntryCount;
        }

        private int GetBytesPerPixel(TextureFormat format)
        {
            switch (format)
            {
                case TextureFormat.RGBA32:
                case TextureFormat.ARGB32:
                case TextureFormat.BGRA32:
                    return 4;
                case TextureFormat.RGB24:
                    return 3;
                case TextureFormat.RGBA4444:
                case TextureFormat.RGB565:
                    return 2;
                case TextureFormat.Alpha8:
                case TextureFormat.R8:
                    return 1;
                default:
                    return 4;
            }
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.Space(50);
                EditorGUILayout.HelpBox("Press Play to start tracking atlas allocations", MessageType.Info);
                return;
            }

            if (_atlases.Count == 0)
            {
                EditorGUILayout.Space(50);
                EditorGUILayout.HelpBox("No atlases detected. Create atlases using AtlasPacker to see tracking.", MessageType.Warning);
                return;
            }

            DrawStats();

            if (_showGraph && _graphData.Count > 1)
            {
                DrawGraph();
            }

            DrawAtlasList();

            if (_showPreviews)
            {
                DrawAtlasPreviews();
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            var color = EditorApplication.isPlaying ? Color.green : Color.gray;
            var oldBg = GUI.backgroundColor;
            GUI.backgroundColor = color;
            GUILayout.Label(EditorApplication.isPlaying ? "● TRACKING" : "○ STOPPED", EditorStyles.toolbarButton, GUILayout.Width(100));
            GUI.backgroundColor = oldBg;

            GUILayout.FlexibleSpace();

            _showGraph = GUILayout.Toggle(_showGraph, "Graph", EditorStyles.toolbarButton);
            _showPreviews = GUILayout.Toggle(_showPreviews, "Previews", EditorStyles.toolbarButton);

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                UpdateAtlasData();
            }

            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                _graphData.Clear();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawStats()
        {
            EditorGUILayout.BeginHorizontal();

            DrawStatBox("Atlases", _totalAtlasCount.ToString(), new Color(0.3f, 0.7f, 1f));
            DrawStatBox("Memory", FormatBytes(_totalMemoryBytes), new Color(1f, 0.7f, 0.3f));
            DrawStatBox("Textures", _totalTextureCount.ToString(), new Color(0.3f, 1f, 0.5f));

            var avgFill = _atlases.Count > 0 ? _atlases.Average(a => a.FillRatio) : 0;
            DrawStatBox("Avg Fill", $"{avgFill * 100:F0}%", new Color(1f, 0.3f, 0.7f));

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(5);
        }

        private void DrawStatBox(string label, string value, Color color)
        {
            var oldBg = GUI.backgroundColor;
            GUI.backgroundColor = color * 0.3f;

            EditorGUILayout.BeginVertical("box", GUILayout.MinWidth(120));
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);

            var style = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            style.normal.textColor = color;
            EditorGUILayout.LabelField(value, style);

            EditorGUILayout.EndVertical();
            GUI.backgroundColor = oldBg;
        }

        private void DrawGraph()
        {
            EditorGUILayout.LabelField("Memory Over Time (Normalized)", EditorStyles.boldLabel);

            var rect = GUILayoutUtility.GetRect(100, 150, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.1f, 0.1f, 0.1f));

            if (_graphData.Count < 2)
            {
                return;
            }

            // Calculate max values for normalization
            var maxAtlasMem = _graphData.Max(p => p.MemoryBytes);
            var maxGcMem = _graphData.Max(p => p.GcAllocated);
            var maxTotalMem = _graphData.Max(p => p.TotalAllocated);

            if (maxAtlasMem == 0) maxAtlasMem = 1;
            if (maxGcMem == 0) maxGcMem = 1;
            if (maxTotalMem == 0) maxTotalMem = 1;

            Handles.BeginGUI();

            // Draw Atlas Memory (Blue)
            DrawLine(rect, maxAtlasMem, p => p.MemoryBytes, new Color(0.3f, 0.7f, 1f));

            // Draw GC Allocated (Green)
            DrawLine(rect, maxGcMem, p => p.GcAllocated, new Color(0.3f, 1f, 0.5f));

            // Draw Total Allocated (Orange)
            DrawLine(rect, maxTotalMem, p => p.TotalAllocated, new Color(1f, 0.7f, 0.3f));

            Handles.EndGUI();

            // Legend with values
            var lastPoint = _graphData[_graphData.Count - 1];

            GUILayout.BeginHorizontal();
            DrawLegendItem("Atlas Memory", FormatBytes(lastPoint.MemoryBytes), new Color(0.3f, 0.7f, 1f));
            DrawLegendItem("GC Allocated", FormatBytes(lastPoint.GcAllocated), new Color(0.3f, 1f, 0.5f));
            DrawLegendItem("Total Allocated", FormatBytes(lastPoint.TotalAllocated), new Color(1f, 0.7f, 0.3f));
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(5);
        }

        private void DrawLine(Rect rect, long maxVal, Func<GraphPoint, long> valueSelector, Color color)
        {
            Handles.color = color;
            var points = new List<Vector3>();
            for (var i = 0; i < _graphData.Count; i++)
            {
                var x = rect.x + (rect.width * i / (_graphData.Count - 1));
                var y = rect.yMax - (rect.height * valueSelector(_graphData[i]) / maxVal);
                points.Add(new Vector3(x, y, 0));
            }
            Handles.DrawAAPolyLine(2f, points.ToArray());
        }

        private void DrawLegendItem(string label, string value, Color color)
        {
            var oldColor = GUI.color;
            GUI.color = color;

            GUILayout.BeginVertical(GUILayout.Width(150));
            GUILayout.Label("● " + label, EditorStyles.miniLabel);
            GUILayout.Label("   " + value, EditorStyles.boldLabel);
            GUILayout.EndVertical();

            GUI.color = oldColor;
        }

        private void DrawAtlasList()
        {
            EditorGUILayout.LabelField("Atlas Details", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal("box");
            EditorGUILayout.LabelField("Name", EditorStyles.boldLabel, GUILayout.Width(120));
            EditorGUILayout.LabelField("Size", EditorStyles.boldLabel, GUILayout.Width(80));
            EditorGUILayout.LabelField("Format", EditorStyles.boldLabel, GUILayout.Width(70));
            EditorGUILayout.LabelField("Textures", EditorStyles.boldLabel, GUILayout.Width(60));
            EditorGUILayout.LabelField("Fill", EditorStyles.boldLabel, GUILayout.Width(50));
            EditorGUILayout.LabelField("Cache", EditorStyles.boldLabel, GUILayout.Width(70));
            EditorGUILayout.LabelField("Memory", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(150));

            foreach (var atlas in _atlases)
            {
                EditorGUILayout.BeginHorizontal("box");

                EditorGUILayout.LabelField(atlas.Name, GUILayout.Width(120));
                EditorGUILayout.LabelField($"{atlas.Width}x{atlas.Height}", GUILayout.Width(80));
                EditorGUILayout.LabelField(atlas.Format.ToString(), GUILayout.Width(70));
                EditorGUILayout.LabelField(atlas.EntryCount.ToString(), GUILayout.Width(60));

                var fillColor = atlas.FillRatio >= 0.8f ? Color.green : atlas.FillRatio >= 0.5f ? Color.yellow : Color.red;
                var oldColor = GUI.contentColor;
                GUI.contentColor = fillColor;
                EditorGUILayout.LabelField($"{atlas.FillRatio * 100:F0}%", GUILayout.Width(50));
                GUI.contentColor = oldColor;

                // Show cached sprite count
                if (atlas.CachedSpriteCount > 0)
                {
                    GUI.contentColor = Color.yellow;
                    EditorGUILayout.LabelField($"{atlas.CachedSpriteCount}", GUILayout.Width(70));
                    GUI.contentColor = oldColor;
                }
                else
                {
                    EditorGUILayout.LabelField("-", GUILayout.Width(70));
                }

                // Show total memory (texture + cache)
                var totalMem = atlas.MemoryBytes + atlas.CachedSpriteMemoryBytes;
                var memLabel = atlas.CachedSpriteMemoryBytes > 0 
                    ? $"{FormatBytes(totalMem)} ({FormatBytes(atlas.CachedSpriteMemoryBytes)} cached)"
                    : FormatBytes(totalMem);
                EditorGUILayout.LabelField(memLabel, GUILayout.ExpandWidth(true));

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawAtlasPreviews()
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Atlas Texture Previews", EditorStyles.boldLabel);

            _previewScrollPos = EditorGUILayout.BeginScrollView(_previewScrollPos, GUILayout.ExpandHeight(true));

            foreach (var atlas in _atlases)
            {
                if (atlas.Texture == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical("box");

                EditorGUILayout.LabelField($"{atlas.Name} - {atlas.Width}x{atlas.Height}", EditorStyles.boldLabel);
                
                var infoText = $"Textures: {atlas.EntryCount}, Fill: {atlas.FillRatio * 100:F0}%, Memory: {FormatBytes(atlas.MemoryBytes)}";
                if (atlas.CachedSpriteCount > 0)
                {
                    infoText += $", Cached Sprites: {atlas.CachedSpriteCount} ({FormatBytes(atlas.CachedSpriteMemoryBytes)})";
                }
                EditorGUILayout.LabelField(infoText);

                var previewSize = Mathf.Min(300, position.width - 40);
                var aspect = (float)atlas.Height / atlas.Width;
                var previewHeight = previewSize * aspect;

                var previewRect = GUILayoutUtility.GetRect(previewSize, previewHeight);
                EditorGUI.DrawPreviewTexture(previewRect, atlas.Texture, null, ScaleMode.ScaleToFit);

                EditorGUILayout.Space(5);
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(10);
            }

            EditorGUILayout.EndScrollView();
        }

        private string FormatBytes(long bytes)
        {
            if (bytes < 1024)
            {
                return $"{bytes} B";
            }
            if (bytes < 1024 * 1024)
            {
                return $"{bytes / 1024f:F2} KB";
            }
            if (bytes < 1024 * 1024 * 1024)
            {
                return $"{bytes / (1024f * 1024f):F2} MB";
            }
            return $"{bytes / (1024f * 1024f * 1024f):F2} GB";
        }

        private class AtlasInfo
        {
            public string Name;
            public RuntimeAtlas Atlas;
            public int Width;
            public int Height;
            public TextureFormat Format;
            public int EntryCount;
            public float FillRatio;
            public long MemoryBytes;
            public Texture2D Texture;
            public int PageCount;
            public int CachedSpriteCount;
            public long CachedSpriteMemoryBytes;
        }

        private struct GraphPoint
        {
            public float Time;
            public long MemoryBytes;
            public int AtlasCount;
            public int TextureCount;
            public long GcAllocated;
            public long TotalAllocated;
        }
    }
}

