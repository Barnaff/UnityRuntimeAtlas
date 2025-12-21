using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace RuntimeAtlasPacker.Editor
{
    /// <summary>
    /// Profiler window for tracking atlas operations and performance.
    /// Automatically enabled and recording when play mode starts.
    /// </summary>
    public class AtlasProfilerWindow : EditorWindow
    {
        private Vector2 _scrollPosition;
        private int _maxLogEntries = 500;
        
        private static readonly List<ProfileEntry> _entries = new();
        private static readonly Stopwatch _stopwatch = new();
        
        // Filters
        private bool _showAdd = true;
        private bool _showRemove = true;
        private bool _showResize = true;
        private bool _showRepack = true;
        private bool _showOther = true;
        
        // Statistics
        private int _totalOperations;
        private double _totalTimeMs;
        private double _avgTimeMs;
        private int _addCount;
        private int _removeCount;
        private int _resizeCount;
        private int _repackCount;
        
        [MenuItem("Window/Runtime Atlas Packer/Profiler")]
        public static void ShowWindow()
        {
            var window = GetWindow<AtlasProfilerWindow>();
            window.titleContent = new GUIContent("Atlas Profiler", EditorGUIUtility.IconContent("d_Profiler.Memory").image);
            window.minSize = new Vector2(500, 300);
            window.Show();
        }

        private void OnEnable()
        {
            // Connect to both profiling systems
            AtlasProfiler.OnOperationLogged += OnOperationLogged;
            RuntimeAtlasProfiler.EditorProfilerBridge += OnRuntimeProfilerData;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            
            // Ensure runtime profiler is enabled
            RuntimeAtlasProfiler.Enabled = true;
        }

        private void OnDisable()
        {
            AtlasProfiler.OnOperationLogged -= OnOperationLogged;
            RuntimeAtlasProfiler.EditorProfilerBridge -= OnRuntimeProfilerData;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                // Clear entries when entering play mode for fresh data
                _entries.Clear();
                UpdateStatistics();
                
                // Ensure runtime profiling is enabled
                RuntimeAtlasProfiler.Enabled = true;
                
                Repaint();
            }
        }

        private void OnRuntimeProfilerData(ProfileData data)
        {
            // Convert RuntimeAtlasProfiler data to ProfileEntry
            var entry = new ProfileEntry
            {
                OperationType = ParseOperationType(data.OperationType),
                AtlasName = data.AtlasName ?? "Unknown",
                Details = data.Details ?? "",
                Timestamp = data.Timestamp,
                DurationMs = data.DurationMs
            };
            
            OnOperationLogged(entry);
        }

        private ProfileOperationType ParseOperationType(string opType)
        {
            if (string.IsNullOrEmpty(opType)) return ProfileOperationType.Other;
            
            if (opType.Contains("Add")) return ProfileOperationType.Add;
            if (opType.Contains("Remove")) return ProfileOperationType.Remove;
            if (opType.Contains("Resize")) return ProfileOperationType.Resize;
            if (opType.Contains("Repack")) return ProfileOperationType.Repack;
            if (opType.Contains("Clear")) return ProfileOperationType.Clear;
            if (opType.Contains("Apply")) return ProfileOperationType.Apply;
            
            return ProfileOperationType.Other;
        }

        private void OnOperationLogged(ProfileEntry entry)
        {
            _entries.Add(entry);
            
            // Trim if too many entries
            while (_entries.Count > _maxLogEntries)
            {
                _entries.RemoveAt(0);
            }
            
            UpdateStatistics();
            Repaint();
        }

        private void UpdateStatistics()
        {
            _totalOperations = _entries.Count;
            _totalTimeMs = 0;
            _addCount = 0;
            _removeCount = 0;
            _resizeCount = 0;
            _repackCount = 0;
            
            foreach (var entry in _entries)
            {
                _totalTimeMs += entry.DurationMs;
                
                switch (entry.OperationType)
                {
                    case ProfileOperationType.Add:
                    case ProfileOperationType.AddBatch:
                    case ProfileOperationType.AddAsync:
                        _addCount++;
                        break;
                    case ProfileOperationType.Remove:
                        _removeCount++;
                        break;
                    case ProfileOperationType.Resize:
                        _resizeCount++;
                        break;
                    case ProfileOperationType.Repack:
                        _repackCount++;
                        break;
                }
            }
            
            _avgTimeMs = _totalOperations > 0 ? _totalTimeMs / _totalOperations : 0;
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawStatistics();
            DrawFilters();
            DrawLogEntries();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            // Status indicator
            var statusColor = EditorApplication.isPlaying ? Color.green : Color.gray;
            var oldColor = GUI.backgroundColor;
            GUI.backgroundColor = statusColor;
            GUILayout.Label(EditorApplication.isPlaying ? "● Recording" : "○ Waiting for Play Mode", 
                EditorStyles.toolbarButton, GUILayout.Width(150));
            GUI.backgroundColor = oldColor;
            
            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                _entries.Clear();
                UpdateStatistics();
            }
            
            GUILayout.FlexibleSpace();
            
            EditorGUILayout.LabelField("Max Entries:", GUILayout.Width(75));
            _maxLogEntries = EditorGUILayout.IntField(_maxLogEntries, GUILayout.Width(60));
            _maxLogEntries = Mathf.Clamp(_maxLogEntries, 100, 10000);
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatistics()
        {
            EditorGUILayout.BeginHorizontal();
            
            DrawStatBox("Operations", _totalOperations.ToString());
            DrawStatBox("Total Time", $"{_totalTimeMs:F2}ms");
            DrawStatBox("Avg Time", $"{_avgTimeMs:F3}ms");
            DrawStatBox("Add", _addCount.ToString());
            DrawStatBox("Remove", _removeCount.ToString());
            DrawStatBox("Resize", _resizeCount.ToString());
            DrawStatBox("Repack", _repackCount.ToString());
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatBox(string label, string value)
        {
            EditorGUILayout.BeginVertical("box", GUILayout.Width(70));
            EditorGUILayout.LabelField(label, EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawFilters()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Filter:", GUILayout.Width(40));
            
            _showAdd = GUILayout.Toggle(_showAdd, "Add", EditorStyles.miniButtonLeft);
            _showRemove = GUILayout.Toggle(_showRemove, "Remove", EditorStyles.miniButtonMid);
            _showResize = GUILayout.Toggle(_showResize, "Resize", EditorStyles.miniButtonMid);
            _showRepack = GUILayout.Toggle(_showRepack, "Repack", EditorStyles.miniButtonMid);
            _showOther = GUILayout.Toggle(_showOther, "Other", EditorStyles.miniButtonRight);
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLogEntries()
        {
            // Header
            EditorGUILayout.BeginHorizontal("box");
            EditorGUILayout.LabelField("Time", EditorStyles.boldLabel, GUILayout.Width(80));
            EditorGUILayout.LabelField("Operation", EditorStyles.boldLabel, GUILayout.Width(100));
            EditorGUILayout.LabelField("Atlas", EditorStyles.boldLabel, GUILayout.Width(100));
            EditorGUILayout.LabelField("Details", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField("Duration", EditorStyles.boldLabel, GUILayout.Width(70));
            EditorGUILayout.EndHorizontal();
            
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            
            // Draw entries in reverse order (newest first)
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                
                // Apply filters
                if (!ShouldShowEntry(entry)) continue;
                
                EditorGUILayout.BeginHorizontal("box");
                
                EditorGUILayout.LabelField(entry.Timestamp.ToString("HH:mm:ss.fff"), GUILayout.Width(80));
                
                // Color-coded operation type
                var oldColor = GUI.color;
                GUI.color = GetOperationColor(entry.OperationType);
                EditorGUILayout.LabelField(entry.OperationType.ToString(), GUILayout.Width(100));
                GUI.color = oldColor;
                
                EditorGUILayout.LabelField(entry.AtlasName ?? "-", GUILayout.Width(100));
                EditorGUILayout.LabelField(entry.Details ?? "-", GUILayout.ExpandWidth(true));
                
                // Highlight slow operations
                var durationColor = entry.DurationMs > 10 ? Color.red : 
                                    entry.DurationMs > 5 ? Color.yellow : Color.white;
                oldColor = GUI.color;
                GUI.color = durationColor;
                EditorGUILayout.LabelField($"{entry.DurationMs:F2}ms", GUILayout.Width(70));
                GUI.color = oldColor;
                
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.EndScrollView();
        }

        private bool ShouldShowEntry(ProfileEntry entry)
        {
            switch (entry.OperationType)
            {
                case ProfileOperationType.Add:
                case ProfileOperationType.AddBatch:
                case ProfileOperationType.AddAsync:
                    return _showAdd;
                case ProfileOperationType.Remove:
                    return _showRemove;
                case ProfileOperationType.Resize:
                    return _showResize;
                case ProfileOperationType.Repack:
                    return _showRepack;
                default:
                    return _showOther;
            }
        }

        private Color GetOperationColor(ProfileOperationType type)
        {
            switch (type)
            {
                case ProfileOperationType.Add:
                case ProfileOperationType.AddBatch:
                case ProfileOperationType.AddAsync:
                    return new Color(0.5f, 1f, 0.5f);
                case ProfileOperationType.Remove:
                    return new Color(1f, 0.5f, 0.5f);
                case ProfileOperationType.Resize:
                    return new Color(1f, 1f, 0.5f);
                case ProfileOperationType.Repack:
                    return new Color(0.5f, 0.5f, 1f);
                default:
                    return Color.white;
            }
        }
    }

    /// <summary>
    /// Static profiler class for logging atlas operations.
    /// </summary>
    public static class AtlasProfiler
    {
        public static event Action<ProfileEntry> OnOperationLogged;
        
        private static readonly Stopwatch _stopwatch = new();
        
        // Always enabled
        public static bool Enabled => true;

        /// <summary>
        /// Begin timing an operation. Returns a handle to stop timing.
        /// </summary>
        public static ProfileHandle Begin(ProfileOperationType type, string atlasName = null, string details = null)
        {
            return new ProfileHandle
            {
                OperationType = type,
                AtlasName = atlasName,
                Details = details,
                StartTime = DateTime.Now,
                StopwatchStart = Stopwatch.GetTimestamp()
            };
        }

        /// <summary>
        /// End timing an operation and log it.
        /// </summary>
        public static void End(ProfileHandle handle)
        {
            if (handle.StopwatchStart == 0) return;
            
            var elapsed = (Stopwatch.GetTimestamp() - handle.StopwatchStart) * 1000.0 / Stopwatch.Frequency;
            
            var entry = new ProfileEntry
            {
                OperationType = handle.OperationType,
                AtlasName = handle.AtlasName,
                Details = handle.Details,
                Timestamp = handle.StartTime,
                DurationMs = elapsed
            };
            
            OnOperationLogged?.Invoke(entry);
        }

        /// <summary>
        /// Log an operation with known duration.
        /// </summary>
        public static void Log(ProfileOperationType type, double durationMs, string atlasName = null, string details = null)
        {
            var entry = new ProfileEntry
            {
                OperationType = type,
                AtlasName = atlasName,
                Details = details,
                Timestamp = DateTime.Now,
                DurationMs = durationMs
            };
            
            OnOperationLogged?.Invoke(entry);
        }
    }

    public struct ProfileHandle
    {
        public ProfileOperationType OperationType;
        public string AtlasName;
        public string Details;
        public DateTime StartTime;
        public long StopwatchStart;
    }

    public struct ProfileEntry
    {
        public ProfileOperationType OperationType;
        public string AtlasName;
        public string Details;
        public DateTime Timestamp;
        public double DurationMs;
    }

    public enum ProfileOperationType
    {
        Add,
        AddBatch,
        AddAsync,
        Remove,
        Resize,
        Repack,
        Clear,
        Apply,
        Other
    }
}
