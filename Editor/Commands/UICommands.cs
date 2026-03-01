using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace UnityMcpPro
{
    public class UICommands : BaseCommand
    {
        public static void Register(CommandRouter router)
        {
            router.Register("create_canvas", CreateCanvas);
            router.Register("add_ui_element", AddUIElement);
            router.Register("set_rect_transform", SetRectTransform);
            router.Register("set_ui_text", SetUIText);
            router.Register("set_ui_image", SetUIImage);
            router.Register("add_ui_layout", AddUILayout);
        }

        private static object CreateCanvas(Dictionary<string, object> p)
        {
            string renderModeStr = GetStringParam(p, "render_mode", "ScreenSpaceOverlay");
            string name = GetStringParam(p, "name", "Canvas");
            string parentPath = GetStringParam(p, "parent");

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "MCP: Create Canvas");

            var canvas = go.AddComponent<Canvas>();
            switch (renderModeStr.ToLower())
            {
                case "screenspacecamera":
                case "camera":
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    break;
                case "worldspace":
                case "world":
                    canvas.renderMode = RenderMode.WorldSpace;
                    break;
                case "screenspaceoverlay":
                case "overlay":
                default:
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    break;
            }

            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();

            if (!string.IsNullOrEmpty(parentPath))
            {
                var parent = FindGameObject(parentPath);
                go.transform.SetParent(parent.transform, false);
            }

            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<StandaloneInputModule>();
                Undo.RegisterCreatedObjectUndo(esGo, "MCP: Create EventSystem");
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "name", go.name },
                { "renderMode", canvas.renderMode.ToString() },
                { "path", GetGameObjectPath(go) }
            };
        }

        private static object AddUIElement(Dictionary<string, object> p)
        {
            string type = GetStringParam(p, "type", "Text");
            string parentPath = GetStringParam(p, "parent");
            string name = GetStringParam(p, "name");
            string text = GetStringParam(p, "text");
            string sizeStr = GetStringParam(p, "size");
            string posStr = GetStringParam(p, "position");

            if (string.IsNullOrEmpty(parentPath))
                throw new ArgumentException("parent is required (usually a Canvas path)");

            var parent = FindGameObject(parentPath);
            GameObject go;

            var resources = new DefaultControls.Resources();

            switch (type.ToLower())
            {
                case "text":
                    go = DefaultControls.CreateText(resources);
                    if (!string.IsNullOrEmpty(text))
                        go.GetComponent<Text>().text = text;
                    break;
                case "image":
                    go = DefaultControls.CreateImage(resources);
                    break;
                case "button":
                    go = DefaultControls.CreateButton(resources);
                    if (!string.IsNullOrEmpty(text))
                    {
                        var btnText = go.GetComponentInChildren<Text>();
                        if (btnText != null) btnText.text = text;
                    }
                    break;
                case "toggle":
                    go = DefaultControls.CreateToggle(resources);
                    break;
                case "slider":
                    go = DefaultControls.CreateSlider(resources);
                    break;
                case "inputfield":
                case "input":
                    go = DefaultControls.CreateInputField(resources);
                    break;
                case "dropdown":
                    go = DefaultControls.CreateDropdown(resources);
                    break;
                case "scrollview":
                case "scroll":
                    go = DefaultControls.CreateScrollView(resources);
                    break;
                case "rawimage":
                    go = DefaultControls.CreateRawImage(resources);
                    break;
                case "panel":
                    go = DefaultControls.CreatePanel(resources);
                    break;
                default:
                    go = new GameObject(type);
                    go.AddComponent<RectTransform>();
                    break;
            }

            go.transform.SetParent(parent.transform, false);
            Undo.RegisterCreatedObjectUndo(go, $"MCP: Add UI {type}");

            if (!string.IsNullOrEmpty(name))
                go.name = name;

            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                if (!string.IsNullOrEmpty(sizeStr))
                    rt.sizeDelta = TypeParser.ParseVector2(sizeStr);
                if (!string.IsNullOrEmpty(posStr))
                    rt.anchoredPosition = TypeParser.ParseVector2(posStr);
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "name", go.name },
                { "type", type },
                { "path", GetGameObjectPath(go) }
            };
        }

        private static object SetRectTransform(Dictionary<string, object> p)
        {
            string goPath = GetStringParam(p, "game_object_path");
            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("game_object_path is required");

            var go = FindGameObject(goPath);
            var rt = go.GetComponent<RectTransform>();
            if (rt == null)
                throw new ArgumentException($"No RectTransform on {go.name}");

            RecordUndo(rt, "Set RectTransform");

            string anchoredPosStr = GetStringParam(p, "anchored_position");
            string sizeDeltaStr = GetStringParam(p, "size_delta");
            string anchorsMinStr = GetStringParam(p, "anchors_min");
            string anchorsMaxStr = GetStringParam(p, "anchors_max");
            string pivotStr = GetStringParam(p, "pivot");

            if (!string.IsNullOrEmpty(anchoredPosStr))
                rt.anchoredPosition = TypeParser.ParseVector2(anchoredPosStr);
            if (!string.IsNullOrEmpty(sizeDeltaStr))
                rt.sizeDelta = TypeParser.ParseVector2(sizeDeltaStr);
            if (!string.IsNullOrEmpty(anchorsMinStr))
                rt.anchorMin = TypeParser.ParseVector2(anchorsMinStr);
            if (!string.IsNullOrEmpty(anchorsMaxStr))
                rt.anchorMax = TypeParser.ParseVector2(anchorsMaxStr);
            if (!string.IsNullOrEmpty(pivotStr))
                rt.pivot = TypeParser.ParseVector2(pivotStr);

            EditorUtility.SetDirty(go);

            return new Dictionary<string, object>
            {
                { "success", true },
                { "gameObject", go.name },
                { "anchoredPosition", $"{rt.anchoredPosition.x},{rt.anchoredPosition.y}" },
                { "sizeDelta", $"{rt.sizeDelta.x},{rt.sizeDelta.y}" }
            };
        }

        private static object SetUIText(Dictionary<string, object> p)
        {
            string goPath = GetStringParam(p, "game_object_path");
            string text = GetStringParam(p, "text");
            string colorStr = GetStringParam(p, "color");
            string alignStr = GetStringParam(p, "alignment");

            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("game_object_path is required");

            var go = FindGameObject(goPath);
            var textComp = go.GetComponent<Text>();
            if (textComp == null)
                throw new ArgumentException($"No Text component on {go.name}");

            RecordUndo(textComp, "Set UI Text");

            if (text != null) textComp.text = text;
            if (p.ContainsKey("font_size"))
                textComp.fontSize = GetIntParam(p, "font_size", 14);
            if (!string.IsNullOrEmpty(colorStr))
                textComp.color = TypeParser.ParseColor(colorStr);
            if (!string.IsNullOrEmpty(alignStr))
            {
                if (Enum.TryParse<TextAnchor>(alignStr, true, out var anchor))
                    textComp.alignment = anchor;
            }

            EditorUtility.SetDirty(textComp);

            return Success($"Updated text on {go.name}");
        }

        private static object SetUIImage(Dictionary<string, object> p)
        {
            string goPath = GetStringParam(p, "game_object_path");
            string spritePath = GetStringParam(p, "sprite_path");
            string colorStr = GetStringParam(p, "color");
            string typeStr = GetStringParam(p, "type");

            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("game_object_path is required");

            var go = FindGameObject(goPath);
            var image = go.GetComponent<Image>();
            if (image == null)
                throw new ArgumentException($"No Image component on {go.name}");

            RecordUndo(image, "Set UI Image");

            if (!string.IsNullOrEmpty(spritePath))
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
                if (sprite != null)
                    image.sprite = sprite;
            }

            if (!string.IsNullOrEmpty(colorStr))
                image.color = TypeParser.ParseColor(colorStr);

            if (!string.IsNullOrEmpty(typeStr))
            {
                if (Enum.TryParse<Image.Type>(typeStr, true, out var imgType))
                    image.type = imgType;
            }

            if (p.ContainsKey("raycast_target"))
                image.raycastTarget = GetBoolParam(p, "raycast_target", true);

            EditorUtility.SetDirty(image);

            return Success($"Updated image on {go.name}");
        }

        private static object AddUILayout(Dictionary<string, object> p)
        {
            string goPath = GetStringParam(p, "game_object_path");
            string type = GetStringParam(p, "type", "Vertical");

            if (string.IsNullOrEmpty(goPath))
                throw new ArgumentException("game_object_path is required");

            var go = FindGameObject(goPath);
            HorizontalOrVerticalLayoutGroup layout;

            switch (type.ToLower())
            {
                case "horizontal":
                    layout = Undo.AddComponent<HorizontalLayoutGroup>(go);
                    break;
                case "grid":
                    var grid = Undo.AddComponent<GridLayoutGroup>(go);
                    if (p.ContainsKey("spacing"))
                    {
                        var spacing = TypeParser.ParseVector2(GetStringParam(p, "spacing", "0,0"));
                        grid.spacing = spacing;
                    }
                    string childAlignGridStr = GetStringParam(p, "child_alignment");
                    if (!string.IsNullOrEmpty(childAlignGridStr))
                    {
                        if (Enum.TryParse<TextAnchor>(childAlignGridStr, true, out var align))
                            grid.childAlignment = align;
                    }
                    return new Dictionary<string, object>
                    {
                        { "success", true },
                        { "gameObject", go.name },
                        { "layoutType", "GridLayoutGroup" }
                    };
                case "vertical":
                default:
                    layout = Undo.AddComponent<VerticalLayoutGroup>(go);
                    break;
            }

            if (p.ContainsKey("spacing"))
                layout.spacing = GetFloatParam(p, "spacing");

            string paddingStr = GetStringParam(p, "padding");
            if (!string.IsNullOrEmpty(paddingStr))
            {
                var parts = paddingStr.Split(',');
                if (parts.Length >= 4)
                {
                    layout.padding = new RectOffset(
                        int.Parse(parts[0].Trim()),
                        int.Parse(parts[1].Trim()),
                        int.Parse(parts[2].Trim()),
                        int.Parse(parts[3].Trim())
                    );
                }
            }

            string childAlignStr = GetStringParam(p, "child_alignment");
            if (!string.IsNullOrEmpty(childAlignStr))
            {
                if (Enum.TryParse<TextAnchor>(childAlignStr, true, out var align))
                    layout.childAlignment = align;
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "gameObject", go.name },
                { "layoutType", layout.GetType().Name }
            };
        }
    }
}
