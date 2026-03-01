using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AI;
using UnityEngine;
using UnityEngine.AI;

namespace UnityMcpPro
{
    public class NavigationCommands : BaseCommand
    {
        public static void Register(CommandRouter router)
        {
            router.Register("bake_navmesh", BakeNavMesh);
            router.Register("add_navmesh_agent", AddNavMeshAgent);
            router.Register("add_navmesh_obstacle", AddNavMeshObstacle);
            router.Register("add_offmesh_link", AddOffMeshLink);
            router.Register("get_navmesh_info", GetNavMeshInfo);
        }

        private static object BakeNavMesh(Dictionary<string, object> p)
        {
            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "message", "NavMesh bake completed" }
            };
        }

        private static object AddNavMeshAgent(Dictionary<string, object> p)
        {
            string goPath = GetStringParam(p, "game_object_path");
            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("game_object_path is required");

            var go = FindGameObject(goPath);
            var agent = go.GetComponent<NavMeshAgent>();
            if (agent == null)
                agent = Undo.AddComponent<NavMeshAgent>(go);
            else
                RecordUndo(agent, "Setup NavMeshAgent");

            if (p.ContainsKey("speed"))
                agent.speed = GetFloatParam(p, "speed", 3.5f);
            if (p.ContainsKey("angular_speed"))
                agent.angularSpeed = GetFloatParam(p, "angular_speed", 120f);
            if (p.ContainsKey("acceleration"))
                agent.acceleration = GetFloatParam(p, "acceleration", 8f);
            if (p.ContainsKey("stopping_distance"))
                agent.stoppingDistance = GetFloatParam(p, "stopping_distance");
            if (p.ContainsKey("radius"))
                agent.radius = GetFloatParam(p, "radius", 0.5f);
            if (p.ContainsKey("height"))
                agent.height = GetFloatParam(p, "height", 2f);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "gameObject", go.name },
                { "speed", agent.speed },
                { "radius", agent.radius }
            };
        }

        private static object AddNavMeshObstacle(Dictionary<string, object> p)
        {
            string goPath = GetStringParam(p, "game_object_path");
            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("game_object_path is required");

            var go = FindGameObject(goPath);
            var obstacle = go.GetComponent<NavMeshObstacle>();
            if (obstacle == null)
                obstacle = Undo.AddComponent<NavMeshObstacle>(go);
            else
                RecordUndo(obstacle, "Setup NavMeshObstacle");

            string shapeStr = GetStringParam(p, "shape");
            if (!string.IsNullOrEmpty(shapeStr))
            {
                if (Enum.TryParse<NavMeshObstacleShape>(shapeStr, true, out var shape))
                    obstacle.shape = shape;
            }

            if (p.ContainsKey("carve"))
                obstacle.carving = GetBoolParam(p, "carve");

            string sizeStr = GetStringParam(p, "size");
            if (!string.IsNullOrEmpty(sizeStr))
                obstacle.size = TypeParser.ParseVector3(sizeStr);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "gameObject", go.name },
                { "shape", obstacle.shape.ToString() },
                { "carving", obstacle.carving }
            };
        }

        private static object AddOffMeshLink(Dictionary<string, object> p)
        {
            string goPath = GetStringParam(p, "game_object_path");
            string startPath = GetStringParam(p, "start_point");
            string endPath = GetStringParam(p, "end_point");

            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("game_object_path is required");

            var go = FindGameObject(goPath);
            var link = Undo.AddComponent<OffMeshLink>(go);

            if (!string.IsNullOrEmpty(startPath))
            {
                var startGo = FindGameObject(startPath);
                link.startTransform = startGo.transform;
            }
            if (!string.IsNullOrEmpty(endPath))
            {
                var endGo = FindGameObject(endPath);
                link.endTransform = endGo.transform;
            }

            if (p.ContainsKey("bidirectional"))
                link.biDirectional = GetBoolParam(p, "bidirectional", true);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "gameObject", go.name },
                { "biDirectional", link.biDirectional }
            };
        }

        private static object GetNavMeshInfo(Dictionary<string, object> p)
        {
            var triangulation = NavMesh.CalculateTriangulation();

            // Count agents and obstacles in scene
            var agents = UnityEngine.Object.FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None);
            var obstacles = UnityEngine.Object.FindObjectsByType<NavMeshObstacle>(FindObjectsSortMode.None);

            var agentList = new List<object>();
            foreach (var agent in agents)
            {
                agentList.Add(new Dictionary<string, object>
                {
                    { "gameObject", agent.gameObject.name },
                    { "speed", agent.speed },
                    { "enabled", agent.enabled }
                });
            }

            var obstacleList = new List<object>();
            foreach (var obstacle in obstacles)
            {
                obstacleList.Add(new Dictionary<string, object>
                {
                    { "gameObject", obstacle.gameObject.name },
                    { "shape", obstacle.shape.ToString() },
                    { "carving", obstacle.carving }
                });
            }

            return new Dictionary<string, object>
            {
                { "hasNavMesh", triangulation.vertices.Length > 0 },
                { "vertices", triangulation.vertices.Length },
                { "triangles", triangulation.indices.Length / 3 },
                { "agents", agentList },
                { "obstacles", obstacleList }
            };
        }
    }
}
