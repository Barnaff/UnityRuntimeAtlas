using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;

namespace RuntimeAtlasPacker.Editor
{
    public class AtlasMemoryAnalyzer : EditorWindow
    {
        private List<AtlasInfo> _atlases = new List<AtlasInfo>();
        private List<GraphPoint> _graphData = new List<GraphPoint>();
        private const int MAX_GRAPH_POINTS = 200;
        
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
            if (EditorApplication.isPlaying)
            {
                float currentTime = (float)EditorApplication.timeSinceStartup;
                if (currentTime - _lastUpdateTime > 0.5f)
                {
                    _lastUpdateTime = currentTime;
                    UpdateAtlasData();
                    Repaint();
                }
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
            float time = (float)EditorApplication.timeSinceStartup - _playSessionStartTime;
            bool shouldAdd = _graphData.Count == 0 || 
                           _totalMemoryBytes != _graphData[_graphData.Count - 1].memoryBytes ||
                           _totalAtlasCount != _graphData[_graphData.Count - 1].atlasCount;
            
            if (shouldAdd)
            {
                _graphData.Add(new GraphPoint
                {
                    time = time,
                    memoryBytes = _totalMemoryBytes,
                    atlasCount = _totalAtlasCount,
                    textureCount = _totalTextureCount
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
                name = name,
                atlas = atlas,
                width = atlas.Width,
                height = atlas.Height,
                format = atlas.Settings.Format,
                entryCount = atlas.EntryCount,
                fillRatio = atlas.FillRatio,
                texture = atlas.Texture
            };

            int bpp = GetBytesPerPixel(info.format);
            long bytes = (long)info.width * info.height * bpp;
            if (atlas.Settings.GenerateMipMaps) bytes = (long)(bytes * 1.33f);

            info.memoryBytes = bytes;
            
            _atlases.Add(info);
            _totalMemoryBytes += bytes;
            _totalAtlasCount++;
            _totalTextureCount += info.entryCount;
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
            
            float avgFill = _atlases.Count > 0 ? _atlases.Average(a => a.fillRatio) : 0;
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
            EditorGUILayout.LabelField("Memory Over Time", EditorStyles.boldLabel);
            
            var rect = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.1f, 0.1f, 0.1f));
            
            if (_graphData.Count < 2) return;

            long maxMem = _graphData.Max(p => p.memoryBytes);
            if (maxMem == 0) return;

            Handles.BeginGUI();
            Handles.color = new Color(0.3f, 0.7f, 1f);
            
            var points = new List<Vector3>();
            for (int i = 0; i < _graphData.Count; i++)
            {
                float x = rect.x + (rect.width * i / (_graphData.Count - 1));
                float y = rect.yMax - (rect.height * _graphData[i].memoryBytes / maxMem);
                points.Add(new Vector3(x, y, 0));
            }
            
            Handles.DrawAAPolyLine(3f, points.ToArray());
            Handles.EndGUI();
            
            GUI.Label(new Rect(rect.x + 5, rect.y + 5, 200, 20), 
                $"Peak: {FormatBytes(maxMem)}", 
                new GUIStyle(EditorStyles.miniLabel) { normal = new GUIStyleState { textColor = Color.white } });
            
            EditorGUILayout.Space(5);
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
            EditorGUILayout.LabelField("Memory", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
            
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(150));
            
            foreach (var atlas in _atlases)
            {
                EditorGUILayout.BeginHorizontal("box");
                
                EditorGUILayout.LabelField(atlas.name, GUILayout.Width(120));
                EditorGUILayout.LabelField($"{atlas.width}x{atlas.height}", GUILayout.Width(80));
                EditorGUILayout.LabelField(atlas.format.ToString(), GUILayout.Width(70));
                EditorGUILayout.LabelField(atlas.entryCount.ToString(), GUILayout.Width(60));
                
                var fillColor = atlas.fillRatio >= 0.8f ? Color.green : atlas.fillRatio >= 0.5f ? Color.yellow : Color.red;
                var oldColor = GUI.contentColor;
                GUI.contentColor = fillColor;
                EditorGUILayout.LabelField($"{atlas.fillRatio * 100:F0}%", GUILayout.Width(50));
                GUI.contentColor = oldColor;
                
                EditorGUILayout.LabelField(FormatBytes(atlas.memoryBytes), GUILayout.ExpandWidth(true));
                
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
                if (atlas.texture == null) continue;
                
                EditorGUILayout.BeginVertical("box");
                
                EditorGUILayout.LabelField($"{atlas.name} - {atlas.width}x{atlas.height}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Textures: {atlas.entryCount}, Fill: {atlas.fillRatio * 100:F0}%, Memory: {FormatBytes(atlas.memoryBytes)}");
                
                float previewSize = Mathf.Min(300, position.width - 40);
                float aspect = (float)atlas.height / atlas.width;
                float previewHeight = previewSize * aspect;
                
                var previewRect = GUILayoutUtility.GetRect(previewSize, previewHeight);
                EditorGUI.DrawPreviewTexture(previewRect, atlas.texture, null, ScaleMode.ScaleToFit);
                
                EditorGUILayout.Space(5);
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(10);
            }
            
            EditorGUILayout.EndScrollView();
        }

        private string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024f:F2} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024f * 1024f):F2} MB";
            return $"{bytes / (1024f * 1024f * 1024f):F2} GB";
        }

        private class AtlasInfo
        {
            public string name;
            public RuntimeAtlas atlas;
            public int width;
            public int height;
            public TextureFormat format;
            public int entryCount;
            public float fillRatio;
            public long memoryBytes;
            public Texture2D texture;
        }

        private struct GraphPoint
        {
            public float time;
            public long memoryBytes;
            public int atlasCount;
            public int textureCount;
        }
    }
}

