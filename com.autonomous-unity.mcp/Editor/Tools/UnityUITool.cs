using System.Collections.Generic;
using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AutonomousMcp.Editor.Tools
{
    /// <summary>
    /// unity_ui — uGUI scaffolding (Canvas, Button, Text, Image, Panel).
    /// Use unity_component for fine-grained edits afterwards.
    /// </summary>
    public static class UnityUITool
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            ToolRegistry.Register("unity_ui", ToolMode.Mutate, ToolCategory.UI,
                "uGUI scaffolding. Actions: create_canvas, create_panel, create_button, " +
                "create_text, create_image, set_anchor, set_rect.",
                Handle);
        }

        private static AutonomousMcpToolResponse Handle(JObject args)
        {
            var action = args.Value<string>("action") ?? "create_canvas";
            switch (action)
            {
                case "create_canvas": return CreateCanvas(args);
                case "create_panel": return CreatePanel(args);
                case "create_button": return CreateButton(args);
                case "create_text": return CreateText(args);
                case "create_image": return CreateImage(args);
                case "set_anchor": return SetAnchor(args);
                case "set_rect": return SetRect(args);
                default:
                    return Err($"Unsupported unity_ui action '{action}'.");
            }
        }

        private static AutonomousMcpToolResponse CreateCanvas(JObject args)
        {
            var name = args.Value<string>("name") ?? "Canvas";
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            EnsureEventSystem();
            Selection.activeGameObject = go;
            return Ok(new { action = "create_canvas", instanceId = go.GetInstanceID(), name });
        }

        private static AutonomousMcpToolResponse CreatePanel(JObject args)
        {
            var parent = ResolveParent(args, out var err);
            if (parent == null) return Err(err);
            var name = args.Value<string>("name") ?? "Panel";
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0, 0, 0, 0.5f);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return Ok(new { action = "create_panel", instanceId = go.GetInstanceID(), name });
        }

        private static AutonomousMcpToolResponse CreateButton(JObject args)
        {
            var parent = ResolveParent(args, out var err);
            if (parent == null) return Err(err);
            var name = args.Value<string>("name") ?? "Button";
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(160, 40);
            var labelGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelGo.transform.SetParent(go.transform, false);
            var label = labelGo.GetComponent<Text>();
            label.text = args.Value<string>("label") ?? "Button";
            label.alignment = TextAnchor.MiddleCenter;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.color = Color.black;
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero; labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero; labelRt.offsetMax = Vector2.zero;
            return Ok(new { action = "create_button", instanceId = go.GetInstanceID(), name });
        }

        private static AutonomousMcpToolResponse CreateText(JObject args)
        {
            var parent = ResolveParent(args, out var err);
            if (parent == null) return Err(err);
            var name = args.Value<string>("name") ?? "Text";
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var txt = go.GetComponent<Text>();
            txt.text = args.Value<string>("text") ?? "New Text";
            txt.alignment = TextAnchor.MiddleCenter;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.color = Color.white;
            return Ok(new { action = "create_text", instanceId = go.GetInstanceID(), name });
        }

        private static AutonomousMcpToolResponse CreateImage(JObject args)
        {
            var parent = ResolveParent(args, out var err);
            if (parent == null) return Err(err);
            var name = args.Value<string>("name") ?? "Image";
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            return Ok(new { action = "create_image", instanceId = go.GetInstanceID(), name });
        }

        private static AutonomousMcpToolResponse SetAnchor(JObject args)
        {
            var go = ResolveGo(args, out var err);
            if (go == null) return Err(err);
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) return Err("Target has no RectTransform.");
            var minX = args.Value<float?>("min_x") ?? rt.anchorMin.x;
            var minY = args.Value<float?>("min_y") ?? rt.anchorMin.y;
            var maxX = args.Value<float?>("max_x") ?? rt.anchorMax.x;
            var maxY = args.Value<float?>("max_y") ?? rt.anchorMax.y;
            rt.anchorMin = new Vector2(minX, minY);
            rt.anchorMax = new Vector2(maxX, maxY);
            return Ok(new { action = "set_anchor", anchorMin = new[] { minX, minY }, anchorMax = new[] { maxX, maxY } });
        }

        private static AutonomousMcpToolResponse SetRect(JObject args)
        {
            var go = ResolveGo(args, out var err);
            if (go == null) return Err(err);
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) return Err("Target has no RectTransform.");
            if (args["sizeDelta"] is JObject sd)
            {
                rt.sizeDelta = new Vector2(sd.Value<float?>("x") ?? rt.sizeDelta.x,
                                            sd.Value<float?>("y") ?? rt.sizeDelta.y);
            }
            if (args["anchoredPosition"] is JObject ap)
            {
                rt.anchoredPosition = new Vector2(ap.Value<float?>("x") ?? rt.anchoredPosition.x,
                                                   ap.Value<float?>("y") ?? rt.anchoredPosition.y);
            }
            return Ok(new
            {
                action = "set_rect",
                sizeDelta = new[] { rt.sizeDelta.x, rt.sizeDelta.y },
                anchoredPosition = new[] { rt.anchoredPosition.x, rt.anchoredPosition.y }
            });
        }

        private static Transform ResolveParent(JObject args, out string err)
        {
            err = string.Empty;
            var name = args.Value<string>("parent") ?? args.Value<string>("name");
            if (string.IsNullOrEmpty(name))
            {
                var canvas = Object.FindFirstObjectByType<Canvas>();
                if (canvas == null) { err = "No parent specified and no Canvas in scene."; return null; }
                return canvas.transform;
            }
            var go = GameObject.Find(name);
            if (go == null) { err = $"Parent '{name}' not found."; return null; }
            return go.transform;
        }

        private static GameObject ResolveGo(JObject args, out string err)
        {
            err = string.Empty;
            var id = args.Value<int?>("instanceId");
            if (id.HasValue)
            {
                var obj = EditorUtility.InstanceIDToObject(id.Value) as GameObject;
                if (obj == null) { err = $"instanceId {id} not a GameObject."; return null; }
                return obj;
            }
            var name = args.Value<string>("name");
            if (string.IsNullOrEmpty(name)) { err = "instanceId or name required."; return null; }
            var go = GameObject.Find(name);
            if (go == null) { err = $"GameObject '{name}' not found."; return null; }
            return go;
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                new GameObject("EventSystem",
                    typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.EventSystems.StandaloneInputModule));
            }
        }

        private static AutonomousMcpToolResponse Ok(object d) =>
            new AutonomousMcpToolResponse { success = true, data = JToken.FromObject(d), error = string.Empty };
        private static AutonomousMcpToolResponse Err(string m) =>
            new AutonomousMcpToolResponse { success = false, data = null, error = m };
    }
}
