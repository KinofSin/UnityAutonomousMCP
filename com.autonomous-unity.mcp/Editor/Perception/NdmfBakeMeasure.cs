using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AutonomousMcp.Editor.Perception
{
    /// <summary>
    /// NDMF manual-bake measurement: bake a clone, run <see cref="AvatarCost"/> on it,
    /// diff against the editor-time hierarchy, then destroy the clone.
    ///
    /// The editor hierarchy is not the uploaded avatar when VRCFury / MA / AAO are present.
    /// This converts that caveat into a number.
    /// </summary>
    public static class NdmfBakeMeasure
    {
        public static object Measure(GameObject root)
        {
            if (root == null)
                return new { success = false, error = "root is null" };

            var before = AvatarCost.Build(root, SceneManager.GetActiveScene());

            var processor = FindType("nadena.dev.ndmf.AvatarProcessor");
            if (processor == null)
                return new
                {
                    success = false,
                    error = "nadena.dev.ndmf.AvatarProcessor not found — is NDMF installed?",
                    editor = Snapshot(before)
                };

            var can = processor.GetMethod("CanProcessObject", BindingFlags.Public | BindingFlags.Static);
            if (can != null && can.Invoke(null, new object[] { root }) is bool ok && !ok)
                return new
                {
                    success = false,
                    error = "NDMF refuses this object (no avatar descriptor / unsupported platform).",
                    editor = Snapshot(before)
                };

            var bake = processor.GetMethod(
                "ManualProcessAvatar",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(GameObject), FindType("nadena.dev.ndmf.platform.INDMFPlatformProvider") ?? typeof(object) },
                null);

            // Overload ManualProcessAvatar(GameObject, INDMFPlatformProvider = null)
            if (bake == null)
            {
                foreach (var m in processor.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != "ManualProcessAvatar") continue;
                    var ps = m.GetParameters();
                    if (ps.Length >= 1 && ps[0].ParameterType == typeof(GameObject))
                    {
                        bake = m;
                        break;
                    }
                }
            }

            if (bake == null)
                return new { success = false, error = "ManualProcessAvatar not found on AvatarProcessor.", editor = Snapshot(before) };

            GameObject baked = null;
            try
            {
                var args = bake.GetParameters().Length == 1
                    ? new object[] { root }
                    : new object[] { root, null };
                baked = bake.Invoke(null, args) as GameObject;
                if (baked == null)
                    return new { success = false, error = "ManualProcessAvatar returned null.", editor = Snapshot(before) };

                var after = AvatarCost.Build(baked, SceneManager.GetActiveScene());
                return new
                {
                    success = true,
                    bakedName = baked.name,
                    editor = Snapshot(before),
                    baked = Snapshot(after),
                    delta = new
                    {
                        polygons = after.TotalPolygons - before.TotalPolygons,
                        materialSlots = after.TotalMaterialSlots - before.TotalMaterialSlots,
                        skinnedMeshes = after.SkinnedMeshes - before.SkinnedMeshes,
                        physBones = after.TotalPhysBones - before.TotalPhysBones,
                        bones = after.TotalBones - before.TotalBones
                    },
                    note = "Baked clone destroyed after measurement. Negative delta = build-time optimization removed cost. " +
                           "Generated assets may remain under Assets/ZZZ_GeneratedAssets until cleaned."
                };
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie && tie.InnerException != null
                    ? tie.InnerException
                    : ex;
                return new
                {
                    success = false,
                    error = inner.GetType().Name + ": " + inner.Message,
                    editor = Snapshot(before)
                };
            }
            finally
            {
                if (baked != null)
                {
                    try { UnityEngine.Object.DestroyImmediate(baked); }
                    catch { /* best effort */ }
                }
            }
        }

        private static object Snapshot(CostReport r) => new
        {
            polygons = r.TotalPolygons,
            polygonRank = r.PolygonRank,
            materialSlots = r.TotalMaterialSlots,
            materialSlotRank = r.MaterialSlotRank,
            skinnedMeshes = r.SkinnedMeshes,
            skinnedMeshRank = r.SkinnedMeshRank,
            physBones = r.TotalPhysBones,
            bones = r.TotalBones,
            objects = r.Entries.Count,
            inactivePolygons = r.InactivePolygons
        };

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
