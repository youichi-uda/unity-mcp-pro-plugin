using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityMcpPro
{
    public class TerrainCommands : BaseCommand
    {
        public static void Register(CommandRouter router)
        {
            router.Register("create_terrain", CreateTerrain);
            router.Register("set_terrain_heightmap", SetTerrainHeightmap);
            router.Register("add_terrain_layer", AddTerrainLayer);
            router.Register("set_terrain_trees", SetTerrainTrees);
        }

        private static object CreateTerrain(Dictionary<string, object> p)
        {
            string name = GetStringParam(p, "name", "Terrain");
            int width = GetIntParam(p, "width", 500);
            int length = GetIntParam(p, "length", 500);
            int height = GetIntParam(p, "height", 200);
            int heightmapResolution = GetIntParam(p, "heightmap_resolution", 513);
            string posStr = GetStringParam(p, "position");

            var terrainData = new TerrainData();
            terrainData.heightmapResolution = heightmapResolution;
            terrainData.size = new Vector3(width, height, length);

            // Save terrain data as asset
            string assetPath = $"Assets/{name}_Data.asset";
            AssetDatabase.CreateAsset(terrainData, assetPath);

            var go = Terrain.CreateTerrainGameObject(terrainData);
            go.name = name;
            Undo.RegisterCreatedObjectUndo(go, "MCP: Create Terrain");

            if (!string.IsNullOrEmpty(posStr))
                go.transform.position = TypeParser.ParseVector3(posStr);

            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "name", go.name },
                { "size", $"{width}x{height}x{length}" },
                { "heightmapResolution", heightmapResolution },
                { "dataAsset", assetPath },
                { "path", GetGameObjectPath(go) }
            };
        }

        private static object SetTerrainHeightmap(Dictionary<string, object> p)
        {
            string goPath = GetStringParam(p, "game_object_path");
            string mode = GetStringParam(p, "mode", "flat");
            float value = GetFloatParam(p, "value", 0f);
            int centerX = GetIntParam(p, "center_x", -1);
            int centerY = GetIntParam(p, "center_y", -1);
            int radius = GetIntParam(p, "radius", 50);
            float strength = GetFloatParam(p, "strength", 0.1f);

            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("game_object_path is required");

            var go = FindGameObject(goPath);
            var terrain = go.GetComponent<Terrain>();
            if (terrain == null)
                throw new ArgumentException($"No Terrain on {go.name}");

            var data = terrain.terrainData;
            RecordUndo(data, "Set Heightmap");

            int res = data.heightmapResolution;

            switch (mode.ToLower())
            {
                case "flat":
                    var flatHeights = new float[res, res];
                    for (int y = 0; y < res; y++)
                        for (int x = 0; x < res; x++)
                            flatHeights[y, x] = value;
                    data.SetHeights(0, 0, flatHeights);
                    break;

                case "raise":
                    if (centerX < 0) centerX = res / 2;
                    if (centerY < 0) centerY = res / 2;
                    var currentHeights = data.GetHeights(0, 0, res, res);
                    for (int y = 0; y < res; y++)
                    {
                        for (int x = 0; x < res; x++)
                        {
                            float dist = Mathf.Sqrt((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY));
                            if (dist < radius)
                            {
                                float falloff = 1f - (dist / radius);
                                currentHeights[y, x] += strength * falloff;
                                currentHeights[y, x] = Mathf.Clamp01(currentHeights[y, x]);
                            }
                        }
                    }
                    data.SetHeights(0, 0, currentHeights);
                    break;

                case "perlin":
                    float scale = GetFloatParam(p, "scale", 20f);
                    var perlinHeights = new float[res, res];
                    for (int y = 0; y < res; y++)
                    {
                        for (int x = 0; x < res; x++)
                        {
                            perlinHeights[y, x] = Mathf.PerlinNoise(
                                (float)x / res * scale,
                                (float)y / res * scale
                            ) * strength;
                        }
                    }
                    data.SetHeights(0, 0, perlinHeights);
                    break;

                default:
                    throw new ArgumentException($"Unknown mode: {mode}. Use 'flat', 'raise', or 'perlin'");
            }

            terrain.Flush();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "terrain", go.name },
                { "mode", mode },
                { "resolution", res }
            };
        }

        private static object AddTerrainLayer(Dictionary<string, object> p)
        {
            string goPath = GetStringParam(p, "game_object_path");
            string diffusePath = GetStringParam(p, "diffuse_texture");
            string normalPath = GetStringParam(p, "normal_texture");
            float tileX = GetFloatParam(p, "tile_size_x", 15f);
            float tileY = GetFloatParam(p, "tile_size_y", 15f);

            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("game_object_path is required");
            if (string.IsNullOrEmpty(diffusePath))
                throw new ArgumentException("diffuse_texture is required");

            var go = FindGameObject(goPath);
            var terrain = go.GetComponent<Terrain>();
            if (terrain == null)
                throw new ArgumentException($"No Terrain on {go.name}");

            var diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(diffusePath);
            if (diffuse == null)
                throw new ArgumentException($"Texture not found at: {diffusePath}");

            var layer = new TerrainLayer();
            layer.diffuseTexture = diffuse;
            layer.tileSize = new Vector2(tileX, tileY);

            if (!string.IsNullOrEmpty(normalPath))
            {
                var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                if (normal != null)
                    layer.normalMapTexture = normal;
            }

            // Save the terrain layer as an asset
            string layerPath = $"Assets/TerrainLayer_{diffuse.name}.asset";
            AssetDatabase.CreateAsset(layer, layerPath);

            var data = terrain.terrainData;
            RecordUndo(data, "Add Terrain Layer");

            var layers = new List<TerrainLayer>(data.terrainLayers);
            layers.Add(layer);
            data.terrainLayers = layers.ToArray();

            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "terrain", go.name },
                { "layerCount", data.terrainLayers.Length },
                { "diffuse", diffuse.name },
                { "layerAsset", layerPath }
            };
        }

        private static object SetTerrainTrees(Dictionary<string, object> p)
        {
            string goPath = GetStringParam(p, "game_object_path");
            string prefabPath = GetStringParam(p, "tree_prefab");
            int count = GetIntParam(p, "count", 100);
            float minHeight = GetFloatParam(p, "min_height", 0.8f);
            float maxHeight = GetFloatParam(p, "max_height", 1.2f);
            float minWidth = GetFloatParam(p, "min_width", 0.8f);
            float maxWidth = GetFloatParam(p, "max_width", 1.2f);
            int seed = GetIntParam(p, "seed", 42);

            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("game_object_path is required");
            if (string.IsNullOrEmpty(prefabPath))
                throw new ArgumentException("tree_prefab is required");

            var go = FindGameObject(goPath);
            var terrain = go.GetComponent<Terrain>();
            if (terrain == null)
                throw new ArgumentException($"No Terrain on {go.name}");

            var treePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (treePrefab == null)
                throw new ArgumentException($"Tree prefab not found at: {prefabPath}");

            var data = terrain.terrainData;
            RecordUndo(data, "Set Terrain Trees");

            // Add tree prototype if not already present
            var protos = new List<TreePrototype>(data.treePrototypes);
            int protoIdx = -1;
            for (int i = 0; i < protos.Count; i++)
            {
                if (protos[i].prefab == treePrefab)
                {
                    protoIdx = i;
                    break;
                }
            }

            if (protoIdx < 0)
            {
                protos.Add(new TreePrototype { prefab = treePrefab });
                data.treePrototypes = protos.ToArray();
                protoIdx = protos.Count - 1;
            }

            // Generate random tree instances
            var rng = new System.Random(seed);
            var trees = new List<TreeInstance>(data.treeInstances);

            for (int i = 0; i < count; i++)
            {
                var tree = new TreeInstance
                {
                    prototypeIndex = protoIdx,
                    position = new Vector3((float)rng.NextDouble(), 0, (float)rng.NextDouble()),
                    widthScale = Mathf.Lerp(minWidth, maxWidth, (float)rng.NextDouble()),
                    heightScale = Mathf.Lerp(minHeight, maxHeight, (float)rng.NextDouble()),
                    color = Color.white,
                    lightmapColor = Color.white,
                    rotation = (float)(rng.NextDouble() * Mathf.PI * 2)
                };
                trees.Add(tree);
            }

            data.treeInstances = trees.ToArray();
            terrain.Flush();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "terrain", go.name },
                { "treesAdded", count },
                { "totalTrees", data.treeInstances.Length },
                { "prototype", treePrefab.name }
            };
        }
    }
}
