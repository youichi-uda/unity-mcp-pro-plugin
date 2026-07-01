using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnityMcpPro
{
    public class XRCommands : BaseCommand
    {
        public static void Register(CommandRouter router)
        {
            router.Register("setup_xr", SetupXR);
            router.Register("add_xr_interactable", AddXRInteractable);
            router.Register("add_xr_controller", AddXRController);
            router.Register("get_xr_info", GetXRInfo);
        }

        private static Type FindXRType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name == typeName)
                        return type;
                }
            }
            return null;
        }

        private static object SetupXR(Dictionary<string, object> p)
        {
            ThrowIfPlaying("setup_xr");

            string mode = GetStringParam(p, "mode", "VR");
            string trackingOrigin = GetStringParam(p, "tracking_origin", "Floor");
            bool controllers = GetBoolParam(p, "controllers", true);

            // Check for XR Origin type (XR Interaction Toolkit 2.x+)
            var xrOriginType = FindXRType("XROrigin");
            // Fallback to older XR Rig
            if (xrOriginType == null)
                xrOriginType = FindXRType("XRRig");

            if (xrOriginType == null)
                throw new InvalidOperationException(
                    "XR Interaction Toolkit not found. Install 'com.unity.xr.interaction.toolkit' via Package Manager.");

            // Check if XR Origin already exists
            var existingOrigins = FindObjectsByTypeCompat(xrOriginType);
            GameObject xrOriginGo;
            Component xrOriginComp;

            if (existingOrigins.Length > 0)
            {
                xrOriginComp = existingOrigins[0] as Component;
                xrOriginGo = xrOriginComp.gameObject;
                RecordUndo(xrOriginGo, "Configure XR Origin");
            }
            else
            {
                xrOriginGo = new GameObject("XR Origin");
                Undo.RegisterCreatedObjectUndo(xrOriginGo, "Create XR Origin");
                xrOriginComp = Undo.AddComponent(xrOriginGo, xrOriginType);

                // Create Camera Offset
                var cameraOffset = new GameObject("Camera Offset");
                Undo.RegisterCreatedObjectUndo(cameraOffset, "Create Camera Offset");
                cameraOffset.transform.SetParent(xrOriginGo.transform, false);

                // Set CameraFloorOffsetObject
                var offsetProp = xrOriginType.GetProperty("CameraFloorOffsetObject");
                if (offsetProp != null && offsetProp.CanWrite)
                    offsetProp.SetValue(xrOriginComp, cameraOffset);

                // Create Main Camera under Camera Offset
                var cameraGo = new GameObject("Main Camera");
                Undo.RegisterCreatedObjectUndo(cameraGo, "Create XR Camera");
                cameraGo.transform.SetParent(cameraOffset.transform, false);
                cameraGo.tag = "MainCamera";
                var camera = cameraGo.AddComponent<Camera>();
                cameraGo.AddComponent<AudioListener>();

                // Add TrackedPoseDriver
                var trackedPoseType = FindXRType("TrackedPoseDriver");
                if (trackedPoseType != null)
                    Undo.AddComponent(cameraGo, trackedPoseType);

                // Set Camera property on XR Origin
                var cameraProp = xrOriginType.GetProperty("Camera");
                if (cameraProp != null && cameraProp.CanWrite)
                    cameraProp.SetValue(xrOriginComp, camera);

                // Remove default camera if exists
                var defaultCam = Camera.main;
                if (defaultCam != null && defaultCam.gameObject != cameraGo)
                {
                    // Don't destroy default camera; just disable it
                    defaultCam.gameObject.SetActive(false);
                }

                // Set tracking origin mode
                try
                {
                    var requestedTrackingProp = xrOriginType.GetProperty("RequestedTrackingOriginMode") ??
                                                xrOriginType.GetProperty("TrackingOriginMode");
                    if (requestedTrackingProp != null && requestedTrackingProp.CanWrite)
                    {
                        var enumType = requestedTrackingProp.PropertyType;
                        if (enumType.IsEnum)
                        {
                            var enumValue = Enum.Parse(enumType, trackingOrigin, true);
                            requestedTrackingProp.SetValue(xrOriginComp, enumValue);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Could not set tracking origin: {ex.Message}");
                }

                // Add controllers
                if (controllers)
                {
                    CreateController(cameraOffset.transform, "Left", xrOriginGo);
                    CreateController(cameraOffset.transform, "Right", xrOriginGo);
                }
            }

            // Add XR Interaction Manager if not present
            var interactionManagerType = FindXRType("XRInteractionManager");
            if (interactionManagerType != null)
            {
                var existingManagers = FindObjectsByTypeCompat(interactionManagerType);
                if (existingManagers.Length == 0)
                {
                    var managerGo = new GameObject("XR Interaction Manager");
                    Undo.RegisterCreatedObjectUndo(managerGo, "Create XR Interaction Manager");
                    Undo.AddComponent(managerGo, interactionManagerType);
                }
            }

            EditorUtility.SetDirty(xrOriginGo);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "xr_origin", xrOriginGo.name },
                { "mode", mode },
                { "tracking_origin", trackingOrigin },
                { "controllers_added", controllers },
                { "path", GetGameObjectPath(xrOriginGo) }
            };
        }

        private static void CreateController(Transform parent, string hand, GameObject xrOrigin)
        {
            var controllerGo = new GameObject($"{hand} Controller");
            Undo.RegisterCreatedObjectUndo(controllerGo, $"Create {hand} Controller");
            controllerGo.transform.SetParent(parent, false);

            // Add ActionBasedController or XRController
            var actionControllerType = FindXRType("ActionBasedController");
            var xrControllerType = actionControllerType ?? FindXRType("XRController");

            if (xrControllerType != null)
                Undo.AddComponent(controllerGo, xrControllerType);

            // Add XRDirectInteractor
            var directInteractorType = FindXRType("XRDirectInteractor");
            if (directInteractorType != null)
                Undo.AddComponent(controllerGo, directInteractorType);

            // Add TrackedPoseDriver or set input action references
            var trackedPoseType = FindXRType("TrackedPoseDriver");
            if (trackedPoseType != null)
                Undo.AddComponent(controllerGo, trackedPoseType);
        }

        private static object AddXRInteractable(Dictionary<string, object> p)
        {
            ThrowIfPlaying("add_xr_interactable");

            string targetPath = GetStringParam(p, "target");
            string interactionType = GetStringParam(p, "interaction_type");

            if (string.IsNullOrEmpty(targetPath))
                throw new ArgumentException("target is required");
            if (string.IsNullOrEmpty(interactionType))
                throw new ArgumentException("interaction_type is required");

            bool useGravity = GetBoolParam(p, "use_gravity", interactionType == "Grab");
            bool throwOnDetach = GetBoolParam(p, "throw_on_detach", true);

            var go = FindGameObject(targetPath);

            string typeName;
            bool needsRigidbody = false;
            bool needsCollider = false;

            switch (interactionType)
            {
                case "Grab":
                    typeName = "XRGrabInteractable";
                    needsRigidbody = true;
                    needsCollider = true;
                    break;
                case "Poke":
                    typeName = "XRSimpleInteractable";
                    needsCollider = true;
                    break;
                case "RayInteractable":
                    typeName = "XRSimpleInteractable";
                    needsCollider = true;
                    break;
                case "Teleport":
                    typeName = "TeleportationArea";
                    needsCollider = true;
                    break;
                case "Socket":
                    typeName = "XRSocketInteractor";
                    needsCollider = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown interaction_type: {interactionType}");
            }

            var interactableType = FindXRType(typeName);
            if (interactableType == null)
                throw new InvalidOperationException(
                    $"XR type '{typeName}' not found. Install 'com.unity.xr.interaction.toolkit' via Package Manager.");

            // Add Collider if needed and missing
            if (needsCollider && go.GetComponent<Collider>() == null)
            {
                // Add a BoxCollider by default
                Undo.AddComponent<BoxCollider>(go);
            }

            // Add Rigidbody if needed
            if (needsRigidbody)
            {
                var rb = go.GetComponent<Rigidbody>();
                if (rb == null)
                    rb = Undo.AddComponent<Rigidbody>(go);
                rb.useGravity = useGravity;
            }

            // Add interactable component
            var existingComp = go.GetComponent(interactableType);
            if (existingComp == null)
                existingComp = Undo.AddComponent(go, interactableType);

            // Configure grab-specific settings
            if (interactionType == "Grab")
            {
                try
                {
                    var throwProp = interactableType.GetProperty("throwOnDetach");
                    if (throwProp != null && throwProp.CanWrite)
                        throwProp.SetValue(existingComp, throwOnDetach);
                }
                catch { /* Best effort */ }
            }

            EditorUtility.SetDirty(go);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "gameObject", go.name },
                { "path", GetGameObjectPath(go) },
                { "interaction_type", interactionType },
                { "component", typeName },
                { "has_rigidbody", needsRigidbody },
                { "has_collider", true }
            };
        }

        private static object AddXRController(Dictionary<string, object> p)
        {
            ThrowIfPlaying("add_xr_controller");

            string hand = GetStringParam(p, "hand");
            string controllerType = GetStringParam(p, "controller_type", "Direct");
            string modelPrefab = GetStringParam(p, "model_prefab");

            if (string.IsNullOrEmpty(hand))
                throw new ArgumentException("hand is required");

            // Find XR Origin
            var xrOriginType = FindXRType("XROrigin") ?? FindXRType("XRRig");
            if (xrOriginType == null)
                throw new InvalidOperationException("XR Origin not found in scene. Run setup_xr first.");

            var origins = FindObjectsByTypeCompat(xrOriginType);
            if (origins.Length == 0)
                throw new InvalidOperationException("No XR Origin found in scene. Run setup_xr first.");

            var originComp = origins[0] as Component;
            var originGo = originComp.gameObject;

            // Find Camera Offset
            Transform cameraOffset = null;
            foreach (Transform child in originGo.transform)
            {
                if (child.name.Contains("Camera Offset") || child.name.Contains("CameraOffset"))
                {
                    cameraOffset = child;
                    break;
                }
            }
            if (cameraOffset == null)
                cameraOffset = originGo.transform;

            // Find or create controller GO
            string controllerName = $"{hand} Controller";
            Transform controllerTransform = null;
            foreach (Transform child in cameraOffset)
            {
                if (child.name.Contains(hand))
                {
                    controllerTransform = child;
                    break;
                }
            }

            GameObject controllerGo;
            if (controllerTransform != null)
            {
                controllerGo = controllerTransform.gameObject;
                RecordUndo(controllerGo, "Configure XR Controller");
            }
            else
            {
                controllerGo = new GameObject(controllerName);
                Undo.RegisterCreatedObjectUndo(controllerGo, "Create XR Controller");
                controllerGo.transform.SetParent(cameraOffset, false);
            }

            // Add ActionBasedController or XRController
            var actionControllerType = FindXRType("ActionBasedController");
            var xrCtrlType = actionControllerType ?? FindXRType("XRController");
            if (xrCtrlType != null && controllerGo.GetComponent(xrCtrlType) == null)
                Undo.AddComponent(controllerGo, xrCtrlType);

            // Add TrackedPoseDriver
            var trackedPoseType = FindXRType("TrackedPoseDriver");
            if (trackedPoseType != null && controllerGo.GetComponent(trackedPoseType) == null)
                Undo.AddComponent(controllerGo, trackedPoseType);

            // Remove existing interactors and add the requested one
            string interactorTypeName;
            switch (controllerType)
            {
                case "Direct":
                    interactorTypeName = "XRDirectInteractor";
                    break;
                case "Ray":
                    interactorTypeName = "XRRayInteractor";
                    break;
                case "Poke":
                    interactorTypeName = "XRPokeInteractor";
                    break;
                default:
                    interactorTypeName = "XRDirectInteractor";
                    break;
            }

            var interactorType = FindXRType(interactorTypeName);
            if (interactorType != null && controllerGo.GetComponent(interactorType) == null)
                Undo.AddComponent(controllerGo, interactorType);

            // Set model prefab if provided
            if (!string.IsNullOrEmpty(modelPrefab))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPrefab);
                if (prefab != null)
                {
                    try
                    {
                        // Try to set modelPrefab property on the controller
                        var ctrlComp = controllerGo.GetComponent(xrCtrlType);
                        if (ctrlComp != null)
                        {
                            var modelPrefabProp = xrCtrlType.GetProperty("modelPrefab") ??
                                                   xrCtrlType.GetProperty("ModelPrefab");
                            if (modelPrefabProp != null && modelPrefabProp.CanWrite)
                                modelPrefabProp.SetValue(ctrlComp, prefab.transform);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Could not set controller model: {ex.Message}");
                    }
                }
            }

            EditorUtility.SetDirty(controllerGo);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "controller", controllerGo.name },
                { "hand", hand },
                { "controller_type", controllerType },
                { "interactor", interactorTypeName },
                { "path", GetGameObjectPath(controllerGo) }
            };
        }

        private static object GetXRInfo(Dictionary<string, object> p)
        {
            var result = new Dictionary<string, object> { { "success", true } };

            // Check installed XR plugins
            var installedPlugins = new List<string>();
            var xrOriginType = FindXRType("XROrigin");
            var xrRigType = FindXRType("XRRig");
            var xrInteractionManager = FindXRType("XRInteractionManager");

            result["xr_interaction_toolkit_installed"] = xrOriginType != null || xrRigType != null;

            // Check for specific XR loaders
            var oculusLoaderType = FindXRType("OculusLoader");
            if (oculusLoaderType != null) installedPlugins.Add("Oculus/Meta");

            var openXRLoaderType = FindXRType("OpenXRLoader");
            if (openXRLoaderType != null) installedPlugins.Add("OpenXR");

            var arCoreLoaderType = FindXRType("ARCoreLoader");
            if (arCoreLoaderType != null) installedPlugins.Add("ARCore");

            var arKitLoaderType = FindXRType("ARKitLoader");
            if (arKitLoaderType != null) installedPlugins.Add("ARKit");

            result["installed_plugins"] = installedPlugins;

            // Find XR Origin
            var originType = xrOriginType ?? xrRigType;
            if (originType != null)
            {
                var origins = FindObjectsByTypeCompat(originType);
                if (origins.Length > 0)
                {
                    var originComp = origins[0] as Component;
                    var originInfo = new Dictionary<string, object>
                    {
                        { "name", originComp.gameObject.name },
                        { "path", GetGameObjectPath(originComp.gameObject) },
                        { "type", originType.Name }
                    };

                    // Get tracking origin mode
                    try
                    {
                        var trackingProp = originType.GetProperty("RequestedTrackingOriginMode") ??
                                           originType.GetProperty("TrackingOriginMode");
                        if (trackingProp != null)
                            originInfo["tracking_origin"] = trackingProp.GetValue(originComp)?.ToString();
                    }
                    catch { }

                    result["xr_origin"] = originInfo;
                }
                else
                {
                    result["xr_origin"] = null;
                }
            }

            // Find interactors
            var interactors = new List<object>();
            var baseInteractorType = FindXRType("XRBaseInteractor") ?? FindXRType("XRBaseInputInteractor");
            if (baseInteractorType != null)
            {
                var found = FindObjectsByTypeCompat(baseInteractorType);
                foreach (var interactor in found)
                {
                    var comp = interactor as Component;
                    if (comp == null) continue;
                    interactors.Add(new Dictionary<string, object>
                    {
                        { "name", comp.gameObject.name },
                        { "type", comp.GetType().Name },
                        { "path", GetGameObjectPath(comp.gameObject) }
                    });
                }
            }
            result["interactors"] = interactors;
            result["interactor_count"] = interactors.Count;

            // Find interactables
            var interactables = new List<object>();
            var baseInteractableType = FindXRType("XRBaseInteractable");
            if (baseInteractableType != null)
            {
                var found = FindObjectsByTypeCompat(baseInteractableType);
                foreach (var interactable in found)
                {
                    var comp = interactable as Component;
                    if (comp == null) continue;
                    interactables.Add(new Dictionary<string, object>
                    {
                        { "name", comp.gameObject.name },
                        { "type", comp.GetType().Name },
                        { "path", GetGameObjectPath(comp.gameObject) }
                    });
                }
            }
            result["interactables"] = interactables;
            result["interactable_count"] = interactables.Count;

            return result;
        }
    }
}
