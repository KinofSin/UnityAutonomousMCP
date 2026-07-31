using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
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

        /// <summary>VRAM of textures used ONLY by this object (reclaimed on delete).</summary>
        public long ExclusiveVramBytes;
        /// <summary>VRAM of textures this object shares with others (not reclaimed by deleting it alone).</summary>
        public long SharedVramBytes;
        public int PhysBones;
        public int PhysBoneColliders;
        public List<ObjectDriver> DrivenBy = new List<ObjectDriver>();

        public bool IsDriven => DrivenBy != null && DrivenBy.Count > 0;
        public string DrivenBySummary => AvatarReferences.Summarize(DrivenBy);

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

    public sealed class TwinInfo
    {
        public string Name;
        public int InstanceId;
        public bool Active;
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
        public int InactiveDriven;
        public long InactiveDrivenPolygons;
        public int InactiveUndriven;
        public long InactiveUndrivenPolygons;

        public int TotalPhysBones;
        public int TotalPhysBoneColliders;
        public int TotalBones;
        public long TotalExclusiveVramBytes;
        public long TotalSharedVramBytes;

        public List<TwinInfo> Twins = new List<TwinInfo>();

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
    /// That makes disabled objects the least obvious removal candidates on a typical avatar —
    /// but "disabled" is NOT "unused": check <see cref="CostEntry.DrivenBy"/>.
    /// </summary>
    public static class AvatarCost
    {
        public static CostReport Build(GameObject root, Scene scene)
        {
            var report = new CostReport();
            var groups = new Dictionary<int, CostEntry>();
            // texture instanceId -> set of cost-entry instanceIds that use it
            var texUsers = new Dictionary<int, HashSet<int>>();
            var texBytes = new Dictionary<int, long>();

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

                CollectTextures(r, id, texUsers, texBytes);
            }

            AttributeTextures(report, texUsers, texBytes);
            AttributePhysBones(root, report, groups);
            report.TotalBones = root != null
                ? root.GetComponentsInChildren<Transform>(true).Length
                : 0;

            report.Entries.Sort((a, b) => b.Polygons.CompareTo(a.Polygons));

            var drivers = AvatarReferences.Resolve(root, report.Entries);
            foreach (var entry in report.Entries)
            {
                if (drivers.TryGetValue(entry.InstanceId, out var list))
                    entry.DrivenBy = list;

                if (entry.Active) continue;
                report.InactiveObjects++;
                report.InactivePolygons += entry.Polygons;
                report.InactiveMaterialSlots += entry.MaterialSlots;
                if (entry.IsDriven)
                {
                    report.InactiveDriven++;
                    report.InactiveDrivenPolygons += entry.Polygons;
                }
                else
                {
                    report.InactiveUndriven++;
                    report.InactiveUndrivenPolygons += entry.Polygons;
                }
            }

            report.Twins = FindTwins(root, scene);
            return report;
        }

        private static void CollectTextures(
            Renderer r,
            int ownerId,
            Dictionary<int, HashSet<int>> texUsers,
            Dictionary<int, long> texBytes)
        {
            if (r.sharedMaterials == null) return;
            foreach (var mat in r.sharedMaterials)
            {
                if (mat == null) continue;
                var ids = mat.GetTexturePropertyNameIDs();
                if (ids == null) continue;
                foreach (var propId in ids)
                {
                    var tex = mat.GetTexture(propId);
                    if (tex == null) continue;
                    var tid = tex.GetInstanceID();
                    if (!texUsers.TryGetValue(tid, out var users))
                    {
                        texUsers[tid] = users = new HashSet<int>();
                        try { texBytes[tid] = Profiler.GetRuntimeMemorySizeLong(tex); }
                        catch { texBytes[tid] = 0; }
                    }
                    users.Add(ownerId);
                }
            }
        }

        private static void AttributeTextures(
            CostReport report,
            Dictionary<int, HashSet<int>> texUsers,
            Dictionary<int, long> texBytes)
        {
            var byId = report.Entries.ToDictionary(e => e.InstanceId);
            foreach (var kv in texUsers)
            {
                if (!texBytes.TryGetValue(kv.Key, out var bytes)) continue;
                var users = kv.Value;
                if (users.Count == 1)
                {
                    var only = users.First();
                    if (byId.TryGetValue(only, out var e))
                    {
                        e.ExclusiveVramBytes += bytes;
                        report.TotalExclusiveVramBytes += bytes;
                    }
                }
                else
                {
                    report.TotalSharedVramBytes += bytes;
                    foreach (var uid in users)
                    {
                        if (byId.TryGetValue(uid, out var e))
                            e.SharedVramBytes += bytes;
                    }
                }
            }
        }

        private static void AttributePhysBones(
            GameObject root,
            CostReport report,
            Dictionary<int, CostEntry> groups)
        {
            if (root == null) return;
            var pbType = FindType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone");
            var pbcType = FindType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider");

            if (pbType != null)
            {
                foreach (var c in root.GetComponentsInChildren(pbType, true))
                {
                    if (c == null) continue;
                    report.TotalPhysBones++;
                    AttributeToNearestEntry(c.gameObject, root, groups, e => e.PhysBones++);
                }
            }
            if (pbcType != null)
            {
                foreach (var c in root.GetComponentsInChildren(pbcType, true))
                {
                    if (c == null) continue;
                    report.TotalPhysBoneColliders++;
                    AttributeToNearestEntry(c.gameObject, root, groups, e => e.PhysBoneColliders++);
                }
            }
        }

        private static void AttributeToNearestEntry(
            GameObject go,
            GameObject root,
            Dictionary<int, CostEntry> groups,
            System.Action<CostEntry> apply)
        {
            var cur = go;
            while (cur != null)
            {
                if (groups.TryGetValue(cur.GetInstanceID(), out var entry))
                {
                    apply(entry);
                    return;
                }
                if (cur == root) return;
                cur = cur.transform.parent != null ? cur.transform.parent.gameObject : null;
            }
        }

        private static List<TwinInfo> FindTwins(GameObject root, Scene scene)
        {
            var twins = new List<TwinInfo>();
            if (root == null || !scene.IsValid()) return twins;

            var descType = FindType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            if (descType == null) return twins;

            var rootName = root.name;
            // "LEAF" ↔ "LEAF QUEST", "LEAF_PC" ↔ "LEAF_Quest", etc.
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go == null || go == root) continue;
                if (go.GetComponent(descType) == null) continue;
                var other = go.name;
                var related =
                    other.StartsWith(rootName, System.StringComparison.OrdinalIgnoreCase) ||
                    rootName.StartsWith(other, System.StringComparison.OrdinalIgnoreCase) ||
                    ShareStem(rootName, other);
                if (!related) continue;
                twins.Add(new TwinInfo
                {
                    Name = other,
                    InstanceId = go.GetInstanceID(),
                    Active = go.activeInHierarchy
                });
            }
            return twins;
        }

        private static bool ShareStem(string a, string b)
        {
            static string Stem(string s)
            {
                var cut = s;
                foreach (var token in new[] { " QUEST", " Quest", "_QUEST", "_Quest", " PC", "_PC", " Android", "_Android" })
                {
                    var ix = cut.IndexOf(token, System.StringComparison.OrdinalIgnoreCase);
                    if (ix > 0) cut = cut.Substring(0, ix);
                }
                return cut.Trim();
            }
            var sa = Stem(a);
            var sb = Stem(b);
            return sa.Length >= 3 && string.Equals(sa, sb, System.StringComparison.OrdinalIgnoreCase);
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

        private static System.Type FindType(string fullName)
        {
            var t = System.Type.GetType(fullName);
            if (t != null) return t;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                try { t = asm.GetType(fullName); }
                catch { continue; }
                if (t != null) return t;
            }
            return null;
        }
    }
}
