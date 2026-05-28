using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.Editor.Core
{
    /// <summary>
    /// Persisted per-kind provider selection. Lives in ProjectSettings/AutonomousMCPGenerators.json
    /// (separate file from PermissionStore so it can be VCS-tracked or git-ignored independently).
    ///
    /// API keys are NEVER stored here — they are read from environment variables at generation time.
    /// This keeps the config file safe to commit while the secrets stay per-machine.
    /// </summary>
    [InitializeOnLoad]
    public static class GeneratorConfig
    {
        [Serializable]
        public sealed class ConfigData
        {
            /// <summary>kind name → providerId. Missing kind means "use first available provider".</summary>
            public Dictionary<string, string> providerByKind = new Dictionary<string, string>(StringComparer.Ordinal);

            /// <summary>Default output directory for generated assets (under Assets/).</summary>
            public string defaultOutputDirectory = "Assets/Generated";
        }

        private const string FileName = "AutonomousMCPGenerators.json";
        private static ConfigData _data;
        private static readonly object _lock = new object();
        private static string _filePath;

        public static event Action OnChanged;

        static GeneratorConfig() { /* lazy */ }

        private static string ResolveFilePath()
        {
            if (!string.IsNullOrEmpty(_filePath)) return _filePath;
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            var dir = Path.Combine(projectRoot, "ProjectSettings");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, FileName);
            return _filePath;
        }

        public static ConfigData Data
        {
            get
            {
                EnsureLoaded();
                return _data;
            }
        }

        public static void EnsureLoaded()
        {
            if (_data != null) return;
            lock (_lock)
            {
                if (_data != null) return;
                Reload();
            }
        }

        public static void Reload()
        {
            lock (_lock)
            {
                var path = ResolveFilePath();
                if (!File.Exists(path))
                {
                    _data = new ConfigData();
                    Save();
                    return;
                }
                try
                {
                    _data = JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(path)) ?? new ConfigData();
                    _data.providerByKind ??= new Dictionary<string, string>(StringComparer.Ordinal);
                    if (string.IsNullOrEmpty(_data.defaultOutputDirectory))
                        _data.defaultOutputDirectory = "Assets/Generated";
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AutonomousMCP] GeneratorConfig load failed: {ex.Message}. Resetting.");
                    _data = new ConfigData();
                    Save();
                }
            }
        }

        public static void Save()
        {
            lock (_lock)
            {
                if (_data == null) _data = new ConfigData();
                try
                {
                    File.WriteAllText(ResolveFilePath(),
                        JsonConvert.SerializeObject(_data, Formatting.Indented));
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AutonomousMCP] GeneratorConfig save failed: {ex.Message}");
                }
            }
            OnChanged?.Invoke();
        }

        public static string GetProviderFor(GeneratorKind kind)
        {
            EnsureLoaded();
            lock (_lock)
            {
                return _data.providerByKind.TryGetValue(kind.ToString(), out var v) ? v : null;
            }
        }

        public static void SetProviderFor(GeneratorKind kind, string providerId)
        {
            EnsureLoaded();
            lock (_lock)
            {
                if (string.IsNullOrEmpty(providerId))
                    _data.providerByKind.Remove(kind.ToString());
                else
                    _data.providerByKind[kind.ToString()] = providerId;
            }
            Save();
        }
    }
}
