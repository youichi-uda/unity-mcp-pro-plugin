using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnityMcpPro
{
    public class GameObjectCommands : BaseCommand
    {
        public static void Register(CommandRouter router)
        {
            router.Register("add_gameobject", AddGameObject);
            router.Register("delete_gameobject", DeleteGameObject);
            router.Register("rename_gameobject", RenameGameObject);
            router.Register("get_components", GetComponents);
            router.Register("update_component", UpdateComponent);
            router.Register("add_component", AddComponent);
            router.Register("set_transform", SetTransform);
            router.Register("duplicate_gameobject", DuplicateGameObject);
            router.Register("move_gameobject", MoveGameObject);
            router.Register("select_gameobject", SelectGameObject);
            router.Register("find_gameobjects", FindGameObjects);
        }

        private static object AddGameObject(Dictionary<string, object> p)
        {
            string name = GetStringParam(p, "name", "GameObject");
            string type = GetStringParam(p, "type", "Empty");
            string parentPath = GetStringParam(p, "parent");

            GameObject go;

            switch (type.ToLower())
            {
                case "cube": go = GameObject.CreatePrimitive(PrimitiveType.Cube); break;
                case "sphere": go = GameObject.CreatePrimitive(PrimitiveType.Sphere); break;
                case "capsule": go = GameObject.CreatePrimitive(PrimitiveType.Capsule); break;
                case "cylinder": go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); break;
                case "plane": go = GameObject.CreatePrimitive(PrimitiveType.Plane); break;
                case "quad": go = GameObject.CreatePrimitive(PrimitiveType.Quad); break;
                default: go = new GameObject(); break;
            }

            go.name = name;

            if (!string.IsNullOrEmpty(parentPath))
            {
                var parent = FindGameObject(parentPath);
                go.transform.SetParent(parent.transform, false);
            }

            // Apply initial properties
            var props = GetDictParam(p, "properties");
            if (props != null)
            {
                if (props.TryGetValue("position", out var pos))
                    go.transform.position = TypeParser.ParseVector3(pos.ToString());
                if (props.TryGetValue("rotation", out var rot))
                    go.transform.eulerAngles = TypeParser.ParseVector3(rot.ToString());
                if (props.TryGetValue("scale", out var scl))
                    go.transform.localScale = TypeParser.ParseVector3(scl.ToString());
            }

            Undo.RegisterCreatedObjectUndo(go, $"MCP: Create {name}");

            return new Dictionary<string, object>
            {
                { "success", true },
                { "name", go.name },
                { "type", type },
                { "path", GetGameObjectPath(go) }
            };
        }

        private static object DeleteGameObject(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            var go = FindGameObject(path);
            string goName = go.name;
            Undo.DestroyObjectImmediate(go);
            return Success($"Deleted GameObject: {goName}");
        }

        private static object RenameGameObject(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            string newName = GetStringParam(p, "new_name");

            if (string.IsNullOrEmpty(newName))
                throw new ArgumentException("new_name is required");

            var go = FindGameObject(path);
            RecordUndo(go, "Rename");
            string oldName = go.name;
            go.name = newName;

            return Success($"Renamed '{oldName}' to '{newName}'");
        }

        private static object GetComponents(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            string componentFilter = GetStringParam(p, "component");

            var go = FindGameObject(path);
            var components = go.GetComponents<Component>();
            var result = new List<object>();

            foreach (var comp in components)
            {
                if (comp == null) continue;
                string typeName = comp.GetType().Name;

                if (componentFilter != null &&
                    !typeName.Equals(componentFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var compData = new Dictionary<string, object>
                {
                    { "type", typeName }
                };

                // Serialize properties using SerializedObject
                var so = new SerializedObject(comp);
                var props = new Dictionary<string, object>();
                var prop = so.GetIterator();

                if (prop.NextVisible(true))
                {
                    do
                    {
                        if (prop.name == "m_Script") continue;
                        props[prop.name] = GetSerializedPropertyValue(prop);
                    }
                    while (prop.NextVisible(false));
                }

                compData["properties"] = props;
                result.Add(compData);
            }

            return new Dictionary<string, object>
            {
                { "gameObject", go.name },
                { "components", result }
            };
        }

        private static object UpdateComponent(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            string componentName = GetStringParam(p, "component");
            string property = GetStringParam(p, "property");
            object value = p.ContainsKey("value") ? p["value"] : null;

            if (string.IsNullOrEmpty(componentName))
                throw new ArgumentException("component is required");
            if (string.IsNullOrEmpty(property))
                throw new ArgumentException("property is required");

            var go = FindGameObject(path);
            var comp = FindComponent(go, componentName);

            RecordUndo(comp, $"Update {componentName}.{property}");

            // Use SerializedObject for reliable property setting
            var so = new SerializedObject(comp);
            var prop = so.FindProperty(property);

            if (prop != null)
            {
                SetSerializedPropertyValue(prop, value);
                so.ApplyModifiedProperties();
            }
            else
            {
                // Fallback: try reflection
                var fieldInfo = comp.GetType().GetField(property,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var propInfo = comp.GetType().GetProperty(property,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (propInfo != null && propInfo.CanWrite)
                {
                    propInfo.SetValue(comp, TypeParser.ConvertValue(value, propInfo.PropertyType));
                }
                else if (fieldInfo != null)
                {
                    fieldInfo.SetValue(comp, TypeParser.ConvertValue(value, fieldInfo.FieldType));
                }
                else
                {
                    throw new ArgumentException($"Property '{property}' not found on {componentName}");
                }
            }

            EditorUtility.SetDirty(comp);
            return Success($"Updated {componentName}.{property}");
        }

        private static object AddComponent(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            string componentName = GetStringParam(p, "component");

            if (string.IsNullOrEmpty(componentName))
                throw new ArgumentException("component is required");

            var go = FindGameObject(path);

            // Try to find the type
            Type componentType = TypeParser.FindComponentType(componentName);
            if (componentType == null)
                throw new ArgumentException($"Component type not found: {componentName}");

            var comp = Undo.AddComponent(go, componentType);

            // Apply initial properties
            var props = GetDictParam(p, "properties");
            if (props != null)
            {
                var so = new SerializedObject(comp);
                foreach (var kvp in props)
                {
                    var prop = so.FindProperty(kvp.Key);
                    if (prop != null)
                    {
                        SetSerializedPropertyValue(prop, kvp.Value);
                    }
                }
                so.ApplyModifiedProperties();
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "gameObject", go.name },
                { "component", componentType.Name }
            };
        }

        private static object SetTransform(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            string posStr = GetStringParam(p, "position");
            string rotStr = GetStringParam(p, "rotation");
            string scaleStr = GetStringParam(p, "scale");
            bool local = GetBoolParam(p, "local");

            var go = FindGameObject(path);
            RecordUndo(go.transform, "Set Transform");

            if (posStr != null)
            {
                var pos = TypeParser.ParseVector3(posStr);
                if (local) go.transform.localPosition = pos;
                else go.transform.position = pos;
            }

            if (rotStr != null)
            {
                var rot = TypeParser.ParseVector3(rotStr);
                if (local) go.transform.localEulerAngles = rot;
                else go.transform.eulerAngles = rot;
            }

            if (scaleStr != null)
            {
                go.transform.localScale = TypeParser.ParseVector3(scaleStr);
            }

            EditorUtility.SetDirty(go);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "gameObject", go.name },
                { "position", $"{go.transform.position.x},{go.transform.position.y},{go.transform.position.z}" },
                { "rotation", $"{go.transform.eulerAngles.x},{go.transform.eulerAngles.y},{go.transform.eulerAngles.z}" },
                { "scale", $"{go.transform.localScale.x},{go.transform.localScale.y},{go.transform.localScale.z}" }
            };
        }

        private static object DuplicateGameObject(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            string newName = GetStringParam(p, "new_name");
            string parentPath = GetStringParam(p, "parent");

            var original = FindGameObject(path);
            var duplicate = UnityEngine.Object.Instantiate(original);
            duplicate.name = !string.IsNullOrEmpty(newName) ? newName : original.name + " (Copy)";

            if (!string.IsNullOrEmpty(parentPath))
            {
                var parent = FindGameObject(parentPath);
                duplicate.transform.SetParent(parent.transform, false);
            }
            else if (original.transform.parent != null)
            {
                duplicate.transform.SetParent(original.transform.parent, false);
            }

            Undo.RegisterCreatedObjectUndo(duplicate, $"MCP: Duplicate {original.name}");

            return new Dictionary<string, object>
            {
                { "success", true },
                { "original", GetGameObjectPath(original) },
                { "duplicate", GetGameObjectPath(duplicate) },
                { "name", duplicate.name }
            };
        }

        private static object MoveGameObject(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            string newParentPath = GetStringParam(p, "new_parent");
            int siblingIndex = GetIntParam(p, "sibling_index", -1);

            var go = FindGameObject(path);
            Undo.SetTransformParent(go.transform, null, $"MCP: Move {go.name}");

            if (!string.IsNullOrEmpty(newParentPath))
            {
                var newParent = FindGameObject(newParentPath);
                Undo.SetTransformParent(go.transform, newParent.transform, $"MCP: Move {go.name}");
            }
            else
            {
                go.transform.SetParent(null);
            }

            if (siblingIndex >= 0)
                go.transform.SetSiblingIndex(siblingIndex);

            EditorUtility.SetDirty(go);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "gameObject", go.name },
                { "newPath", GetGameObjectPath(go) },
                { "siblingIndex", go.transform.GetSiblingIndex() }
            };
        }

        private static object SelectGameObject(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            bool ping = GetBoolParam(p, "ping", true);

            var go = FindGameObject(path);
            Selection.activeGameObject = go;

            if (ping)
                EditorGUIUtility.PingObject(go);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "selected", go.name },
                { "path", GetGameObjectPath(go) }
            };
        }

        private static object FindGameObjects(Dictionary<string, object> p)
        {
            string query = GetStringParam(p, "query");
            string tag = GetStringParam(p, "tag");
            string componentName = GetStringParam(p, "component");
            string layerName = GetStringParam(p, "layer");
            bool includeInactive = GetBoolParam(p, "include_inactive");
            int maxResults = GetIntParam(p, "max_results", 100);

            var results = new List<object>();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var rootObjects = scene.GetRootGameObjects();

            // Collect candidates
            Type componentType = null;
            if (!string.IsNullOrEmpty(componentName))
            {
                componentType = TypeParser.FindComponentType(componentName);
                if (componentType == null)
                    throw new ArgumentException($"Component type not found: {componentName}");
            }

            int layerIndex = -1;
            if (!string.IsNullOrEmpty(layerName))
            {
                layerIndex = LayerMask.NameToLayer(layerName);
                if (layerIndex < 0)
                    throw new ArgumentException($"Layer not found: {layerName}");
            }

            // If tag is specified, use Unity's tag search first
            GameObject[] candidates;
            if (!string.IsNullOrEmpty(tag))
            {
                try
                {
                    candidates = GameObject.FindGameObjectsWithTag(tag);
                }
                catch
                {
                    throw new ArgumentException($"Tag not found: {tag}");
                }
            }
            else
            {
                // Collect all objects from scene
                var allObjects = new List<GameObject>();
                foreach (var root in rootObjects)
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive))
                        allObjects.Add(t.gameObject);
                }
                candidates = allObjects.ToArray();
            }

            foreach (var go in candidates)
            {
                if (results.Count >= maxResults) break;
                if (!includeInactive && !go.activeInHierarchy) continue;

                // Name filter
                if (!string.IsNullOrEmpty(query))
                {
                    if (go.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }

                // Component filter
                if (componentType != null && go.GetComponent(componentType) == null)
                    continue;

                // Layer filter
                if (layerIndex >= 0 && go.layer != layerIndex)
                    continue;

                results.Add(new Dictionary<string, object>
                {
                    { "name", go.name },
                    { "path", GetGameObjectPath(go) },
                    { "tag", go.tag },
                    { "layer", LayerMask.LayerToName(go.layer) },
                    { "active", go.activeSelf },
                    { "childCount", go.transform.childCount }
                });
            }

            return new Dictionary<string, object>
            {
                { "count", results.Count },
                { "results", results }
            };
        }

        // Helper methods now in BaseCommand: FindComponent, GetGameObjectPath, GetSerializedPropertyValue, SetSerializedPropertyValue
    }
}
