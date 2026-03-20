using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMcpPro
{
    public class PostProcessCommands : BaseCommand
    {
        public static void Register(CommandRouter router)
        {
            router.Register("add_volume", AddVolume);
            router.Register("set_volume_effect", SetVolumeEffect);
            router.Register("get_volume_info", GetVolumeInfo);
            router.Register("create_volume_profile", CreateVolumeProfile);
            router.Register("apply_visual_preset", ApplyVisualPreset);
        }

        // --- Reflection helpers for Volume system ---

        private static Type FindRenderingType(string typeName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType(typeName);
                if (type != null) return type;
            }
            return null;
        }

        private static Type GetVolumeType()
        {
            return FindRenderingType("UnityEngine.Rendering.Volume");
        }

        private static Type GetVolumeProfileType()
        {
            return FindRenderingType("UnityEngine.Rendering.VolumeProfile");
        }

        private static Type FindVolumeComponentType(string effectName)
        {
            // Search common namespaces for URP/HDRP/Core
            string[] prefixes = new[]
            {
                "UnityEngine.Rendering.Universal.",
                "UnityEngine.Rendering.HighDefinition.",
                "UnityEngine.Rendering.",
                "UnityEngine.Rendering.PostProcessing.",
            };

            foreach (var prefix in prefixes)
            {
                var type = FindRenderingType(prefix + effectName);
                if (type != null) return type;
            }

            // Fallback: search all assemblies for matching name
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in asm.GetTypes())
                {
                    if (type.Name.Equals(effectName, StringComparison.OrdinalIgnoreCase) &&
                        type.Namespace != null && type.Namespace.Contains("Rendering"))
                        return type;
                }
            }

            return null;
        }

        private static void RequireVolumeSystem()
        {
            if (GetVolumeType() == null)
                throw new InvalidOperationException(
                    "Volume system not found. Ensure URP or HDRP is installed. " +
                    "The Volume-based post-processing requires a Scriptable Render Pipeline.");
        }

        // --- Tool handlers ---

        private static object AddVolume(Dictionary<string, object> p)
        {
            RequireVolumeSystem();

            string name = GetStringParam(p, "name", "Post-Process Volume");
            bool isGlobal = GetBoolParam(p, "is_global", true);
            float priority = GetFloatParam(p, "priority", 0f);
            string profilePath = GetStringParam(p, "profile_path");

            var volumeType = GetVolumeType();
            var profileType = GetVolumeProfileType();

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "MCP: Add Volume");

            var volume = go.AddComponent(volumeType);

            // Set isGlobal
            var isGlobalProp = volumeType.GetProperty("isGlobal", BindingFlags.Public | BindingFlags.Instance);
            if (isGlobalProp != null)
                isGlobalProp.SetValue(volume, isGlobal);

            // Set priority
            var priorityProp = volumeType.GetProperty("priority", BindingFlags.Public | BindingFlags.Instance);
            if (priorityProp != null)
                priorityProp.SetValue(volume, priority);

            // If not global, add a BoxCollider for the local volume bounds
            if (!isGlobal)
            {
                var collider = go.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = new Vector3(10, 10, 10);
            }

            // Set or create profile
            var profilePropInfo = volumeType.GetProperty("profile", BindingFlags.Public | BindingFlags.Instance)
                ?? volumeType.GetProperty("sharedProfile", BindingFlags.Public | BindingFlags.Instance);

            if (!string.IsNullOrEmpty(profilePath))
            {
                var profile = AssetDatabase.LoadAssetAtPath(profilePath, profileType);
                if (profile == null)
                    throw new ArgumentException($"VolumeProfile not found at: {profilePath}");
                if (profilePropInfo != null)
                    profilePropInfo.SetValue(volume, profile);
            }
            else
            {
                // Create a new inline profile
                var newProfile = ScriptableObject.CreateInstance(profileType);
                (newProfile as UnityEngine.Object).name = name + " Profile";
                if (profilePropInfo != null)
                    profilePropInfo.SetValue(volume, newProfile);
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "name", go.name },
                { "path", GetGameObjectPath(go) },
                { "isGlobal", isGlobal },
                { "priority", priority },
                { "hasProfile", true }
            };
        }

        private static object SetVolumeEffect(Dictionary<string, object> p)
        {
            RequireVolumeSystem();

            string targetPath = GetStringParam(p, "target");
            string effectName = GetStringParam(p, "effect");
            var properties = GetDictParam(p, "properties");

            if (string.IsNullOrEmpty(targetPath))
                throw new ArgumentException("target is required");
            if (string.IsNullOrEmpty(effectName))
                throw new ArgumentException("effect is required");

            var go = FindGameObject(targetPath);
            var volumeType = GetVolumeType();
            var volume = go.GetComponent(volumeType);
            if (volume == null)
                throw new ArgumentException($"No Volume component found on {targetPath}");

            // Get the profile
            var profileProp = volumeType.GetProperty("profile", BindingFlags.Public | BindingFlags.Instance);
            var profile = profileProp?.GetValue(volume);
            if (profile == null)
                throw new ArgumentException("Volume has no profile assigned. Use add_volume first.");

            RecordUndo(profile as UnityEngine.Object, "Set Volume Effect");

            // Find the effect type
            var effectType = FindVolumeComponentType(effectName);
            if (effectType == null)
                throw new ArgumentException(
                    $"Effect type '{effectName}' not found. Available effects depend on your render pipeline (URP/HDRP). " +
                    "Common effects: Bloom, Vignette, ColorAdjustments, DepthOfField, MotionBlur, ChromaticAberration, " +
                    "FilmGrain, LensDistortion, Tonemapping.");

            // Check if effect already exists on the profile, if not add it
            var profileType = profile.GetType();
            var componentsField = profileType.GetField("components",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);

            object effectInstance = null;

            // Try profile.Has<T>() and profile.Add<T>()
            var hasMethod = profileType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "Has" && m.IsGenericMethod);
            if (hasMethod != null)
            {
                var specificHas = hasMethod.MakeGenericMethod(effectType);
                bool exists = (bool)specificHas.Invoke(profile, null);

                if (!exists)
                {
                    var addMethod = profileType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "Add" && m.IsGenericMethod);
                    if (addMethod != null)
                    {
                        var specificAdd = addMethod.MakeGenericMethod(effectType);
                        effectInstance = specificAdd.Invoke(profile, new object[] { false });
                    }
                }
            }

            // If Has/Add didn't work, try components list directly
            if (effectInstance == null && componentsField != null)
            {
                var componentsList = componentsField.GetValue(profile) as System.Collections.IList;
                if (componentsList != null)
                {
                    foreach (var comp in componentsList)
                    {
                        if (comp.GetType() == effectType)
                        {
                            effectInstance = comp;
                            break;
                        }
                    }

                    if (effectInstance == null)
                    {
                        effectInstance = ScriptableObject.CreateInstance(effectType);
                        (effectInstance as UnityEngine.Object).hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
                        // Set active
                        var activeProp = effectType.GetProperty("active", BindingFlags.Public | BindingFlags.Instance);
                        if (activeProp != null)
                            activeProp.SetValue(effectInstance, true);
                        componentsList.Add(effectInstance);
                    }
                }
            }

            // Try TryGet<T> as last resort
            if (effectInstance == null)
            {
                var tryGetMethod = profileType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "TryGet" && m.IsGenericMethod);
                if (tryGetMethod != null)
                {
                    var specificTryGet = tryGetMethod.MakeGenericMethod(effectType);
                    var outParams = new object[] { null };
                    bool found = (bool)specificTryGet.Invoke(profile, outParams);
                    if (found) effectInstance = outParams[0];
                }
            }

            if (effectInstance == null)
                throw new Exception($"Failed to add or find effect '{effectName}' on the Volume profile.");

            // Set properties via reflection
            var setProps = new List<string>();
            if (properties != null)
            {
                foreach (var kvp in properties)
                {
                    try
                    {
                        SetVolumeParameter(effectInstance, kvp.Key, kvp.Value);
                        setProps.Add(kvp.Key);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[MCP] Could not set {effectName}.{kvp.Key}: {ex.Message}");
                    }
                }
            }

            EditorUtility.SetDirty(profile as UnityEngine.Object);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "path", GetGameObjectPath(go) },
                { "effect", effectName },
                { "propertiesSet", setProps }
            };
        }

        private static void SetVolumeParameter(object effectInstance, string paramName, object value)
        {
            var effectType = effectInstance.GetType();

            // Volume parameters are VolumeParameter<T> fields
            var field = effectType.GetField(paramName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (field == null)
            {
                // Try with common property naming conventions
                var candidates = effectType.GetFields(BindingFlags.Public | BindingFlags.Instance);
                field = candidates.FirstOrDefault(f =>
                    f.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase));
            }

            if (field == null)
                throw new ArgumentException($"Parameter '{paramName}' not found on effect");

            var paramObj = field.GetValue(effectInstance);
            if (paramObj == null)
                throw new ArgumentException($"Parameter '{paramName}' is null");

            var paramType = paramObj.GetType();

            // Set the override state to true
            var overrideStateProp = paramType.GetProperty("overrideState",
                BindingFlags.Public | BindingFlags.Instance);
            if (overrideStateProp != null)
                overrideStateProp.SetValue(paramObj, true);

            // Set the value
            var valueProp = paramType.GetProperty("value",
                BindingFlags.Public | BindingFlags.Instance);
            if (valueProp == null)
                throw new ArgumentException($"Cannot set value on parameter '{paramName}'");

            var targetType = valueProp.PropertyType;
            object convertedValue = ConvertParameterValue(value, targetType);
            valueProp.SetValue(paramObj, convertedValue);
        }

        private static object ConvertParameterValue(object value, Type targetType)
        {
            if (value == null) return null;

            string strVal = value.ToString();

            if (targetType == typeof(float))
                return Convert.ToSingle(value);
            if (targetType == typeof(int))
                return Convert.ToInt32(value);
            if (targetType == typeof(bool))
                return Convert.ToBoolean(value);
            if (targetType == typeof(Color))
                return TypeParser.ParseColor(strVal);
            if (targetType == typeof(Vector2))
                return TypeParser.ParseVector2(strVal);
            if (targetType == typeof(Vector3))
                return TypeParser.ParseVector3(strVal);
            if (targetType == typeof(Vector4))
                return TypeParser.ParseVector4(strVal);

            // Handle enums
            if (targetType.IsEnum)
            {
                if (Enum.TryParse(targetType, strVal, true, out var enumVal))
                    return enumVal;
                if (int.TryParse(strVal, out int enumInt))
                    return Enum.ToObject(targetType, enumInt);
            }

            // Try generic conversion
            try { return Convert.ChangeType(value, targetType); }
            catch { }

            return value;
        }

        private static object GetVolumeInfo(Dictionary<string, object> p)
        {
            var volumeType = GetVolumeType();
            if (volumeType == null)
            {
                return new Dictionary<string, object>
                {
                    { "success", true },
                    { "volumeSystemAvailable", false },
                    { "message", "Volume system not found. URP or HDRP is required." },
                    { "volumes", new List<object>() }
                };
            }

            var scene = SceneManager.GetActiveScene();
            var rootObjects = scene.GetRootGameObjects();
            var volumes = new List<object>();

            foreach (var root in rootObjects)
            {
                foreach (var vol in root.GetComponentsInChildren(volumeType, true))
                {
                    var info = new Dictionary<string, object>
                    {
                        { "name", vol.gameObject.name },
                        { "path", GetGameObjectPath(vol.gameObject) },
                        { "enabled", (vol as Behaviour)?.enabled ?? false }
                    };

                    var isGlobalProp = volumeType.GetProperty("isGlobal", BindingFlags.Public | BindingFlags.Instance);
                    if (isGlobalProp != null)
                        info["isGlobal"] = isGlobalProp.GetValue(vol);

                    var priorityProp = volumeType.GetProperty("priority", BindingFlags.Public | BindingFlags.Instance);
                    if (priorityProp != null)
                        info["priority"] = priorityProp.GetValue(vol);

                    var weightProp = volumeType.GetProperty("weight", BindingFlags.Public | BindingFlags.Instance);
                    if (weightProp != null)
                        info["weight"] = weightProp.GetValue(vol);

                    // Get profile and its effects
                    var profileProp = volumeType.GetProperty("profile", BindingFlags.Public | BindingFlags.Instance);
                    var profile = profileProp?.GetValue(vol);

                    if (profile != null)
                    {
                        var effects = new List<object>();
                        var componentsField = profile.GetType().GetField("components",
                            BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);

                        if (componentsField != null)
                        {
                            var componentsList = componentsField.GetValue(profile) as System.Collections.IList;
                            if (componentsList != null)
                            {
                                foreach (var comp in componentsList)
                                {
                                    if (comp == null) continue;
                                    var effectInfo = new Dictionary<string, object>
                                    {
                                        { "type", comp.GetType().Name }
                                    };

                                    var activeProp = comp.GetType().GetProperty("active",
                                        BindingFlags.Public | BindingFlags.Instance);
                                    if (activeProp != null)
                                        effectInfo["active"] = activeProp.GetValue(comp);

                                    // List override parameters
                                    var overrides = new Dictionary<string, object>();
                                    foreach (var field in comp.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                                    {
                                        var fieldVal = field.GetValue(comp);
                                        if (fieldVal == null) continue;

                                        var overrideState = fieldVal.GetType().GetProperty("overrideState",
                                            BindingFlags.Public | BindingFlags.Instance);
                                        if (overrideState != null && (bool)overrideState.GetValue(fieldVal))
                                        {
                                            var valProp = fieldVal.GetType().GetProperty("value",
                                                BindingFlags.Public | BindingFlags.Instance);
                                            if (valProp != null)
                                            {
                                                try
                                                {
                                                    overrides[field.Name] = valProp.GetValue(fieldVal)?.ToString() ?? "null";
                                                }
                                                catch { overrides[field.Name] = "error reading"; }
                                            }
                                        }
                                    }
                                    if (overrides.Count > 0)
                                        effectInfo["overrides"] = overrides;

                                    effects.Add(effectInfo);
                                }
                            }
                        }

                        info["profileName"] = (profile as UnityEngine.Object)?.name ?? "inline";
                        info["effects"] = effects;
                    }
                    else
                    {
                        info["profileName"] = "none";
                        info["effects"] = new List<object>();
                    }

                    volumes.Add(info);
                }
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "volumeSystemAvailable", true },
                { "volumeCount", volumes.Count },
                { "volumes", volumes }
            };
        }

        private static object CreateVolumeProfile(Dictionary<string, object> p)
        {
            RequireVolumeSystem();

            string path = GetStringParam(p, "path");
            var effectNames = GetStringListParam(p, "effects");

            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("path is required (e.g. 'Assets/Settings/MyProfile.asset')");

            var profileType = GetVolumeProfileType();
            var profile = ScriptableObject.CreateInstance(profileType);

            // Add requested effects
            var addedEffects = new List<string>();
            if (effectNames != null)
            {
                foreach (var effectName in effectNames)
                {
                    var effectType = FindVolumeComponentType(effectName);
                    if (effectType == null)
                    {
                        Debug.LogWarning($"[MCP] Effect type '{effectName}' not found, skipping.");
                        continue;
                    }

                    try
                    {
                        // Use profile.Add<T>()
                        var addMethod = profileType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .FirstOrDefault(m => m.Name == "Add" && m.IsGenericMethod);

                        if (addMethod != null)
                        {
                            var specificAdd = addMethod.MakeGenericMethod(effectType);
                            specificAdd.Invoke(profile, new object[] { false });
                            addedEffects.Add(effectName);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[MCP] Failed to add effect '{effectName}': {ex.Message}");
                    }
                }
            }

            // Ensure directory exists
            string dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                string[] parts = dir.Replace("\\", "/").Split('/');
                string current = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    string next = current + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                        AssetDatabase.CreateFolder(current, parts[i]);
                    current = next;
                }
            }

            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "path", path },
                { "addedEffects", addedEffects }
            };
        }

        private static object ApplyVisualPreset(Dictionary<string, object> p)
        {
            RequireVolumeSystem();

            string targetPath = GetStringParam(p, "target");
            string presetName = GetStringParam(p, "preset");

            if (string.IsNullOrEmpty(targetPath))
                throw new ArgumentException("target is required");
            if (string.IsNullOrEmpty(presetName))
                throw new ArgumentException("preset is required");

            var go = FindGameObject(targetPath);
            var volumeType = GetVolumeType();
            var volume = go.GetComponent(volumeType);
            if (volume == null)
                throw new ArgumentException($"No Volume component found on {targetPath}");

            // Get or create profile
            var profileProp = volumeType.GetProperty("profile", BindingFlags.Public | BindingFlags.Instance);
            var profile = profileProp?.GetValue(volume);
            if (profile == null)
            {
                var profileType = GetVolumeProfileType();
                profile = ScriptableObject.CreateInstance(profileType);
                (profile as UnityEngine.Object).name = presetName + " Profile";
                profileProp?.SetValue(volume, profile);
            }

            RecordUndo(profile as UnityEngine.Object, "Apply Visual Preset");

            var preset = GetPresetDefinition(presetName);
            var appliedEffects = new List<string>();

            foreach (var effectDef in preset)
            {
                string effectName = effectDef.Key;
                var effectType = FindVolumeComponentType(effectName);
                if (effectType == null)
                {
                    Debug.LogWarning($"[MCP] Preset effect '{effectName}' not found in render pipeline, skipping.");
                    continue;
                }

                // Add or get effect on profile
                object effectInstance = null;

                var profileType = profile.GetType();
                var addMethod = profileType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Add" && m.IsGenericMethod);
                var tryGetMethod = profileType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "TryGet" && m.IsGenericMethod);

                // Try to get existing
                if (tryGetMethod != null)
                {
                    var specificTryGet = tryGetMethod.MakeGenericMethod(effectType);
                    var outParams = new object[] { null };
                    bool found = (bool)specificTryGet.Invoke(profile, outParams);
                    if (found) effectInstance = outParams[0];
                }

                // Add if not found
                if (effectInstance == null && addMethod != null)
                {
                    var specificAdd = addMethod.MakeGenericMethod(effectType);
                    effectInstance = specificAdd.Invoke(profile, new object[] { false });
                }

                if (effectInstance == null) continue;

                // Set active
                var activeProp = effectInstance.GetType().GetProperty("active",
                    BindingFlags.Public | BindingFlags.Instance);
                if (activeProp != null)
                    activeProp.SetValue(effectInstance, true);

                // Apply properties
                foreach (var prop in effectDef.Value)
                {
                    try
                    {
                        SetVolumeParameter(effectInstance, prop.Key, prop.Value);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[MCP] Could not set {effectName}.{prop.Key}: {ex.Message}");
                    }
                }

                appliedEffects.Add(effectName);
            }

            EditorUtility.SetDirty(profile as UnityEngine.Object);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "path", GetGameObjectPath(go) },
                { "preset", presetName },
                { "appliedEffects", appliedEffects }
            };
        }

        private static Dictionary<string, Dictionary<string, object>> GetPresetDefinition(string presetName)
        {
            switch (presetName.ToLower())
            {
                case "cinematic":
                    return new Dictionary<string, Dictionary<string, object>>
                    {
                        { "Bloom", new Dictionary<string, object> { { "intensity", 0.3f }, { "threshold", 0.9f }, { "scatter", 0.7f } } },
                        { "Vignette", new Dictionary<string, object> { { "intensity", 0.3f }, { "smoothness", 0.4f } } },
                        { "ColorAdjustments", new Dictionary<string, object> { { "contrast", 15f }, { "saturation", 10f } } },
                        { "Tonemapping", new Dictionary<string, object> { { "mode", 2 } } }, // ACES
                        { "FilmGrain", new Dictionary<string, object> { { "intensity", 0.15f } } }
                    };

                case "horror":
                    return new Dictionary<string, Dictionary<string, object>>
                    {
                        { "ColorAdjustments", new Dictionary<string, object> { { "saturation", -40f }, { "contrast", 30f }, { "postExposure", -0.5f } } },
                        { "Vignette", new Dictionary<string, object> { { "intensity", 0.5f }, { "smoothness", 0.3f } } },
                        { "FilmGrain", new Dictionary<string, object> { { "intensity", 0.4f } } },
                        { "ChromaticAberration", new Dictionary<string, object> { { "intensity", 0.15f } } },
                        { "Bloom", new Dictionary<string, object> { { "intensity", 0.1f }, { "threshold", 1.2f } } }
                    };

                case "retro":
                    return new Dictionary<string, Dictionary<string, object>>
                    {
                        { "ColorAdjustments", new Dictionary<string, object> { { "saturation", -20f }, { "contrast", 25f } } },
                        { "FilmGrain", new Dictionary<string, object> { { "intensity", 0.6f } } },
                        { "Vignette", new Dictionary<string, object> { { "intensity", 0.4f }, { "smoothness", 0.5f } } },
                        { "ChromaticAberration", new Dictionary<string, object> { { "intensity", 0.3f } } },
                        { "LensDistortion", new Dictionary<string, object> { { "intensity", -0.15f } } }
                    };

                case "noir":
                    return new Dictionary<string, Dictionary<string, object>>
                    {
                        { "ColorAdjustments", new Dictionary<string, object> { { "saturation", -100f }, { "contrast", 40f }, { "postExposure", -0.3f } } },
                        { "Vignette", new Dictionary<string, object> { { "intensity", 0.45f }, { "smoothness", 0.35f } } },
                        { "FilmGrain", new Dictionary<string, object> { { "intensity", 0.35f } } },
                        { "Tonemapping", new Dictionary<string, object> { { "mode", 2 } } }
                    };

                case "dream":
                    return new Dictionary<string, Dictionary<string, object>>
                    {
                        { "Bloom", new Dictionary<string, object> { { "intensity", 1.5f }, { "threshold", 0.5f }, { "scatter", 0.8f } } },
                        { "ColorAdjustments", new Dictionary<string, object> { { "saturation", 20f }, { "postExposure", 0.3f } } },
                        { "Vignette", new Dictionary<string, object> { { "intensity", 0.25f }, { "smoothness", 0.6f } } },
                        { "ChromaticAberration", new Dictionary<string, object> { { "intensity", 0.1f } } },
                        { "LensDistortion", new Dictionary<string, object> { { "intensity", -0.1f } } }
                    };

                case "sci_fi":
                    return new Dictionary<string, Dictionary<string, object>>
                    {
                        { "Bloom", new Dictionary<string, object> { { "intensity", 0.8f }, { "threshold", 0.7f }, { "scatter", 0.65f } } },
                        { "ChromaticAberration", new Dictionary<string, object> { { "intensity", 0.2f } } },
                        { "ColorAdjustments", new Dictionary<string, object> { { "contrast", 20f }, { "saturation", -10f } } },
                        { "Vignette", new Dictionary<string, object> { { "intensity", 0.3f } } },
                        { "Tonemapping", new Dictionary<string, object> { { "mode", 2 } } }
                    };

                case "warm_sunset":
                    return new Dictionary<string, Dictionary<string, object>>
                    {
                        { "ColorAdjustments", new Dictionary<string, object> { { "saturation", 25f }, { "contrast", 10f }, { "postExposure", 0.2f } } },
                        { "Bloom", new Dictionary<string, object> { { "intensity", 0.5f }, { "threshold", 0.8f } } },
                        { "Vignette", new Dictionary<string, object> { { "intensity", 0.2f } } },
                        { "Tonemapping", new Dictionary<string, object> { { "mode", 2 } } }
                    };

                case "cold_night":
                    return new Dictionary<string, Dictionary<string, object>>
                    {
                        { "ColorAdjustments", new Dictionary<string, object> { { "saturation", -15f }, { "contrast", 20f }, { "postExposure", -0.4f } } },
                        { "Bloom", new Dictionary<string, object> { { "intensity", 0.2f }, { "threshold", 1.0f } } },
                        { "Vignette", new Dictionary<string, object> { { "intensity", 0.4f }, { "smoothness", 0.3f } } },
                        { "FilmGrain", new Dictionary<string, object> { { "intensity", 0.1f } } }
                    };

                case "underwater":
                    return new Dictionary<string, Dictionary<string, object>>
                    {
                        { "ColorAdjustments", new Dictionary<string, object> { { "saturation", -10f }, { "contrast", 5f } } },
                        { "Bloom", new Dictionary<string, object> { { "intensity", 0.6f }, { "threshold", 0.6f }, { "scatter", 0.85f } } },
                        { "Vignette", new Dictionary<string, object> { { "intensity", 0.35f } } },
                        { "ChromaticAberration", new Dictionary<string, object> { { "intensity", 0.25f } } },
                        { "LensDistortion", new Dictionary<string, object> { { "intensity", -0.2f } } }
                    };

                case "vintage":
                    return new Dictionary<string, Dictionary<string, object>>
                    {
                        { "ColorAdjustments", new Dictionary<string, object> { { "saturation", -30f }, { "contrast", 20f } } },
                        { "Vignette", new Dictionary<string, object> { { "intensity", 0.45f }, { "smoothness", 0.5f } } },
                        { "FilmGrain", new Dictionary<string, object> { { "intensity", 0.5f } } },
                        { "Bloom", new Dictionary<string, object> { { "intensity", 0.2f }, { "threshold", 1.0f } } },
                        { "LensDistortion", new Dictionary<string, object> { { "intensity", -0.1f } } }
                    };

                default:
                    throw new ArgumentException(
                        $"Unknown preset: '{presetName}'. Available: cinematic, horror, retro, noir, dream, sci_fi, warm_sunset, cold_night, underwater, vintage");
            }
        }
    }
}
