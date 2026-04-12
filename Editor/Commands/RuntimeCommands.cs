using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
#if HAS_UGUI
using UnityEngine.EventSystems;
using UnityEngine.UI;
#endif

namespace UnityMcpPro
{
    public class RuntimeCommands : BaseCommand
    {
        // Clear the reference cache after each domain reload so stale assembly
        // paths from a previous reload cycle don't cause duplicate-reference errors.
        [UnityEditor.InitializeOnLoadMethod]
        private static void OnDomainReload() => _referenceCache = null;

        public static void Register(CommandRouter router)
        {
            router.Register("monitor_properties", MonitorProperties);
            router.Register("execute_editor_script", ExecuteEditorScript);
            router.Register("execute_game_script", ExecuteGameScript);
#if HAS_UGUI
            router.Register("find_ui_elements", FindUIElements);
            router.Register("click_button_by_text", ClickButtonByText);
#endif
            router.Register("wait_for_node", WaitForNode);
            router.Register("find_nearby_objects", FindNearbyObjects);
        }

        private static object MonitorProperties(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            string componentName = GetStringParam(p, "component");
            string[] properties = GetStringListParam(p, "properties");
            float duration = GetFloatParam(p, "duration", 2f);
            float interval = GetFloatParam(p, "interval", 0.1f);

            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("path is required");
            if (string.IsNullOrEmpty(componentName))
                throw new ArgumentException("component is required");
            if (properties == null || properties.Length == 0)
                throw new ArgumentException("properties is required");

            var go = FindGameObject(path);
            var comp = FindComponent(go, componentName);
            var samples = new List<object>();
            float startTime = (float)EditorApplication.timeSinceStartup;
            float endTime = startTime + duration;
            float nextSample = startTime;

            EditorApplication.CallbackFunction monitorCallback = null;
            monitorCallback = () =>
            {
                float now = (float)EditorApplication.timeSinceStartup;
                if (now >= endTime)
                {
                    EditorApplication.update -= monitorCallback;
                    return;
                }

                if (now >= nextSample)
                {
                    var sample = new Dictionary<string, object>
                    {
                        { "time", Math.Round(now - startTime, 3) }
                    };

                    foreach (var propName in properties)
                    {
                        try
                        {
                            var propInfo = comp.GetType().GetProperty(propName,
                                BindingFlags.Public | BindingFlags.Instance);
                            if (propInfo != null)
                            {
                                sample[propName] = propInfo.GetValue(comp)?.ToString();
                            }
                            else
                            {
                                var fieldInfo = comp.GetType().GetField(propName,
                                    BindingFlags.Public | BindingFlags.Instance);
                                if (fieldInfo != null)
                                    sample[propName] = fieldInfo.GetValue(comp)?.ToString();
                            }
                        }
                        catch { sample[propName] = "error"; }
                    }

                    samples.Add(sample);
                    nextSample = now + interval;
                }
            };

            EditorApplication.update += monitorCallback;

            return new Dictionary<string, object>
            {
                { "status", "monitoring" },
                { "gameObject", go.name },
                { "component", componentName },
                { "properties", properties },
                { "duration", duration },
                { "interval", interval },
                { "message", $"Monitoring {properties.Length} properties for {duration}s" }
            };
        }

        private static object ExecuteEditorScript(Dictionary<string, object> p)
        {
            string code = GetStringParam(p, "code");
            if (string.IsNullOrEmpty(code))
                throw new ArgumentException("code is required");

            // Build a wrapper class and compile dynamically
            string wrappedCode = @"
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;

public static class McpDynamicScript
{
    public static object Execute()
    {
        " + code + @"
    }
}";

            try
            {
                // Use Unity's Mono compiler via reflection to compile and execute
                var assembly = CompileCode(wrappedCode);
                if (assembly == null)
                    throw new InvalidOperationException("Compilation failed");

                var type = assembly.GetType("McpDynamicScript");
                var method = type.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static);
                var result = method.Invoke(null, null);

                return new Dictionary<string, object>
                {
                    { "success", true },
                    { "result", result?.ToString() },
                    { "type", result?.GetType().Name ?? "null" }
                };
            }
            catch (TargetInvocationException tie)
            {
                throw new InvalidOperationException($"Script execution error: {tie.InnerException?.Message ?? tie.Message}");
            }
        }

        private static object ExecuteGameScript(Dictionary<string, object> p)
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Play mode is required for execute_game_script");

            return ExecuteEditorScript(p);
        }

        // Deduplicated list of loaded assembly paths, rebuilt once per domain load.
        private static List<string> _referenceCache;

        private static List<string> GetReferences()
        {
            if (_referenceCache != null) return _referenceCache;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var refs = new List<string>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location)) continue;
                    if (seen.Add(Path.GetFileName(asm.Location)))
                        refs.Add(asm.Location);
                }
                catch { }
            }
            _referenceCache = refs;
            return refs;
        }

        private static Assembly CompileCode(string code)
        {
            // Prefer Roslyn (csc.dll) via Unity's bundled .NET runtime for full C# support.
            // Fall back to CodeDom when the subprocess approach is unavailable.
            var contentsPath = EditorApplication.applicationContentsPath;
            var cscDll  = Path.Combine(contentsPath, "DotNetSdkRoslyn", "csc.dll");
            var dotnet  = Path.Combine(contentsPath, "NetCoreRuntime", "dotnet");

            if (File.Exists(cscDll) && File.Exists(dotnet))
                return CompileWithRoslyn(code, cscDll, dotnet);

            return CompileWithCodeDom(code);
        }

        private static Assembly CompileWithRoslyn(string code, string cscDll, string dotnet)
        {
            var tempDir    = Path.Combine(Path.GetTempPath(), "UnityMcpPro");
            Directory.CreateDirectory(tempDir);
            var sourceFile = Path.Combine(tempDir, "McpDynamic.cs");
            var outputDll  = Path.Combine(tempDir, "McpDynamic.dll");

            File.WriteAllText(sourceFile, code);

            // Build /reference: args, deduplicated by filename.
            var refArgs = string.Join(" ",
                GetReferences().Select(r => $"\"/reference:{r}\""));

            var arguments = $"\"{cscDll}\" /target:library /langversion:latest /optimize- " +
                            $"\"/out:{outputDll}\" {refArgs} \"{sourceFile}\"";

            var psi = new ProcessStartInfo
            {
                FileName               = dotnet,
                Arguments              = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };

            string stdout, stderr;
            int exitCode;
            using (var proc = Process.Start(psi))
            {
                stdout   = proc.StandardOutput.ReadToEnd();
                stderr   = proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                exitCode = proc.ExitCode;
            }

            if (exitCode != 0)
            {
                // Parse csc error lines: "file(line,col): error CS####: message"
                var errors = (stdout + "\n" + stderr)
                    .Split('\n')
                    .Where(l => l.Contains(": error ") || l.Contains(": Error "))
                    .Select(l =>
                    {
                        // Trim the temp file prefix so only "Line N: message" is shown.
                        var msgStart = l.IndexOf("): ", StringComparison.Ordinal);
                        if (msgStart >= 0) l = l.Substring(msgStart + 2).Trim();
                        return l;
                    })
                    .ToList();

                var message = errors.Count > 0
                    ? "Compilation errors:\n" + string.Join("\n", errors)
                    : $"Compilation failed (exit {exitCode}):\n{stdout}\n{stderr}".Trim();

                throw new InvalidOperationException(message);
            }

            var bytes = File.ReadAllBytes(outputDll);
            try { File.Delete(sourceFile); File.Delete(outputDll); } catch { }

            return Assembly.Load(bytes);
        }

        private static Assembly CompileWithCodeDom(string code)
        {
            var provider = new Microsoft.CSharp.CSharpCodeProvider();
            var compilerParams = new System.CodeDom.Compiler.CompilerParameters
            {
                GenerateInMemory   = true,
                GenerateExecutable = false,
            };

            // Deduplicate references to avoid "defined multiple times" errors
            // caused by Unity domain reloads re-registering the same assemblies.
            foreach (var path in GetReferences())
                compilerParams.ReferencedAssemblies.Add(path);

            var results = provider.CompileAssemblyFromSource(compilerParams, code);
            if (results.Errors.HasErrors)
            {
                var errors = new List<string>();
                foreach (System.CodeDom.Compiler.CompilerError error in results.Errors)
                {
                    if (error.IsWarning) continue;
                    if (error.ErrorText.Contains("is defined multiple times")) continue;
                    errors.Add($"Line {error.Line}: {error.ErrorText}");
                }
                if (errors.Count > 0)
                    throw new InvalidOperationException("Compilation errors:\n" + string.Join("\n", errors));
            }

            return results.CompiledAssembly;
        }

#if HAS_UGUI
        private static object FindUIElements(Dictionary<string, object> p)
        {
            string canvasName = GetStringParam(p, "canvas_name");
            string typeFilter = GetStringParam(p, "type_filter", "all");

            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var elements = new List<object>();

            foreach (var canvas in canvases)
            {
                if (canvasName != null && !canvas.name.Equals(canvasName, StringComparison.OrdinalIgnoreCase))
                    continue;

                ScanUIElement(canvas.transform, canvas.name, typeFilter, elements);
            }

            return new Dictionary<string, object>
            {
                { "canvasCount", canvases.Length },
                { "elementCount", elements.Count },
                { "elements", elements }
            };
        }

        private static void ScanUIElement(Transform parent, string canvasName, string typeFilter, List<object> elements)
        {
            foreach (Transform child in parent)
            {
                var go = child.gameObject;
                if (!go.activeInHierarchy) continue;

                var text = go.GetComponent<Text>();
                var button = go.GetComponent<Button>();
                var image = go.GetComponent<Image>();
                var toggle = go.GetComponent<Toggle>();
                var slider = go.GetComponent<Slider>();
                var inputField = go.GetComponent<InputField>();
                var dropdown = go.GetComponent<Dropdown>();

                string uiType = null;
                var info = new Dictionary<string, object>
                {
                    { "name", go.name },
                    { "path", GetGameObjectPath(go) },
                    { "canvas", canvasName },
                    { "active", go.activeSelf }
                };

                if (button != null) { uiType = "button"; info["interactable"] = button.interactable; }
                if (text != null)
                {
                    if (uiType == null) uiType = "text";
                    info["text"] = text.text;
                }
                if (image != null && uiType == null) uiType = "image";
                if (toggle != null) { uiType = "toggle"; info["isOn"] = toggle.isOn; }
                if (slider != null) { uiType = "slider"; info["value"] = slider.value; }
                if (inputField != null) { uiType = "input"; info["text"] = inputField.text; }
                if (dropdown != null) { uiType = "dropdown"; info["value"] = dropdown.value; }

                // Also check TMP components
                var tmpText = go.GetComponent("TMP_Text");
                if (tmpText != null)
                {
                    if (uiType == null) uiType = "text";
                    var textProp = tmpText.GetType().GetProperty("text");
                    if (textProp != null)
                        info["text"] = textProp.GetValue(tmpText)?.ToString();
                }

                if (uiType != null)
                {
                    bool matchFilter = typeFilter == "all" ||
                                       uiType.Equals(typeFilter, StringComparison.OrdinalIgnoreCase);
                    if (matchFilter)
                    {
                        info["uiType"] = uiType;
                        elements.Add(info);
                    }
                }

                ScanUIElement(child, canvasName, typeFilter, elements);
            }
        }

        private static object ClickButtonByText(Dictionary<string, object> p)
        {
            string searchText = GetStringParam(p, "text");
            bool partial = GetBoolParam(p, "partial");

            if (string.IsNullOrEmpty(searchText))
                throw new ArgumentException("text is required");

            var buttons = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None);

            foreach (var button in buttons)
            {
                if (!button.gameObject.activeInHierarchy || !button.interactable)
                    continue;

                string buttonText = null;

                // Check legacy Text
                var text = button.GetComponentInChildren<Text>();
                if (text != null) buttonText = text.text;

                // Check TMP
                if (buttonText == null)
                {
                    var tmp = button.GetComponentInChildren(typeof(Component));
                    // Search for TMP component
                    foreach (var comp in button.GetComponentsInChildren<Component>())
                    {
                        if (comp != null && comp.GetType().Name.Contains("TMP_Text"))
                        {
                            var textProp = comp.GetType().GetProperty("text");
                            if (textProp != null)
                            {
                                buttonText = textProp.GetValue(comp)?.ToString();
                                break;
                            }
                        }
                    }
                }

                if (buttonText == null) continue;

                bool match = partial
                    ? buttonText.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
                    : buttonText.Equals(searchText, StringComparison.OrdinalIgnoreCase);

                if (match)
                {
                    button.onClick.Invoke();
                    return new Dictionary<string, object>
                    {
                        { "success", true },
                        { "buttonName", button.name },
                        { "buttonText", buttonText },
                        { "path", GetGameObjectPath(button.gameObject) }
                    };
                }
            }

            throw new ArgumentException($"No button found with text '{searchText}'");
        }
#endif

        private static object WaitForNode(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            float timeout = GetFloatParam(p, "timeout", 10f);
            float pollInterval = GetFloatParam(p, "poll_interval", 0.25f);

            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("path is required");

            float startTime = (float)EditorApplication.timeSinceStartup;
            float endTime = startTime + timeout;

            // Synchronous polling within reason
            while ((float)EditorApplication.timeSinceStartup < endTime)
            {
                try
                {
                    var go = GameObject.Find(path);
                    if (go != null)
                    {
                        return new Dictionary<string, object>
                        {
                            { "found", true },
                            { "path", GetGameObjectPath(go) },
                            { "name", go.name },
                            { "waitTime", Math.Round(EditorApplication.timeSinceStartup - startTime, 3) }
                        };
                    }

                    // Also try recursive search
                    var scene = SceneManager.GetActiveScene();
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        if (root.name == path)
                        {
                            return new Dictionary<string, object>
                            {
                                { "found", true },
                                { "path", GetGameObjectPath(root) },
                                { "name", root.name },
                                { "waitTime", Math.Round(EditorApplication.timeSinceStartup - startTime, 3) }
                            };
                        }
                    }
                }
                catch { }

                System.Threading.Thread.Sleep((int)(pollInterval * 1000));
            }

            return new Dictionary<string, object>
            {
                { "found", false },
                { "path", path },
                { "timeout", timeout },
                { "message", $"GameObject '{path}' not found within {timeout}s" }
            };
        }

        private static object FindNearbyObjects(Dictionary<string, object> p)
        {
            string posStr = GetStringParam(p, "position");
            float radius = GetFloatParam(p, "radius", 10f);
            string layerMask = GetStringParam(p, "layer_mask");
            int maxResults = GetIntParam(p, "max_results", 50);

            if (string.IsNullOrEmpty(posStr))
                throw new ArgumentException("position is required");

            Vector3 center = TypeParser.ParseVector3(posStr);

            int mask = -1; // all layers
            if (!string.IsNullOrEmpty(layerMask))
            {
                mask = LayerMask.GetMask(layerMask);
                if (mask == 0)
                    throw new ArgumentException($"Layer not found: {layerMask}");
            }

            var colliders = Physics.OverlapSphere(center, radius, mask);
            var results = new List<object>();

            // Sort by distance
            Array.Sort(colliders, (a, b) =>
            {
                float distA = Vector3.Distance(center, a.transform.position);
                float distB = Vector3.Distance(center, b.transform.position);
                return distA.CompareTo(distB);
            });

            int count = 0;
            var seenObjects = new HashSet<int>();

            foreach (var col in colliders)
            {
                if (count >= maxResults) break;

                int instanceId = col.gameObject.GetInstanceID();
                if (seenObjects.Contains(instanceId)) continue;
                seenObjects.Add(instanceId);

                float distance = Vector3.Distance(center, col.transform.position);
                var pos = col.transform.position;

                results.Add(new Dictionary<string, object>
                {
                    { "name", col.gameObject.name },
                    { "path", GetGameObjectPath(col.gameObject) },
                    { "distance", Math.Round(distance, 3) },
                    { "position", $"{pos.x},{pos.y},{pos.z}" },
                    { "layer", LayerMask.LayerToName(col.gameObject.layer) },
                    { "tag", col.gameObject.tag },
                    { "colliderType", col.GetType().Name }
                });
                count++;
            }

            return new Dictionary<string, object>
            {
                { "center", posStr },
                { "radius", radius },
                { "count", results.Count },
                { "objects", results }
            };
        }
    }
}
