using System;
using System.Diagnostics;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// Runtime-accessible profiler bridge for atlas operations.
    /// Always enabled in Unity Editor, automatically tracks all atlas operations.
    /// </summary>
    public static class RuntimeAtlasProfiler
    {
#if UNITY_EDITOR
        public static event Action<ProfileData> OnOperationLogged;
        
        // Always enabled in editor, can be disabled in builds
        private static bool _enabled = true;
        
        public static bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            _enabled = true;
        }

        /// <summary>
        /// Begin timing an operation. Returns a handle to stop timing.
        /// </summary>
        public static ProfileSession Begin(string operationType, string atlasName = null, string details = null)
        {
            if (!_enabled) return default;
            
            return new ProfileSession
            {
                OperationType = operationType,
                AtlasName = atlasName,
                Details = details,
                StartTime = DateTime.Now,
                StopwatchStart = Stopwatch.GetTimestamp()
            };
        }

        /// <summary>
        /// End timing an operation and log it.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void End(ProfileSession session)
        {
            if (!_enabled || session.StopwatchStart == 0) return;
            
            var elapsed = (Stopwatch.GetTimestamp() - session.StopwatchStart) * 1000.0 / Stopwatch.Frequency;
            
            var data = new ProfileData
            {
                OperationType = session.OperationType,
                AtlasName = session.AtlasName,
                Details = session.Details,
                Timestamp = session.StartTime,
                DurationMs = elapsed
            };
            
            OnOperationLogged?.Invoke(data);
            
            // Bridge to editor profiler if available
            NotifyEditorProfiler(data);
        }

        /// <summary>
        /// Log an operation with known duration.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void Log(string operationType, double durationMs, string atlasName = null, string details = null)
        {
            if (!_enabled) return;
            
            var data = new ProfileData
            {
                OperationType = operationType,
                AtlasName = atlasName,
                Details = details,
                Timestamp = DateTime.Now,
                DurationMs = durationMs
            };
            
            OnOperationLogged?.Invoke(data);
            
            NotifyEditorProfiler(data);
        }

        private static void NotifyEditorProfiler(ProfileData data)
        {
            // This will be called by the editor profiler window
            EditorProfilerBridge?.Invoke(data);
        }
        
        public static Action<ProfileData> EditorProfilerBridge;
#else
        // Stub methods for non-editor builds
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static ProfileSession Begin(string operationType, string atlasName = null, string details = null)
        {
            return default;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void End(ProfileSession session)
        {
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void Log(string operationType, double durationMs, string atlasName = null, string details = null)
        {
        }
#endif
    }

    public struct ProfileSession
    {
        public string OperationType;
        public string AtlasName;
        public string Details;
        public DateTime StartTime;
        public long StopwatchStart;
    }

    public struct ProfileData
    {
        public string OperationType;
        public string AtlasName;
        public string Details;
        public DateTime Timestamp;
        public double DurationMs;
    }
}

