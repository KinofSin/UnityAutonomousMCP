using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using AutonomousMcp.Editor.Core;
using AutonomousMcp.Editor.Generators;

namespace AutonomousMcp.SelfTest
{
    // Key-free tests for the testable (non-network) half of the image generators.
    public sealed class GeneratedAssetWriterTests
    {
        private const string Dir = "Assets/_MCPSelfTest";

        private static byte[] TinyPng()
        {
            var t = new Texture2D(4, 4);
            for (int x = 0; x < 4; x++) for (int y = 0; y < 4; y++) t.SetPixel(x, y, Color.red);
            t.Apply();
            var png = t.EncodeToPNG();
            Object.DestroyImmediate(t);
            return png;
        }

        [TearDown]
        public void Cleanup()
        {
            if (AssetDatabase.IsValidFolder(Dir)) AssetDatabase.DeleteAsset(Dir);
        }

        [Test]
        public void Write_texture_creates_a_texture_asset()
        {
            var path = GeneratedAssetWriter.Write(GeneratorKind.Texture, TinyPng(), Dir + "/tex", out var err);
            Assert.IsNull(err, err);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            StringAssert.EndsWith(".png", path);
        }

        [Test]
        public void Write_sprite_sets_importer_to_sprite()
        {
            var path = GeneratedAssetWriter.Write(GeneratorKind.Sprite, TinyPng(), Dir + "/spr", out var err);
            Assert.IsNull(err, err);
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsNotNull(ti);
            Assert.AreEqual(TextureImporterType.Sprite, ti.textureType);
        }

        [Test]
        public void Write_material_creates_a_material_with_main_texture()
        {
            var path = GeneratedAssetWriter.Write(GeneratorKind.Material, TinyPng(), Dir + "/mat", out var err);
            Assert.IsNull(err, err);
            StringAssert.EndsWith(".mat", path);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.IsNotNull(mat);
            Assert.IsNotNull(mat.mainTexture, "material should reference the generated texture");
        }

        [Test]
        public void Write_rejects_non_image_bytes()
        {
            var path = GeneratedAssetWriter.Write(GeneratorKind.Texture, new byte[] { 1, 2, 3 }, Dir + "/bad", out var err);
            Assert.IsNull(path);
            Assert.IsNotNull(err);
        }

        [Test]
        public void OpenAi_generator_is_not_configured_without_a_key()
        {
            if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("GENERATOR_OPENAI_API_KEY")))
                Assert.Ignore("GENERATOR_OPENAI_API_KEY is set in this environment");
            Assert.IsFalse(new OpenAiTextureGenerator().IsConfigured());
        }
    }
}
