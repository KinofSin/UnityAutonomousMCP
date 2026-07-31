using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AutonomousMcp.Editor.Perception
{
    /// <summary>One thing that references / drives a hierarchy object.</summary>
    public sealed class ObjectDriver
    {
        /// <summary>"animation", "vrcfury", "modular_avatar", or "other".</summary>
        public string Kind;
        /// <summary>Short human label, e.g. "FX: Dog" or "VRCFury: Toggle".</summary>
        public string Label;
        /// <summary>Clip / component / asset name when known.</summary>
        public string Source;
        /// <summary>Animator parameter when known.</summary>
        public string Parameter;

        /// <summary>
        /// True when deleting the object breaks this driver outright: an activeness curve
        /// (a menu toggle switching the object on/off) or a direct component reference.
        ///
        /// False for property-only curves — blendshape, material and transform animation.
        /// Those merely lose a target. Counting them made almost every renderer on a real
        /// avatar look "driven" (one hue-shift slider touching every material is enough),
        /// which buried the handful of objects a wardrobe menu actually controls.
        /// </summary>
        public bool Controls;
    }

    /// <summary>
    /// Resolves who drives each renderer-holding object: animation clips on the
    /// descriptor's playable layers, VRCFury components, and Modular Avatar / NDMF
    /// components. The cleanup window and cost dossier both consume this so "disabled"
    /// is never mistaken for "unused wardrobe".
    /// </summary>
    public static class AvatarReferences
    {
        public static Dictionary<int, List<ObjectDriver>> Resolve(GameObject root, IEnumerable<CostEntry> entries)
        {
            var result = new Dictionary<int, List<ObjectDriver>>();
            if (root == null || entries == null) return result;

            var byRelPath = new Dictionary<string, CostEntry>(StringComparer.OrdinalIgnoreCase);
            var byId = new Dictionary<int, CostEntry>();
            foreach (var e in entries)
            {
                byId[e.InstanceId] = e;
                var rel = RelativePath(root.name, e.Path);
                if (!string.IsNullOrEmpty(rel) && !byRelPath.ContainsKey(rel))
                    byRelPath[rel] = e;
                // Also index by bare name when unique — animation paths sometimes omit parents.
                if (!byRelPath.ContainsKey(e.Name))
                    byRelPath[e.Name] = e;
            }

            CollectAnimationDrivers(root, byRelPath, result);
            CollectComponentDrivers(root, byId, result, "VF.Model.VRCFury", "vrcfury", "VRCFury");
            CollectComponentDrivers(root, byId, result, null, "modular_avatar", "MA",
                nsPrefix: "nadena.dev.");

            return result;
        }

        public static string Summarize(IList<ObjectDriver> drivers) => Summarize(drivers, false);

        /// <param name="controllingOnly">
        /// Restrict to drivers that break when the object is deleted. Pass true wherever the
        /// answer decides a warning; pass false for informational listings.
        /// </param>
        public static string Summarize(IList<ObjectDriver> drivers, bool controllingOnly)
        {
            if (drivers == null || drivers.Count == 0) return "-";
            var source = controllingOnly ? drivers.Where(d => d.Controls) : drivers;
            var labels = source.Select(d => d.Label).Distinct().Take(3).ToList();
            return labels.Count > 0 ? string.Join("; ", labels) : "-";
        }

        private static void CollectAnimationDrivers(
            GameObject root,
            Dictionary<string, CostEntry> byRelPath,
            Dictionary<int, List<ObjectDriver>> result)
        {
            foreach (var controller in EnumerateControllers(root))
            {
                if (controller == null) continue;
                var paramByClip = BuildClipParameterHints(controller);
                foreach (var clip in controller.animationClips)
                {
                    if (clip == null) continue;
                    var controlled = new HashSet<int>();
                    var touched = new HashSet<int>();
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                        MatchBinding(binding.path, byRelPath, IsActiveness(binding) ? controlled : touched);
                    foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                        MatchBinding(binding.path, byRelPath, touched);

                    paramByClip.TryGetValue(clip, out var param);
                    var label = string.IsNullOrEmpty(param)
                        ? $"FX: {clip.name}"
                        : $"FX: {param}";

                    foreach (var id in controlled)
                    {
                        Add(result, id, new ObjectDriver
                        {
                            Kind = "animation",
                            Label = label,
                            Source = clip.name,
                            Parameter = param,
                            Controls = true
                        });
                    }
                    foreach (var id in touched)
                    {
                        if (controlled.Contains(id)) continue;
                        Add(result, id, new ObjectDriver
                        {
                            Kind = "animation",
                            Label = label,
                            Source = clip.name,
                            Parameter = param,
                            Controls = false
                        });
                    }
                }
            }
        }

        /// <summary>
        /// m_IsActive is GameObject.SetActive and m_Enabled is the component/renderer switch —
        /// between them, how every VRChat menu toggle turns a wardrobe item on and off.
        /// </summary>
        private static bool IsActiveness(EditorCurveBinding binding) =>
            binding.propertyName == "m_IsActive" || binding.propertyName == "m_Enabled";

        private static void MatchBinding(
            string bindingPath,
            Dictionary<string, CostEntry> byRelPath,
            HashSet<int> hit)
        {
            if (string.IsNullOrEmpty(bindingPath)) return;
            // Binding path is relative to the animator root. Match longest prefix first.
            if (byRelPath.TryGetValue(bindingPath, out var exact))
            {
                hit.Add(exact.InstanceId);
                return;
            }
            // "ANIMAL DOG/Ear.L" should still credit "ANIMAL DOG".
            var slash = bindingPath.IndexOf('/');
            var head = slash >= 0 ? bindingPath.Substring(0, slash) : bindingPath;
            if (byRelPath.TryGetValue(head, out var parent))
                hit.Add(parent.InstanceId);
        }

        private static Dictionary<AnimationClip, string> BuildClipParameterHints(AnimatorController controller)
        {
            var map = new Dictionary<AnimationClip, string>();
            if (controller == null) return map;
            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine == null) continue;
                WalkStateMachine(layer.stateMachine, map);
            }
            return map;
        }

        private static void WalkStateMachine(
            AnimatorStateMachine sm,
            Dictionary<AnimationClip, string> map)
        {
            foreach (var child in sm.states)
            {
                var state = child.state;
                if (state == null) continue;
                var clip = state.motion as AnimationClip;
                if (clip == null) continue;

                // Prefer a transition condition that leads INTO this state.
                string param = null;
                foreach (var t in sm.anyStateTransitions)
                {
                    if (t.destinationState != state) continue;
                    param = FirstBoolOrTrigger(t);
                    if (param != null) break;
                }
                if (param == null)
                {
                    foreach (var src in sm.states)
                    {
                        foreach (var t in src.state.transitions)
                        {
                            if (t.destinationState != state) continue;
                            param = FirstBoolOrTrigger(t);
                            if (param != null) break;
                        }
                        if (param != null) break;
                    }
                }
                if (param != null && !map.ContainsKey(clip))
                    map[clip] = param;
            }
            foreach (var sub in sm.stateMachines)
                if (sub.stateMachine != null) WalkStateMachine(sub.stateMachine, map);
        }

        private static string FirstBoolOrTrigger(AnimatorTransitionBase t)
        {
            if (t?.conditions == null) return null;
            foreach (var c in t.conditions)
            {
                if (c.mode == AnimatorConditionMode.If ||
                    c.mode == AnimatorConditionMode.IfNot ||
                    c.mode == AnimatorConditionMode.Equals ||
                    c.mode == AnimatorConditionMode.NotEqual)
                    return c.parameter;
            }
            return t.conditions.Length > 0 ? t.conditions[0].parameter : null;
        }

        private static IEnumerable<AnimatorController> EnumerateControllers(GameObject root)
        {
            var seen = new HashSet<int>();
            foreach (var a in root.GetComponentsInChildren<Animator>(true))
            {
                var ctrl = a.runtimeAnimatorController as AnimatorController;
                if (ctrl != null && seen.Add(ctrl.GetInstanceID()))
                    yield return ctrl;
            }

            var descType = FindType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            var desc = descType != null ? root.GetComponent(descType) : null;
            if (desc == null) yield break;

            var so = new SerializedObject(desc);
            foreach (var propName in new[] { "baseAnimationLayers", "specialAnimationLayers" })
            {
                var layers = so.FindProperty(propName);
                if (layers == null || !layers.isArray) continue;
                for (int i = 0; i < layers.arraySize; i++)
                {
                    var elem = layers.GetArrayElementAtIndex(i);
                    var anim = elem?.FindPropertyRelative("animatorController");
                    var ctrl = anim?.objectReferenceValue as AnimatorController;
                    if (ctrl != null && seen.Add(ctrl.GetInstanceID()))
                        yield return ctrl;
                }
            }
            so.Dispose();
        }

        private static void CollectComponentDrivers(
            GameObject root,
            Dictionary<int, CostEntry> byId,
            Dictionary<int, List<ObjectDriver>> result,
            string exactType,
            string kind,
            string labelPrefix,
            string nsPrefix = null)
        {
            IEnumerable<Component> comps;
            if (!string.IsNullOrEmpty(exactType))
            {
                var t = FindType(exactType);
                if (t == null) return;
                comps = root.GetComponentsInChildren(t, true).Cast<Component>();
            }
            else if (!string.IsNullOrEmpty(nsPrefix))
            {
                comps = root.GetComponentsInChildren<Component>(true)
                    .Where(c => c != null &&
                                (c.GetType().Namespace ?? "").StartsWith(nsPrefix, StringComparison.Ordinal));
            }
            else return;

            foreach (var c in comps)
            {
                if (c == null) continue;
                var contentLabel = ContentTypeLabel(c) ?? c.GetType().Name;
                var refs = CollectObjectRefs(c);
                foreach (var go in refs)
                {
                    if (go == null) continue;
                    // Credit the referenced object itself, or the nearest ancestor that is a cost entry.
                    var cur = go;
                    while (cur != null)
                    {
                        if (byId.ContainsKey(cur.GetInstanceID()))
                        {
                            Add(result, cur.GetInstanceID(), new ObjectDriver
                            {
                                Kind = kind,
                                Label = $"{labelPrefix}: {contentLabel}",
                                Source = c.GetType().Name,
                                Parameter = null,
                                // A build-time component holding a direct reference breaks on
                                // delete regardless of what it does with the object.
                                Controls = true
                            });
                            break;
                        }
                        if (cur == root) break;
                        cur = cur.transform.parent != null ? cur.transform.parent.gameObject : null;
                    }
                }
            }
        }

        private static string ContentTypeLabel(Component c)
        {
            try
            {
                var so = new SerializedObject(c);
                var content = so.FindProperty("content");
                if (content != null && !string.IsNullOrEmpty(content.managedReferenceFullTypename))
                {
                    var full = content.managedReferenceFullTypename;
                    var ix = full.LastIndexOf('.');
                    so.Dispose();
                    return ix >= 0 ? full.Substring(ix + 1) : full;
                }
                so.Dispose();
            }
            catch { /* best effort */ }
            return null;
        }

        private static List<GameObject> CollectObjectRefs(Component c)
        {
            var list = new List<GameObject>();
            var seen = new HashSet<int>();
            try
            {
                var so = new SerializedObject(c);
                var prop = so.GetIterator();
                var enter = true;
                while (prop.Next(enter))
                {
                    enter = true;
                    if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                    enter = false;
                    var obj = prop.objectReferenceValue;
                    if (obj == null) continue;
                    GameObject go = obj as GameObject;
                    if (go == null && obj is Component comp) go = comp.gameObject;
                    if (go != null && seen.Add(go.GetInstanceID()))
                        list.Add(go);
                }
                so.Dispose();
            }
            catch { /* best effort */ }
            return list;
        }

        private static void Add(Dictionary<int, List<ObjectDriver>> map, int id, ObjectDriver d)
        {
            if (!map.TryGetValue(id, out var list))
                map[id] = list = new List<ObjectDriver>();
            // Dedup by label so the same clip/param doesn't spam.
            var existing = list.FirstOrDefault(x => x.Label == d.Label && x.Kind == d.Kind);
            if (existing != null)
            {
                // Two clips can share a label while only one carries the activeness curve;
                // whichever arrives second must not downgrade the object to property-only.
                if (d.Controls) existing.Controls = true;
                return;
            }
            list.Add(d);
        }

        private static string RelativePath(string rootName, string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return fullPath;
            var prefix = rootName + "/";
            if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return fullPath.Substring(prefix.Length);
            if (string.Equals(fullPath, rootName, StringComparison.OrdinalIgnoreCase))
                return "";
            return fullPath;
        }

        private static Type FindType(string fullName)
        {
            var t = Type.GetType(fullName);
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { t = asm.GetType(fullName); }
                catch { continue; }
                if (t != null) return t;
            }
            return null;
        }
    }
}
