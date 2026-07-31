using System.IO;
using System.Linq;
using AutonomousMcp.Editor.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.SelfTest
{
    /// <summary>
    /// Offline EditMode tests for checkpoint asset coverage. These exist because the
    /// optimization loop applies texture-importer changes autonomously (Tier 1), and before
    /// copy-on-first-touch capture existed a checkpoint restore could not undo them:
    /// Create() only ever snapshotted active_scene.unity.
    ///
    /// Scope note: these do not open or dirty a scene. Doing so in EditMode would hijack
    /// whichever scene the user has open in a live avatar project.
    /// </summary>
    public sealed class CheckpointStoreTests
    {
        private const string TempDir = "Assets/__mcp_checkpoint_tests";
        private string _checkpointId;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempDir))
                AssetDatabase.CreateFolder("Assets", "__mcp_checkpoint_tests");
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_checkpointId))
            {
                CheckpointStore.Delete(_checkpointId, out _);
                _checkpointId = null;
            }
            if (AssetDatabase.IsValidFolder(TempDir))
                AssetDatabase.DeleteAsset(TempDir);
        }

        private string CreateTexture(string name, int maxSize)
        {
            var path = $"{TempDir}/{name}.png";
            var tex = new Texture2D(64, 64);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.maxTextureSize = maxSize;
            importer.SaveAndReimport();
            return path;
        }

        private CheckpointStore.Manifest NewCheckpoint()
        {
            var manifest = CheckpointStore.Create("test", "CheckpointStoreTests");
            _checkpointId = manifest.id;
            return manifest;
        }

        [Test]
        public void Capture_then_restore_puts_importer_settings_back()
        {
            var path = CreateTexture("importer_roundtrip", 2048);
            NewCheckpoint();

            CheckpointStore.CaptureAsset(path, "test");

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.maxTextureSize = 256;
            importer.crunchedCompression = true;
            importer.SaveAndReimport();
            Assert.AreEqual(256, ((TextureImporter)AssetImporter.GetAtPath(path)).maxTextureSize,
                "precondition: the importer change must actually land");

            Assert.IsTrue(CheckpointStore.Restore(_checkpointId, false, out var error), error);

            var restored = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.AreEqual(2048, restored.maxTextureSize,
                "importer settings live in .meta — restore must bring them back");
            Assert.IsFalse(restored.crunchedCompression);
        }

        [Test]
        public void Capture_stores_the_meta_sibling()
        {
            var path = CreateTexture("meta_capture", 512);
            var manifest = NewCheckpoint();

            CheckpointStore.CaptureAsset(path, "test");

            var reloaded = CheckpointStore.Find(manifest.id);
            var captured = reloaded.capturedAssets.Single(c => c.assetPath == path);
            Assert.IsNotEmpty(captured.storedMeta, "a texture always has a .meta sibling");

            var root = Path.Combine(CheckpointStore.RootDirectory, manifest.id);
            Assert.IsTrue(File.Exists(Path.Combine(root, captured.storedFile.Replace('/', Path.DirectorySeparatorChar))));
            Assert.IsTrue(File.Exists(Path.Combine(root, captured.storedMeta.Replace('/', Path.DirectorySeparatorChar))));
        }

        [Test]
        public void Capture_is_copy_on_first_touch_and_does_not_re_snapshot()
        {
            var path = CreateTexture("first_touch", 2048);
            var manifest = NewCheckpoint();

            CheckpointStore.CaptureAsset(path, "first");

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.maxTextureSize = 256;
            importer.SaveAndReimport();

            // A second capture must NOT overwrite the checkpoint-time bytes, or the stored
            // state would drift forward to 256 and restore would be a no-op.
            CheckpointStore.CaptureAsset(path, "second");

            var reloaded = CheckpointStore.Find(manifest.id);
            Assert.AreEqual(1, reloaded.capturedAssets.Count(c => c.assetPath == path),
                "one entry per path per checkpoint");
            Assert.AreEqual("first", reloaded.capturedAssets.Single(c => c.assetPath == path).capturedByTool);

            Assert.IsTrue(CheckpointStore.Restore(manifest.id, false, out var error), error);
            Assert.AreEqual(2048, ((TextureImporter)AssetImporter.GetAtPath(path)).maxTextureSize);
        }

        [Test]
        public void Capture_always_returns_a_checkpoint_holding_the_asset()
        {
            // The zero-checkpoints auto-create branch is deliberately not forced here: doing so
            // would need DeleteAll(), which would destroy the user's real checkpoints.
            var path = CreateTexture("auto_checkpoint", 1024);
            NewCheckpoint();

            var manifest = CheckpointStore.CaptureAsset(path, "manage_texture.set_import_settings");
            Assert.IsNotNull(manifest, "an autonomous edit must never go uncaptured");
            Assert.AreEqual(_checkpointId, manifest.id, "capture targets the newest checkpoint");
            Assert.IsTrue(manifest.capturedAssets.Any(c => c.assetPath == path));
            Assert.AreEqual("manage_texture.set_import_settings",
                manifest.capturedAssets.Single(c => c.assetPath == path).capturedByTool);
        }

        [Test]
        public void Material_property_survives_a_restore_round_trip()
        {
            var shader = Shader.Find("Standard");
            Assert.IsNotNull(shader, "built-in Standard shader required");

            var matPath = $"{TempDir}/roundtrip.mat";
            var mat = new Material(shader);
            mat.SetFloat("_Metallic", 0.25f);
            AssetDatabase.CreateAsset(mat, matPath);
            AssetDatabase.SaveAssets();

            NewCheckpoint();
            CheckpointStore.CaptureAsset(matPath, "test");

            var live = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            live.SetFloat("_Metallic", 0.9f);
            EditorUtility.SetDirty(live);
            AssetDatabase.SaveAssets();

            Assert.IsTrue(CheckpointStore.Restore(_checkpointId, false, out var error), error);

            var restored = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            Assert.AreEqual(0.25f, restored.GetFloat("_Metallic"), 0.0001f,
                ".mat asset bytes must be restored, not just the in-memory object");
        }

        [Test]
        public void Diff_compares_each_asset_against_its_own_snapshot()
        {
            var path = CreateTexture("diff_target", 2048);
            NewCheckpoint();
            CheckpointStore.CaptureAsset(path, "test");

            var diff = CheckpointStore.Diff(_checkpointId);
            StringAssert.Contains(path, diff);
            StringAssert.Contains("+meta", diff);
            // Previously every tracked path was sized against active_scene.unity, so a
            // just-captured asset reported "changed" instead of matching itself.
            StringAssert.Contains("size-match", diff);
        }

        [Test]
        public void Create_records_whether_the_scene_was_dirty()
        {
            var manifest = NewCheckpoint();
            Assert.AreEqual(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty,
                manifest.sceneWasDirty);
            Assert.IsNotNull(manifest.capturedAssets, "capturedAssets must be initialised for older manifests");
        }
    }
}
