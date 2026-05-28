using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AutonomousMcp;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.Editor.Core
{
    public readonly struct RegistryEntry
    {
        public string Name { get; }
        public string Description { get; }
        public ToolMode Mode { get; }
        public ToolCategory Category { get; }
        public Func<JObject, AutonomousMcpToolResponse> Handler { get; }

        public RegistryEntry(
            string name,
            string description,
            ToolMode mode,
            ToolCategory category,
            Func<JObject, AutonomousMcpToolResponse> handler)
        {
            Name = name;
            Description = description;
            Mode = mode;
            Category = category;
            Handler = handler;
        }
    }

    public static class ToolRegistry
    {
        private static readonly Dictionary<string, RegistryEntry> Entries =
            new Dictionary<string, RegistryEntry>(StringComparer.Ordinal);
        private static readonly object Gate = new object();

        public static bool TryResolve(string name, out RegistryEntry entry)
        {
            lock (Gate)
            {
                if (string.IsNullOrEmpty(name))
                {
                    entry = default;
                    return false;
                }
                return Entries.TryGetValue(name, out entry);
            }
        }

        public static void Register(RegistryEntry entry)
        {
            if (string.IsNullOrEmpty(entry.Name) || entry.Handler == null)
            {
                return;
            }
            lock (Gate)
            {
                Entries[entry.Name] = entry;
            }
        }

        public static IReadOnlyList<RegistryEntry> All()
        {
            lock (Gate)
            {
                return Entries.Values.ToList();
            }
        }

        [InitializeOnLoadMethod]
        private static void DiscoverAttributes()
        {
            // Find every type / method tagged with [McpTool] across user assemblies and register it.
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
                catch { continue; }

                foreach (var type in types)
                {
                    if (type == null) continue;
                    TryRegisterClassForm(type);
                    TryRegisterStaticMethods(type);
                }
            }
        }

        private static void TryRegisterClassForm(Type type)
        {
            if (type.IsAbstract || type.IsInterface) return;
            if (!typeof(IMcpTool).IsAssignableFrom(type)) return;

            var attr = type.GetCustomAttribute(typeof(McpToolAttribute), inherit: false);
            if (attr == null) return;

            try
            {
                var instance = (IMcpTool)Activator.CreateInstance(type);
                Register(new RegistryEntry(
                    instance.Name,
                    instance.Description,
                    instance.Mode,
                    instance.Category,
                    instance.Execute));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AutonomousMCP] Failed to instantiate IMcpTool '{type.FullName}': {ex.Message}");
            }
        }

        private static void TryRegisterStaticMethods(Type type)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var method in type.GetMethods(flags))
            {
                var attr = method.GetCustomAttribute(typeof(McpToolAttribute), inherit: false) as McpToolAttribute;
                if (attr == null) continue;

                var parameters = method.GetParameters();
                if (parameters.Length != 1 || parameters[0].ParameterType != typeof(JObject)) continue;

                var capturedMethod = method;
                Func<JObject, AutonomousMcpToolResponse> handler = args =>
                {
                    var result = capturedMethod.Invoke(null, new object[] { args });
                    return WrapStaticResult(result, capturedMethod.Name);
                };

                Register(new RegistryEntry(
                    attr.Name,
                    attr.Description,
                    attr.Mode,
                    attr.Category,
                    handler));
            }
        }

        private static AutonomousMcpToolResponse WrapStaticResult(object raw, string methodName)
        {
            if (raw is AutonomousMcpToolResponse response) return response;

            JToken data;
            switch (raw)
            {
                case null:
                    data = JValue.CreateNull();
                    break;
                case JToken token:
                    data = token;
                    break;
                case string s:
                    data = new JValue(s);
                    break;
                default:
                    data = JToken.FromObject(raw);
                    break;
            }

            return new AutonomousMcpToolResponse
            {
                success = true,
                data = data,
                error = string.Empty
            };
        }
    }
}
