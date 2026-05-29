using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.SelfTest
{
    public sealed class McpMutateTests_Assets : McpTestHarness
    {
        private static string MakeTempTexture()
        {
            var path = TestFolder + "/t_tex.png";
            var tex = new Texture2D(4, 4);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return path;
        }

        [Test]
        public void Importer_get_then_set_property()
        {
            var path = MakeTempTexture();
            AssertOk(Invoke("unity_importer", new { action = "get_importer_type", asset_path = path }));
            AssertOk(Invoke("unity_importer", new { action = "get_properties", asset_path = path, prefix = "m_" }));
            // m_IsReadable round-trips cleanly (no platform-override resolution like maxTextureSize).
            AssertOk(Invoke("unity_importer", new { action = "set_property", asset_path = path, property_path = "m_IsReadable", value = true }));
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.IsTrue(imp.isReadable);
        }

        [Test]
        public void Cleaner_finds_and_deletes_scoped_orphan()
        {
            AssetDatabase.CreateFolder(TestFolder, "Orphans");
            var so = ScriptableObject.CreateInstance<McpTestSO>();
            AssetDatabase.CreateAsset(so, TestFolder + "/Orphans/orphan.asset");
            AssetDatabase.SaveAssets();

            var find = Invoke("unity_cleaner", new { action = "find_orphans", folder = TestFolder + "/Orphans" });
            AssertOk(find);
            AssertOk(Invoke("unity_cleaner", new { action = "delete_orphans", folder = TestFolder + "/Orphans", confirm = true }));
            Assert.IsFalse(File.Exists(TestFolder + "/Orphans/orphan.asset"));
        }

        [Test]
        public void Cleaner_finds_and_deletes_scoped_empty_folder()
        {
            AssetDatabase.CreateFolder(TestFolder, "EmptyDir");
            AssetDatabase.Refresh();
            AssertOk(Invoke("unity_cleaner", new { action = "find_empty_folders", folder = TestFolder }));
            AssertOk(Invoke("unity_cleaner", new { action = "delete_empty_folders", folder = TestFolder, confirm = true }));
            Assert.IsFalse(AssetDatabase.IsValidFolder(TestFolder + "/EmptyDir"));
        }

        [Test]
        public void Cleaner_reads_are_safe()
        {
            AssertOk(Invoke("unity_cleaner", new { action = "find_unused_materials", folder = TestFolder }));
            AssertOk(Invoke("unity_cleaner", new { action = "find_internal_error_shaders", folder = TestFolder }));
        }
    }
}
