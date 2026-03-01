using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityMcpPro
{
    public class PrefabCommands : BaseCommand
    {
        public static void Register(CommandRouter router)
        {
            router.Register("create_prefab", CreatePrefab);
            router.Register("instantiate_prefab", InstantiatePrefab);
            router.Register("get_prefab_info", GetPrefabInfo);
            router.Register("apply_prefab_overrides", ApplyPrefabOverrides);
            router.Register("revert_prefab_overrides", RevertPrefabOverrides);
            router.Register("unpack_prefab", UnpackPrefab);
        }

        private static object CreatePrefab(Dictionary<string, object> p)
        {
            string goPath = GetStringParam(p, "game_object_path");
            string savePath = GetStringParam(p, "save_path");

            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("game_object_path is required");
            if (string.IsNullOrEmpty(savePath))
                throw new ArgumentException("save_path is required");

            if (!savePath.EndsWith(".prefab"))
                savePath += ".prefab";

            var go = FindGameObject(goPath);

            // Ensure directory exists
            var dir = System.IO.Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            bool success;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, savePath, out success);

            if (!success || prefab == null)
                throw new Exception($"Failed to create prefab at {savePath}");

            return new Dictionary<string, object>
            {
                { "success", true },
                { "path", savePath },
                { "name", prefab.name }
            };
        }

        private static object InstantiatePrefab(Dictionary<string, object> p)
        {
            string prefabPath = GetStringParam(p, "prefab_path");
            string parentPath = GetStringParam(p, "parent");
            string posStr = GetStringParam(p, "position");
            string rotStr = GetStringParam(p, "rotation");
            string name = GetStringParam(p, "name");

            if (string.IsNullOrEmpty(prefabPath))
                throw new ArgumentException("prefab_path is required");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                throw new ArgumentException($"Prefab not found at: {prefabPath}");

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(instance, "MCP: Instantiate Prefab");

            if (!string.IsNullOrEmpty(parentPath))
            {
                var parent = FindGameObject(parentPath);
                instance.transform.SetParent(parent.transform, false);
            }

            if (!string.IsNullOrEmpty(posStr))
                instance.transform.position = TypeParser.ParseVector3(posStr);
            if (!string.IsNullOrEmpty(rotStr))
                instance.transform.eulerAngles = TypeParser.ParseVector3(rotStr);
            if (!string.IsNullOrEmpty(name))
                instance.name = name;

            return new Dictionary<string, object>
            {
                { "success", true },
                { "name", instance.name },
                { "path", GetGameObjectPath(instance) }
            };
        }

        private static object GetPrefabInfo(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("path is required");

            // Check if it's an asset path or scene object
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                var info = new Dictionary<string, object>
                {
                    { "name", prefab.name },
                    { "assetPath", path },
                    { "type", PrefabUtility.GetPrefabAssetType(prefab).ToString() },
                    { "childCount", prefab.transform.childCount }
                };

                var components = new List<string>();
                foreach (var comp in prefab.GetComponents<Component>())
                {
                    if (comp != null) components.Add(comp.GetType().Name);
                }
                info["components"] = components;

                return info;
            }

            // Try as scene object
            var go = FindGameObject(path);
            var status = PrefabUtility.GetPrefabInstanceStatus(go);
            var result = new Dictionary<string, object>
            {
                { "name", go.name },
                { "instanceStatus", status.ToString() },
                { "isPrefabInstance", status != PrefabInstanceStatus.NotAPrefab }
            };

            if (status != PrefabInstanceStatus.NotAPrefab)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
                if (source != null)
                {
                    result["sourcePrefab"] = AssetDatabase.GetAssetPath(source);
                }

                var overrides = PrefabUtility.GetObjectOverrides(go, false);
                result["overrideCount"] = overrides.Count;

                var mods = PrefabUtility.GetPropertyModifications(go);
                if (mods != null)
                {
                    var modList = new List<object>();
                    foreach (var mod in mods)
                    {
                        modList.Add(new Dictionary<string, object>
                        {
                            { "target", mod.target != null ? mod.target.GetType().Name : "null" },
                            { "propertyPath", mod.propertyPath },
                            { "value", mod.value }
                        });
                    }
                    result["modifications"] = modList;
                }
            }

            return result;
        }

        private static object ApplyPrefabOverrides(Dictionary<string, object> p)
        {
            string goPath = GetStringParam(p, "game_object_path");
            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("game_object_path is required");

            var go = FindGameObject(goPath);

            if (PrefabUtility.GetPrefabInstanceStatus(go) == PrefabInstanceStatus.NotAPrefab)
                throw new ArgumentException($"{go.name} is not a prefab instance");

            var root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
            PrefabUtility.ApplyPrefabInstance(root, InteractionMode.UserAction);

            return Success($"Applied overrides for {root.name}");
        }

        private static object RevertPrefabOverrides(Dictionary<string, object> p)
        {
            string goPath = GetStringParam(p, "game_object_path");
            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("game_object_path is required");

            var go = FindGameObject(goPath);

            if (PrefabUtility.GetPrefabInstanceStatus(go) == PrefabInstanceStatus.NotAPrefab)
                throw new ArgumentException($"{go.name} is not a prefab instance");

            var root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
            PrefabUtility.RevertPrefabInstance(root, InteractionMode.UserAction);

            return Success($"Reverted overrides for {root.name}");
        }

        private static object UnpackPrefab(Dictionary<string, object> p)
        {
            string goPath = GetStringParam(p, "game_object_path");
            string modeStr = GetStringParam(p, "mode", "OutermostRoot");

            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("game_object_path is required");

            var go = FindGameObject(goPath);

            if (PrefabUtility.GetPrefabInstanceStatus(go) == PrefabInstanceStatus.NotAPrefab)
                throw new ArgumentException($"{go.name} is not a prefab instance");

            var mode = modeStr.Equals("Completely", StringComparison.OrdinalIgnoreCase)
                ? PrefabUnpackMode.Completely
                : PrefabUnpackMode.OutermostRoot;

            PrefabUtility.UnpackPrefabInstance(go, mode, InteractionMode.UserAction);

            return Success($"Unpacked prefab {go.name} ({mode})");
        }
    }
}
