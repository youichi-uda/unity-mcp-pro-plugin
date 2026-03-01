using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnityMcpPro
{
    public class DebugCommands : BaseCommand
    {
        public static void Register(CommandRouter router)
        {
            router.Register("get_play_state", GetPlayState);
            router.Register("inspect_runtime", InspectRuntime);
            router.Register("call_method", CallMethod);
            router.Register("find_objects_of_type", FindObjectsOfType);
            router.Register("debug_log_inject", DebugLogInject);
        }

        // -----------------------------------------------------------------
        // get_play_state — プレイモード状態、時間情報
        // -----------------------------------------------------------------
        private static object GetPlayState(Dictionary<string, object> p)
        {
            return new Dictionary<string, object>
            {
                { "isPlaying", EditorApplication.isPlaying },
                { "isPaused", EditorApplication.isPaused },
                { "isCompiling", EditorApplication.isCompiling },
                { "timeSinceStartup", EditorApplication.timeSinceStartup },
                { "gameTime", Application.isPlaying ? Time.time : 0f },
                { "timeScale", Time.timeScale },
                { "frameCount", Application.isPlaying ? Time.frameCount : 0 },
                { "targetFrameRate", Application.targetFrameRate },
                { "platform", Application.platform.ToString() }
            };
        }

        // -----------------------------------------------------------------
        // inspect_runtime — リフレクションでコンポーネントのフィールド・プロパティを取得
        //   play mode中でもSerializedObjectではなくリフレクションで「実際の値」を読む
        // -----------------------------------------------------------------
        private static object InspectRuntime(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            string componentName = GetStringParam(p, "component");
            bool includePrivate = GetBoolParam(p, "include_private", false);

            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("path is required");

            var go = FindGameObject(path);

            // componentが指定されない場合、全コンポーネントのサマリーを返す
            if (string.IsNullOrEmpty(componentName))
            {
                var compSummary = new List<object>();
                foreach (var c in go.GetComponents<Component>())
                {
                    if (c == null) continue;
                    var entry = new Dictionary<string, object>
                    {
                        { "type", c.GetType().Name },
                        { "enabled", c is Behaviour b ? (object)b.enabled : "N/A" },
                        { "fullType", c.GetType().FullName }
                    };
                    compSummary.Add(entry);
                }

                return new Dictionary<string, object>
                {
                    { "gameObject", go.name },
                    { "active", go.activeSelf },
                    { "activeInHierarchy", go.activeInHierarchy },
                    { "tag", go.tag },
                    { "layer", LayerMask.LayerToName(go.layer) },
                    { "position", FormatVector3(go.transform.position) },
                    { "rotation", FormatVector3(go.transform.eulerAngles) },
                    { "scale", FormatVector3(go.transform.localScale) },
                    { "components", compSummary }
                };
            }

            // 特定コンポーネントの詳細
            var comp = FindComponent(go, componentName);
            var flags = BindingFlags.Public | BindingFlags.Instance;
            if (includePrivate)
                flags |= BindingFlags.NonPublic;

            var fields = new Dictionary<string, object>();
            foreach (var fi in comp.GetType().GetFields(flags))
            {
                if (fi.IsSpecialName) continue;
                try
                {
                    fields[fi.Name] = SerializeRuntimeValue(fi.GetValue(comp));
                }
                catch (Exception ex)
                {
                    fields[fi.Name] = $"<error: {ex.Message}>";
                }
            }

            var properties = new Dictionary<string, object>();
            foreach (var pi in comp.GetType().GetProperties(flags))
            {
                if (!pi.CanRead) continue;
                if (pi.GetIndexParameters().Length > 0) continue;
                // Skip heavy Unity properties that could cause issues
                string name = pi.Name;
                if (name == "mesh" || name == "material" || name == "materials" ||
                    name == "sharedMesh" || name == "sharedMaterial" || name == "sharedMaterials")
                    continue;
                try
                {
                    properties[pi.Name] = SerializeRuntimeValue(pi.GetValue(comp));
                }
                catch
                {
                    // Skip properties that throw
                }
            }

            var result = new Dictionary<string, object>
            {
                { "gameObject", go.name },
                { "component", comp.GetType().Name },
                { "enabled", comp is Behaviour bh ? (object)bh.enabled : "N/A" },
                { "fields", fields },
                { "properties", properties }
            };

            return result;
        }

        // -----------------------------------------------------------------
        // call_method — コンポーネントのメソッドを呼び出す
        // -----------------------------------------------------------------
        private static object CallMethod(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            string componentName = GetStringParam(p, "component");
            string methodName = GetStringParam(p, "method");
            var args = GetStringListParam(p, "args");

            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("path is required");
            if (string.IsNullOrEmpty(componentName))
                throw new ArgumentException("component is required");
            if (string.IsNullOrEmpty(methodName))
                throw new ArgumentException("method is required");

            var go = FindGameObject(path);
            var comp = FindComponent(go, componentName);

            // Find method (public + non-public instance)
            var methods = comp.GetType().GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            ).Where(m => m.Name == methodName).ToArray();

            if (methods.Length == 0)
                throw new ArgumentException($"Method '{methodName}' not found on {componentName}");

            // Try to find matching overload
            MethodInfo method = null;
            object[] convertedArgs = null;
            int argCount = args?.Length ?? 0;

            foreach (var m in methods)
            {
                var methodParams = m.GetParameters();
                if (methodParams.Length != argCount) continue;

                try
                {
                    convertedArgs = new object[argCount];
                    for (int i = 0; i < argCount; i++)
                    {
                        convertedArgs[i] = TypeParser.ConvertValue(args[i], methodParams[i].ParameterType);
                    }
                    method = m;
                    break;
                }
                catch
                {
                    // Try next overload
                }
            }

            if (method == null)
            {
                // Fallback: call with no args or string args
                method = methods.FirstOrDefault(m => m.GetParameters().Length == argCount)
                      ?? methods.First();
                convertedArgs = args?.Select(a => (object)a).ToArray() ?? new object[0];
            }

            object returnVal;
            try
            {
                returnVal = method.Invoke(comp, convertedArgs);
            }
            catch (TargetInvocationException tie)
            {
                throw new Exception($"Method threw: {tie.InnerException?.Message ?? tie.Message}");
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "gameObject", go.name },
                { "component", componentName },
                { "method", methodName },
                { "returnValue", SerializeRuntimeValue(returnVal) }
            };
        }

        // -----------------------------------------------------------------
        // find_objects_of_type — シーン内の指定型オブジェクトを一覧
        // -----------------------------------------------------------------
        private static object FindObjectsOfType(Dictionary<string, object> p)
        {
            string typeName = GetStringParam(p, "type");
            bool includeInactive = GetBoolParam(p, "include_inactive", true);

            if (string.IsNullOrEmpty(typeName))
                throw new ArgumentException("type is required");

            Type t = TypeParser.FindComponentType(typeName);
            if (t == null)
                throw new ArgumentException($"Type not found: {typeName}");

            // Use FindObjectsByType (Unity 2023+) or fallback
            UnityEngine.Object[] objects;
            try
            {
                objects = UnityEngine.Object.FindObjectsByType(t,
                    includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);
            }
            catch
            {
                // Older Unity fallback
                #pragma warning disable CS0618
                objects = UnityEngine.Object.FindObjectsOfType(t);
                #pragma warning restore CS0618
            }

            var results = new List<object>();
            foreach (var obj in objects)
            {
                if (obj is Component comp)
                {
                    var entry = new Dictionary<string, object>
                    {
                        { "name", comp.gameObject.name },
                        { "path", GetGameObjectPath(comp.gameObject) },
                        { "active", comp.gameObject.activeSelf },
                        { "enabled", comp is Behaviour b ? (object)b.enabled : true }
                    };
                    results.Add(entry);
                }
                else
                {
                    results.Add(new Dictionary<string, object>
                    {
                        { "name", obj.name },
                        { "type", obj.GetType().Name }
                    });
                }
            }

            return new Dictionary<string, object>
            {
                { "type", typeName },
                { "count", results.Count },
                { "objects", results }
            };
        }

        // -----------------------------------------------------------------
        // debug_log_inject — ランタイムにDebug.Logを発行
        //   特定のオブジェクト/コンポーネントの状態をログ出力させる
        // -----------------------------------------------------------------
        private static object DebugLogInject(Dictionary<string, object> p)
        {
            string expression = GetStringParam(p, "expression");
            string path = GetStringParam(p, "path");

            if (string.IsNullOrEmpty(expression))
                throw new ArgumentException("expression is required (e.g. 'transform.position', 'Input.GetKey(KeyCode.Space)')");

            // If path specified, evaluate on that GameObject
            if (!string.IsNullOrEmpty(path))
            {
                var go = FindGameObject(path);
                string result = EvaluateSimpleExpression(go, expression);
                Debug.Log($"[MCP Debug] {path}.{expression} = {result}");
                return new Dictionary<string, object>
                {
                    { "success", true },
                    { "expression", $"{path}.{expression}" },
                    { "result", result }
                };
            }

            // Static evaluation (Input, Time, etc.)
            string staticResult = EvaluateStaticExpression(expression);
            Debug.Log($"[MCP Debug] {expression} = {staticResult}");
            return new Dictionary<string, object>
            {
                { "success", true },
                { "expression", expression },
                { "result", staticResult }
            };
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------
        private static string SerializeRuntimeValue(object val)
        {
            if (val == null) return "null";
            if (val is Vector3 v3) return $"({v3.x:F3}, {v3.y:F3}, {v3.z:F3})";
            if (val is Vector2 v2) return $"({v2.x:F3}, {v2.y:F3})";
            if (val is Quaternion q) { var e = q.eulerAngles; return $"Euler({e.x:F1}, {e.y:F1}, {e.z:F1})"; }
            if (val is Color c) return $"Color({c.r:F2}, {c.g:F2}, {c.b:F2}, {c.a:F2})";
            if (val is bool b) return b.ToString().ToLower();
            if (val is float f) return f.ToString("F3");
            if (val is double d) return d.ToString("F3");
            if (val is Enum en) return en.ToString();
            if (val is UnityEngine.Object uobj) return uobj != null ? $"{uobj.GetType().Name}({uobj.name})" : "null (destroyed)";
            if (val is LayerMask lm) return lm.value.ToString();
            return val.ToString();
        }

        private static string FormatVector3(Vector3 v)
        {
            return $"({v.x:F3}, {v.y:F3}, {v.z:F3})";
        }

        private static string EvaluateSimpleExpression(GameObject go, string expression)
        {
            // Handle dot-separated property chains on a GameObject
            // e.g. "transform.position", "Rigidbody.linearVelocity"
            string[] parts = expression.Split('.');
            if (parts.Length == 0) return "empty expression";

            object current;

            // First part: component name or "transform"
            string first = parts[0];
            if (first.Equals("transform", StringComparison.OrdinalIgnoreCase))
            {
                current = go.transform;
            }
            else if (first.Equals("gameObject", StringComparison.OrdinalIgnoreCase))
            {
                current = go;
            }
            else
            {
                // Try as component
                Component comp = null;
                foreach (var c in go.GetComponents<Component>())
                {
                    if (c != null && c.GetType().Name.Equals(first, StringComparison.OrdinalIgnoreCase))
                    {
                        comp = c;
                        break;
                    }
                }
                if (comp == null) return $"Component '{first}' not found";
                current = comp;
            }

            // Walk remaining parts
            for (int i = 1; i < parts.Length; i++)
            {
                if (current == null) return "null";
                string member = parts[i];
                Type t = current.GetType();

                var fi = t.GetField(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi != null)
                {
                    current = fi.GetValue(current);
                    continue;
                }

                var pi = t.GetProperty(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pi != null && pi.CanRead)
                {
                    current = pi.GetValue(current);
                    continue;
                }

                return $"Member '{member}' not found on {t.Name}";
            }

            return SerializeRuntimeValue(current);
        }

        private static string EvaluateStaticExpression(string expression)
        {
            // Handle known static expressions for debugging
            string lower = expression.ToLower().Trim();

            // Input checks
            if (lower.StartsWith("input.getkey(") || lower.StartsWith("input.getkeydown("))
            {
                // Parse: Input.GetKey(KeyCode.Space)
                int start = expression.IndexOf('(') + 1;
                int end = expression.IndexOf(')');
                if (start > 0 && end > start)
                {
                    string keyStr = expression.Substring(start, end - start).Trim();
                    keyStr = keyStr.Replace("KeyCode.", "");
                    if (Enum.TryParse<KeyCode>(keyStr, true, out var key))
                    {
                        bool isDown = lower.Contains("getkeydown");
                        bool result = isDown ? Input.GetKeyDown(key) : Input.GetKey(key);
                        return result.ToString().ToLower();
                    }
                    return $"Unknown KeyCode: {keyStr}";
                }
            }

            // Input axis
            if (lower.StartsWith("input.getaxis(") || lower.StartsWith("input.getaxisraw("))
            {
                int start = expression.IndexOf('(') + 1;
                int end = expression.IndexOf(')');
                if (start > 0 && end > start)
                {
                    string axisName = expression.Substring(start, end - start).Trim().Trim('"', '\'');
                    bool raw = lower.Contains("raw");
                    try
                    {
                        float val = raw ? Input.GetAxisRaw(axisName) : Input.GetAxis(axisName);
                        return val.ToString("F3");
                    }
                    catch (Exception ex)
                    {
                        return $"Error: {ex.Message}";
                    }
                }
            }

            // Time
            if (lower == "time.time") return Time.time.ToString("F3");
            if (lower == "time.deltatime") return Time.deltaTime.ToString("F5");
            if (lower == "time.timescale") return Time.timeScale.ToString("F3");
            if (lower == "time.framecount") return Time.frameCount.ToString();

            // Screen
            if (lower == "screen.width") return Screen.width.ToString();
            if (lower == "screen.height") return Screen.height.ToString();

            // Cursor
            if (lower == "cursor.lockstate") return Cursor.lockState.ToString();
            if (lower == "cursor.visible") return Cursor.visible.ToString().ToLower();

            // Application
            if (lower == "application.isplaying") return Application.isPlaying.ToString().ToLower();
            if (lower == "application.targetframerate") return Application.targetFrameRate.ToString();

            return $"Unknown expression: {expression}. Supported: Input.GetKey(KeyCode.X), Input.GetAxis(\"name\"), Time.*, Cursor.*, Screen.*";
        }
    }
}
