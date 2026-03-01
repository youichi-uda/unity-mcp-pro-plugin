using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace UnityMcpPro
{
    public class ScriptCommands : BaseCommand
    {
        public static void Register(CommandRouter router)
        {
            router.Register("list_scripts", ListScripts);
            router.Register("read_script", ReadScript);
            router.Register("create_script", CreateScript);
            router.Register("edit_script", EditScript);
            router.Register("attach_script", AttachScript);
            router.Register("get_compilation_errors", GetCompilationErrors);
        }

        private static object ListScripts(Dictionary<string, object> p)
        {
            string relativePath = GetStringParam(p, "path", "");
            bool recursive = GetBoolParam(p, "recursive", true);

            string searchPath = string.IsNullOrEmpty(relativePath) ? "Assets" : "Assets/" + relativePath;
            var guids = AssetDatabase.FindAssets("t:MonoScript", new[] { searchPath });

            var scripts = new List<object>();
            foreach (var guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                if (!recursive && Path.GetDirectoryName(assetPath).Replace("\\", "/") != searchPath)
                    continue;

                if (!assetPath.EndsWith(".cs")) continue;

                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
                var scriptClass = script?.GetClass();

                scripts.Add(new Dictionary<string, object>
                {
                    { "path", assetPath },
                    { "name", Path.GetFileNameWithoutExtension(assetPath) },
                    { "class_name", scriptClass?.Name },
                    { "base_class", scriptClass?.BaseType?.Name },
                    { "namespace", scriptClass?.Namespace }
                });
            }

            return new Dictionary<string, object>
            {
                { "count", scripts.Count },
                { "scripts", scripts }
            };
        }

        private static object ReadScript(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Script path is required");

            string fullPath = path.StartsWith("Assets")
                ? Path.Combine(Application.dataPath.Replace("/Assets", ""), path)
                : path;

            if (!File.Exists(fullPath))
                throw new ArgumentException($"Script file not found: {path}");

            string content = File.ReadAllText(fullPath);

            return new Dictionary<string, object>
            {
                { "path", path },
                { "content", content },
                { "lines", content.Split('\n').Length }
            };
        }

        private static object CreateScript(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            string content = GetStringParam(p, "content");
            string baseClass = GetStringParam(p, "base_class", "MonoBehaviour");
            string ns = GetStringParam(p, "namespace");

            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Script path is required");

            if (!path.StartsWith("Assets/"))
                path = "Assets/" + path;

            if (!path.EndsWith(".cs"))
                path += ".cs";

            string fullPath = Path.Combine(Application.dataPath.Replace("/Assets", ""), path);

            // Create directory if needed
            string dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Generate template if no content provided
            if (string.IsNullOrEmpty(content))
            {
                string className = Path.GetFileNameWithoutExtension(path);
                content = GenerateTemplate(className, baseClass, ns);
            }

            File.WriteAllText(fullPath, content);
            AssetDatabase.Refresh();

            return Success($"Script created at {path}");
        }

        private static object EditScript(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            string fullContent = GetStringParam(p, "content");

            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Script path is required");

            string fullPath = path.StartsWith("Assets")
                ? Path.Combine(Application.dataPath.Replace("/Assets", ""), path)
                : path;

            if (!File.Exists(fullPath))
                throw new ArgumentException($"Script file not found: {path}");

            if (!string.IsNullOrEmpty(fullContent))
            {
                // Full replacement
                File.WriteAllText(fullPath, fullContent);
                AssetDatabase.Refresh();
                return Success($"Script replaced: {path}");
            }

            // Search and replace
            if (p.TryGetValue("replacements", out var replacementsObj) && replacementsObj is List<object> replacements)
            {
                string content = File.ReadAllText(fullPath);
                int totalReplacements = 0;

                foreach (var r in replacements)
                {
                    if (r is Dictionary<string, object> rep)
                    {
                        string search = rep.ContainsKey("search") ? rep["search"].ToString() : null;
                        string replace = rep.ContainsKey("replace") ? rep["replace"].ToString() : "";

                        if (string.IsNullOrEmpty(search)) continue;

                        if (content.Contains(search))
                        {
                            content = content.Replace(search, replace);
                            totalReplacements++;
                        }
                    }
                }

                File.WriteAllText(fullPath, content);
                AssetDatabase.Refresh();

                return new Dictionary<string, object>
                {
                    { "success", true },
                    { "path", path },
                    { "replacements_made", totalReplacements }
                };
            }

            throw new ArgumentException("Provide either 'content' (full replacement) or 'replacements' (search/replace)");
        }

        private static object AttachScript(Dictionary<string, object> p)
        {
            string goPath = GetStringParam(p, "game_object_path");
            string scriptPath = GetStringParam(p, "script_path");

            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("game_object_path is required");
            if (string.IsNullOrEmpty(scriptPath))
                throw new ArgumentException("script_path is required");

            var go = FindGameObject(goPath);

            // Force refresh to ensure script is compiled
            AssetDatabase.Refresh();

            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            if (script == null)
                throw new ArgumentException($"Script not found: {scriptPath}");

            var scriptClass = script.GetClass();
            if (scriptClass == null)
            {
                // Try finding the class by filename as fallback
                string className = System.IO.Path.GetFileNameWithoutExtension(scriptPath);
                scriptClass = TypeParser.FindComponentType(className);
                if (scriptClass == null)
                    throw new ArgumentException($"Script class not yet compiled: {scriptPath}. Try calling refresh_asset_db first and wait for compilation.");
            }

            if (!typeof(Component).IsAssignableFrom(scriptClass))
                throw new ArgumentException($"Script class '{scriptClass.Name}' does not derive from Component/MonoBehaviour");

            Undo.AddComponent(go, scriptClass);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "gameObject", go.name },
                { "script", scriptClass.Name }
            };
        }

        private static readonly List<Dictionary<string, object>> _compilationErrors = new List<Dictionary<string, object>>();
        private static readonly List<Dictionary<string, object>> _compilationWarnings = new List<Dictionary<string, object>>();
        private static bool _compilationListenerRegistered;

        private static void EnsureCompilationListener()
        {
            if (_compilationListenerRegistered) return;
            _compilationListenerRegistered = true;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            _compilationErrors.Clear();
            _compilationWarnings.Clear();

            foreach (var msg in messages)
            {
                var entry = new Dictionary<string, object>
                {
                    { "message", msg.message },
                    { "file", msg.file },
                    { "line", msg.line },
                    { "column", msg.column }
                };

                if (msg.type == CompilerMessageType.Error)
                    _compilationErrors.Add(entry);
                else if (msg.type == CompilerMessageType.Warning)
                    _compilationWarnings.Add(entry);
            }
        }

        private static object GetCompilationErrors(Dictionary<string, object> p)
        {
            EnsureCompilationListener();

            return new Dictionary<string, object>
            {
                { "errors", _compilationErrors },
                { "warnings", _compilationWarnings },
                { "error_count", _compilationErrors.Count },
                { "warning_count", _compilationWarnings.Count },
                { "has_errors", _compilationErrors.Count > 0 }
            };
        }

        private static string GenerateTemplate(string className, string baseClass, string ns)
        {
            string indent = string.IsNullOrEmpty(ns) ? "" : "    ";
            string classContent = $@"{indent}public class {className} : {baseClass}
{indent}{{
{indent}    void Start()
{indent}    {{
{indent}
{indent}    }}

{indent}    void Update()
{indent}    {{
{indent}
{indent}    }}
{indent}}}";

            if (!string.IsNullOrEmpty(ns))
            {
                return $@"using UnityEngine;

namespace {ns}
{{
{classContent}
}}
";
            }

            return $@"using UnityEngine;

{classContent}
";
        }
    }
}
