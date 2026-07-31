using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AutonomousMcp.Editor.Perception
{
    /// <summary>One renderer-holding object and what it costs.</summary>
    public sealed class CostEntry
    {
        public string Path;
        public string Name;
        public int InstanceId;
        public bool Active;
        public bool ActiveSelf;
        public int Renderers;
        public int SkinnedMeshes;
        public int Polygons;
        public int Verts;
        public int MaterialSlots;
        public int Blendshapes;

        public GameObject Resolve() => EditorUtility.InstanceIDToObject(InstanceId) as GameObject;
    }

    /// <summary>Totals with the rank they land in, used for both "now" and "if removed".</summary>
    public struct CostProjection
    {
        public long Polygons;
        public int MaterialSlots;
        public string PolygonRank;
        public string MaterialSlotRank;
    }

    public sealed class CostReport
    {
        public long TotalPolygons;
        public long TotalVerts;
        public int TotalMaterialSlots;
        public int SkinnedMeshes;
        public int MeshRenderers;
        public List<CostEntry> Entries = new List<CostEntry>();

        public int InactiveObjects;
        public long InactivePolygons;
        public int InactiveMaterialSlots;

        public string PolygonRank => StateDossier.PolygonTier.Rank(TotalPolygons);
        public string MaterialSlotRank => StateDossier.MaterialSlotTier.Rank(TotalMaterialSlots);
        public string SkinnedMeshRank => StateDossier.SkinnedMeshTier.Rank(SkinnedMeshes);

        /// <summary>What the totals become with the given cost taken out.</summary>
        public CostProjection Without(long polygons, int materialSlots)
        {
            var p = TotalPolygons - polygons;
            var m = TotalMaterialSlots - materialSlots;
            return new CostProjection
            {
                Polygons = p,
                MaterialSlots = m,
                PolygonRank = StateDossier.PolygonTier.Rank(p),
                MaterialSlotRank = StateDossier.MaterialSlotTier.Rank(m)
            };
        }

        public double ShareOfPolygons(long polygons) =>
            TotalPolygons > 0 ? polygons / (double)TotalPolygons : 0d;
    }

    /// <summary>
    /// Per-object cost attribution for an avatar or scene.
    ///
    /// Deliberately the single source for both the <c>cost</c> dossier section and the Avatar
    /// Cleanup window. A UI-only query would eventually disagree with the numbers the agent
    /// reasons about for the same avatar, and there is no worse bug in a tool whose entire job
    /// is telling you what something costs.
    ///
    /// Inactive objects are included on purpose: VRChat's stats walk renderers with
    /// includeInactive, so a switched-off wardrobe toggle still costs rank and download size.
    /// That makes disabled objects the least obvious removal candidates on a typical avatar.
    /// </summary>
    public static class AvatarCost
    {
        public static CostReport Build(GameObject root, Scene scene)
        {
            var report = new CostReport();
            var groups = new Dictionary<int, CostEntry>();

            foreach (var r in StateDossier.EnumerateRenderers(root, scene))
            {
                var mesh = MeshOf(r);
                var polys = TriangleCount(mesh);
                var verts = mesh != null ? mesh.vertexCount : 0;
                var slots = r.sharedMaterials != null ? r.sharedMaterials.Length : 0;
                var isSkinned = r is SkinnedMeshRenderer;

                report.TotalPolygons += polys;
                report.TotalVerts += verts;
                report.TotalMaterialSlots += slots;
                if (isSkinned) report.SkinnedMeshes++; else report.MeshRenderers++;

                var go = r.gameObject;
                var id = go.GetInstanceID();
                if (!groups.TryGetValue(id, out var entry))
                {
                    groups[id] = entry = new CostEntry
                    {
                        Path = StateDossier.HierarchyPath(go.transform),
                        Name = go.name,
                        InstanceId = id,
                        Active = go.activeInHierarchy,
                        ActiveSelf = go.activeSelf
                    };
                    report.Entries.Add(entry);
                }
                entry.Renderers++;
                if (isSkinned) entry.SkinnedMeshes++;
                entry.Polygons += polys;
                entry.Verts += verts;
                entry.MaterialSlots += slots;
                entry.Blendshapes += mesh != null ? mesh.blendShapeCount : 0;
            }

            report.Entries.Sort((a, b) => b.Polygons.CompareTo(a.Polygons));

            foreach (var entry in report.Entries)
            {
                if (entry.Active) continue;
                report.InactiveObjects++;
                report.InactivePolygons += entry.Polygons;
                report.InactiveMaterialSlots += entry.MaterialSlots;
            }

            return report;
        }

        public static Mesh MeshOf(Renderer r)
        {
            if (r is SkinnedMeshRenderer smr) return smr.sharedMesh;
            var mf = r.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        // GetIndexCount rather than mesh.triangles.Length: the latter allocates and marshals the
        // whole index array per renderer — ~150k ints for one head — purely to divide by three.
        // Non-triangle submeshes are skipped instead of being miscounted as triangles.
        public static int TriangleCount(Mesh mesh)
        {
            if (mesh == null) return 0;
            long indices = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                if (mesh.GetTopology(i) != MeshTopology.Triangles) continue;
                indices += mesh.GetIndexCount(i);
            }
            return (int)(indices / 3);
        }
    }
}
