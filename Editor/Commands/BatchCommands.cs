using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UnityMcpPro
{
    public class BatchCommands : BaseCommand
    {
        public static void Register(CommandRouter router)
        {
            _routerRef = router;
            router.Register("batch_rename", BatchRename);
            router.Register("batch_set_layer", BatchSetLayer);
            router.Register("batch_set_tag", BatchSetTag);
            router.Register("batch_set_static", BatchSetStatic);
            router.Register("batch_add_component", BatchAddComponent);
            router.Register("batch_execute", BatchExecute);
        }

        private static CommandRouter _routerRef;

        private static object BatchRename(Dictionary<string, object> p)
        {
            string parentPath = GetStringParam(p, "parent_path");
            string pattern = GetStringParam(p, "pattern");
            string replacement = GetStringParam(p, "replacement", "");
            bool useRegex = GetBoolParam(p, "regex");

            if (string.IsNullOrEmpty(pattern))
                throw new ArgumentException("pattern is required");

            List<Transform> targets = new List<Transform>();

            if (!string.IsNullOrEmpty(parentPath))
            {
                var parent = FindGameObject(parentPath);
                foreach (Transform child in parent.transform)
                    targets.Add(child);
            }
            else
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                foreach (var root in scene.GetRootGameObjects())
                    targets.Add(root.transform);
            }

            int renamed = 0;
            Undo.SetCurrentGroupName("MCP: Batch Rename");

            foreach (var t in targets)
            {
                string oldName = t.name;
                string newName;

                if (useRegex)
                    newName = Regex.Replace(oldName, pattern, replacement);
                else
                    newName = oldName.Replace(pattern, replacement);

                if (newName != oldName)
                {
                    Undo.RecordObject(t.gameObject, "MCP: Batch Rename");
                    t.name = newName;
                    renamed++;
                }
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "renamed", renamed },
                { "total", targets.Count }
            };
        }

        private static object BatchSetLayer(Dictionary<string, object> p)
        {
            var paths = GetStringListParam(p, "game_object_paths");
            string layer = GetStringParam(p, "layer");
            bool includeChildren = GetBoolParam(p, "include_children");

            if (paths == null || paths.Length == 0)
                throw new ArgumentException("game_object_paths is required");
            if (string.IsNullOrEmpty(layer))
                throw new ArgumentException("layer is required");

            int layerIdx = LayerMask.NameToLayer(layer);
            if (layerIdx < 0)
            {
                if (int.TryParse(layer, out int idx) && idx >= 0 && idx < 32)
                    layerIdx = idx;
                else
                    throw new ArgumentException($"Layer not found: {layer}");
            }

            int modified = 0;
            Undo.SetCurrentGroupName("MCP: Batch Set Layer");

            foreach (var path in paths)
            {
                var go = FindGameObject(path);
                SetLayerRecursive(go, layerIdx, includeChildren, ref modified);
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "modified", modified },
                { "layer", LayerMask.LayerToName(layerIdx) }
            };
        }

        private static void SetLayerRecursive(GameObject go, int layer, bool includeChildren, ref int count)
        {
            Undo.RecordObject(go, "MCP: Batch Set Layer");
            go.layer = layer;
            count++;

            if (includeChildren)
            {
                foreach (Transform child in go.transform)
                    SetLayerRecursive(child.gameObject, layer, true, ref count);
            }
        }

        private static object BatchSetTag(Dictionary<string, object> p)
        {
            var paths = GetStringListParam(p, "game_object_paths");
            string tag = GetStringParam(p, "tag");

            if (paths == null || paths.Length == 0)
                throw new ArgumentException("game_object_paths is required");
            if (string.IsNullOrEmpty(tag))
                throw new ArgumentException("tag is required");

            int modified = 0;
            Undo.SetCurrentGroupName("MCP: Batch Set Tag");

            foreach (var path in paths)
            {
                var go = FindGameObject(path);
                Undo.RecordObject(go, "MCP: Batch Set Tag");
                go.tag = tag;
                modified++;
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "modified", modified },
                { "tag", tag }
            };
        }

        private static object BatchSetStatic(Dictionary<string, object> p)
        {
            var paths = GetStringListParam(p, "game_object_paths");
            var flagNames = GetStringListParam(p, "static_flags");
            bool includeChildren = GetBoolParam(p, "include_children");

            if (paths == null || paths.Length == 0)
                throw new ArgumentException("game_object_paths is required");

            StaticEditorFlags flags = 0;
            if (flagNames != null)
            {
                foreach (var name in flagNames)
                {
                    if (name.Equals("Everything", StringComparison.OrdinalIgnoreCase))
                    {
                        flags = (StaticEditorFlags)(-1);
                        break;
                    }
                    if (Enum.TryParse<StaticEditorFlags>(name.Trim(), true, out var parsed))
                        flags |= parsed;
                }
            }

            int modified = 0;
            Undo.SetCurrentGroupName("MCP: Batch Set Static");

            foreach (var path in paths)
            {
                var go = FindGameObject(path);
                SetStaticRecursive(go, flags, includeChildren, ref modified);
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "modified", modified },
                { "flags", flags.ToString() }
            };
        }

        private static void SetStaticRecursive(GameObject go, StaticEditorFlags flags, bool includeChildren, ref int count)
        {
            Undo.RecordObject(go, "MCP: Batch Set Static");
            GameObjectUtility.SetStaticEditorFlags(go, flags);
            count++;

            if (includeChildren)
            {
                foreach (Transform child in go.transform)
                    SetStaticRecursive(child.gameObject, flags, true, ref count);
            }
        }

        private static object BatchAddComponent(Dictionary<string, object> p)
        {
            var paths = GetStringListParam(p, "game_object_paths");
            string componentName = GetStringParam(p, "component");

            if (paths == null || paths.Length == 0)
                throw new ArgumentException("game_object_paths is required");
            if (string.IsNullOrEmpty(componentName))
                throw new ArgumentException("component is required");

            Type componentType = TypeParser.FindComponentType(componentName);
            if (componentType == null)
                throw new ArgumentException($"Component type not found: {componentName}");

            var props = GetDictParam(p, "properties");
            int added = 0;
            Undo.SetCurrentGroupName("MCP: Batch Add Component");

            foreach (var path in paths)
            {
                var go = FindGameObject(path);
                var comp = Undo.AddComponent(go, componentType);
                added++;

                if (props != null)
                {
                    var so = new SerializedObject(comp);
                    foreach (var kvp in props)
                    {
                        var prop = so.FindProperty(kvp.Key);
                        if (prop != null)
                            SetSerializedPropertyValue(prop, kvp.Value);
                    }
                    so.ApplyModifiedProperties();
                }
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "added", added },
                { "component", componentType.Name }
            };
        }
        private static object BatchExecute(Dictionary<string, object> p)
        {
            var commands = p.ContainsKey("commands") ? p["commands"] as List<object> : null;
            bool stopOnError = GetBoolParam(p, "stop_on_error", true);

            if (commands == null || commands.Count == 0)
                throw new ArgumentException("commands is required and must be non-empty");

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("MCP: Batch Execute");

            var results = new List<object>();
            int succeeded = 0;
            int failed = 0;

            foreach (var cmdObj in commands)
            {
                var cmd = cmdObj as Dictionary<string, object>;
                if (cmd == null) continue;

                string method = cmd.ContainsKey("method") ? cmd["method"].ToString() : null;
                var cmdParams = cmd.ContainsKey("params") ? cmd["params"] as Dictionary<string, object> : new Dictionary<string, object>();

                if (string.IsNullOrEmpty(method))
                {
                    results.Add(new Dictionary<string, object>
                    {
                        { "method", "unknown" },
                        { "success", false },
                        { "error", "method is required" }
                    });
                    failed++;
                    if (stopOnError) break;
                    continue;
                }

                try
                {
                    var result = _routerRef.ExecuteDirect(method, cmdParams ?? new Dictionary<string, object>());
                    results.Add(new Dictionary<string, object>
                    {
                        { "method", method },
                        { "success", true },
                        { "result", result }
                    });
                    succeeded++;
                }
                catch (Exception ex)
                {
                    results.Add(new Dictionary<string, object>
                    {
                        { "method", method },
                        { "success", false },
                        { "error", ex.Message }
                    });
                    failed++;
                    if (stopOnError)
                    {
                        // Undo all changes on error
                        Undo.RevertAllDownToGroup(undoGroup);
                        break;
                    }
                }
            }

            Undo.CollapseUndoOperations(undoGroup);

            return new Dictionary<string, object>
            {
                { "totalCommands", commands.Count },
                { "succeeded", succeeded },
                { "failed", failed },
                { "stoppedOnError", stopOnError && failed > 0 },
                { "results", results }
            };
        }
    }
}
