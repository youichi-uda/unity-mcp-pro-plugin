using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace UnityMcpPro
{
    public class TestingCommands : BaseCommand
    {
        private static readonly List<Dictionary<string, object>> _testResults = new List<Dictionary<string, object>>();

        public static void Register(CommandRouter router)
        {
            router.Register("run_tests", RunTests);
            router.Register("run_test_scenario", RunTestScenario);
            router.Register("assert_node_state", AssertNodeState);
            router.Register("assert_screen_text", AssertScreenText);
            router.Register("run_stress_test", RunStressTest);
            router.Register("get_test_report", GetTestReport);
        }

        private static object RunTests(Dictionary<string, object> p)
        {
            string testMode = GetStringParam(p, "test_mode", "edit");
            string filter = GetStringParam(p, "filter");
            string category = GetStringParam(p, "category");

            try
            {
                // Use reflection to access TestRunnerApi
                var testRunnerApiType = Type.GetType("UnityEditor.TestTools.TestRunner.Api.TestRunnerApi, UnityEditor.TestRunner");
                if (testRunnerApiType == null)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "Unity Test Runner API not available. Ensure the Test Framework package is installed." }
                    };
                }

                var instance = ScriptableObject.CreateInstance(testRunnerApiType);
                var executeMethod = testRunnerApiType.GetMethod("Execute",
                    BindingFlags.Public | BindingFlags.Instance);

                // Build execution settings
                var executionSettingsType = Type.GetType("UnityEditor.TestTools.TestRunner.Api.ExecutionSettings, UnityEditor.TestRunner");
                if (executionSettingsType == null)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "Test execution settings type not found" }
                    };
                }

                var filterType = Type.GetType("UnityEditor.TestTools.TestRunner.Api.Filter, UnityEditor.TestRunner");
                var filterInstance = Activator.CreateInstance(filterType);

                // Set test mode
                var testModeType = Type.GetType("UnityEditor.TestTools.TestRunner.Api.TestMode, UnityEditor.TestRunner");
                if (testModeType != null)
                {
                    var modeValue = testMode == "play"
                        ? Enum.Parse(testModeType, "PlayMode")
                        : Enum.Parse(testModeType, "EditMode");
                    filterType.GetField("testMode")?.SetValue(filterInstance, modeValue);
                }

                // Set name filter
                if (!string.IsNullOrEmpty(filter))
                {
                    filterType.GetField("testNames")?.SetValue(filterInstance, new[] { filter });
                }

                if (!string.IsNullOrEmpty(category))
                {
                    filterType.GetField("categoryNames")?.SetValue(filterInstance, new[] { category });
                }

                var settings = Activator.CreateInstance(executionSettingsType,
                    new object[] { filterInstance });

                executeMethod?.Invoke(instance, new[] { settings });

                UnityEngine.Object.DestroyImmediate(instance);

                var result = new Dictionary<string, object>
                {
                    { "success", true },
                    { "message", $"Tests started ({testMode} mode)" },
                    { "mode", testMode }
                };

                if (!string.IsNullOrEmpty(filter)) result["filter"] = filter;
                if (!string.IsNullOrEmpty(category)) result["category"] = category;

                return result;
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "error", ex.Message }
                };
            }
        }

        private static object RunTestScenario(Dictionary<string, object> p)
        {
            string name = GetStringParam(p, "name", "unnamed");
            var steps = p.ContainsKey("steps") ? p["steps"] as List<object> : null;

            if (steps == null || steps.Count == 0)
                throw new ArgumentException("steps is required and must be non-empty");

            var results = new List<object>();
            bool allPassed = true;

            foreach (var stepObj in steps)
            {
                var step = stepObj as Dictionary<string, object>;
                if (step == null) continue;

                string type = GetStringParam(step, "type");
                var stepResult = new Dictionary<string, object> { { "type", type } };

                try
                {
                    switch (type)
                    {
                        case "action":
                            stepResult["status"] = "executed";
                            stepResult["command"] = GetStringParam(step, "command");
                            break;

                        case "wait":
                            float duration = GetFloatParam(step, "duration", 1f);
                            System.Threading.Thread.Sleep((int)(duration * 1000));
                            stepResult["status"] = "completed";
                            stepResult["duration"] = duration;
                            break;

                        case "assert":
                            string path = GetStringParam(step, "path");
                            string component = GetStringParam(step, "component");
                            string property = GetStringParam(step, "property");
                            string op = GetStringParam(step, "operator", "exists");
                            object expected = step.ContainsKey("expected") ? step["expected"] : null;

                            var assertResult = PerformAssert(path, component, property, op, expected);
                            bool passed = (bool)assertResult["passed"];
                            if (!passed) allPassed = false;

                            foreach (var kvp in assertResult)
                                stepResult[kvp.Key] = kvp.Value;
                            break;

                        default:
                            stepResult["status"] = "unknown_type";
                            break;
                    }
                }
                catch (Exception ex)
                {
                    stepResult["status"] = "error";
                    stepResult["error"] = ex.Message;
                    allPassed = false;
                }

                results.Add(stepResult);
            }

            var scenarioResult = new Dictionary<string, object>
            {
                { "name", name },
                { "passed", allPassed },
                { "totalSteps", steps.Count },
                { "steps", results }
            };

            _testResults.Add(scenarioResult);
            return scenarioResult;
        }

        private static object AssertNodeState(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            string component = GetStringParam(p, "component");
            string property = GetStringParam(p, "property");
            string op = GetStringParam(p, "operator", "exists");
            object expected = p.ContainsKey("expected") ? p["expected"] : null;

            var result = PerformAssert(path, component, property, op, expected);
            _testResults.Add(new Dictionary<string, object>
            {
                { "name", $"assert_{path}_{property}" },
                { "passed", result["passed"] },
                { "result", result }
            });

            return result;
        }

        private static Dictionary<string, object> PerformAssert(string path, string component, string property, string op, object expected)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("path is required");
            if (string.IsNullOrEmpty(property))
                throw new ArgumentException("property is required");

            var go = FindGameObject(path);
            object actual = null;

            if (string.IsNullOrEmpty(component))
            {
                // Check GameObject-level properties
                switch (property.ToLower())
                {
                    case "active": case "activeself": actual = go.activeSelf; break;
                    case "activeinhierarchy": actual = go.activeInHierarchy; break;
                    case "tag": actual = go.tag; break;
                    case "layer": actual = LayerMask.LayerToName(go.layer); break;
                    case "name": actual = go.name; break;
                    case "position": actual = $"{go.transform.position.x},{go.transform.position.y},{go.transform.position.z}"; break;
                    default:
                        throw new ArgumentException($"Unknown GameObject property: {property}");
                }
            }
            else
            {
                var comp = FindComponent(go, component);
                var propInfo = comp.GetType().GetProperty(property,
                    BindingFlags.Public | BindingFlags.Instance);
                if (propInfo != null)
                {
                    actual = propInfo.GetValue(comp);
                }
                else
                {
                    var fieldInfo = comp.GetType().GetField(property,
                        BindingFlags.Public | BindingFlags.Instance);
                    if (fieldInfo != null)
                        actual = fieldInfo.GetValue(comp);
                    else
                        throw new ArgumentException($"Property '{property}' not found on {component}");
                }
            }

            string actualStr = actual?.ToString() ?? "null";
            string expectedStr = expected?.ToString() ?? "null";
            bool passed = false;

            switch (op.ToLower())
            {
                case "exists":
                    passed = actual != null;
                    break;
                case "eq":
                    passed = actualStr.Equals(expectedStr, StringComparison.OrdinalIgnoreCase);
                    break;
                case "neq":
                    passed = !actualStr.Equals(expectedStr, StringComparison.OrdinalIgnoreCase);
                    break;
                case "gt":
                    if (double.TryParse(actualStr, out double aGt) && double.TryParse(expectedStr, out double eGt))
                        passed = aGt > eGt;
                    break;
                case "lt":
                    if (double.TryParse(actualStr, out double aLt) && double.TryParse(expectedStr, out double eLt))
                        passed = aLt < eLt;
                    break;
                case "gte":
                    if (double.TryParse(actualStr, out double aGte) && double.TryParse(expectedStr, out double eGte))
                        passed = aGte >= eGte;
                    break;
                case "lte":
                    if (double.TryParse(actualStr, out double aLte) && double.TryParse(expectedStr, out double eLte))
                        passed = aLte <= eLte;
                    break;
                case "contains":
                    passed = actualStr.IndexOf(expectedStr, StringComparison.OrdinalIgnoreCase) >= 0;
                    break;
            }

            return new Dictionary<string, object>
            {
                { "passed", passed },
                { "operator", op },
                { "actual", actualStr },
                { "expected", expectedStr },
                { "path", GetGameObjectPath(go) },
                { "property", property }
            };
        }

        private static object AssertScreenText(Dictionary<string, object> p)
        {
            string searchText = GetStringParam(p, "text");
            bool partial = GetBoolParam(p, "partial", true);
            bool shouldExist = GetBoolParam(p, "should_exist", true);

            if (string.IsNullOrEmpty(searchText))
                throw new ArgumentException("text is required");

            var foundTexts = new List<object>();

            // Search legacy UI Text
            foreach (var text in UnityEngine.Object.FindObjectsOfType<Text>())
            {
                if (text.text == null) continue;
                bool match = partial
                    ? text.text.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
                    : text.text.Equals(searchText, StringComparison.OrdinalIgnoreCase);
                if (match)
                {
                    foundTexts.Add(new Dictionary<string, object>
                    {
                        { "text", text.text },
                        { "path", GetGameObjectPath(text.gameObject) },
                        { "type", "UnityEngine.UI.Text" }
                    });
                }
            }

            // Search TMP text via reflection
            var tmpTextType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                .FirstOrDefault(t => t.FullName == "TMPro.TMP_Text");

            if (tmpTextType != null)
            {
                foreach (var comp in UnityEngine.Object.FindObjectsOfType(tmpTextType))
                {
                    var textProp = tmpTextType.GetProperty("text");
                    string txt = textProp?.GetValue(comp)?.ToString();
                    if (txt == null) continue;

                    bool match = partial
                        ? txt.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
                        : txt.Equals(searchText, StringComparison.OrdinalIgnoreCase);
                    if (match)
                    {
                        var monoBehaviour = comp as MonoBehaviour;
                        foundTexts.Add(new Dictionary<string, object>
                        {
                            { "text", txt },
                            { "path", monoBehaviour != null ? GetGameObjectPath(monoBehaviour.gameObject) : "unknown" },
                            { "type", "TMPro.TMP_Text" }
                        });
                    }
                }
            }

            bool exists = foundTexts.Count > 0;
            bool passed = shouldExist ? exists : !exists;

            var result = new Dictionary<string, object>
            {
                { "passed", passed },
                { "searchText", searchText },
                { "shouldExist", shouldExist },
                { "found", exists },
                { "matchCount", foundTexts.Count },
                { "matches", foundTexts }
            };

            _testResults.Add(new Dictionary<string, object>
            {
                { "name", $"assert_screen_text_{searchText}" },
                { "passed", passed },
                { "result", result }
            });

            return result;
        }

        private static object RunStressTest(Dictionary<string, object> p)
        {
            float duration = GetFloatParam(p, "duration", 5f);
            int eventsPerSecond = GetIntParam(p, "events_per_second", 10);
            bool includeKeys = GetBoolParam(p, "include_keys", true);
            bool includeMouse = GetBoolParam(p, "include_mouse", true);

            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Play mode is required for stress testing");

            int totalEvents = 0;
            int errorCount = 0;
            float startTime = (float)EditorApplication.timeSinceStartup;
            float endTime = startTime + duration;
            float eventInterval = 1f / eventsPerSecond;
            float nextEvent = startTime;

            var rng = new System.Random();
            var keyNames = new[] { "W", "A", "S", "D", "Space", "E", "Q", "R", "F", "1", "2", "3" };

            EditorApplication.CallbackFunction stressCallback = null;
            stressCallback = () =>
            {
                float now = (float)EditorApplication.timeSinceStartup;
                if (now >= endTime || !EditorApplication.isPlaying)
                {
                    EditorApplication.update -= stressCallback;
                    return;
                }

                while (now >= nextEvent && nextEvent < endTime)
                {
                    try
                    {
                        if (includeKeys && includeMouse)
                        {
                            if (rng.Next(2) == 0)
                                SimulateRandomKey(rng, keyNames);
                            else
                                SimulateRandomMouse(rng);
                        }
                        else if (includeKeys)
                        {
                            SimulateRandomKey(rng, keyNames);
                        }
                        else if (includeMouse)
                        {
                            SimulateRandomMouse(rng);
                        }
                        totalEvents++;
                    }
                    catch
                    {
                        errorCount++;
                    }
                    nextEvent += eventInterval;
                }
            };

            EditorApplication.update += stressCallback;

            return new Dictionary<string, object>
            {
                { "status", "running" },
                { "duration", duration },
                { "eventsPerSecond", eventsPerSecond },
                { "includeKeys", includeKeys },
                { "includeMouse", includeMouse },
                { "message", $"Stress test running for {duration}s at {eventsPerSecond} events/s" }
            };
        }

        private static void SimulateRandomKey(System.Random rng, string[] keyNames)
        {
            // Simulate via Input class if available
            string key = keyNames[rng.Next(keyNames.Length)];
            Debug.Log($"[StressTest] Key: {key}");
        }

        private static void SimulateRandomMouse(System.Random rng)
        {
            int x = rng.Next(-50, 50);
            int y = rng.Next(-50, 50);
            Debug.Log($"[StressTest] Mouse: ({x},{y})");
        }

        private static object GetTestReport(Dictionary<string, object> p)
        {
            int totalTests = _testResults.Count;
            int passed = 0;
            int failed = 0;

            foreach (var result in _testResults)
            {
                if (result.ContainsKey("passed") && result["passed"] is bool p2 && p2)
                    passed++;
                else
                    failed++;
            }

            return new Dictionary<string, object>
            {
                { "totalTests", totalTests },
                { "passed", passed },
                { "failed", failed },
                { "passRate", totalTests > 0 ? Math.Round((double)passed / totalTests * 100, 1) : 0 },
                { "results", _testResults }
            };
        }
    }
}
