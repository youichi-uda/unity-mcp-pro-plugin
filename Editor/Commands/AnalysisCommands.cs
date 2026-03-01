using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMcpPro
{
    public class AnalysisCommands : BaseCommand
    {
        public static void Register(CommandRouter router)
        {
            router.Register("get_scene_statistics", GetSceneStatistics);
            router.Register("find_missing_references", FindMissingReferences);
            router.Register("find_unused_assets", FindUnusedAssets);
            router.Register("get_asset_dependencies", GetAssetDependencies);
            router.Register("get_memory_profile", GetMemoryProfile);
            router.Register("analyze_scripts", AnalyzeScripts);
            router.Register("find_script_references", FindScriptReferences);
            router.Register("detect_circular_dependencies", DetectCircularDependencies);
            router.Register("get_project_statistics", GetProjectStatistics);
            router.Register("analyze_scene_complexity", AnalyzeSceneComplexity);
        }

        private static object GetSceneStatistics(Dictionary<string, object> p)
        {
            var scene = SceneManager.GetActiveScene();
            var rootObjects = scene.GetRootGameObjects();

            int totalObjects = 0;
            int totalTriangles = 0;
            int totalVertices = 0;
            int totalMaterials = 0;
            int totalLights = 0;
            int totalCameras = 0;
            int totalColliders = 0;
            int totalRigidbodies = 0;
            var materialSet = new HashSet<Material>();

            foreach (var root in rootObjects)
            {
                var allTransforms = root.GetComponentsInChildren<Transform>(true);
                totalObjects += allTransforms.Length;

                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = mf.sharedMesh;
                    if (mesh != null)
                    {
                        totalTriangles += mesh.triangles.Length / 3;
                        totalVertices += mesh.vertexCount;
                    }
                }

                foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mesh = smr.sharedMesh;
                    if (mesh != null)
                    {
                        totalTriangles += mesh.triangles.Length / 3;
                        totalVertices += mesh.vertexCount;
                    }
                }

                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var mat in renderer.sharedMaterials)
                    {
                        if (mat != null) materialSet.Add(mat);
                    }
                }

                totalLights += root.GetComponentsInChildren<Light>(true).Length;
                totalCameras += root.GetComponentsInChildren<Camera>(true).Length;
                totalColliders += root.GetComponentsInChildren<Collider>(true).Length;
                totalRigidbodies += root.GetComponentsInChildren<Rigidbody>(true).Length;
            }

            totalMaterials = materialSet.Count;

            return new Dictionary<string, object>
            {
                { "sceneName", scene.name },
                { "totalGameObjects", totalObjects },
                { "totalTriangles", totalTriangles },
                { "totalVertices", totalVertices },
                { "uniqueMaterials", totalMaterials },
                { "lights", totalLights },
                { "cameras", totalCameras },
                { "colliders", totalColliders },
                { "rigidbodies", totalRigidbodies }
            };
        }

        private static object FindMissingReferences(Dictionary<string, object> p)
        {
            var scene = SceneManager.GetActiveScene();
            var rootObjects = scene.GetRootGameObjects();
            var missingScripts = new List<object>();
            var missingRefs = new List<object>();

            foreach (var root in rootObjects)
            {
                foreach (var go in root.GetComponentsInChildren<Transform>(true))
                {
                    var components = go.gameObject.GetComponents<Component>();
                    for (int i = 0; i < components.Length; i++)
                    {
                        if (components[i] == null)
                        {
                            missingScripts.Add(new Dictionary<string, object>
                            {
                                { "gameObject", GetGameObjectPath(go.gameObject) },
                                { "componentIndex", i }
                            });
                            continue;
                        }

                        var so = new SerializedObject(components[i]);
                        var prop = so.GetIterator();
                        while (prop.NextVisible(true))
                        {
                            if (prop.propertyType == SerializedPropertyType.ObjectReference &&
                                prop.objectReferenceValue == null &&
                                prop.objectReferenceInstanceIDValue != 0)
                            {
                                missingRefs.Add(new Dictionary<string, object>
                                {
                                    { "gameObject", GetGameObjectPath(go.gameObject) },
                                    { "component", components[i].GetType().Name },
                                    { "property", prop.propertyPath }
                                });
                            }
                        }
                    }
                }
            }

            return new Dictionary<string, object>
            {
                { "missingScripts", missingScripts },
                { "missingReferences", missingRefs },
                { "totalIssues", missingScripts.Count + missingRefs.Count }
            };
        }

        private static object FindUnusedAssets(Dictionary<string, object> p)
        {
            var buildScenes = EditorBuildSettings.scenes;
            var usedAssets = new HashSet<string>();

            foreach (var scene in buildScenes)
            {
                if (!scene.enabled) continue;
                var deps = AssetDatabase.GetDependencies(scene.path, true);
                foreach (var dep in deps)
                    usedAssets.Add(dep);
            }

            var allAssets = AssetDatabase.GetAllAssetPaths()
                .Where(path => path.StartsWith("Assets/") &&
                       !AssetDatabase.IsValidFolder(path) &&
                       !path.EndsWith(".cs") &&
                       !path.EndsWith(".asmdef") &&
                       !path.EndsWith(".asmref"))
                .ToArray();

            var unused = new List<object>();
            int maxResults = 100;

            foreach (var asset in allAssets)
            {
                if (unused.Count >= maxResults) break;
                if (!usedAssets.Contains(asset))
                {
                    unused.Add(new Dictionary<string, object>
                    {
                        { "path", asset },
                        { "type", AssetDatabase.GetMainAssetTypeAtPath(asset)?.Name ?? "Unknown" }
                    });
                }
            }

            return new Dictionary<string, object>
            {
                { "totalAssets", allAssets.Length },
                { "usedAssets", usedAssets.Count },
                { "unusedAssets", unused },
                { "unusedCount", unused.Count },
                { "capped", unused.Count >= maxResults }
            };
        }

        private static object GetAssetDependencies(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            bool recursive = GetBoolParam(p, "recursive", true);

            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("path is required");

            var deps = AssetDatabase.GetDependencies(path, recursive);
            var grouped = new Dictionary<string, List<string>>();

            foreach (var dep in deps)
            {
                if (dep == path) continue;
                string ext = Path.GetExtension(dep).ToLower();
                string category = GetAssetCategory(ext);
                if (!grouped.ContainsKey(category))
                    grouped[category] = new List<string>();
                grouped[category].Add(dep);
            }

            var result = new Dictionary<string, object>();
            foreach (var kvp in grouped)
                result[kvp.Key] = kvp.Value;

            return new Dictionary<string, object>
            {
                { "asset", path },
                { "totalDependencies", deps.Length - 1 },
                { "dependencies", result }
            };
        }

        private static string GetAssetCategory(string ext)
        {
            switch (ext)
            {
                case ".mat": return "materials";
                case ".shader": case ".shadergraph": case ".compute": return "shaders";
                case ".png": case ".jpg": case ".jpeg": case ".tga": case ".psd": case ".exr": return "textures";
                case ".fbx": case ".obj": case ".blend": case ".dae": return "models";
                case ".anim": return "animations";
                case ".controller": return "animators";
                case ".prefab": return "prefabs";
                case ".unity": return "scenes";
                case ".cs": return "scripts";
                case ".wav": case ".mp3": case ".ogg": return "audio";
                case ".ttf": case ".otf": return "fonts";
                case ".asset": return "scriptableObjects";
                default: return "other";
            }
        }

        private static object GetMemoryProfile(Dictionary<string, object> p)
        {
            var typeGroups = new Dictionary<string, int[]>(); // [count, estimatedBytes]

            string[] guids = AssetDatabase.FindAssets("", new[] { "Assets" });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path)) continue;

                var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                string typeName = type?.Name ?? "Unknown";

                if (!typeGroups.ContainsKey(typeName))
                    typeGroups[typeName] = new int[] { 0, 0 };

                typeGroups[typeName][0]++;

                try
                {
                    var fileInfo = new FileInfo(path);
                    if (fileInfo.Exists)
                        typeGroups[typeName][1] += (int)Math.Min(fileInfo.Length, int.MaxValue);
                }
                catch { }
            }

            var profile = new List<object>();
            foreach (var kvp in typeGroups.OrderByDescending(x => x.Value[1]))
            {
                profile.Add(new Dictionary<string, object>
                {
                    { "type", kvp.Key },
                    { "count", kvp.Value[0] },
                    { "sizeBytes", kvp.Value[1] },
                    { "sizeMB", Math.Round(kvp.Value[1] / (1024.0 * 1024.0), 2) }
                });
            }

            return new Dictionary<string, object>
            {
                { "totalAssets", guids.Length },
                { "profile", profile }
            };
        }

        private static object AnalyzeScripts(Dictionary<string, object> p)
        {
            string[] guids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" });
            var scripts = new List<object>();
            int totalLines = 0;

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".cs")) continue;

                try
                {
                    string content = File.ReadAllText(path);
                    string[] lines = content.Split('\n');
                    int lineCount = lines.Length;
                    totalLines += lineCount;

                    string className = null;
                    string baseClass = null;
                    string namespaceName = null;

                    var nsMatch = Regex.Match(content, @"namespace\s+([\w.]+)");
                    if (nsMatch.Success) namespaceName = nsMatch.Groups[1].Value;

                    var classMatch = Regex.Match(content, @"class\s+(\w+)\s*(?::\s*(\w+))?");
                    if (classMatch.Success)
                    {
                        className = classMatch.Groups[1].Value;
                        if (classMatch.Groups[2].Success)
                            baseClass = classMatch.Groups[2].Value;
                    }

                    scripts.Add(new Dictionary<string, object>
                    {
                        { "path", path },
                        { "className", className },
                        { "baseClass", baseClass },
                        { "namespace", namespaceName },
                        { "lineCount", lineCount }
                    });
                }
                catch { }
            }

            return new Dictionary<string, object>
            {
                { "totalScripts", scripts.Count },
                { "totalLines", totalLines },
                { "scripts", scripts }
            };
        }

        private static object FindScriptReferences(Dictionary<string, object> p)
        {
            string targetPath = GetStringParam(p, "path");
            int maxResults = GetIntParam(p, "max_results", 100);

            if (string.IsNullOrEmpty(targetPath))
                throw new ArgumentException("path is required");

            var allAssets = AssetDatabase.GetAllAssetPaths()
                .Where(path => path.StartsWith("Assets/") && !AssetDatabase.IsValidFolder(path))
                .ToArray();

            var references = new List<object>();

            foreach (var assetPath in allAssets)
            {
                if (references.Count >= maxResults) break;
                if (assetPath == targetPath) continue;

                var deps = AssetDatabase.GetDependencies(assetPath, false);
                foreach (var dep in deps)
                {
                    if (dep == targetPath)
                    {
                        references.Add(new Dictionary<string, object>
                        {
                            { "path", assetPath },
                            { "type", AssetDatabase.GetMainAssetTypeAtPath(assetPath)?.Name ?? "Unknown" }
                        });
                        break;
                    }
                }
            }

            return new Dictionary<string, object>
            {
                { "target", targetPath },
                { "referenceCount", references.Count },
                { "capped", references.Count >= maxResults },
                { "references", references }
            };
        }

        private static object DetectCircularDependencies(Dictionary<string, object> p)
        {
            string rootPath = GetStringParam(p, "path", "Assets");

            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { rootPath });
            var sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { rootPath });

            var allPaths = new List<string>();
            foreach (var guid in prefabGuids) allPaths.Add(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var guid in sceneGuids) allPaths.Add(AssetDatabase.GUIDToAssetPath(guid));

            var cycles = new List<object>();
            var visited = new HashSet<string>();
            var inStack = new HashSet<string>();

            foreach (var path in allPaths)
            {
                var currentPath = new List<string>();
                DetectCyclesDFS(path, visited, inStack, currentPath, cycles);
            }

            return new Dictionary<string, object>
            {
                { "assetsAnalyzed", allPaths.Count },
                { "cyclesFound", cycles.Count },
                { "cycles", cycles }
            };
        }

        private static void DetectCyclesDFS(string node, HashSet<string> visited,
            HashSet<string> inStack, List<string> currentPath, List<object> cycles)
        {
            if (inStack.Contains(node))
            {
                // Found a cycle
                int cycleStart = currentPath.IndexOf(node);
                if (cycleStart >= 0)
                {
                    var cycle = new List<string>();
                    for (int i = cycleStart; i < currentPath.Count; i++)
                        cycle.Add(currentPath[i]);
                    cycle.Add(node);
                    cycles.Add(cycle);
                }
                return;
            }

            if (visited.Contains(node)) return;

            visited.Add(node);
            inStack.Add(node);
            currentPath.Add(node);

            var deps = AssetDatabase.GetDependencies(node, false);
            foreach (var dep in deps)
            {
                if (dep == node) continue;
                if (dep.EndsWith(".prefab") || dep.EndsWith(".unity"))
                    DetectCyclesDFS(dep, visited, inStack, currentPath, cycles);
            }

            currentPath.RemoveAt(currentPath.Count - 1);
            inStack.Remove(node);
        }

        private static object GetProjectStatistics(Dictionary<string, object> p)
        {
            var stats = new Dictionary<string, object>();
            var fileCounts = new Dictionary<string, int>();
            long totalSize = 0;

            string[] allFiles;
            try
            {
                allFiles = Directory.GetFiles(Application.dataPath, "*.*", SearchOption.AllDirectories);
            }
            catch
            {
                allFiles = new string[0];
            }

            int totalScriptLines = 0;
            int scriptCount = 0;

            foreach (var file in allFiles)
            {
                string ext = Path.GetExtension(file).ToLower();
                if (ext == ".meta") continue;

                if (!fileCounts.ContainsKey(ext))
                    fileCounts[ext] = 0;
                fileCounts[ext]++;

                try
                {
                    totalSize += new FileInfo(file).Length;
                }
                catch { }

                if (ext == ".cs")
                {
                    scriptCount++;
                    try
                    {
                        totalScriptLines += File.ReadAllLines(file).Length;
                    }
                    catch { }
                }
            }

            stats["totalFiles"] = allFiles.Length;
            stats["totalSizeMB"] = Math.Round(totalSize / (1024.0 * 1024.0), 2);
            stats["scriptFiles"] = scriptCount;
            stats["totalScriptLines"] = totalScriptLines;

            // Count scenes
            var sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
            stats["scenes"] = sceneGuids.Length;

            // Count prefabs
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            stats["prefabs"] = prefabGuids.Length;

            // Count materials
            var matGuids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
            stats["materials"] = matGuids.Length;

            // Count textures
            var texGuids = AssetDatabase.FindAssets("t:Texture", new[] { "Assets" });
            stats["textures"] = texGuids.Length;

            // Top extensions
            var topExtensions = fileCounts.OrderByDescending(x => x.Value).Take(15);
            var extList = new List<object>();
            foreach (var kvp in topExtensions)
            {
                extList.Add(new Dictionary<string, object>
                {
                    { "extension", kvp.Key },
                    { "count", kvp.Value }
                });
            }
            stats["filesByExtension"] = extList;

            return stats;
        }

        private static object AnalyzeSceneComplexity(Dictionary<string, object> p)
        {
            string scenePath = GetStringParam(p, "scene_path");

            var scene = string.IsNullOrEmpty(scenePath)
                ? SceneManager.GetActiveScene()
                : SceneManager.GetActiveScene(); // Could load scene, but for simplicity use active

            var rootObjects = scene.GetRootGameObjects();
            int totalNodes = 0;
            int maxDepth = 0;
            int totalComponents = 0;
            var componentCounts = new Dictionary<string, int>();
            var warnings = new List<string>();

            foreach (var root in rootObjects)
            {
                AnalyzeNodeRecursive(root.transform, 0, ref totalNodes, ref maxDepth,
                    ref totalComponents, componentCounts);
            }

            // Generate warnings
            if (maxDepth > 10)
                warnings.Add($"Deep hierarchy detected (depth: {maxDepth}). Consider flattening for performance.");
            if (totalNodes > 5000)
                warnings.Add($"High node count ({totalNodes}). This may impact editor performance.");
            if (totalComponents > 10000)
                warnings.Add($"High component count ({totalComponents}). Consider component-based optimization.");

            if (componentCounts.ContainsKey("MeshRenderer"))
            {
                int meshCount = componentCounts["MeshRenderer"];
                if (meshCount > 1000)
                    warnings.Add($"High mesh renderer count ({meshCount}). Consider LOD groups or occlusion culling.");
            }

            // Top component types
            var topComponents = componentCounts.OrderByDescending(x => x.Value).Take(10);
            var compList = new List<object>();
            foreach (var kvp in topComponents)
            {
                compList.Add(new Dictionary<string, object>
                {
                    { "type", kvp.Key },
                    { "count", kvp.Value }
                });
            }

            return new Dictionary<string, object>
            {
                { "sceneName", scene.name },
                { "totalGameObjects", totalNodes },
                { "maxHierarchyDepth", maxDepth },
                { "totalComponents", totalComponents },
                { "rootObjectCount", rootObjects.Length },
                { "topComponentTypes", compList },
                { "warnings", warnings },
                { "warningCount", warnings.Count }
            };
        }

        private static void AnalyzeNodeRecursive(Transform node, int depth,
            ref int totalNodes, ref int maxDepth, ref int totalComponents,
            Dictionary<string, int> componentCounts)
        {
            totalNodes++;
            if (depth > maxDepth) maxDepth = depth;

            var components = node.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                totalComponents++;
                string typeName = comp.GetType().Name;
                if (!componentCounts.ContainsKey(typeName))
                    componentCounts[typeName] = 0;
                componentCounts[typeName]++;
            }

            foreach (Transform child in node)
            {
                AnalyzeNodeRecursive(child, depth + 1, ref totalNodes, ref maxDepth,
                    ref totalComponents, componentCounts);
            }
        }
    }
}
