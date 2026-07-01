using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityMcpPro
{
    public class EnvironmentCommands : BaseCommand
    {
        public static void Register(CommandRouter router)
        {
            router.Register("apply_lighting_preset", ApplyLightingPreset);
            router.Register("set_fog_settings", SetFogSettings);
            router.Register("create_weather_system", CreateWeatherSystem);
            router.Register("add_wind_zone", AddWindZone);
            router.Register("set_render_pipeline_settings", SetRenderPipelineSettings);
            router.Register("get_environment_info", GetEnvironmentInfo);
        }

        private static Light FindOrCreateDirectionalLight()
        {
            var lights = FindObjectsByTypeCompat<Light>();
            foreach (var light in lights)
            {
                if (light.type == LightType.Directional)
                    return light;
            }

            var go = new GameObject("Directional Light");
            var newLight = go.AddComponent<Light>();
            newLight.type = LightType.Directional;
            Undo.RegisterCreatedObjectUndo(go, "MCP: Create Directional Light");
            return newLight;
        }

        private static object ApplyLightingPreset(Dictionary<string, object> p)
        {
            ThrowIfPlaying("apply_lighting_preset");
            string preset = GetStringParam(p, "preset");

            if (string.IsNullOrEmpty(preset))
                throw new ArgumentException("preset is required");

            var light = FindOrCreateDirectionalLight();
            RecordUndo(light, "Apply Lighting Preset");
            RecordUndo(light.transform, "Apply Lighting Preset");

            string appliedPreset = preset;

            switch (preset.ToLower())
            {
                case "noon_sunny":
                    light.transform.eulerAngles = new Vector3(50f, -30f, 0f);
                    light.color = new Color(1f, 0.96f, 0.84f);
                    light.intensity = 1.2f;
                    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                    RenderSettings.ambientSkyColor = new Color(0.53f, 0.81f, 0.92f);
                    RenderSettings.ambientEquatorColor = new Color(0.82f, 0.84f, 0.76f);
                    RenderSettings.ambientGroundColor = new Color(0.35f, 0.27f, 0.18f);
                    RenderSettings.fog = false;
                    break;

                case "sunset_warm":
                    light.transform.eulerAngles = new Vector3(5f, -50f, 0f);
                    light.color = new Color(1f, 0.55f, 0.2f);
                    light.intensity = 0.9f;
                    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                    RenderSettings.ambientSkyColor = new Color(0.95f, 0.5f, 0.25f);
                    RenderSettings.ambientEquatorColor = new Color(0.85f, 0.45f, 0.2f);
                    RenderSettings.ambientGroundColor = new Color(0.25f, 0.15f, 0.1f);
                    RenderSettings.fog = true;
                    RenderSettings.fogColor = new Color(0.9f, 0.5f, 0.3f);
                    RenderSettings.fogMode = FogMode.Exponential;
                    RenderSettings.fogDensity = 0.005f;
                    break;

                case "sunrise_soft":
                    light.transform.eulerAngles = new Vector3(8f, 60f, 0f);
                    light.color = new Color(1f, 0.78f, 0.56f);
                    light.intensity = 0.7f;
                    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                    RenderSettings.ambientSkyColor = new Color(0.6f, 0.65f, 0.85f);
                    RenderSettings.ambientEquatorColor = new Color(0.75f, 0.55f, 0.45f);
                    RenderSettings.ambientGroundColor = new Color(0.2f, 0.15f, 0.12f);
                    RenderSettings.fog = true;
                    RenderSettings.fogColor = new Color(0.8f, 0.7f, 0.6f);
                    RenderSettings.fogMode = FogMode.Exponential;
                    RenderSettings.fogDensity = 0.01f;
                    break;

                case "night_moonlit":
                    light.transform.eulerAngles = new Vector3(40f, 120f, 0f);
                    light.color = new Color(0.47f, 0.55f, 0.73f);
                    light.intensity = 0.15f;
                    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                    RenderSettings.ambientLight = new Color(0.05f, 0.06f, 0.1f);
                    RenderSettings.fog = true;
                    RenderSettings.fogColor = new Color(0.04f, 0.05f, 0.08f);
                    RenderSettings.fogMode = FogMode.Exponential;
                    RenderSettings.fogDensity = 0.02f;
                    break;

                case "overcast_cloudy":
                    light.transform.eulerAngles = new Vector3(45f, -20f, 0f);
                    light.color = new Color(0.75f, 0.78f, 0.82f);
                    light.intensity = 0.5f;
                    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                    RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.5f);
                    RenderSettings.fog = true;
                    RenderSettings.fogColor = new Color(0.6f, 0.62f, 0.65f);
                    RenderSettings.fogMode = FogMode.Exponential;
                    RenderSettings.fogDensity = 0.008f;
                    break;

                case "golden_hour":
                    light.transform.eulerAngles = new Vector3(12f, -40f, 0f);
                    light.color = new Color(1f, 0.72f, 0.32f);
                    light.intensity = 1.0f;
                    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                    RenderSettings.ambientSkyColor = new Color(0.85f, 0.65f, 0.35f);
                    RenderSettings.ambientEquatorColor = new Color(0.8f, 0.55f, 0.3f);
                    RenderSettings.ambientGroundColor = new Color(0.3f, 0.2f, 0.1f);
                    RenderSettings.fog = true;
                    RenderSettings.fogColor = new Color(0.95f, 0.7f, 0.35f);
                    RenderSettings.fogMode = FogMode.Exponential;
                    RenderSettings.fogDensity = 0.003f;
                    break;

                case "blue_hour":
                    light.transform.eulerAngles = new Vector3(2f, -60f, 0f);
                    light.color = new Color(0.45f, 0.55f, 0.8f);
                    light.intensity = 0.3f;
                    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                    RenderSettings.ambientSkyColor = new Color(0.25f, 0.35f, 0.65f);
                    RenderSettings.ambientEquatorColor = new Color(0.35f, 0.35f, 0.55f);
                    RenderSettings.ambientGroundColor = new Color(0.1f, 0.1f, 0.15f);
                    RenderSettings.fog = true;
                    RenderSettings.fogColor = new Color(0.2f, 0.25f, 0.45f);
                    RenderSettings.fogMode = FogMode.Exponential;
                    RenderSettings.fogDensity = 0.012f;
                    break;

                case "indoor_warm":
                    light.transform.eulerAngles = new Vector3(90f, 0f, 0f);
                    light.color = new Color(1f, 0.88f, 0.7f);
                    light.intensity = 0.4f;
                    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                    RenderSettings.ambientLight = new Color(0.35f, 0.28f, 0.2f);
                    RenderSettings.fog = false;
                    break;

                case "indoor_cool":
                    light.transform.eulerAngles = new Vector3(90f, 0f, 0f);
                    light.color = new Color(0.85f, 0.92f, 1f);
                    light.intensity = 0.6f;
                    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                    RenderSettings.ambientLight = new Color(0.3f, 0.33f, 0.38f);
                    RenderSettings.fog = false;
                    break;

                case "studio_neutral":
                    light.transform.eulerAngles = new Vector3(50f, -30f, 0f);
                    light.color = Color.white;
                    light.intensity = 0.8f;
                    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                    RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f);
                    RenderSettings.fog = false;
                    break;

                default:
                    throw new ArgumentException($"Unknown preset: {preset}. Available: noon_sunny, sunset_warm, sunrise_soft, night_moonlit, overcast_cloudy, golden_hour, blue_hour, indoor_warm, indoor_cool, studio_neutral");
            }

            EditorUtility.SetDirty(light);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "preset", appliedPreset },
                { "lightRotation", $"{light.transform.eulerAngles.x},{light.transform.eulerAngles.y},{light.transform.eulerAngles.z}" },
                { "lightColor", $"Color({light.color.r},{light.color.g},{light.color.b},{light.color.a})" },
                { "lightIntensity", light.intensity }
            };
        }

        private static object SetFogSettings(Dictionary<string, object> p)
        {
            ThrowIfPlaying("set_fog_settings");
            bool enabled = GetBoolParam(p, "enabled");
            string modeStr = GetStringParam(p, "mode");
            string colorStr = GetStringParam(p, "color");
            float density = GetFloatParam(p, "density", -1f);
            float startDistance = GetFloatParam(p, "start_distance", -1f);
            float endDistance = GetFloatParam(p, "end_distance", -1f);

            RenderSettings.fog = enabled;

            if (!string.IsNullOrEmpty(modeStr))
            {
                switch (modeStr.ToLower())
                {
                    case "linear":
                        RenderSettings.fogMode = FogMode.Linear;
                        break;
                    case "exponential":
                        RenderSettings.fogMode = FogMode.Exponential;
                        break;
                    case "exponentialsquared":
                        RenderSettings.fogMode = FogMode.ExponentialSquared;
                        break;
                    default:
                        throw new ArgumentException($"Unknown fog mode: {modeStr}. Available: Linear, Exponential, ExponentialSquared");
                }
            }

            if (!string.IsNullOrEmpty(colorStr))
            {
                RenderSettings.fogColor = TypeParser.ParseColor(colorStr);
            }

            if (density >= 0f)
            {
                RenderSettings.fogDensity = density;
            }

            if (startDistance >= 0f)
            {
                RenderSettings.fogStartDistance = startDistance;
            }

            if (endDistance >= 0f)
            {
                RenderSettings.fogEndDistance = endDistance;
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "enabled", enabled },
                { "mode", RenderSettings.fogMode.ToString() },
                { "color", $"Color({RenderSettings.fogColor.r},{RenderSettings.fogColor.g},{RenderSettings.fogColor.b},{RenderSettings.fogColor.a})" },
                { "density", RenderSettings.fogDensity },
                { "startDistance", RenderSettings.fogStartDistance },
                { "endDistance", RenderSettings.fogEndDistance }
            };
        }

        private static object CreateWeatherSystem(Dictionary<string, object> p)
        {
            ThrowIfPlaying("create_weather_system");
            string weatherType = GetStringParam(p, "weather_type");
            float intensity = GetFloatParam(p, "intensity", 0.5f);

            if (string.IsNullOrEmpty(weatherType))
                throw new ArgumentException("weather_type is required");

            intensity = Mathf.Clamp01(intensity);

            var go = new GameObject($"Weather_{weatherType}");
            Undo.RegisterCreatedObjectUndo(go, $"MCP: Create Weather {weatherType}");

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            var emission = ps.emission;
            var shape = ps.shape;
            var renderer = go.GetComponent<ParticleSystemRenderer>();

            // Use default particle material
            renderer.material = new Material(Shader.Find("Particles/Standard Unlit"));

            switch (weatherType.ToLower())
            {
                case "rain":
                    go.transform.position = new Vector3(0, 20, 0);
                    main.startLifetime = 1.5f;
                    main.startSpeed = 15f + intensity * 15f;
                    main.startSize = 0.03f;
                    main.startColor = new Color(0.7f, 0.75f, 0.85f, 0.6f);
                    main.maxParticles = (int)(2000 * intensity);
                    main.gravityModifier = 1.5f;
                    emission.rateOverTime = 500 * intensity;
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(30, 0, 30);
                    renderer.renderMode = ParticleSystemRenderMode.Stretch;
                    renderer.lengthScale = 5f;
                    break;

                case "snow":
                    go.transform.position = new Vector3(0, 15, 0);
                    main.startLifetime = 6f;
                    main.startSpeed = 1f + intensity * 2f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
                    main.startColor = new Color(0.95f, 0.95f, 1f, 0.8f);
                    main.maxParticles = (int)(1500 * intensity);
                    main.gravityModifier = 0.1f;
                    emission.rateOverTime = 200 * intensity;
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(30, 0, 30);
                    // Add slight random velocity for natural motion
                    var velocityOverLifetime = ps.velocityOverLifetime;
                    velocityOverLifetime.enabled = true;
                    velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
                    velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
                    break;

                case "fog":
                    go.transform.position = new Vector3(0, 1, 0);
                    main.startLifetime = 8f;
                    main.startSpeed = 0.3f + intensity * 0.5f;
                    main.startSize = new ParticleSystem.MinMaxCurve(3f, 6f);
                    main.startColor = new Color(0.8f, 0.8f, 0.8f, 0.15f * intensity);
                    main.maxParticles = (int)(200 * intensity);
                    main.gravityModifier = 0f;
                    emission.rateOverTime = 20 * intensity;
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(30, 2, 30);
                    var sizeOverLifetime = ps.sizeOverLifetime;
                    sizeOverLifetime.enabled = true;
                    sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                        new Keyframe(0, 0.5f), new Keyframe(0.5f, 1f), new Keyframe(1, 0.5f)));
                    break;

                case "dust":
                    go.transform.position = new Vector3(0, 2, 0);
                    main.startLifetime = 5f;
                    main.startSpeed = 0.5f + intensity;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
                    main.startColor = new Color(0.85f, 0.75f, 0.6f, 0.5f);
                    main.maxParticles = (int)(500 * intensity);
                    main.gravityModifier = -0.02f;
                    emission.rateOverTime = 80 * intensity;
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(20, 5, 20);
                    var dustNoise = ps.noise;
                    dustNoise.enabled = true;
                    dustNoise.strength = 0.5f;
                    dustNoise.frequency = 0.3f;
                    break;

                case "fireflies":
                    go.transform.position = new Vector3(0, 1.5f, 0);
                    main.startLifetime = 4f;
                    main.startSpeed = 0.2f + intensity * 0.3f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
                    main.startColor = new Color(0.8f, 1f, 0.4f, 0.9f);
                    main.maxParticles = (int)(100 * intensity);
                    main.gravityModifier = 0f;
                    emission.rateOverTime = 15 * intensity;
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(15, 3, 15);
                    var fireflyNoise = ps.noise;
                    fireflyNoise.enabled = true;
                    fireflyNoise.strength = 1f;
                    fireflyNoise.frequency = 0.5f;
                    var colorOverLifetime = ps.colorOverLifetime;
                    colorOverLifetime.enabled = true;
                    var gradient = new Gradient();
                    gradient.SetKeys(
                        new[] {
                            new GradientColorKey(new Color(0.8f, 1f, 0.4f), 0f),
                            new GradientColorKey(new Color(0.8f, 1f, 0.4f), 1f)
                        },
                        new[] {
                            new GradientAlphaKey(0f, 0f),
                            new GradientAlphaKey(1f, 0.2f),
                            new GradientAlphaKey(1f, 0.5f),
                            new GradientAlphaKey(0f, 1f)
                        }
                    );
                    colorOverLifetime.color = gradient;
                    break;

                default:
                    UnityEngine.Object.DestroyImmediate(go);
                    throw new ArgumentException($"Unknown weather type: {weatherType}. Available: rain, snow, fog, dust, fireflies");
            }

            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            return new Dictionary<string, object>
            {
                { "success", true },
                { "weatherType", weatherType },
                { "intensity", intensity },
                { "gameObject", GetGameObjectPath(go) },
                { "maxParticles", main.maxParticles }
            };
        }

        private static object AddWindZone(Dictionary<string, object> p)
        {
            ThrowIfPlaying("add_wind_zone");
            string modeStr = GetStringParam(p, "mode", "Directional");
            float mainStrength = GetFloatParam(p, "main_strength", 1f);
            float turbulence = GetFloatParam(p, "turbulence", 0.5f);
            string positionStr = GetStringParam(p, "position");
            float radius = GetFloatParam(p, "radius", 10f);

            var go = new GameObject("WindZone");
            Undo.RegisterCreatedObjectUndo(go, "MCP: Add WindZone");

            var windZone = go.AddComponent<WindZone>();

            switch (modeStr.ToLower())
            {
                case "spherical":
                    windZone.mode = WindZoneMode.Spherical;
                    windZone.radius = radius;
                    break;
                case "directional":
                default:
                    windZone.mode = WindZoneMode.Directional;
                    break;
            }

            windZone.windMain = mainStrength;
            windZone.windTurbulence = turbulence;

            if (!string.IsNullOrEmpty(positionStr))
            {
                go.transform.position = TypeParser.ParseVector3(positionStr);
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "gameObject", GetGameObjectPath(go) },
                { "mode", windZone.mode.ToString() },
                { "mainStrength", mainStrength },
                { "turbulence", turbulence },
                { "radius", windZone.mode == WindZoneMode.Spherical ? radius : 0f }
            };
        }

        private static object SetRenderPipelineSettings(Dictionary<string, object> p)
        {
            ThrowIfPlaying("set_render_pipeline_settings");

            var applied = new List<string>();

            if (p.ContainsKey("shadow_distance"))
            {
                QualitySettings.shadowDistance = GetFloatParam(p, "shadow_distance");
                applied.Add("shadow_distance");
            }

            if (p.ContainsKey("shadow_cascades"))
            {
                int cascades = GetIntParam(p, "shadow_cascades");
                if (cascades != 0 && cascades != 2 && cascades != 4)
                    throw new ArgumentException("shadow_cascades must be 0, 2, or 4");
                QualitySettings.shadowCascades = cascades;
                applied.Add("shadow_cascades");
            }

            if (p.ContainsKey("antialiasing"))
            {
                string aa = GetStringParam(p, "antialiasing");
                switch (aa.ToLower())
                {
                    case "none": QualitySettings.antiAliasing = 0; break;
                    case "msaa2x": QualitySettings.antiAliasing = 2; break;
                    case "msaa4x": QualitySettings.antiAliasing = 4; break;
                    case "msaa8x": QualitySettings.antiAliasing = 8; break;
                    default:
                        throw new ArgumentException($"Unknown antialiasing: {aa}. Available: None, MSAA2x, MSAA4x, MSAA8x");
                }
                applied.Add("antialiasing");
            }

            if (p.ContainsKey("pixel_light_count"))
            {
                QualitySettings.pixelLightCount = GetIntParam(p, "pixel_light_count");
                applied.Add("pixel_light_count");
            }

            if (p.ContainsKey("texture_quality"))
            {
                string tq = GetStringParam(p, "texture_quality");
                switch (tq.ToLower())
                {
                    case "full": QualitySettings.globalTextureMipmapLimit = 0; break;
                    case "half": QualitySettings.globalTextureMipmapLimit = 1; break;
                    case "quarter": QualitySettings.globalTextureMipmapLimit = 2; break;
                    case "eighth": QualitySettings.globalTextureMipmapLimit = 3; break;
                    default:
                        throw new ArgumentException($"Unknown texture quality: {tq}. Available: Full, Half, Quarter, Eighth");
                }
                applied.Add("texture_quality");
            }

            if (applied.Count == 0)
                throw new ArgumentException("At least one setting parameter is required");

            return new Dictionary<string, object>
            {
                { "success", true },
                { "appliedSettings", applied },
                { "shadowDistance", QualitySettings.shadowDistance },
                { "shadowCascades", QualitySettings.shadowCascades },
                { "antiAliasing", QualitySettings.antiAliasing },
                { "pixelLightCount", QualitySettings.pixelLightCount },
                { "textureMipmapLimit", QualitySettings.globalTextureMipmapLimit }
            };
        }

        private static object GetEnvironmentInfo(Dictionary<string, object> p)
        {
            // Lighting info
            var lightingInfo = new Dictionary<string, object>
            {
                { "ambientMode", RenderSettings.ambientMode.ToString() },
                { "ambientLight", $"Color({RenderSettings.ambientLight.r},{RenderSettings.ambientLight.g},{RenderSettings.ambientLight.b},{RenderSettings.ambientLight.a})" },
                { "ambientSkyColor", $"Color({RenderSettings.ambientSkyColor.r},{RenderSettings.ambientSkyColor.g},{RenderSettings.ambientSkyColor.b},{RenderSettings.ambientSkyColor.a})" },
                { "ambientEquatorColor", $"Color({RenderSettings.ambientEquatorColor.r},{RenderSettings.ambientEquatorColor.g},{RenderSettings.ambientEquatorColor.b},{RenderSettings.ambientEquatorColor.a})" },
                { "ambientGroundColor", $"Color({RenderSettings.ambientGroundColor.r},{RenderSettings.ambientGroundColor.g},{RenderSettings.ambientGroundColor.b},{RenderSettings.ambientGroundColor.a})" },
                { "ambientIntensity", RenderSettings.ambientIntensity }
            };

            // Fog info
            var fogInfo = new Dictionary<string, object>
            {
                { "enabled", RenderSettings.fog },
                { "mode", RenderSettings.fogMode.ToString() },
                { "color", $"Color({RenderSettings.fogColor.r},{RenderSettings.fogColor.g},{RenderSettings.fogColor.b},{RenderSettings.fogColor.a})" },
                { "density", RenderSettings.fogDensity },
                { "startDistance", RenderSettings.fogStartDistance },
                { "endDistance", RenderSettings.fogEndDistance }
            };

            // Skybox info
            var skyboxInfo = new Dictionary<string, object>
            {
                { "hasSkybox", RenderSettings.skybox != null },
                { "skyboxMaterial", RenderSettings.skybox != null ? RenderSettings.skybox.name : null },
                { "skyboxShader", RenderSettings.skybox != null ? RenderSettings.skybox.shader.name : null }
            };

            // Directional light info
            Dictionary<string, object> directionalLightInfo = null;
            var lights = FindObjectsByTypeCompat<Light>();
            foreach (var light in lights)
            {
                if (light.type == LightType.Directional)
                {
                    directionalLightInfo = new Dictionary<string, object>
                    {
                        { "name", light.name },
                        { "rotation", $"{light.transform.eulerAngles.x},{light.transform.eulerAngles.y},{light.transform.eulerAngles.z}" },
                        { "color", $"Color({light.color.r},{light.color.g},{light.color.b},{light.color.a})" },
                        { "intensity", light.intensity },
                        { "shadows", light.shadows.ToString() }
                    };
                    break;
                }
            }

            // Quality settings
            var qualityInfo = new Dictionary<string, object>
            {
                { "qualityLevel", QualitySettings.GetQualityLevel() },
                { "qualityName", QualitySettings.names[QualitySettings.GetQualityLevel()] },
                { "shadowDistance", QualitySettings.shadowDistance },
                { "shadowCascades", QualitySettings.shadowCascades },
                { "antiAliasing", QualitySettings.antiAliasing },
                { "pixelLightCount", QualitySettings.pixelLightCount },
                { "textureMipmapLimit", QualitySettings.globalTextureMipmapLimit }
            };

            return new Dictionary<string, object>
            {
                { "success", true },
                { "lighting", lightingInfo },
                { "fog", fogInfo },
                { "skybox", skyboxInfo },
                { "directionalLight", directionalLightInfo },
                { "quality", qualityInfo },
                { "haloStrength", RenderSettings.haloStrength },
                { "flareStrength", RenderSettings.flareStrength },
                { "flareFadeSpeed", RenderSettings.flareFadeSpeed }
            };
        }
    }
}
