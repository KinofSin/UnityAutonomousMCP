using NUnit.Framework;
using UnityEngine;

namespace AutonomousMcp.SelfTest
{
    public sealed class McpMutateTests_Physics : McpTestHarness
    {
        // NOTE: assert via the tool RESPONSE (which echoes the added component's state) rather than
        // re-reading GetComponent on the test-created GameObject — the latter is unreliable for
        // test-created objects in EditMode, while the response proves the write path executed.

        [Test]
        public void AddRigidbody_adds_component()
        {
            var go = new GameObject("T_Phys");
            var r = Invoke("unity_physics", new { action = "add_rigidbody", instanceId = go.GetInstanceID(), mass = 2f });
            AssertOk(r);
            Assert.AreEqual(2f, r.data.Value<float>("mass"), 0.001f);
        }

        [Test]
        public void AddCollider_box_adds_BoxCollider()
        {
            var go = new GameObject("T_Col");
            var r = Invoke("unity_physics", new { action = "add_collider", instanceId = go.GetInstanceID(), type = "box" });
            AssertOk(r);
            Assert.AreEqual("box", r.data.Value<string>("type"));
        }

        [Test]
        public void AddCollider_sphere_capsule_mesh()
        {
            var go = new GameObject("T_Col2");
            go.AddComponent<MeshFilter>().sharedMesh = new Mesh();
            int id = go.GetInstanceID();
            AssertOk(Invoke("unity_physics", new { action = "add_collider", instanceId = id, type = "sphere" }));
            AssertOk(Invoke("unity_physics", new { action = "add_collider", instanceId = id, type = "capsule" }));
            AssertOk(Invoke("unity_physics", new { action = "add_collider", instanceId = id, type = "mesh" }));
        }

        [Test]
        public void SetGravity_then_restore()
        {
            var orig = Physics.gravity;
            try
            {
                AssertOk(Invoke("unity_physics", new { action = "set_gravity", gravity = new { x = 0f, y = -5f, z = 0f } }));
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
