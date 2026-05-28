using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.Editor.Core
{
    /// <summary>
    /// Reflection-based registry of IGenerator implementations. Auto-discovers any non-abstract
    /// IGenerator type on editor load. The Phase 7 scaffolds (StubSpriteGenerator etc.) populate
    /// this with a "stub" provider per kind so the surface is complete out of the box.
    ///
    /// Add a new provider by dropping a class implementing IGenerator anywhere in an editor
    /// assembly that references AutonomousMcp.Editor — no edits needed elsewhere.
    /// </summary>
    [InitializeOnLoad]
    public static class GeneratorRegistry
    {
        private static readonly Dictionary<string, IGenerator> _byKey =
            new Dictionary<string, IGenerator>(StringComparer.Ordinal);

        private static bool _initialized;
        private static readonly object _lock = new object();

        static GeneratorRegistry() { /* lazy */ }

        private static string Key(GeneratorKind kind, string providerId) => $"{kind}::{providerId}";

        public static void Register(IGenerator generator)
        {
            if (generator == null) throw new ArgumentNullException(nameof(generator));
            lock (_lock) { _byKey[Key(generator.Kind, generator.ProviderId)] = generator; }
        }

        public static IReadOnlyList<IGenerator> List()
        {
            EnsureInitialized();
            lock (_lock) { return _byKey.Values.OrderBy(g => g.Kind).ThenBy(g => g.ProviderId, StringComparer.Ordinal).ToList(); }
        }

        public static IReadOnlyList<IGenerator> For(GeneratorKind kind)
        {
            EnsureInitialized();
            lock (_lock) { return _byKey.Values.Where(g => g.Kind == kind).OrderBy(g => g.ProviderId, StringComparer.Ordinal).ToList(); }
        }

        /// <summary>
        /// Resolve which generator should service a request for the given kind:
        ///   1. Provider explicitly named in the request
        ///   2. Provider configured in GeneratorConfig for the kind
        ///   3. First configured (IsConfigured == true) generator for the kind
        ///   4. First registered generator for the kind (typically the stub)
        ///   5. null
        /// </summary>
        public static IGenerator Resolve(GeneratorKind kind, string requestedProvider = null)
        {
            EnsureInitialized();
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(requestedProvider) &&
                    _byKey.TryGetValue(Key(kind, requestedProvider), out var named))
                    return named;

                var configured = GeneratorConfig.GetProviderFor(kind);
                if (!string.IsNullOrEmpty(configured) &&
                    _byKey.TryGetValue(Key(kind, configured), out var cfg))
                    return cfg;

                var candidates = _byKey.Values.Where(g => g.Kind == kind).ToList();
                return candidates.FirstOrDefault(g => g.IsConfigured()) ?? candidates.FirstOrDefault();
            }
        }

        public static int Count
        {
            get
            {
                EnsureInitialized();
                lock (_lock) { return _byKey.Count; }
            }
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            lock (_lock)
            {
                if (_initialized) return;
                Discover();
                _initialized = true;
            }
        }

        private static void Discover()
        {
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                    catch { continue; }

                    foreach (var t in types)
                    {
                        if (t == null || t.IsAbstract || t.IsInterface) continue;
                        if (!typeof(IGenerator).IsAssignableFrom(t)) continue;
                        try
                        {
                            var instance = (IGenerator)Activator.CreateInstance(t);
                            if (instance == null) continue;
                            _byKey[Key(instance.Kind, instance.ProviderId)] = instance;
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[AutonomousMCP] IGenerator instantiation failed for {t.FullName}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AutonomousMCP] GeneratorRegistry discovery error: {ex.Message}");
            }
        }
    }
}
