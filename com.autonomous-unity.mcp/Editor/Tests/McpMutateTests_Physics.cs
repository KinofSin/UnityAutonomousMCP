using NUnit.Framework;
using UnityEngine;

namespace AutonomousMcp.SelfTest
{
    public sealed class McpMutateTests_Physics : McpTestHarness
    {
        [Test]
        public void AddRigidbody_adds_component()
        {
            var go = new GameObject("T_Phys");
            AssertOk(Invoke("unity_physics", new { action = "add_rigidbody", name = "T_Phys", mass = 2f }));
            Assert.IsNotNull(go.GetComponent<Rigidbody>());
            Assert.AreEqual(2f, go.GetComponent<Rigidbody>().mass, 0.001f);
        }

        [Test]
        public void AddCollider_box_adds_BoxCollider()
        {
            var go = new GameObject("T_Col");
            AssertOk(Invoke("unity_physics", new { action = "add_collider", name = "T_Col", type = "box" }));
            Assert.IsNotNull(go.GetComponent<BoxCollider>());
        }

        [Test]
        public void AddCollider_sphere_capsule_mesh()
        {
            var go = new GameObject("T_Col2");
            go.AddComponent<MeshFilter>().sharedMesh = new Mesh();
            AssertOk(Invoke("unity_physics", new { action = "add_collider", name = "T_Col2", type = "sphere" }));
            AssertOk(Invoke("unity_physics", new { action = "add_collider", name = "T_Col2", type = "capsule" }));
            AssertOk(Invoke("unity_physics", new { action = "add_collider", name = "T_Col2", type = "mesh" }));
            Assert.IsNotNull(go.GetComponent<SphereCollider>());
            Assert.IsNotNull(go.GetComponent<CapsuleCollider>());
            Assert.IsNotNull(go.GetComponent<MeshCollider>());
        }

        [Test]
        public void SetGravity_then_restore()
        {
            var orig = Physics.gravity;
            try
            {
                AssertOk(Invoke("unity_physics", new { action = "set_gravity", x = 0f, y = -5f, z = 0f }));
                Assert.AreEqual(-5f, Physics.gravity.y, 0.01f);
            }
            finally { Physics.gravity = orig; }
        }

        [Test]
        public void GetGravity_and_settings_read()
        {
            AssertOk(Invoke("unity_physics", new { action = "get_gravity" }));
            AssertOk(Invoke("unity_physics", new { action = "get_physics_settings" }));
        }

        [Test]
        public void SetIgnoreLayerCollision_then_restore()
        {
            bool orig = Physics.GetIgnoreLayerCollision(8, 9);
            try
            {
                AssertOk(Invoke("unity_physics", new { action = "set_ignore_layer_collision", layer_a = 8, layer_b = 9, ignore = true }));
                Assert.IsTrue(Physics.GetIgnoreLayerCollision(8, 9));
            }
            finally { Physics.IgnoreLayerCollision(8, 9, orig); }
        }
    }
}
