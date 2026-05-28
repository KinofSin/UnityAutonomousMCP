using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.Editor.Core
{
    public sealed class ClientRecord
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Transport { get; set; }
        public string State { get; set; } = "pending";
        public string FirstSeenUtc { get; set; }
        public string LastSeenUtc { get; set; }
        public Dictionary<string, string> ToolOverrides { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public static class PermissionStore
    {
        private const string ModeEditorPrefKey = "AutonomousMcp.Mode";

        private static readonly object Gate = new object();
        private static bool _loaded;
        private static bool _autoApproveMutate;
        private static bool _autoApproveDestructive;
        private static bool _autoApproveNewClients;
        private static readonly Dictionary<string, string> GlobalToolOverrides =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, ClientRecord> Clients =
            new Dictionary<string, ClientRecord>(StringComparer.Ordinal);

        public static AutonomousMcpMode Mode
        {
            get
            {
                var raw = EditorPrefs.GetString(ModeEditorPrefKey, AutonomousMcpMode.Ask.ToString());
                return Enum.TryParse<AutonomousMcpMode>(raw, out var parsed) ? parsed : AutonomousMcpMode.Ask;
            }
        }

        public static void SetMode(AutonomousMcpMode mode)
        {
            EditorPrefs.SetString(ModeEditorPrefKey, mode.ToString());
        }

        public static bool AutoApproveMutate
        {
            get { EnsureLoaded(); lock (Gate) return _autoApproveMutate; }
            set { EnsureLoaded(); lock (Gate) { _autoApproveMutate = value; Save(); } }
        }

        public static bool AutoApproveDestructive
        {
            get { EnsureLoaded(); lock (Gate) return _autoApproveDestructive; }
            set { EnsureLoaded(); lock (Gate) { _autoApproveDestructive = value; Save(); } }
        }

        public static bool AutoApproveNewClients
        {
            get { EnsureLoaded(); lock (Gate) return _autoApproveNewClients; }
            set { EnsureLoaded(); lock (Gate) { _autoApproveNewClients = value; Save(); } }
        }

        public static void UpsertClient(string clientId, string clientName, string transport)
        {
            if (string.IsNullOrEmpty(clientId)) return;
            EnsureLoaded();
            lock (Gate)
            {
                var now = DateTime.UtcNow.ToString("O");
                if (!Clients.TryGetValue(clientId, out var record))
                {
                    record = new ClientRecord
                    {
                        Id = clientId,
                        Name = clientName,
                        Transport = transport,
                        State = _autoApproveNewClients ? "approved" : "pending",
                        FirstSeenUtc = now,
                        LastSeenUtc = now
                    };
                    Clients[clientId] = record;
                    Save();
                    return;
                }

                record.LastSeenUtc = now;
                if (!string.IsNullOrEmpty(clientName)) record.Name = clientName;
                if (!string.IsNullOrEmpty(transport)) record.Transport = transport;
                Save();
            }
        }

        public static PermissionDecision Evaluate(string clientId, string toolName, ToolMode mode)
        {
            EnsureLoaded();
            lock (Gate)
            {
                if (Clients.TryGetValue(clientId ?? string.Empty, out var record))
                {
                    switch (record.State)
                    {
                        case "denied":
                        case "revoked":
                            return PermissionDecision.DenyClientNotApproved;
                        case "pending":
                            return PermissionDecision.RequiresApproval;
                    }

                    if (record.ToolOverrides != null && record.ToolOverrides.TryGetValue(toolName ?? string.Empty, out var perClientOverride))
                    {
                        var resolved = ApplyOverride(perClientOverride, mode);
                        if (resolved.HasValue) return resolved.Value;
                    }
                }

                if (GlobalToolOverrides.TryGetValue(toolName ?? string.Empty, out var globalOverride))
                {
                    var resolved = ApplyOverride(globalOverride, mode);
                    if (resolved.HasValue) return resolved.Value;
                }

                return EvaluateMode(mode);
            }
        }

        private static PermissionDecision? ApplyOverride(string value, ToolMode mode)
        {
            if (string.IsNullOrEmpty(value)) return null;
            switch (value.ToLowerInvariant())
            {
                case "allow": return PermissionDecision.Allow;
                case "deny": return PermissionDecision.DenyByPolicy;
                case "ask": return PermissionDecision.RequiresApproval;
                case "default": return null;
                default: return null;
            }
        }

        private static PermissionDecision EvaluateMode(ToolMode mode)
        {
            if (Mode == AutonomousMcpMode.Ask)
            {
                return mode == ToolMode.Read ? PermissionDecision.Allow : PermissionDecision.DenyByMode;
            }

            // Agent mode
            switch (mode)
            {
                case ToolMode.Read:
                    return PermissionDecision.Allow;
                case ToolMode.Mutate:
                    return _autoApproveMutate ? PermissionDecision.Allow : PermissionDecision.RequiresApproval;
                case ToolMode.Destructive:
                    return _autoApproveDestructive ? PermissionDecision.Allow : PermissionDecision.RequiresApproval;
                default:
                    return PermissionDecision.RequiresApproval;
            }
        }

        public static IReadOnlyList<ClientRecord> ListClients()
        {
            EnsureLoaded();
            lock (Gate)
            {
                return Clients.Values.Select(Clone).ToList();
            }
        }

        public static ClientRecord GetClient(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnsureLoaded();
            lock (Gate)
            {
                return Clients.TryGetValue(id, out var record) ? Clone(record) : null;
            }
        }

        public static bool ApproveClient(string id) => SetClientState(id, "approved");
        public static bool DenyClient(string id) => SetClientState(id, "denied");
        public static bool RevokeClient(string id) => SetClientState(id, "revoked");

        public static int BulkSetState(string state)
        {
            if (string.IsNullOrEmpty(state)) return 0;
            EnsureLoaded();
            lock (Gate)
            {
                var count = 0;
                foreach (var record in Clients.Values)
                {
                    if (record.State == "pending")
                    {
                        record.State = state;
                        count++;
                    }
                }
                if (count > 0) Save();
                return count;
            }
        }

        public static bool SetClientToolOverride(string id, string tool, string value)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(tool)) return false;
            EnsureLoaded();
            lock (Gate)
            {
                if (!Clients.TryGetValue(id, out var record)) return false;
                if (string.IsNullOrEmpty(value) || value.Equals("default", StringComparison.OrdinalIgnoreCase))
                {
                    record.ToolOverrides.Remove(tool);
                }
                else
                {
                    record.ToolOverrides[tool] = value.ToLowerInvariant();
                }
                Save();
                return true;
            }
        }

        public static void SetGlobalToolOverride(string tool, string value)
        {
            if (string.IsNullOrEmpty(tool)) return;
            EnsureLoaded();
            lock (Gate)
            {
                if (string.IsNullOrEmpty(value) || value.Equals("default", StringComparison.OrdinalIgnoreCase))
                {
                    GlobalToolOverrides.Remove(tool);
                }
                else
                {
                    GlobalToolOverrides[tool] = value.ToLowerInvariant();
                }
                Save();
            }
        }

        public static IReadOnlyDictionary<string, string> GetGlobalToolOverrides()
        {
            EnsureLoaded();
            lock (Gate)
            {
                return new Dictionary<string, string>(GlobalToolOverrides, StringComparer.Ordinal);
            }
        }

        private static bool SetClientState(string id, string state)
        {
            if (string.IsNullOrEmpty(id)) return false;
            EnsureLoaded();
            lock (Gate)
            {
                if (!Clients.TryGetValue(id, out var record)) return false;
                record.State = state;
                Save();
                return true;
            }
        }

        private static ClientRecord Clone(ClientRecord source)
        {
            return new ClientRecord
            {
                Id = source.Id,
                Name = source.Name,
                Transport = source.Transport,
                State = source.State,
                FirstSeenUtc = source.FirstSeenUtc,
                LastSeenUtc = source.LastSeenUtc,
                ToolOverrides = new Dictionary<string, string>(source.ToolOverrides ?? new Dictionary<string, string>(), StringComparer.Ordinal)
            };
        }

        private static string StorePath
        {
            get
            {
                var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
                return Path.Combine(projectRoot, "Library", "AutonomousMcp", "permissions.json");
            }
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (Gate)
            {
                if (_loaded) return;
                try
                {
                    var path = StorePath;
                    if (File.Exists(path))
                    {
                        var raw = File.ReadAllText(path);
                        var root = JObject.Parse(raw);
                        _autoApproveMutate = root.Value<bool?>("autoApproveMutate") ?? false;
                        _autoApproveDestructive = root.Value<bool?>("autoApproveDestructive") ?? false;
                        _autoApproveNewClients = root.Value<bool?>("autoApproveNewClients") ?? false;

                        if (root["globalToolOverrides"] is JObject gto)
                        {
                            foreach (var kv in gto)
                            {
                                if (kv.Value is JValue v && v.Type == JTokenType.String)
                                {
                                    GlobalToolOverrides[kv.Key] = v.Value<string>();
                                }
                            }
                        }

                        if (root["clients"] is JObject cl)
                        {
                            foreach (var kv in cl)
                            {
                                if (!(kv.Value is JObject obj)) continue;
                                var record = new ClientRecord
                                {
                                    Id = kv.Key,
                                    Name = obj.Value<string>("name"),
                                    Transport = obj.Value<string>("transport"),
                                    State = obj.Value<string>("state") ?? "pending",
                                    FirstSeenUtc = obj.Value<string>("firstSeenUtc"),
                                    LastSeenUtc = obj.Value<string>("lastSeenUtc"),
                                };
                                if (obj["toolOverrides"] is JObject overrides)
                                {
                                    foreach (var ov in overrides)
                                    {
                                        if (ov.Value is JValue ovv && ovv.Type == JTokenType.String)
                                        {
                                            record.ToolOverrides[ov.Key] = ovv.Value<string>();
                                        }
                                    }
                                }
                                Clients[kv.Key] = record;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AutonomousMCP] Failed to load permissions store: {ex.Message}");
                }
                finally
                {
                    _loaded = true;
                }
            }
        }

        private static void Save()
        {
            try
            {
                var root = new JObject
                {
                    ["autoApproveMutate"] = _autoApproveMutate,
                    ["autoApproveDestructive"] = _autoApproveDestructive,
                    ["autoApproveNewClients"] = _autoApproveNewClients,
                };

                var globals = new JObject();
                foreach (var kv in GlobalToolOverrides) globals[kv.Key] = kv.Value;
                root["globalToolOverrides"] = globals;

                var clients = new JObject();
                foreach (var kv in Clients)
                {
                    var rec = kv.Value;
                    var overrides = new JObject();
                    if (rec.ToolOverrides != null)
                    {
                        foreach (var ov in rec.ToolOverrides) overrides[ov.Key] = ov.Value;
                    }
                    clients[kv.Key] = new JObject
                    {
                        ["name"] = rec.Name,
                        ["transport"] = rec.Transport,
                        ["state"] = rec.State,
                        ["firstSeenUtc"] = rec.FirstSeenUtc,
                        ["lastSeenUtc"] = rec.LastSeenUtc,
                        ["toolOverrides"] = overrides
                    };
                }
                root["clients"] = clients;

                var path = StorePath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, root.ToString(Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AutonomousMCP] Failed to save permissions store: {ex.Message}");
            }
        }
    }
}
