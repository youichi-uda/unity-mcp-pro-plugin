using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityMcpPro
{
    public class AnimationCommands : BaseCommand
    {
        public static void Register(CommandRouter router)
        {
            router.Register("create_animation_clip", CreateAnimationClip);
            router.Register("add_animation_keyframe", AddAnimationKeyframe);
            router.Register("get_animation_clip_info", GetAnimationClipInfo);
            router.Register("create_animator_controller", CreateAnimatorController);
            router.Register("add_animator_state", AddAnimatorState);
            router.Register("add_animator_transition", AddAnimatorTransition);
            router.Register("set_animator_parameter", SetAnimatorParameter);
        }

        private static object CreateAnimationClip(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            float length = GetFloatParam(p, "length", 1f);
            bool loop = GetBoolParam(p, "loop");

            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("path is required");

            if (!path.EndsWith(".anim"))
                path += ".anim";

            var clip = new AnimationClip();

            if (loop)
            {
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
            }

            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            AssetDatabase.CreateAsset(clip, path);
            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "path", path },
                { "length", length },
                { "loop", loop }
            };
        }

        private static object AddAnimationKeyframe(Dictionary<string, object> p)
        {
            string clipPath = GetStringParam(p, "clip_path");
            string propertyPath = GetStringParam(p, "property_path");
            string componentType = GetStringParam(p, "component_type");
            float time = GetFloatParam(p, "time");

            if (string.IsNullOrEmpty(clipPath))
                throw new ArgumentException("clip_path is required");
            if (string.IsNullOrEmpty(propertyPath))
                throw new ArgumentException("property_path is required");
            if (string.IsNullOrEmpty(componentType))
                throw new ArgumentException("component_type is required");
            if (!p.ContainsKey("value"))
                throw new ArgumentException("value is required");

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
                throw new ArgumentException($"Animation clip not found at: {clipPath}");

            float value = GetFloatParam(p, "value");

            var binding = EditorCurveBinding.FloatCurve("", TypeParser.FindComponentType(componentType) ?? typeof(Transform), propertyPath);
            var existingCurve = AnimationUtility.GetEditorCurve(clip, binding);
            var curve = existingCurve ?? new AnimationCurve();

            curve.AddKey(new Keyframe(time, value));
            AnimationUtility.SetEditorCurve(clip, binding, curve);

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "clip", clip.name },
                { "property", propertyPath },
                { "time", time },
                { "value", value },
                { "keyCount", curve.length }
            };
        }

        private static object GetAnimationClipInfo(Dictionary<string, object> p)
        {
            string clipPath = GetStringParam(p, "clip_path");
            if (string.IsNullOrEmpty(clipPath))
                throw new ArgumentException("clip_path is required");

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
                throw new ArgumentException($"Animation clip not found at: {clipPath}");

            var bindings = AnimationUtility.GetCurveBindings(clip);
            var curves = new List<object>();

            foreach (var binding in bindings)
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                var keys = new List<object>();
                if (curve != null)
                {
                    foreach (var key in curve.keys)
                    {
                        keys.Add(new Dictionary<string, object>
                        {
                            { "time", key.time },
                            { "value", key.value }
                        });
                    }
                }

                curves.Add(new Dictionary<string, object>
                {
                    { "path", binding.path },
                    { "propertyName", binding.propertyName },
                    { "type", binding.type.Name },
                    { "keyframes", keys }
                });
            }

            var settings = AnimationUtility.GetAnimationClipSettings(clip);

            return new Dictionary<string, object>
            {
                { "name", clip.name },
                { "length", clip.length },
                { "frameRate", clip.frameRate },
                { "loop", settings.loopTime },
                { "curves", curves }
            };
        }

        private static object CreateAnimatorController(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("path is required");

            if (!path.EndsWith(".controller"))
                path += ".controller";

            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);

            var stateNames = GetStringListParam(p, "states");
            string defaultState = GetStringParam(p, "default_state");
            var stateMachine = controller.layers[0].stateMachine;

            if (stateNames != null)
            {
                foreach (var stateName in stateNames)
                {
                    var state = stateMachine.AddState(stateName);
                    if (stateName == defaultState)
                        stateMachine.defaultState = state;
                }
            }

            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "path", path },
                { "stateCount", stateMachine.states.Length }
            };
        }

        private static object AddAnimatorState(Dictionary<string, object> p)
        {
            string controllerPath = GetStringParam(p, "controller_path");
            string stateName = GetStringParam(p, "state_name");
            string clipPath = GetStringParam(p, "clip_path");
            int layer = GetIntParam(p, "layer", 0);

            if (string.IsNullOrEmpty(controllerPath))
                throw new ArgumentException("controller_path is required");
            if (string.IsNullOrEmpty(stateName))
                throw new ArgumentException("state_name is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                throw new ArgumentException($"Animator controller not found at: {controllerPath}");

            if (layer >= controller.layers.Length)
                throw new ArgumentException($"Layer {layer} does not exist");

            var stateMachine = controller.layers[layer].stateMachine;
            var state = stateMachine.AddState(stateName);

            if (!string.IsNullOrEmpty(clipPath))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clip != null)
                    state.motion = clip;
            }

            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "state", stateName },
                { "controller", controller.name },
                { "hasClip", state.motion != null }
            };
        }

        private static object AddAnimatorTransition(Dictionary<string, object> p)
        {
            string controllerPath = GetStringParam(p, "controller_path");
            string fromState = GetStringParam(p, "from_state");
            string toState = GetStringParam(p, "to_state");
            bool hasExitTime = GetBoolParam(p, "has_exit_time", true);
            int layer = GetIntParam(p, "layer", 0);

            if (string.IsNullOrEmpty(controllerPath))
                throw new ArgumentException("controller_path is required");
            if (string.IsNullOrEmpty(fromState))
                throw new ArgumentException("from_state is required");
            if (string.IsNullOrEmpty(toState))
                throw new ArgumentException("to_state is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                throw new ArgumentException($"Animator controller not found at: {controllerPath}");

            var stateMachine = controller.layers[layer].stateMachine;
            AnimatorState srcState = null, dstState = null;

            foreach (var cs in stateMachine.states)
            {
                if (cs.state.name == fromState) srcState = cs.state;
                if (cs.state.name == toState) dstState = cs.state;
            }

            if (srcState == null) throw new ArgumentException($"State not found: {fromState}");
            if (dstState == null) throw new ArgumentException($"State not found: {toState}");

            var transition = srcState.AddTransition(dstState);
            transition.hasExitTime = hasExitTime;

            var conditions = GetDictParam(p, "conditions");
            if (conditions != null)
            {
                foreach (var kvp in conditions)
                {
                    string paramName = kvp.Key;
                    string condStr = kvp.Value?.ToString() ?? "";

                    AnimatorConditionMode mode = AnimatorConditionMode.If;
                    float threshold = 0;

                    if (condStr.StartsWith(">"))
                    {
                        mode = AnimatorConditionMode.Greater;
                        float.TryParse(condStr.Substring(1), out threshold);
                    }
                    else if (condStr.StartsWith("<"))
                    {
                        mode = AnimatorConditionMode.Less;
                        float.TryParse(condStr.Substring(1), out threshold);
                    }
                    else if (condStr.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        mode = AnimatorConditionMode.If;
                    }
                    else if (condStr.Equals("false", StringComparison.OrdinalIgnoreCase))
                    {
                        mode = AnimatorConditionMode.IfNot;
                    }
                    else if (float.TryParse(condStr, out threshold))
                    {
                        mode = AnimatorConditionMode.Equals;
                    }

                    transition.AddCondition(mode, threshold, paramName);
                }
            }

            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "from", fromState },
                { "to", toState },
                { "hasExitTime", hasExitTime }
            };
        }

        private static object SetAnimatorParameter(Dictionary<string, object> p)
        {
            string controllerPath = GetStringParam(p, "controller_path");
            string paramName = GetStringParam(p, "name");
            string typeStr = GetStringParam(p, "type", "Bool");

            if (string.IsNullOrEmpty(controllerPath))
                throw new ArgumentException("controller_path is required");
            if (string.IsNullOrEmpty(paramName))
                throw new ArgumentException("name is required");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                throw new ArgumentException($"Animator controller not found at: {controllerPath}");

            AnimatorControllerParameterType paramType;
            switch (typeStr.ToLower())
            {
                case "float": paramType = AnimatorControllerParameterType.Float; break;
                case "int": paramType = AnimatorControllerParameterType.Int; break;
                case "trigger": paramType = AnimatorControllerParameterType.Trigger; break;
                case "bool":
                default: paramType = AnimatorControllerParameterType.Bool; break;
            }

            controller.AddParameter(paramName, paramType);

            if (p.ContainsKey("default_value"))
            {
                var parameters = controller.parameters;
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].name == paramName)
                    {
                        switch (paramType)
                        {
                            case AnimatorControllerParameterType.Float:
                                parameters[i].defaultFloat = GetFloatParam(p, "default_value");
                                break;
                            case AnimatorControllerParameterType.Int:
                                parameters[i].defaultInt = GetIntParam(p, "default_value");
                                break;
                            case AnimatorControllerParameterType.Bool:
                                parameters[i].defaultBool = GetBoolParam(p, "default_value");
                                break;
                        }
                        controller.parameters = parameters;
                        break;
                    }
                }
            }

            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "controller", controller.name },
                { "parameter", paramName },
                { "type", paramType.ToString() }
            };
        }
    }
}
