using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityMcpPro
{
    public class ProjectCommands : BaseCommand
    {
        public static void Register(CommandRouter router)
        {
            router.Register("get_project_info", GetProjectInfo);
            router.Register("get_project_settings", GetProjectSettings);
            router.Register("get_asset_tree", GetAssetTree);
            router.Register("search_assets", SearchAssets);
            router.Register("search_in_files", SearchInFiles);
            router.Register("set_project_setting", SetProjectSetting);
            router.Register("get_resource_preview", GetResourcePreview);
        }

        private static object GetProjectInfo(Dictionary<string, object> p)
        {
            return new Dictionary<string, object>
            {
                { "project_name", Application.productName },
                { "company_name", Application.companyName },
                { "unity_version", Application.unityVersion },
                { "platform", EditorUserBuildSettings.activeBuildTarget.ToString() },
                { "scripting_backend", PlayerSettings.GetScriptingBackend(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup)).ToString() },
                { "color_space", PlayerSettings.colorSpace.ToString() },
                { "render_pipeline", UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                    ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.GetType().Name
                    : "Built-in" },
                { "project_path", Application.dataPath.Replace("/Assets", "") },
                { "is_playing", EditorApplication.isPlaying }
            };
        }

        private static object GetProjectSettings(Dictionary<string, object> p)
        {
            string category = GetStringParam(p, "category", "player");
            string key = GetStringParam(p, "key");

            switch (category.ToLower())
            {
                case "player":
                    var playerSettings = new Dictionary<string, object>
                    {
                        { "companyName", PlayerSettings.companyName },
                        { "productName", PlayerSettings.productName },
                        { "bundleVersion", PlayerSettings.bundleVersion },
                        { "defaultScreenWidth", PlayerSettings.defaultScreenWidth },
                        { "defaultScreenHeight", PlayerSettings.defaultScreenHeight },
                        { "fullscreenMode", PlayerSettings.fullScreenMode.ToString() },
                        { "runInBackground", PlayerSettings.runInBackground },
                        { "colorSpace", PlayerSettings.colorSpace.ToString() },
                        { "apiCompatibilityLevel", PlayerSettings.GetApiCompatibilityLevel(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup)).ToString() }
                    };
                    if (key != null && playerSettings.ContainsKey(key))
                        return new Dictionary<string, object> { { key, playerSettings[key] } };
                    return playerSettings;

                case "quality":
                    return new Dictionary<string, object>
                    {
                        { "currentLevel", QualitySettings.GetQualityLevel() },
                        { "names", QualitySettings.names },
                        { "vSyncCount", QualitySettings.vSyncCount },
                        { "antiAliasing", QualitySettings.antiAliasing },
                        { "shadowDistance", QualitySettings.shadowDistance },
                        { "shadowResolution", QualitySettings.shadowResolution.ToString() }
                    };

                case "physics":
                    return new Dictionary<string, object>
                    {
                        { "gravity", $"{Physics.gravity.x},{Physics.gravity.y},{Physics.gravity.z}" },
                        { "defaultSolverIterations", Physics.defaultSolverIterations },
                        { "defaultContactOffset", Physics.defaultContactOffset },
                        { "bounceThreshold", Physics.bounceThreshold }
                    };

                case "time":
                    return new Dictionary<string, object>
                    {
                        { "fixedDeltaTime", Time.fixedDeltaTime },
                        { "maximumDeltaTime", Time.maximumDeltaTime },
                        { "timeScale", Time.timeScale },
                        { "maximumParticleDeltaTime", Time.maximumParticleDeltaTime }
                    };

                case "audio":
                    return new Dictionary<string, object>
                    {
                        { "spatializerPlugin", AudioSettings.GetSpatializerPluginName() },
                        { "sampleRate", AudioSettings.outputSampleRate },
                        { "speakerMode", AudioSettings.speakerMode.ToString() }
                    };

                default:
                    throw new System.ArgumentException($"Unknown settings category: {category}. Use: player, quality, physics, time, audio");
            }
        }

        private static object GetAssetTree(Dictionary<string, object> p)
        {
            string relativePath = GetStringParam(p, "path", "");
            string filter = GetStringParam(p, "filter");
            int maxDepth = GetIntParam(p, "max_depth", 10);

            string rootPath = string.IsNullOrEmpty(relativePath)
                ? Application.dataPath
                : Path.Combine(Application.dataPath, relativePath);

            if (!Directory.Exists(rootPath))
                throw new System.ArgumentException($"Directory not found: {relativePath}");

            return BuildDirectoryTree(rootPath, filter, maxDepth, 0);
        }

        private static Dictionary<string, object> BuildDirectoryTree(string path, string filter, int maxDepth, int currentDepth)
        {
            var info = new DirectoryInfo(path);
            var result = new Dictionary<string, object>
            {
                { "name", info.Name },
                { "type", "directory" }
            };

            if (currentDepth >= maxDepth)
                return result;

            var children = new List<object>();

            // Add subdirectories
            foreach (var dir in info.GetDirectories())
            {
                if (dir.Name.StartsWith(".")) continue;
                children.Add(BuildDirectoryTree(dir.FullName, filter, maxDepth, currentDepth + 1));
            }

            // Add files
            var files = string.IsNullOrEmpty(filter)
                ? info.GetFiles()
                : info.GetFiles(filter);

            foreach (var file in files)
            {
                if (file.Extension == ".meta") continue;
                children.Add(new Dictionary<string, object>
                {
                    { "name", file.Name },
                    { "type", "file" },
                    { "size", file.Length }
                });
            }

            result["children"] = children;
            return result;
        }

        private static object SearchAssets(Dictionary<string, object> p)
        {
            string query = GetStringParam(p, "query");
            string searchPath = GetStringParam(p, "path", "Assets");
            int maxResults = GetIntParam(p, "max_results", 50);

            if (string.IsNullOrEmpty(query))
                throw new System.ArgumentException("Search query cannot be empty");

            var guids = AssetDatabase.FindAssets(query, new[] { searchPath });
            var results = new List<object>();
            int count = 0;

            foreach (var guid in guids)
            {
                if (count >= maxResults) break;

                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var assetType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);

                results.Add(new Dictionary<string, object>
                {
                    { "path", assetPath },
                    { "type", assetType?.Name ?? "Unknown" },
                    { "guid", guid }
                });
                count++;
            }

            return new Dictionary<string, object>
            {
                { "query", query },
                { "count", results.Count },
                { "total", guids.Length },
                { "results", results }
            };
        }
        private static object SearchInFiles(Dictionary<string, object> p)
        {
            string query = GetStringParam(p, "query");
            string relativePath = GetStringParam(p, "path", "");
            bool useRegex = GetBoolParam(p, "regex");
            int maxResults = GetIntParam(p, "max_results", 50);

            if (string.IsNullOrEmpty(query))
                throw new System.ArgumentException("query is required");

            string[] defaultExtensions = { ".cs", ".json", ".xml", ".yaml", ".txt", ".shader" };
            var extensionsList = GetStringListParam(p, "extensions") ?? defaultExtensions;

            string rootPath = string.IsNullOrEmpty(relativePath)
                ? Application.dataPath
                : Path.Combine(Application.dataPath, relativePath);

            if (!Directory.Exists(rootPath))
                throw new System.ArgumentException($"Directory not found: {relativePath}");

            var results = new List<object>();
            System.Text.RegularExpressions.Regex regex = null;
            if (useRegex)
                regex = new System.Text.RegularExpressions.Regex(query, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            SearchFilesRecursive(rootPath, query, regex, extensionsList, results, maxResults);

            return new Dictionary<string, object>
            {
                { "query", query },
                { "count", results.Count },
                { "capped", results.Count >= maxResults },
                { "results", results }
            };
        }

        private static void SearchFilesRecursive(string dir, string query,
            System.Text.RegularExpressions.Regex regex, string[] extensions,
            List<object> results, int maxResults)
        {
            if (results.Count >= maxResults) return;

            foreach (var file in Directory.GetFiles(dir))
            {
                if (results.Count >= maxResults) break;

                string ext = Path.GetExtension(file).ToLower();
                bool extMatch = false;
                foreach (var e in extensions)
                {
                    if (ext == e.ToLower() || ext == e.TrimStart('*').ToLower())
                    {
                        extMatch = true;
                        break;
                    }
                }
                if (!extMatch) continue;

                try
                {
                    string content = File.ReadAllText(file);
                    string[] lines = content.Split('\n');

                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (results.Count >= maxResults) break;

                        bool match;
                        if (regex != null)
                            match = regex.IsMatch(lines[i]);
                        else
                            match = lines[i].IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0;

                        if (match)
                        {
                            string relativFilePath = file.Replace(Application.dataPath, "Assets").Replace('\\', '/');
                            results.Add(new Dictionary<string, object>
                            {
                                { "file", relativFilePath },
                                { "line", i + 1 },
                                { "content", lines[i].Trim() }
                            });
                        }
                    }
                }
                catch { }
            }

            foreach (var subDir in Directory.GetDirectories(dir))
            {
                if (results.Count >= maxResults) break;
                if (Path.GetFileName(subDir).StartsWith(".")) continue;
                SearchFilesRecursive(subDir, query, regex, extensions, results, maxResults);
            }
        }

        private static object SetProjectSetting(Dictionary<string, object> p)
        {
            string category = GetStringParam(p, "category");
            string key = GetStringParam(p, "key");
            object value = p.ContainsKey("value") ? p["value"] : null;

            if (string.IsNullOrEmpty(category))
                throw new System.ArgumentException("category is required");
            if (string.IsNullOrEmpty(key))
                throw new System.ArgumentException("key is required");

            string strVal = value?.ToString() ?? "";

            switch (category.ToLower())
            {
                case "player":
                    switch (key.ToLower())
                    {
                        case "companyname": PlayerSettings.companyName = strVal; break;
                        case "productname": PlayerSettings.productName = strVal; break;
                        case "bundleversion": PlayerSettings.bundleVersion = strVal; break;
                        case "defaultscreenwidth": PlayerSettings.defaultScreenWidth = int.Parse(strVal); break;
                        case "defaultscreenheight": PlayerSettings.defaultScreenHeight = int.Parse(strVal); break;
                        case "runinbackground": PlayerSettings.runInBackground = bool.Parse(strVal); break;
                        default: throw new System.ArgumentException($"Unknown player setting: {key}");
                    }
                    break;

                case "quality":
                    switch (key.ToLower())
                    {
                        case "vsynccount": QualitySettings.vSyncCount = int.Parse(strVal); break;
                        case "antialiasing": QualitySettings.antiAliasing = int.Parse(strVal); break;
                        case "shadowdistance": QualitySettings.shadowDistance = float.Parse(strVal); break;
                        default: throw new System.ArgumentException($"Unknown quality setting: {key}");
                    }
                    break;

                case "physics":
                    switch (key.ToLower())
                    {
                        case "gravity":
                            Physics.gravity = TypeParser.ParseVector3(strVal);
                            break;
                        case "defaultsolveriterations":
                            Physics.defaultSolverIterations = int.Parse(strVal);
                            break;
                        default: throw new System.ArgumentException($"Unknown physics setting: {key}");
                    }
                    break;

                case "time":
                    switch (key.ToLower())
                    {
                        case "fixeddeltatime": Time.fixedDeltaTime = float.Parse(strVal); break;
                        case "timescale": Time.timeScale = float.Parse(strVal); break;
                        case "maximumdeltatime": Time.maximumDeltaTime = float.Parse(strVal); break;
                        default: throw new System.ArgumentException($"Unknown time setting: {key}");
                    }
                    break;

                default:
                    throw new System.ArgumentException($"Unknown category: {category}. Use: player, quality, physics, time");
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "category", category },
                { "key", key },
                { "value", strVal },
                { "message", $"Set {category}.{key} = {strVal}" }
            };
        }

        private static object GetResourcePreview(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            int width = GetIntParam(p, "width", 128);
            int height = GetIntParam(p, "height", 128);

            if (string.IsNullOrEmpty(path))
                throw new System.ArgumentException("path is required");

            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null)
                throw new System.ArgumentException($"Asset not found: {path}");

            var preview = AssetPreview.GetAssetPreview(asset);

            // AssetPreview can be null if preview hasn't been generated yet
            int retries = 10;
            while (preview == null && retries > 0)
            {
                AssetPreview.SetPreviewTextureCacheSize(256);
                System.Threading.Thread.Sleep(100);
                preview = AssetPreview.GetAssetPreview(asset);
                retries--;
            }

            if (preview == null)
            {
                // Fallback: try mini thumbnail
                preview = AssetPreview.GetMiniThumbnail(asset);
            }

            if (preview == null)
            {
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "path", path },
                    { "message", "Preview not available for this asset" }
                };
            }

            // Resize if needed
            var resized = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var rt = RenderTexture.GetTemporary(width, height);
            Graphics.Blit(preview, rt);
            RenderTexture.active = rt;
            resized.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            resized.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);

            byte[] png = resized.EncodeToPNG();
            string base64 = System.Convert.ToBase64String(png);

            UnityEngine.Object.DestroyImmediate(resized);

            return new Dictionary<string, object>
            {
                { "image", base64 },
                { "path", path },
                { "assetType", asset.GetType().Name },
                { "width", width },
                { "height", height }
            };
        }
    }
}
