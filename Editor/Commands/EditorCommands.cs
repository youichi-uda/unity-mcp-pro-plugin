using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnityMcpPro
{
    public class EditorCommands : BaseCommand
    {
        private static readonly List<LogEntry> _capturedLogs = new List<LogEntry>();
        private static bool _logCaptureInitialized;

        private struct LogEntry
        {
            public string message;
            public string stackTrace;
            public LogType type;
            public double timestamp;
        }

        public static void Register(CommandRouter router)
        {
            InitLogCapture();
            router.Register("get_console_logs", GetConsoleLogs);
            router.Register("clear_console", ClearConsole);
            router.Register("refresh_asset_db", RefreshAssetDb);
            router.Register("execute_menu_item", ExecuteMenuItem);
            router.Register("get_performance_monitors", GetPerformanceMonitors);
        }

        private static void InitLogCapture()
        {
            if (_logCaptureInitialized) return;
            _logCaptureInitialized = true;
            Application.logMessageReceived += OnLogMessage;
        }

        private static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            _capturedLogs.Add(new LogEntry
            {
                message = condition,
                stackTrace = stackTrace,
                type = type,
                timestamp = EditorApplication.timeSinceStartup
            });

            // Keep buffer bounded
            while (_capturedLogs.Count > 500)
                _capturedLogs.RemoveAt(0);
        }

        private static object GetConsoleLogs(Dictionary<string, object> p)
        {
            string typeFilter = GetStringParam(p, "type", "all");
            int maxLines = GetIntParam(p, "max_lines", 50);

            var logs = new List<object>();
            int startIndex = Math.Max(0, _capturedLogs.Count - maxLines);

            for (int i = startIndex; i < _capturedLogs.Count; i++)
            {
                var entry = _capturedLogs[i];
                string entryType = entry.type.ToString().ToLower();

                if (typeFilter != "all")
                {
                    if (typeFilter == "error" && entry.type != LogType.Error && entry.type != LogType.Exception)
                        continue;
                    if (typeFilter == "warning" && entry.type != LogType.Warning)
                        continue;
                    if (typeFilter == "log" && entry.type != LogType.Log)
                        continue;
                }

                logs.Add(new Dictionary<string, object>
                {
                    { "message", entry.message },
                    { "type", entryType },
                    { "stackTrace", entry.stackTrace },
                    { "timestamp", entry.timestamp }
                });
            }

            return new Dictionary<string, object>
            {
                { "count", logs.Count },
                { "total_captured", _capturedLogs.Count },
                { "logs", logs }
            };
        }

        private static object ClearConsole(Dictionary<string, object> p)
        {
            _capturedLogs.Clear();

            // Also clear Unity's console window via reflection
            var logEntries = typeof(Editor).Assembly.GetType("UnityEditor.LogEntries");
            if (logEntries != null)
            {
                var clearMethod = logEntries.GetMethod("Clear",
                    BindingFlags.Static | BindingFlags.Public);
                clearMethod?.Invoke(null, null);
            }

            return Success("Console cleared");
        }

        private static object RefreshAssetDb(Dictionary<string, object> p)
        {
            AssetDatabase.Refresh();
            return Success("AssetDatabase refreshed");
        }
        private static object ExecuteMenuItem(Dictionary<string, object> p)
        {
            string menuPath = GetStringParam(p, "menu_path");
            if (string.IsNullOrEmpty(menuPath))
                throw new ArgumentException("menu_path is required");

            bool result = EditorApplication.ExecuteMenuItem(menuPath);
            if (!result)
                throw new InvalidOperationException($"Menu item not found or failed to execute: {menuPath}");

            return new Dictionary<string, object>
            {
                { "success", true },
                { "menuPath", menuPath },
                { "message", $"Executed menu item: {menuPath}" }
            };
        }

        private static object GetPerformanceMonitors(Dictionary<string, object> p)
        {
            var result = new Dictionary<string, object>();

            // Frame rate
            if (EditorApplication.isPlaying)
            {
                result["fps"] = 1.0f / Time.unscaledDeltaTime;
                result["deltaTime"] = Time.deltaTime;
                result["unscaledDeltaTime"] = Time.unscaledDeltaTime;
                result["frameCount"] = Time.frameCount;
                result["timeScale"] = Time.timeScale;
            }

            // Memory
            result["totalAllocatedMemoryMB"] = Math.Round(UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024.0 * 1024.0), 2);
            result["totalReservedMemoryMB"] = Math.Round(UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / (1024.0 * 1024.0), 2);
            result["totalUnusedReservedMemoryMB"] = Math.Round(UnityEngine.Profiling.Profiler.GetTotalUnusedReservedMemoryLong() / (1024.0 * 1024.0), 2);
            result["monoUsedSizeMB"] = Math.Round(UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / (1024.0 * 1024.0), 2);
            result["monoHeapSizeMB"] = Math.Round(UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() / (1024.0 * 1024.0), 2);

            // System
            result["systemMemoryMB"] = SystemInfo.systemMemorySize;
            result["graphicsMemoryMB"] = SystemInfo.graphicsMemorySize;
            result["processorCount"] = SystemInfo.processorCount;
            result["graphicsDeviceName"] = SystemInfo.graphicsDeviceName;

            // GC
            result["gcCollectionCount0"] = GC.CollectionCount(0);
            result["gcCollectionCount1"] = GC.CollectionCount(1);
            result["gcCollectionCount2"] = GC.CollectionCount(2);

            return result;
        }
    }
}
