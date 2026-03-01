using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityMcpPro
{
    public class LightingCommands : BaseCommand
    {
        public static void Register(CommandRouter router)
        {
            router.Register("add_light", AddLight);
            router.Register("set_lighting_settings", SetLightingSettings);
            router.Register("set_skybox", SetSkybox);
            router.Register("bake_lighting", BakeLighting);
            router.Register("add_reflection_probe", AddReflectionProbe);
        }

        private static object AddLight(Dictionary<string, object> p)
        {
            string typeStr = GetStringParam(p, "type", "Point");
            string name = GetStringParam(p, "name");
            string posStr = GetStringParam(p, "position");
            string rotStr = GetStringParam(p, "rotation");
            string colorStr = GetStringParam(p, "color");
            float intensity = GetFloatParam(p, "intensity", -1f);
            float range = GetFloatParam(p, "range", -1f);
            string shadowStr = GetStringParam(p, "shadows");

            LightType lightType;
            switch (typeStr.ToLower())
            {
                case "directional": lightType = LightType.Directional; break;
                case "spot": lightType = LightType.Spot; break;
                case "area": lightType = LightType.Area; break;
                case "point":
                default: lightType = LightType.Point; break;
            }

            var go = new GameObject(name ?? (typeStr + " Light"));
            var light = go.AddComponent<Light>();
            light.type = lightType;
            Undo.RegisterCreatedObjectUndo(go, "MCP: Add Light");

            if (!string.IsNullOrEmpty(posStr))
                go.transform.position = TypeParser.ParseVector3(posStr);
            if (!string.IsNullOrEmpty(rotStr))
                go.transform.eulerAngles = TypeParser.ParseVector3(rotStr);
            if (!string.IsNullOrEmpty(colorStr))
                light.color = TypeParser.ParseColor(colorStr);
            if (intensity >= 0)
                light.intensity = intensity;
            if (range >= 0)
                light.range = range;

            if (!string.IsNullOrEmpty(shadowStr))
            {
                if (Enum.TryParse<LightShadows>(shadowStr, true, out var shadows))
                    light.shadows = shadows;
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "name", go.name },
                { "type", lightType.ToString() },
                { "path", GetGameObjectPath(go) }
            };
        }

        private static object SetLightingSettings(Dictionary<string, object> p)
        {
            string ambientModeStr = GetStringParam(p, "ambient_mode");
            string ambientColorStr = GetStringParam(p, "ambient_color");
            string fogColorStr = GetStringParam(p, "fog_color");

            if (!string.IsNullOrEmpty(ambientModeStr))
            {
                if (Enum.TryParse<AmbientMode>(ambientModeStr, true, out var mode))
                    RenderSettings.ambientMode = mode;
            }

            if (!string.IsNullOrEmpty(ambientColorStr))
                RenderSettings.ambientLight = TypeParser.ParseColor(ambientColorStr);

            if (p.ContainsKey("fog_enabled"))
                RenderSettings.fog = GetBoolParam(p, "fog_enabled");

            if (!string.IsNullOrEmpty(fogColorStr))
                RenderSettings.fogColor = TypeParser.ParseColor(fogColorStr);

            if (p.ContainsKey("fog_density"))
                RenderSettings.fogDensity = GetFloatParam(p, "fog_density", 0.01f);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "ambientMode", RenderSettings.ambientMode.ToString() },
                { "fog", RenderSettings.fog },
                { "fogDensity", RenderSettings.fogDensity }
            };
        }

        private static object SetSkybox(Dictionary<string, object> p)
        {
            string matPath = GetStringParam(p, "material_path");
            string proceduralColorStr = GetStringParam(p, "procedural_color");

            if (!string.IsNullOrEmpty(matPath))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                    throw new ArgumentException($"Material not found at: {matPath}");
                RenderSettings.skybox = mat;
            }
            else if (!string.IsNullOrEmpty(proceduralColorStr))
            {
                // Create or reuse procedural skybox
                var skyMat = RenderSettings.skybox;
                if (skyMat == null || skyMat.shader.name != "Skybox/Procedural")
                {
                    var shader = Shader.Find("Skybox/Procedural");
                    if (shader == null)
                        throw new Exception("Skybox/Procedural shader not found");
                    skyMat = new Material(shader);
                    RenderSettings.skybox = skyMat;
                }

                var color = TypeParser.ParseColor(proceduralColorStr);
                skyMat.SetColor("_SkyTint", color);
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "skybox", RenderSettings.skybox != null ? RenderSettings.skybox.name : "none" }
            };
        }

        private static object BakeLighting(Dictionary<string, object> p)
        {
            string modeStr = GetStringParam(p, "mode", "bake");

            switch (modeStr.ToLower())
            {
                case "clear":
                    Lightmapping.Clear();
                    return Success("Cleared baked lighting data");
                case "cancel":
                    Lightmapping.Cancel();
                    return Success("Cancelled lighting bake");
                case "bake":
                default:
                    bool started = Lightmapping.BakeAsync();
                    return new Dictionary<string, object>
                    {
                        { "success", true },
                        { "message", started ? "Lighting bake started (async)" : "Failed to start bake" },
                        { "started", started }
                    };
            }
        }

        private static object AddReflectionProbe(Dictionary<string, object> p)
        {
            string posStr = GetStringParam(p, "position");
            string sizeStr = GetStringParam(p, "size", "10,10,10");
            string modeStr = GetStringParam(p, "mode", "Baked");
            int resolution = GetIntParam(p, "resolution", 256);

            var go = new GameObject("Reflection Probe");
            var probe = go.AddComponent<ReflectionProbe>();
            Undo.RegisterCreatedObjectUndo(go, "MCP: Add Reflection Probe");

            if (!string.IsNullOrEmpty(posStr))
                go.transform.position = TypeParser.ParseVector3(posStr);

            probe.size = TypeParser.ParseVector3(sizeStr);
            probe.resolution = resolution;

            if (Enum.TryParse<ReflectionProbeMode>(modeStr, true, out var mode))
                probe.mode = mode;

            return new Dictionary<string, object>
            {
                { "success", true },
                { "name", go.name },
                { "mode", probe.mode.ToString() },
                { "resolution", probe.resolution },
                { "path", GetGameObjectPath(go) }
            };
        }
    }
}
