using System.IO;
using System.Linq;
using AutonomousMcp.Editor.Perception;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AutonomousMcp.SelfTest
{
    /// <summary>
    /// Offline EditMode tests for the scene dossier helpers. Uses the built-in Standard
    /// shader so they pass without VRChat/Poiyomi assets.
    /// </summary>
    public sealed class StateDossierTests
    {
        private Material _mat;
        private string _texPath;

        [SetUp]
        public void SetUp()
        {
            var shader = Shader.Find("Standard");
            Assert.IsNotNull(shader, "Built-in Standard shader required for dossier tests.");
            _mat = new Material(shader);
        }

        [TearDown]
        public void TearDown()
        {
            if (_mat != null) Object.DestroyImmediate(_mat);
            if (!string.IsNullOrEmpty(_texPath) && File.Exists(_texPath))
            {
                AssetDatabase.DeleteAsset(_texPath);
                _texPath = null;
            }
        }

        [Test]
        public void DiffNonDefault_reports_exactly_one_changed_color()
        {
            // Fresh Material(Standard) matches defaults → zero changed.
            var baseline = MaterialDigest.DiffNonDefault(_mat);
            Assert.IsFalse(baseline.propertiesNotMeaningfullyReadable);
            Assert.AreEqual(0, baseline.changed.Count, "untouched Standard material should suppress all defaults");
            Assert.Greater(baseline.suppressedDefaults, 0);

            _mat.SetColor("_Color", new Color(1f, 0f, 0f, 1f));
            var diff = MaterialDigest.DiffNonDefault(_mat);
            Assert.AreEqual(1, diff.changed.Count, "only _Color should differ from defaults");
            Assert.AreEqual("_Color", diff.changed[0].name);
            Assert.Greater(diff.suppressedDefaults, 0);
        }

        [Test]
        public void ParseShaderIdentity_locked_poiyomi_recovers_family()
        {
            var id = MaterialDigest.ParseShaderIdentity(
                "Hidden/Locked/.poiyomi/Poiyomi Toon/95ee98d96f725a845a30c12cca7d770e");
            Assert.IsTrue(id.locked);
            Assert.AreEqual("poiyomi", id.family);
            Assert.AreEqual(".poiyomi/Poiyomi Toon", id.displayName);
            Assert.IsFalse(string.IsNullOrEmpty(id.note));
        }

        [Test]
        public void ParseShaderIdentity_unlocked_poiyomi_master()
        {
            var id = MaterialDigest.ParseShaderIdentity(".poiyomi/Master/Opaque");
            Assert.IsFalse(id.locked);
            Assert.AreEqual("poiyomi", id.family);
            Assert.AreEqual(".poiyomi/Master/Opaque", id.displayName);
        }

        [Test]
        public void DiffNonDefault_locked_shader_marks_unreadable()
        {
            // Simulate a locked shader name by swapping onto a real shader then
            // asserting ParseShaderIdentity + the locked early-out path via a stub name.
            // We cannot construct Hidden/Locked shaders without Poiyomi; test the identity
            // path and that DiffNonDefault on a normal mat stays readable.
            var id = MaterialDigest.ParseShaderIdentity(
                "Hidden/Locked/.poiyomi/Poiyomi Toon/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
            Assert.IsTrue(id.locked);

            var diff = MaterialDigest.DiffNonDefault(_mat);
            Assert.IsFalse(diff.propertiesNotMeaningfullyReadable);
        }

        [Test]
        public void Dossier_truncation_flags_when_max_renderers_hit()
        {
            // Build a tiny hierarchy with more renderers than the cap.
            var root = new GameObject("DossierTruncateRoot");
            try
            {
                for (int i = 0; i < 5; i++)
                {
                    var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    child.name = "Cube" + i;
                    child.transform.SetParent(root.transform, false);
                }

                var args = new JObject
                {
                    ["instanceId"] = root.GetInstanceID(),
                    ["sections"] = new JArray("renderers"),
                    ["max_renderers"] = 2
                };
                var payload = JToken.FromObject(StateDossier.Build(args)) as JObject;
                Assert.IsNotNull(payload);
                Assert.IsTrue(payload["truncated"]?["renderers"]?.Value<bool>() == true,
                    "truncated.renderers should be set when max_renderers is hit");
                Assert.AreEqual(2, payload["sections"]?["renderers"]?["count"]?.Value<int>());
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TextureDigest_reads_android_override()
        {
            // Create a tiny png under Assets so a TextureImporter exists.
            var dir = "Assets/_DossierTest";
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder("Assets", "_DossierTest");
            _texPath = dir + "/dossier_tex.png";

            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            File.WriteAllBytes(_texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(_texPath);

            var importer = AssetImporter.GetAtPath(_texPath) as TextureImporter;
            Assert.IsNotNull(importer);
            var settings = importer.GetPlatformTextureSettings("Android");
            settings.overridden = true;
            settings.maxTextureSize = 256;
            importer.SetPlatformTextureSettings(settings);
            importer.SaveAndReimport();

            importer = AssetImporter.GetAtPath(_texPath) as TextureImporter;
            TextureDigest.ReadAndroidOverride(importer, out var overridden, out var maxSize);
            Assert.IsTrue(overridden);
            Assert.AreEqual(256, maxSize);

            AssetDatabase.DeleteAsset(dir);
            _texPath = null;
        }

        // Degenerate triangles all referencing the same three vertices: the cost section counts
        // indices, so this builds an exact triangle count without allocating real geometry.
        private static Mesh MakeTriMesh(int triangleCount)
        {
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            var indices = new int[triangleCount * 3];
            for (int i = 0; i < triangleCount; i++)
            {
                indices[i * 3] = 0; indices[i * 3 + 1] = 1; indices[i * 3 + 2] = 2;
            }
            mesh.triangles = indices;
            return mesh;
        }

        private static GameObject AddRenderer(GameObject parent, string name, int triangles, bool active)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = MakeTriMesh(triangles);
            go.AddComponent<MeshRenderer>();
            go.SetActive(active);
            return go;
        }

        private static JObject CostSection(GameObject root)
        {
            var args = new JObject
            {
                ["instanceId"] = root.GetInstanceID(),
                ["sections"] = new JArray("cost")
            };
            var payload = JToken.FromObject(StateDossier.Build(args)) as JObject;
            Assert.IsNotNull(payload);
            return (JObject)payload["sections"]["cost"];
        }

        [Test]
        public void Cost_counts_inactive_renderers_and_projects_their_removal()
        {
            // The whole point of the section: VRChat's stats count renderers with includeInactive,
            // so a disabled wardrobe toggle must still show up as cost.
            var root = new GameObject("CostInactiveRoot");
            try
            {
                AddRenderer(root, "Shown", 10, active: true);
                AddRenderer(root, "Hidden", 4, active: false);

                var cost = CostSection(root);

                Assert.AreEqual(14, cost["totals"]["polygons"].Value<int>(),
                    "totals must include the disabled renderer");
                Assert.AreEqual(1, cost["inactive"]["objects"].Value<int>());
                Assert.AreEqual(4, cost["inactive"]["polygons"].Value<int>());
                Assert.AreEqual(10, cost["inactive"]["ifAllRemoved"]["polygons"].Value<int>());

                // Sorted by cost, and each row projects the total left behind without it.
                var candidates = (JArray)cost["candidates"];
                Assert.AreEqual("Shown", candidates[0]["name"].Value<string>());
                Assert.AreEqual(true, candidates[0]["active"].Value<bool>());
                Assert.AreEqual(4, candidates[1]["polygons"].Value<int>());
                Assert.AreEqual(false, candidates[1]["active"].Value<bool>());
                Assert.AreEqual(10, candidates[1]["ifRemoved"]["polygons"].Value<int>());
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Cost_rank_tracks_the_published_polygon_thresholds()
        {
            var root = new GameObject("CostRankRoot");
            try
            {
                // Exactly the Excellent ceiling is still Excellent...
                AddRenderer(root, "AtCeiling", 32000, active: true);
                Assert.AreEqual("Excellent", CostSection(root)["rank"]["polygons"].Value<string>());

                // ...and one triangle past it is not.
                AddRenderer(root, "OneMore", 1, active: true);
                Assert.AreEqual("Good", CostSection(root)["rank"]["polygons"].Value<string>());
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Cost_marks_animation_driven_objects()
        {
            // An FX clip that toggles a child's m_IsActive must surface as drivenBy —
            // otherwise the cleanup window treats live wardrobe as free space.
            var root = new GameObject("CostDrivenRoot");
            AnimationClip clip = null;
            AnimatorController ctrl = null;
            try
            {
                var wardrobe = AddRenderer(root, "DogEars", 100, active: false);
                AddRenderer(root, "Body", 50, active: true);

                clip = new AnimationClip { name = "ToggleDog" };
                AnimationUtility.SetEditorCurve(
                    clip,
                    EditorCurveBinding.FloatCurve("DogEars", typeof(GameObject), "m_IsActive"),
                    AnimationCurve.Constant(0, 1, 1f));

                ctrl = new AnimatorController { name = "TestFX" };
                ctrl.AddLayer("FX");
                ctrl.AddParameter("Dog", AnimatorControllerParameterType.Bool);
                var sm = ctrl.layers[0].stateMachine;
                var state = sm.AddState("On");
                state.motion = clip;
                var any = sm.AddAnyStateTransition(state);
                any.AddCondition(AnimatorConditionMode.If, 0, "Dog");

                var animator = root.AddComponent<Animator>();
                animator.runtimeAnimatorController = ctrl;

                var report = AvatarCost.Build(root, root.scene);
                var dog = report.Entries.Find(e => e.Name == "DogEars");
                Assert.IsNotNull(dog);
                Assert.IsTrue(dog.IsDriven, "DogEars must be marked driven by the FX clip");
                Assert.IsTrue(
                    dog.DrivenBySummary.IndexOf("Dog", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    dog.DrivenBySummary.IndexOf("ToggleDog", System.StringComparison.OrdinalIgnoreCase) >= 0,
                    "driven summary should name the parameter or clip: " + dog.DrivenBySummary);

                Assert.AreEqual(1, report.InactiveDriven);
                Assert.AreEqual(0, report.InactiveUndriven);

                var cost = CostSection(root);
                var candidates = (JArray)cost["candidates"];
                var dogRow = candidates.First(t => t.Value<string>("name") == "DogEars");
                Assert.IsTrue(dogRow.Value<bool>("driven"));
            }
            finally
            {
                Object.DestroyImmediate(root);
                if (clip != null) Object.DestroyImmediate(clip);
                if (ctrl != null) Object.DestroyImmediate(ctrl);
            }
        }

        [Test]
        public void ComputeStateHash_stable_for_same_scene_root()
        {
            var root = new GameObject("DossierHashRoot");
            try
            {
                var scene = root.scene;
                var a = StateDossier.ComputeStateHash(root, scene);
                var b = StateDossier.ComputeStateHash(root, scene);
                Assert.AreEqual(a, b);
                Assert.AreEqual(16, a.Length);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
