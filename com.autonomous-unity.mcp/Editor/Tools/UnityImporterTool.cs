using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.Editor.Tools
{
    /// <summary>
    /// unity_importer — generic .meta SerializedProperty editor for Texture/Model/Audio importers.
    /// Use this for bulk import-setting changes when manage_texture doesn't cover the property.
    /// </summary>
    public static class UnityImporterTool
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            ToolRegistry.Register("unity_importer", ToolMode.Mutate, ToolCategory.Asset,
                "Generic AssetImporter SerializedProperty editor. " +
                "Actions: get_properties, set_property, get_importer_type.",
                Handle);
        }

        private static AutonomousMcpToolResponse Handle(JObject args)
        {
            var action = args.Value<string>("action") ?? "get_importer_type";
            var path = args.Value<string>("asset_path");
            if (string.IsNullOrEmpty(path)) return Err("asset_path required.");

            var importer = AssetImporter.GetAtPath(path);
            if (importer == null) return Err($"No importer at '{path}'.");

            var so = new SerializedObject(importer);

            switch (action)
            {
                case "get_importer_type":
                    return Ok(new
                    {
                        action,
                        asset_path = path,
                        importer_type = importer.GetType().FullName,
                        userData = importer.userData
                    });

                case "get_properties":
                {
                    var prefix = args.Value<string>("prefix");
                    var list = new System.Collections.Generic.List<object>();
                    var p = so.GetIterator();
                    if (p.NextVisible(true))
                    {
                        do
                        {
                            if (!string.IsNullOrEmpty(prefix) &&
                                !p.propertyPath.StartsWith(prefix, System.StringComparison.Ordinal))
                                continue;
                            list.Add(new
                            {
                                path = p.propertyPath,
                                type = p.propertyType.ToString(),
                                value = ReadValue(p)
                            });
                        } while (p.NextVisible(false));
                    }
                    return Ok(new { action, count = list.Count, properties = list });
                }

                case "set_property":
                {
                    var propPath = args.Value<string>("property_path");
                    if (string.IsNullOrEmpty(propPath)) return Err("property_path required.");
                    var sp = so.FindProperty(propPath);
                    if (sp == null) return Err($"Property '{propPath}' not found.");

                    if (!WriteValue(sp, args["value"]))
                        return Err($"Unsupported property type {sp.propertyType} for set_property.");

                    // Importer writes land in the .meta sibling and are not undoable.
                    CheckpointStore.CaptureAsset(path, "unity_importer.set_property");

                    so.ApplyModifiedProperties();
                    importer.SaveAndReimport();
                    return Ok(new { action, asset_path = path, property_path = propPath, value = ReadValue(sp) });
                }

                default:
                    return Err($"Unsupported unity_importer action '{action}'.");
            }
        }

        private static object ReadValue(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: return p.intValue;
                case SerializedPropertyType.Boolean: return p.boolValue;
                case SerializedPropertyType.Float: return p.floatValue;
                case SerializedPropertyType.String: return p.stringValue;
                case SerializedPropertyType.Enum: return p.enumValueIndex;
                case SerializedPropertyType.Vector2: return new { p.vector2Value.x, p.vector2Value.y };
                case SerializedPropertyType.Vector3: return new { p.vector3Value.x, p.vector3Value.y, p.vector3Value.z };
                case SerializedPropertyType.Color: return new { p.colorValue.r, p.colorValue.g, p.colorValue.b, p.colorValue.a };
                case SerializedPropertyType.ObjectReference:
                    return p.objectReferenceValue == null ? null : p.objectReferenceValue.name;
                default: return null;
            }
        }

        private static bool WriteValue(SerializedProperty p, JToken value)
        {
            if (value == null) return false;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: p.intValue = value.Value<int>(); return true;
                case SerializedPropertyType.Boolean: p.boolValue = value.Value<bool>(); return true;
                case SerializedPropertyType.Float: p.floatValue = value.Value<float>(); return true;
                case SerializedPropertyType.String: p.stringValue = value.Value<string>() ?? ""; return true;
                case SerializedPropertyType.Enum: p.enumValueIndex = value.Value<int>(); return true;
                default: return false;
            }
        }

        private static AutonomousMcpToolResponse Ok(object d) =>
            new AutonomousMcpToolResponse { success = true, data = JToken.FromObject(d), error = string.Empty };
        private static AutonomousMcpToolResponse Err(string m) =>
            new AutonomousMcpToolResponse { success = false, data = null, error = m };
    }
}
