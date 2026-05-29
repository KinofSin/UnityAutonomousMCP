using AutonomousMcp.Editor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AutonomousMcp.Editor.Tools
{
    /// <summary>
    /// unity_physics — rigidbody/collider scaffolding + global physics settings.
    /// </summary>
    public static class UnityPhysicsTool
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            ToolRegistry.Register("unity_physics", ToolMode.Mutate, ToolCategory.Physics,
                "Physics setup. Actions: add_rigidbody, add_collider, set_gravity, " +
                "get_gravity, set_ignore_layer_collision, get_physics_settings.",
                Handle);
        }

        private static AutonomousMcpToolResponse Handle(JObject args)
        {
            var action = args.Value<string>("action") ?? "get_physics_settings";
            switch (action)
            {
                case "add_rigidbody": return AddRigidbody(args);
                case "add_collider": return AddCollider(args);
                case "set_gravity": return SetGravity(args);
                case "get_gravity": return Ok(new { action, gravity = Vec(Physics.gravity) });
                case "set_ignore_layer_collision": return SetIgnoreLayer(args);
                case "get_physics_settings": return GetPhysicsSettings();
                default: return Err($"Unsupported unity_physics action '{action}'.");
            }
        }

        private static AutonomousMcpToolResponse AddRigidbody(JObject args)
        {
            var go = ResolveGo(args, out var err);
            if (go == null) return Err(err);
            // NOTE: '??' does not honor Unity's overloaded == (fake-null), so use an explicit
            // Unity-aware null check before AddComponent.
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.mass = args.Value<float?>("mass") ?? rb.mass;
            rb.useGravity = args.Value<bool?>("use_gravity") ?? rb.useGravity;
            rb.isKinematic = args.Value<bool?>("is_kinematic") ?? rb.isKinematic;
            return Ok(new { action = "add_rigidbody", rb.mass, rb.useGravity, rb.isKinematic });
        }

        private static AutonomousMcpToolResponse AddCollider(JObject args)
        {
            var go = ResolveGo(args, out var err);
            if (go == null) return Err(err);
            var type = args.Value<string>("type") ?? "box";
            // NOTE: '??' does not honor Unity's overloaded == (fake-null); use explicit checks.
            Collider c;
            switch (type.ToLowerInvariant())
            {
                case "box": c = go.GetComponent<BoxCollider>(); if (c == null) c = go.AddComponent<BoxCollider>(); break;
                case "sphere": c = go.GetComponent<SphereCollider>(); if (c == null) c = go.AddComponent<SphereCollider>(); break;
                case "capsule": c = go.GetComponent<CapsuleCollider>(); if (c == null) c = go.AddComponent<CapsuleCollider>(); break;
                case "mesh": c = go.GetComponent<MeshCollider>(); if (c == null) c = go.AddComponent<MeshCollider>(); break;
                default: return Err($"Unknown collider type '{type}'. Use box|sphere|capsule|mesh.");
            }
            c.isTrigger = args.Value<bool?>("is_trigger") ?? c.isTrigger;
            return Ok(new { action = "add_collider", type, isTrigger = c.isTrigger });
        }

        private static AutonomousMcpToolResponse SetGravity(JObject args)
        {
            var g = args["gravity"] as JObject;
            if (g == null) return Err("gravity object required.");
            Physics.gravity = new Vector3(
                g.Value<float?>("x") ?? Physics.gravity.x,
                g.Value<float?>("y") ?? Physics.gravity.y,
                g.Value<float?>("z") ?? Physics.gravity.z);
            return Ok(new { action = "set_gravity", gravity = Vec(Physics.gravity) });
        }

        private static AutonomousMcpToolResponse SetIgnoreLayer(JObject args)
        {
            var a = args.Value<int?>("layer_a");
            var b = args.Value<int?>("layer_b");
            var ignore = args.Value<bool?>("ignore") ?? true;
            if (!a.HasValue || !b.HasValue) return Err("layer_a and layer_b required (int).");
            Physics.IgnoreLayerCollision(a.Value, b.Value, ignore);
            return Ok(new { action = "set_ignore_layer_collision", layer_a = a, layer_b = b, ignored = ignore });
        }

        private static AutonomousMcpToolResponse GetPhysicsSettings()
        {
            return Ok(new
            {
                action = "get_physics_settings",
                gravity = Vec(Physics.gravity),
                Physics.bounceThreshold,
                Physics.defaultMaxAngularSpeed,
                Physics.queriesHitTriggers,
                Physics.queriesHitBackfaces
            });
        }

        private static object Vec(Vector3 v) => new { v.x, v.y, v.z };

        private static GameObject ResolveGo(JObject args, out string err)
        {
            err = string.Empty;
            var id = args.Value<int?>("instanceId");
            if (id.HasValue)
            {
                var obj = EditorUtility.InstanceIDToObject(id.Value) as GameObject;
                if (obj == null) { err = $"instanceId {id} not GameObject."; return null; }
                return obj;
            }
            var name = args.Value<string>("name");
            if (string.IsNullOrEmpty(name)) { err = "instanceId or name required."; return null; }
            var go = GameObject.Find(name);
            if (go == null) { err = $"GameObject '{name}' not found."; return null; }
            return go;
        }

        private static AutonomousMcpToolResponse Ok(object d) =>
            new AutonomousMcpToolResponse { success = true, data = JToken.FromObject(d), error = string.Empty };
        private static AutonomousMcpToolResponse Err(string m) =>
            new AutonomousMcpToolResponse { success = false, data = null, error = m };
    }
}
