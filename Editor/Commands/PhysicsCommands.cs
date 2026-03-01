using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityMcpPro
{
    public class PhysicsCommands : BaseCommand
    {
        public static void Register(CommandRouter router)
        {
            router.Register("add_collider", AddCollider);
            router.Register("setup_rigidbody", SetupRigidbody);
            router.Register("get_physics_layers", GetPhysicsLayers);
            router.Register("set_collision_matrix", SetCollisionMatrix);
            router.Register("raycast_test", RaycastTest);
            router.Register("add_joint", AddJoint);
        }

        private static object AddCollider(Dictionary<string, object> p)
        {
            string goPath = GetStringParam(p, "game_object_path");
            string type = GetStringParam(p, "type", "Box");
            bool isTrigger = GetBoolParam(p, "is_trigger");
            string sizeStr = GetStringParam(p, "size");
            string centerStr = GetStringParam(p, "center");
            float radius = GetFloatParam(p, "radius", -1f);

            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("game_object_path is required");

            var go = FindGameObject(goPath);
            Collider collider;

            switch (type.ToLower())
            {
                case "box":
                    var box = Undo.AddComponent<BoxCollider>(go);
                    if (!string.IsNullOrEmpty(sizeStr)) box.size = TypeParser.ParseVector3(sizeStr);
                    if (!string.IsNullOrEmpty(centerStr)) box.center = TypeParser.ParseVector3(centerStr);
                    collider = box;
                    break;
                case "sphere":
                    var sphere = Undo.AddComponent<SphereCollider>(go);
                    if (radius >= 0) sphere.radius = radius;
                    if (!string.IsNullOrEmpty(centerStr)) sphere.center = TypeParser.ParseVector3(centerStr);
                    collider = sphere;
                    break;
                case "capsule":
                    var capsule = Undo.AddComponent<CapsuleCollider>(go);
                    if (radius >= 0) capsule.radius = radius;
                    if (!string.IsNullOrEmpty(centerStr)) capsule.center = TypeParser.ParseVector3(centerStr);
                    collider = capsule;
                    break;
                case "mesh":
                    collider = Undo.AddComponent<MeshCollider>(go);
                    break;
                default:
                    throw new ArgumentException($"Unknown collider type: {type}");
            }

            collider.isTrigger = isTrigger;

            return new Dictionary<string, object>
            {
                { "success", true },
                { "gameObject", go.name },
                { "colliderType", collider.GetType().Name },
                { "isTrigger", isTrigger }
            };
        }

        private static object SetupRigidbody(Dictionary<string, object> p)
        {
            string goPath = GetStringParam(p, "game_object_path");
            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("game_object_path is required");

            var go = FindGameObject(goPath);
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null)
                rb = Undo.AddComponent<Rigidbody>(go);
            else
                RecordUndo(rb, "Setup Rigidbody");

            if (p.ContainsKey("mass")) rb.mass = GetFloatParam(p, "mass", 1f);
            if (p.ContainsKey("use_gravity")) rb.useGravity = GetBoolParam(p, "use_gravity", true);
            if (p.ContainsKey("is_kinematic")) rb.isKinematic = GetBoolParam(p, "is_kinematic");
            if (p.ContainsKey("drag")) rb.linearDamping = GetFloatParam(p, "drag");
            if (p.ContainsKey("angular_drag")) rb.angularDamping = GetFloatParam(p, "angular_drag", 0.05f);

            string constraintsStr = GetStringParam(p, "constraints");
            if (!string.IsNullOrEmpty(constraintsStr))
            {
                RigidbodyConstraints constraints = RigidbodyConstraints.None;
                foreach (var c in constraintsStr.Split(','))
                {
                    if (Enum.TryParse<RigidbodyConstraints>(c.Trim(), true, out var parsed))
                        constraints |= parsed;
                }
                rb.constraints = constraints;
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "gameObject", go.name },
                { "mass", rb.mass },
                { "useGravity", rb.useGravity },
                { "isKinematic", rb.isKinematic }
            };
        }

        private static object GetPhysicsLayers(Dictionary<string, object> p)
        {
            var layers = new List<object>();
            for (int i = 0; i < 32; i++)
            {
                string name = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(name))
                {
                    layers.Add(new Dictionary<string, object>
                    {
                        { "index", i },
                        { "name", name }
                    });
                }
            }

            var collisionMatrix = new List<object>();
            for (int i = 0; i < 32; i++)
            {
                string nameA = LayerMask.LayerToName(i);
                if (string.IsNullOrEmpty(nameA)) continue;
                for (int j = i; j < 32; j++)
                {
                    string nameB = LayerMask.LayerToName(j);
                    if (string.IsNullOrEmpty(nameB)) continue;
                    if (Physics.GetIgnoreLayerCollision(i, j))
                    {
                        collisionMatrix.Add(new Dictionary<string, object>
                        {
                            { "layerA", nameA },
                            { "layerB", nameB },
                            { "ignored", true }
                        });
                    }
                }
            }

            return new Dictionary<string, object>
            {
                { "layers", layers },
                { "ignoredCollisions", collisionMatrix }
            };
        }

        private static object SetCollisionMatrix(Dictionary<string, object> p)
        {
            string layerA = GetStringParam(p, "layer_a");
            string layerB = GetStringParam(p, "layer_b");
            bool collide = GetBoolParam(p, "collide", true);

            if (string.IsNullOrEmpty(layerA) || string.IsNullOrEmpty(layerB))
                throw new ArgumentException("layer_a and layer_b are required");

            int idxA = LayerMask.NameToLayer(layerA);
            int idxB = LayerMask.NameToLayer(layerB);
            if (idxA < 0) throw new ArgumentException($"Layer not found: {layerA}");
            if (idxB < 0) throw new ArgumentException($"Layer not found: {layerB}");

            Physics.IgnoreLayerCollision(idxA, idxB, !collide);
            return Success($"Set collision between '{layerA}' and '{layerB}' to {(collide ? "enabled" : "disabled")}");
        }

        private static object RaycastTest(Dictionary<string, object> p)
        {
            string originStr = GetStringParam(p, "origin", "0,0,0");
            string directionStr = GetStringParam(p, "direction", "0,-1,0");
            float maxDistance = GetFloatParam(p, "max_distance", 1000f);
            int layerMask = GetIntParam(p, "layer_mask", -1);

            var origin = TypeParser.ParseVector3(originStr);
            var direction = TypeParser.ParseVector3(directionStr);

            if (Physics.Raycast(origin, direction.normalized, out RaycastHit hit, maxDistance, layerMask))
            {
                return new Dictionary<string, object>
                {
                    { "hit", true },
                    { "gameObject", hit.collider.gameObject.name },
                    { "point", $"Vector3({hit.point.x},{hit.point.y},{hit.point.z})" },
                    { "normal", $"Vector3({hit.normal.x},{hit.normal.y},{hit.normal.z})" },
                    { "distance", hit.distance },
                    { "collider", hit.collider.GetType().Name }
                };
            }

            return new Dictionary<string, object> { { "hit", false } };
        }

        private static object AddJoint(Dictionary<string, object> p)
        {
            string goPath = GetStringParam(p, "game_object_path");
            string type = GetStringParam(p, "type", "Fixed");
            string connectedPath = GetStringParam(p, "connected_body");

            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("game_object_path is required");

            var go = FindGameObject(goPath);
            Joint joint;

            switch (type.ToLower())
            {
                case "hinge": joint = Undo.AddComponent<HingeJoint>(go); break;
                case "spring": joint = Undo.AddComponent<SpringJoint>(go); break;
                case "fixed": joint = Undo.AddComponent<FixedJoint>(go); break;
                case "character": joint = Undo.AddComponent<CharacterJoint>(go); break;
                case "configurable": joint = Undo.AddComponent<ConfigurableJoint>(go); break;
                default: throw new ArgumentException($"Unknown joint type: {type}");
            }

            if (!string.IsNullOrEmpty(connectedPath))
            {
                var connected = FindGameObject(connectedPath);
                var connectedRb = connected.GetComponent<Rigidbody>();
                if (connectedRb == null) connectedRb = Undo.AddComponent<Rigidbody>(connected);
                joint.connectedBody = connectedRb;
            }

            if (go.GetComponent<Rigidbody>() == null)
                Undo.AddComponent<Rigidbody>(go);

            var props = GetDictParam(p, "properties");
            if (props != null)
            {
                var so = new SerializedObject(joint);
                foreach (var kvp in props)
                {
                    var prop = so.FindProperty(kvp.Key);
                    if (prop != null) SetSerializedPropertyValue(prop, kvp.Value);
                }
                so.ApplyModifiedProperties();
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "gameObject", go.name },
                { "jointType", joint.GetType().Name },
                { "connectedBody", joint.connectedBody != null ? joint.connectedBody.name : "none" }
            };
        }
    }
}
