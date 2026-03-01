using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMcpPro
{
    public class SceneCommands : BaseCommand
    {
        public static void Register(CommandRouter router)
        {
            router.Register("get_hierarchy", GetHierarchy);
            router.Register("create_scene", CreateScene);
            router.Register("open_scene", OpenScene);
            router.Register("save_scene", SaveScene);
            router.Register("play_scene", PlayScene);
            router.Register("stop_scene", StopScene);
        }

        private static object GetHierarchy(Dictionary<string, object> p)
        {
            int maxDepth = GetIntParam(p, "max_depth", -1);
            var scene = SceneManager.GetActiveScene();

            var hierarchy = new List<object>();
            foreach (var root in scene.GetRootGameObjects())
            {
                hierarchy.Add(BuildHierarchyNode(root, maxDepth, 0));
            }

            return new Dictionary<string, object>
            {
                { "scene_name", scene.name },
                { "scene_path", scene.path },
                { "is_dirty", scene.isDirty },
                { "root_count", scene.rootCount },
                { "hierarchy", hierarchy }
            };
        }

        private static Dictionary<string, object> BuildHierarchyNode(GameObject go, int maxDepth, int currentDepth)
        {
            var components = new List<string>();
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp != null)
                    components.Add(comp.GetType().Name);
            }

            var node = new Dictionary<string, object>
            {
                { "name", go.name },
                { "active", go.activeSelf },
                { "tag", go.tag },
                { "layer", LayerMask.LayerToName(go.layer) },
                { "components", components }
            };

            if (maxDepth < 0 || currentDepth < maxDepth)
            {
                var children = new List<object>();
                foreach (Transform child in go.transform)
                {
                    children.Add(BuildHierarchyNode(child.gameObject, maxDepth, currentDepth + 1));
                }
                if (children.Count > 0)
                    node["children"] = children;
            }

            return node;
        }

        private static object CreateScene(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            if (string.IsNullOrEmpty(path))
                throw new System.ArgumentException("Scene path is required");

            // Ensure path starts with Assets/
            if (!path.StartsWith("Assets/"))
                path = "Assets/" + path;

            // Ensure .unity extension
            if (!path.EndsWith(".unity"))
                path += ".unity";

            // Create directory if needed
            string dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, path);

            return Success($"Scene created at {path}");
        }

        private static object OpenScene(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            if (string.IsNullOrEmpty(path))
                throw new System.ArgumentException("Scene path is required");

            if (!System.IO.File.Exists(path))
                throw new System.ArgumentException($"Scene file not found: {path}");

            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            EditorSceneManager.OpenScene(path);

            return Success($"Opened scene: {path}");
        }

        private static object SaveScene(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            var scene = SceneManager.GetActiveScene();

            if (string.IsNullOrEmpty(path))
                path = scene.path;

            EditorSceneManager.SaveScene(scene, path);
            return Success($"Scene saved to {path}");
        }

        private static object PlayScene(Dictionary<string, object> p)
        {
            if (EditorApplication.isPlaying)
                return Success("Already in Play Mode");

            EditorApplication.isPlaying = true;
            return Success("Entering Play Mode");
        }

        private static object StopScene(Dictionary<string, object> p)
        {
            if (!EditorApplication.isPlaying)
                return Success("Not in Play Mode");

            EditorApplication.isPlaying = false;
            return Success("Exiting Play Mode");
        }
    }
}
