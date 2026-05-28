using System.Reflection;
using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;

namespace AutonomousMcp.Editor.Tools
{
    /// <summary>
    /// unity_event — add/remove persistent listeners on UnityEvent fields (e.g. Button.onClick).
    /// </summary>
    public static class UnityEventTool
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            ToolRegistry.Register("unity_event", ToolMode.Mutate, ToolCategory.UI,
                "UnityEvent persistent listener management. " +
                "Actions: add_persistent, remove_persistent, list_persistent.",
                Handle);
        }

        private static AutonomousMcpToolResponse Handle(JObject args)
        {
            var action = args.Value<string>("action") ?? "list_persistent";
            var sourceName = args.Value<string>("source");
            var sourceField = args.Value<string>("event_field"); // e.g. "onClick"
            var componentType = args.Value<string>("component_type"); // e.g. "Button"
            if (string.IsNullOrEmpty(sourceName) || string.IsNullOrEmpty(sourceField))
                return Err("source and event_field required.");

            var go = GameObject.Find(sourceName);
            if (go == null) return Err($"Source '{sourceName}' not found.");

            Component comp = string.IsNullOrEmpty(componentType)
                ? go.GetComponent<Component>()
                : go.GetComponent(componentType);
            if (comp == null) return Err($"Component '{componentType}' on '{sourceName}' not found.");

            var field = comp.GetType().GetField(sourceField,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var prop = comp.GetType().GetProperty(sourceField,
                BindingFlags.Public | BindingFlags.Instance);
            object evtObj = field?.GetValue(comp) ?? prop?.GetValue(comp);
            if (evtObj is not UnityEventBase evt)
                return Err($"Field/property '{sourceField}' is not a UnityEvent.");

            switch (action)
            {
                case "list_persistent":
                {
                    int n = evt.GetPersistentEventCount();
                    var list = new System.Collections.Generic.List<object>();
                    for (int i = 0; i < n; i++)
                    {
                        list.Add(new
                        {
                            index = i,
                            target = evt.GetPersistentTarget(i)?.name,
                            methodName = evt.GetPersistentMethodName(i)
                        });
                    }
                    return Ok(new { action, count = n, listeners = list });
                }
                case "remove_persistent":
                {
                    var index = args.Value<int?>("index");
                    if (!index.HasValue) return Err("index required.");
                    UnityEventTools.RemovePersistentListener(evt, index.Value);
                    return Ok(new { action, removedIndex = index.Value });
                }
                case "add_persistent":
                {
                    var targetName = args.Value<string>("target_object");
                    var methodName = args.Value<string>("method_name");
                    var targetCompType = args.Value<string>("target_component_type");
                    if (string.IsNullOrEmpty(targetName) || string.IsNullOrEmpty(methodName))
                        return Err("target_object and method_name required.");

                    var targetGo = GameObject.Find(targetName);
                    if (targetGo == null) return Err($"Target '{targetName}' not found.");
                    var targetComp = string.IsNullOrEmpty(targetCompType)
                        ? (Object)targetGo
                        : targetGo.GetComponent(targetCompType);
                    if (targetComp == null) return Err($"Target component '{targetCompType}' not found.");

                    if (evt is UnityEvent ue)
                    {
                        var ueMethod = targetComp.GetType().GetMethod(methodName, System.Type.EmptyTypes);
                        if (ueMethod == null) return Err($"Method '{methodName}()' not found on '{targetComp.GetType().Name}'.");
                        UnityAction call = (UnityAction)System.Delegate.CreateDelegate(typeof(UnityAction), targetComp, ueMethod);
                        UnityEventTools.AddPersistentListener(ue, call);
                        EditorUtility.SetDirty(comp);
                        return Ok(new { action, target = targetName, methodName, kind = "UnityEvent" });
                    }

                    return Err("Only no-arg UnityEvent currently supported. Use unity_component / execute_csharp for typed events.");
                }
                default:
                    return Err($"Unsupported unity_event action '{action}'.");
            }
        }

        private static AutonomousMcpToolResponse Ok(object d) =>
            new AutonomousMcpToolResponse { success = true, data = JToken.FromObject(d), error = string.Empty };
        private static AutonomousMcpToolResponse Err(string m) =>
            new AutonomousMcpToolResponse { success = false, data = null, error = m };
    }
}
