using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcpPro
{
    public abstract class BaseCommand
    {
        protected static object Success(object data = null)
        {
            var result = new Dictionary<string, object>
            {
                { "success", true }
            };
            if (data != null)
            {
                result["data"] = data;
            }
            return result;
        }

        protected static object Success(string message)
        {
            return new Dictionary<string, object>
            {
                { "success", true },
                { "message", message }
            };
        }

        protected static string GetStringParam(Dictionary<string, object> p, string key, string defaultValue = null)
        {
            if (p.TryGetValue(key, out var val) && val != null)
                return val.ToString();
            return defaultValue;
        }

        protected static int GetIntParam(Dictionary<string, object> p, string key, int defaultValue = 0)
        {
            if (p.TryGetValue(key, out var val) && val != null)
            {
                if (val is double d) return (int)d;
                if (val is long l) return (int)l;
                if (val is int i) return i;
                if (int.TryParse(val.ToString(), out int parsed)) return parsed;
            }
            return defaultValue;
        }

        protected static bool GetBoolParam(Dictionary<string, object> p, string key, bool defaultValue = false)
        {
            if (p.TryGetValue(key, out var val) && val != null)
            {
                if (val is bool b) return b;
                if (bool.TryParse(val.ToString(), out bool parsed)) return parsed;
            }
            return defaultValue;
        }

        protected static float GetFloatParam(Dictionary<string, object> p, string key, float defaultValue = 0f)
        {
            if (p.TryGetValue(key, out var val) && val != null)
            {
                if (val is double d) return (float)d;
                if (val is float f) return f;
                if (val is long l) return l;
                if (val is int i) return i;
                if (float.TryParse(val.ToString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float parsed)) return parsed;
            }
            return defaultValue;
        }

        protected static Dictionary<string, object> GetDictParam(Dictionary<string, object> p, string key)
        {
            if (p.TryGetValue(key, out var val) && val is Dictionary<string, object> dict)
                return dict;
            return null;
        }

        protected static string[] GetStringListParam(Dictionary<string, object> p, string key)
        {
            if (p.TryGetValue(key, out var val) && val != null)
            {
                if (val is List<object> list)
                    return list.Select(x => x?.ToString() ?? "").ToArray();
                if (val is string s)
                    return new[] { s };
            }
            return null;
        }

        protected static GameObject FindGameObject(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("GameObject path cannot be empty");

            // Try by exact path first (starting with /)
            if (path.StartsWith("/"))
            {
                var go = GameObject.Find(path);
                if (go != null) return go;
            }

            // Try by name
            var found = GameObject.Find(path);
            if (found != null) return found;

            // Try searching all root objects
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == path) return root;
                var child = FindChildRecursive(root.transform, path);
                if (child != null) return child.gameObject;
            }

            throw new ArgumentException($"GameObject not found: {path}");
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                var found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }

        protected static void RecordUndo(UnityEngine.Object obj, string actionName)
        {
            Undo.RecordObject(obj, $"MCP: {actionName}");
        }

        protected static string GetGameObjectPath(GameObject go)
        {
            string path = "/" + go.name;
            var parent = go.transform.parent;
            while (parent != null)
            {
                path = "/" + parent.name + path;
                parent = parent.parent;
            }
            return path;
        }

        protected static Component FindComponent(GameObject go, string componentName)
        {
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp != null && comp.GetType().Name.Equals(componentName, StringComparison.OrdinalIgnoreCase))
                    return comp;
            }
            throw new ArgumentException($"Component '{componentName}' not found on {go.name}");
        }

        protected static object GetSerializedPropertyValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer: return prop.intValue;
                case SerializedPropertyType.Boolean: return prop.boolValue;
                case SerializedPropertyType.Float: return prop.floatValue;
                case SerializedPropertyType.String: return prop.stringValue;
                case SerializedPropertyType.Color:
                    var c = prop.colorValue;
                    return $"Color({c.r},{c.g},{c.b},{c.a})";
                case SerializedPropertyType.Vector2:
                    var v2 = prop.vector2Value;
                    return $"Vector2({v2.x},{v2.y})";
                case SerializedPropertyType.Vector3:
                    var v3 = prop.vector3Value;
                    return $"Vector3({v3.x},{v3.y},{v3.z})";
                case SerializedPropertyType.Vector4:
                    var v4 = prop.vector4Value;
                    return $"Vector4({v4.x},{v4.y},{v4.z},{v4.w})";
                case SerializedPropertyType.Enum:
                    return prop.enumDisplayNames.Length > prop.enumValueIndex && prop.enumValueIndex >= 0
                        ? prop.enumDisplayNames[prop.enumValueIndex]
                        : prop.enumValueIndex.ToString();
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue != null ? prop.objectReferenceValue.name : null;
                case SerializedPropertyType.Quaternion:
                    var q = prop.quaternionValue.eulerAngles;
                    return $"Euler({q.x},{q.y},{q.z})";
                case SerializedPropertyType.Rect:
                    var r = prop.rectValue;
                    return $"Rect({r.x},{r.y},{r.width},{r.height})";
                case SerializedPropertyType.Bounds:
                    var b = prop.boundsValue;
                    return $"Bounds(center:{b.center}, size:{b.size})";
                default:
                    return prop.propertyType.ToString();
            }
        }

        protected static void SetSerializedPropertyValue(SerializedProperty prop, object value)
        {
            string strVal = value?.ToString() ?? "";

            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    prop.intValue = Convert.ToInt32(value);
                    break;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = Convert.ToBoolean(value);
                    break;
                case SerializedPropertyType.Float:
                    prop.floatValue = Convert.ToSingle(value);
                    break;
                case SerializedPropertyType.String:
                    prop.stringValue = strVal;
                    break;
                case SerializedPropertyType.Color:
                    prop.colorValue = TypeParser.ParseColor(strVal);
                    break;
                case SerializedPropertyType.Vector2:
                    prop.vector2Value = TypeParser.ParseVector2(strVal);
                    break;
                case SerializedPropertyType.Vector3:
                    prop.vector3Value = TypeParser.ParseVector3(strVal);
                    break;
                case SerializedPropertyType.Vector4:
                    prop.vector4Value = TypeParser.ParseVector4(strVal);
                    break;
                case SerializedPropertyType.Quaternion:
                    prop.quaternionValue = Quaternion.Euler(TypeParser.ParseVector3(strVal));
                    break;
                case SerializedPropertyType.Enum:
                    if (int.TryParse(strVal, out int enumIdx))
                        prop.enumValueIndex = enumIdx;
                    else
                    {
                        for (int i = 0; i < prop.enumDisplayNames.Length; i++)
                        {
                            if (prop.enumDisplayNames[i].Equals(strVal, StringComparison.OrdinalIgnoreCase))
                            {
                                prop.enumValueIndex = i;
                                break;
                            }
                        }
                    }
                    break;
            }
        }
    }
}
