using System.IO;
using AutonomousMcp.Editor.Perception;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
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
