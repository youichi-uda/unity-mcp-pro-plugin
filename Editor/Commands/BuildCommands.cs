using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcpPro
{
    public class BuildCommands : BaseCommand
    {
        public static void Register(CommandRouter router)
        {
            router.Register("get_build_settings", GetBuildSettings);
            router.Register("set_build_scenes", SetBuildScenes);
            router.Register("build_player", BuildPlayer);
            router.Register("get_scripting_defines", GetScriptingDefines);
            router.Register("set_scripting_defines", SetScriptingDefines);
        }

        private static object GetBuildSettings(Dictionary<string, object> p)
        {
            var scenes = EditorBuildSettings.scenes;
            var sceneList = new List<object>();
            foreach (var scene in scenes)
            {
                sceneList.Add(new Dictionary<string, object>
                {
                    { "path", scene.path },
                    { "enabled", scene.enabled },
                    { "guid", scene.guid.ToString() }
                });
            }

            return new Dictionary<string, object>
            {
                { "scenes", sceneList },
                { "activeBuildTarget", EditorUserBuildSettings.activeBuildTarget.ToString() },
                { "buildTargetGroup", EditorUserBuildSettings.selectedBuildTargetGroup.ToString() },
                { "development", EditorUserBuildSettings.development },
                { "connectProfiler", EditorUserBuildSettings.connectProfiler }
            };
        }

        private static object SetBuildScenes(Dictionary<string, object> p)
        {
            var scenePaths = GetStringListParam(p, "scenes");
            bool append = GetBoolParam(p, "append");

            if (scenePaths == null || scenePaths.Length == 0)
                throw new ArgumentException("scenes is required");

            var newScenes = scenePaths.Select(s => new EditorBuildSettingsScene(s, true)).ToArray();

            if (append)
            {
                var existing = EditorBuildSettings.scenes.ToList();
                existing.AddRange(newScenes);
                EditorBuildSettings.scenes = existing.ToArray();
            }
            else
            {
                EditorBuildSettings.scenes = newScenes;
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "sceneCount", EditorBuildSettings.scenes.Length }
            };
        }

        private static object BuildPlayer(Dictionary<string, object> p)
        {
            string outputPath = GetStringParam(p, "output_path");
            string targetStr = GetStringParam(p, "target");
            bool development = GetBoolParam(p, "development");

            if (string.IsNullOrEmpty(outputPath))
                throw new ArgumentException("output_path is required");

            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            if (!string.IsNullOrEmpty(targetStr))
            {
                if (Enum.TryParse<BuildTarget>(targetStr, true, out var parsed))
                    target = parsed;
                else
                    throw new ArgumentException($"Unknown build target: {targetStr}");
            }

            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
                throw new Exception("No scenes in build settings. Use set_build_scenes first.");

            BuildOptions options = BuildOptions.None;
            if (development)
                options |= BuildOptions.Development;

            string optionsStr = GetStringParam(p, "options");
            if (!string.IsNullOrEmpty(optionsStr))
            {
                foreach (var opt in optionsStr.Split(','))
                {
                    if (Enum.TryParse<BuildOptions>(opt.Trim(), true, out var parsedOpt))
                        options |= parsedOpt;
                }
            }

            var report = BuildPipeline.BuildPlayer(scenes, outputPath, target, options);

            return new Dictionary<string, object>
            {
                { "success", report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded },
                { "result", report.summary.result.ToString() },
                { "outputPath", report.summary.outputPath },
                { "totalSize", report.summary.totalSize.ToString() },
                { "totalTime", report.summary.totalTime.TotalSeconds },
                { "totalErrors", report.summary.totalErrors },
                { "totalWarnings", report.summary.totalWarnings }
            };
        }

        private static object GetScriptingDefines(Dictionary<string, object> p)
        {
            string groupStr = GetStringParam(p, "target_group");
            BuildTargetGroup group = EditorUserBuildSettings.selectedBuildTargetGroup;

            if (!string.IsNullOrEmpty(groupStr))
            {
                if (Enum.TryParse<BuildTargetGroup>(groupStr, true, out var parsed))
                    group = parsed;
            }

            string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);

            return new Dictionary<string, object>
            {
                { "targetGroup", group.ToString() },
                { "defines", defines.Split(';').Where(s => !string.IsNullOrEmpty(s)).ToArray() }
            };
        }

        private static object SetScriptingDefines(Dictionary<string, object> p)
        {
            var defines = GetStringListParam(p, "defines");
            string groupStr = GetStringParam(p, "target_group");
            bool append = GetBoolParam(p, "append");

            if (defines == null)
                throw new ArgumentException("defines is required");

            BuildTargetGroup group = EditorUserBuildSettings.selectedBuildTargetGroup;
            if (!string.IsNullOrEmpty(groupStr))
            {
                if (Enum.TryParse<BuildTargetGroup>(groupStr, true, out var parsed))
                    group = parsed;
            }

            string result;
            if (append)
            {
                string existing = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
                var allDefines = existing.Split(';')
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Concat(defines)
                    .Distinct()
                    .ToArray();
                result = string.Join(";", allDefines);
            }
            else
            {
                result = string.Join(";", defines);
            }

            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, result);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "targetGroup", group.ToString() },
                { "defines", result }
            };
        }
    }
}
