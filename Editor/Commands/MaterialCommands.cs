using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityMcpPro
{
    public class MaterialCommands : BaseCommand
    {
        public static void Register(CommandRouter router)
        {
            router.Register("create_material", CreateMaterial);
            router.Register("get_material_properties", GetMaterialProperties);
            router.Register("set_material_property", SetMaterialProperty);
            router.Register("assign_material", AssignMaterial);
            router.Register("list_shaders", ListShaders);
            router.Register("create_shader", CreateShader);
        }

        private static object CreateMaterial(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            string shaderName = GetStringParam(p, "shader", "Standard");

            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("path is required");

            if (!path.EndsWith(".mat"))
                path += ".mat";

            var shader = Shader.Find(shaderName);
            if (shader == null)
                throw new ArgumentException($"Shader not found: {shaderName}");

            var mat = new Material(shader);

            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            AssetDatabase.CreateAsset(mat, path);

            var props = GetDictParam(p, "properties");
            if (props != null)
            {
                foreach (var kvp in props)
                    ApplyMaterialProperty(mat, kvp.Key, kvp.Value);
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssets();
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "path", path },
                { "shader", shaderName }
            };
        }

        private static object GetMaterialProperties(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("path is required");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
                throw new ArgumentException($"Material not found at: {path}");

            var shader = mat.shader;
            int count = ShaderUtil.GetPropertyCount(shader);
            var properties = new List<object>();

            for (int i = 0; i < count; i++)
            {
                var propName = ShaderUtil.GetPropertyName(shader, i);
                var propType = ShaderUtil.GetPropertyType(shader, i);
                var propDesc = ShaderUtil.GetPropertyDescription(shader, i);

                var propData = new Dictionary<string, object>
                {
                    { "name", propName },
                    { "type", propType.ToString() },
                    { "description", propDesc }
                };

                switch (propType)
                {
                    case ShaderUtil.ShaderPropertyType.Color:
                        var c = mat.GetColor(propName);
                        propData["value"] = $"Color({c.r},{c.g},{c.b},{c.a})";
                        break;
                    case ShaderUtil.ShaderPropertyType.Float:
                    case ShaderUtil.ShaderPropertyType.Range:
                        propData["value"] = mat.GetFloat(propName);
                        break;
                    case ShaderUtil.ShaderPropertyType.Vector:
                        var v = mat.GetVector(propName);
                        propData["value"] = $"Vector4({v.x},{v.y},{v.z},{v.w})";
                        break;
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        var tex = mat.GetTexture(propName);
                        propData["value"] = tex != null ? AssetDatabase.GetAssetPath(tex) : null;
                        break;
                }

                properties.Add(propData);
            }

            return new Dictionary<string, object>
            {
                { "material", mat.name },
                { "shader", shader.name },
                { "properties", properties }
            };
        }

        private static object SetMaterialProperty(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            string property = GetStringParam(p, "property");
            string type = GetStringParam(p, "type");

            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("path is required");
            if (string.IsNullOrEmpty(property))
                throw new ArgumentException("property is required");
            if (!p.ContainsKey("value"))
                throw new ArgumentException("value is required");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
                throw new ArgumentException($"Material not found at: {path}");

            Undo.RecordObject(mat, "MCP: Set Material Property");
            ApplyMaterialProperty(mat, property, p["value"], type);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            return Success($"Set {property} on {mat.name}");
        }

        private static object AssignMaterial(Dictionary<string, object> p)
        {
            string goPath = GetStringParam(p, "game_object_path");
            string matPath = GetStringParam(p, "material_path");
            int slot = GetIntParam(p, "slot", 0);

            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("game_object_path is required");
            if (string.IsNullOrEmpty(matPath))
                throw new ArgumentException("material_path is required");

            var go = FindGameObject(goPath);
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null)
                throw new ArgumentException($"No Renderer on {go.name}");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
                throw new ArgumentException($"Material not found at: {matPath}");

            RecordUndo(renderer, "Assign Material");

            var mats = renderer.sharedMaterials;
            if (slot < 0 || slot >= mats.Length)
            {
                var newMats = new Material[slot + 1];
                Array.Copy(mats, newMats, mats.Length);
                mats = newMats;
            }
            mats[slot] = mat;
            renderer.sharedMaterials = mats;

            return Success($"Assigned {mat.name} to {go.name} slot {slot}");
        }

        private static object ListShaders(Dictionary<string, object> p)
        {
            string filter = GetStringParam(p, "filter");

            var shaderInfos = ShaderUtil.GetAllShaderInfo();
            var result = new List<object>();

            foreach (var info in shaderInfos)
            {
                if (info.name.StartsWith("Hidden/")) continue;
                if (!string.IsNullOrEmpty(filter) &&
                    info.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                result.Add(new Dictionary<string, object>
                {
                    { "name", info.name },
                    { "supported", info.supported }
                });
            }

            return new Dictionary<string, object>
            {
                { "count", result.Count },
                { "shaders", result }
            };
        }

        private static object CreateShader(Dictionary<string, object> p)
        {
            string path = GetStringParam(p, "path");
            string type = GetStringParam(p, "type", "Unlit");
            string template = GetStringParam(p, "template");

            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("path is required");

            if (!path.EndsWith(".shader"))
                path += ".shader";

            string shaderName = System.IO.Path.GetFileNameWithoutExtension(path);
            string content;

            if (!string.IsNullOrEmpty(template))
            {
                content = template;
            }
            else
            {
                switch (type.ToLower())
                {
                    case "surface":
                        content = GetSurfaceShaderTemplate(shaderName);
                        break;
                    case "unlit":
                    default:
                        content = GetUnlitShaderTemplate(shaderName);
                        break;
                }
            }

            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            System.IO.File.WriteAllText(path, content);
            AssetDatabase.Refresh();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "path", path },
                { "type", type }
            };
        }

        private static void ApplyMaterialProperty(Material mat, string property, object value, string typeHint = null)
        {
            string strVal = value?.ToString() ?? "";

            if (typeHint != null)
            {
                switch (typeHint.ToLower())
                {
                    case "color":
                        mat.SetColor(property, TypeParser.ParseColor(strVal));
                        return;
                    case "float":
                        mat.SetFloat(property, Convert.ToSingle(value));
                        return;
                    case "vector":
                        mat.SetVector(property, TypeParser.ParseVector4(strVal));
                        return;
                    case "texture":
                        var tex = AssetDatabase.LoadAssetAtPath<Texture>(strVal);
                        if (tex != null) mat.SetTexture(property, tex);
                        return;
                    case "int":
                        mat.SetInt(property, Convert.ToInt32(value));
                        return;
                }
            }

            if (mat.HasProperty(property))
            {
                if (strVal.StartsWith("Color(") || strVal.StartsWith("#"))
                    mat.SetColor(property, TypeParser.ParseColor(strVal));
                else if (strVal.StartsWith("Vector"))
                    mat.SetVector(property, TypeParser.ParseVector4(strVal));
                else if (float.TryParse(strVal, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float f))
                    mat.SetFloat(property, f);
                else
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture>(strVal);
                    if (tex != null) mat.SetTexture(property, tex);
                }
            }
        }

        private static string GetUnlitShaderTemplate(string name)
        {
            return "Shader \"Custom/" + name + "\"\n{\n    Properties\n    {\n        _MainTex (\"Texture\", 2D) = \"white\" {}\n        _Color (\"Color\", Color) = (1,1,1,1)\n    }\n    SubShader\n    {\n        Tags { \"RenderType\"=\"Opaque\" }\n        LOD 100\n\n        Pass\n        {\n            CGPROGRAM\n            #pragma vertex vert\n            #pragma fragment frag\n            #include \"UnityCG.cginc\"\n\n            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };\n            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };\n\n            sampler2D _MainTex;\n            float4 _MainTex_ST;\n            fixed4 _Color;\n\n            v2f vert (appdata v)\n            {\n                v2f o;\n                o.vertex = UnityObjectToClipPos(v.vertex);\n                o.uv = TRANSFORM_TEX(v.uv, _MainTex);\n                return o;\n            }\n\n            fixed4 frag (v2f i) : SV_Target\n            {\n                return tex2D(_MainTex, i.uv) * _Color;\n            }\n            ENDCG\n        }\n    }\n}";
        }

        private static string GetSurfaceShaderTemplate(string name)
        {
            return "Shader \"Custom/" + name + "\"\n{\n    Properties\n    {\n        _Color (\"Color\", Color) = (1,1,1,1)\n        _MainTex (\"Albedo (RGB)\", 2D) = \"white\" {}\n        _Glossiness (\"Smoothness\", Range(0,1)) = 0.5\n        _Metallic (\"Metallic\", Range(0,1)) = 0.0\n    }\n    SubShader\n    {\n        Tags { \"RenderType\"=\"Opaque\" }\n        LOD 200\n\n        CGPROGRAM\n        #pragma surface surf Standard fullforwardshadows\n        #pragma target 3.0\n\n        sampler2D _MainTex;\n        half _Glossiness;\n        half _Metallic;\n        fixed4 _Color;\n\n        struct Input { float2 uv_MainTex; };\n\n        void surf (Input IN, inout SurfaceOutputStandard o)\n        {\n            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;\n            o.Albedo = c.rgb;\n            o.Metallic = _Metallic;\n            o.Smoothness = _Glossiness;\n            o.Alpha = c.a;\n        }\n        ENDCG\n    }\n    FallBack \"Diffuse\"\n}";
        }
    }
}
