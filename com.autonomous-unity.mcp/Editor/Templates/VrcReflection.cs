using System;
using System.Reflection;
using UnityEngine;

namespace AutonomousMcp.Editor.Templates
{
    // Reflection wrappers for the VRChat SDK3 Avatars types (the package cannot hard-reference the
    // SDK). Every method no-ops + returns false/note when the SDK or a member is absent, so the
    // package always compiles and runs without VRChat installed.
    internal static class VrcReflection
    {
        private const string DescriptorTypeName = "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor";
        private const string MenuTypeName       = "VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu";
        private const string ParamsTypeName     = "VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters";

        public static bool SdkPresent => FindType(DescriptorTypeName) != null;

        public static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { var t = asm.GetType(fullName); if (t != null) return t; } catch { }
            }
            return null;
        }

        public static Component GetDescriptor(GameObject go)
        {
            var t = FindType(DescriptorTypeName);
            return t == null || go == null ? null : go.GetComponent(t);
        }

        // ViewPosition is a public Vector3 field on VRCAvatarDescriptor. Treat default (0,0,0) as unset.
        public static bool HasViewpoint(Component descriptor)
        {
            if (descriptor == null) return false;
            var f = descriptor.GetType().GetField("ViewPosition");
            if (f == null) return false;
            var v = (Vector3)f.GetValue(descriptor);
            return v != Vector3.zero;
        }

        public static bool HasExpressionMenu(Component descriptor) => RefNonNull(descriptor, "expressionsMenu");
        public static bool HasExpressionParams(Component descriptor) => RefNonNull(descriptor, "expressionParameters");

        private static bool RefNonNull(Component descriptor, string fieldName)
        {
            if (descriptor == null) return false;
            var f = descriptor.GetType().GetField(fieldName);
            return f != null && f.GetValue(descriptor) != null;
        }

        // ── mutations (apply) ──

        public static Component AddDescriptor(GameObject go)
        {
            var t = FindType(DescriptorTypeName);
            if (t == null || go == null) return null;
            var existing = go.GetComponent(t);
            return existing != null ? existing : go.AddComponent(t);
        }

        // Default viewpoint: head bone position (humanoid) nudged slightly forward, in avatar-local space.
        public static bool SetDefaultViewpoint(GameObject avatar, Component descriptor)
        {
            if (avatar == null || descriptor == null) return false;
            var f = descriptor.GetType().GetField("ViewPosition");
            if (f == null) return false;
            var animator = avatar.GetComponent<Animator>();
            Vector3 local = new Vector3(0f, 1.5f, 0.1f); // fallback if no head bone
            if (animator != null && animator.isHuman)
            {
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null)
                    local = avatar.transform.InverseTransformPoint(head.position) + new Vector3(0f, 0.05f, 0.1f);
            }
            f.SetValue(descriptor, local);
            return true;
        }

        // Create the two expression assets at folderPath, link them to the descriptor, set
        // customExpressions = true. Returns a note on failure.
        public static bool CreateAndLinkExpressions(Component descriptor, string folderPath, out string note)
        {
            note = null;
            var menuType = FindType(MenuTypeName);
            var paramsType = FindType(ParamsTypeName);
            if (descriptor == null || menuType == null || paramsType == null)
            { note = "VRChat SDK expression types not found"; return false; }

            var menu = ScriptableObject.CreateInstance(menuType);
            var prms = ScriptableObject.CreateInstance(paramsType);
            UnityEditor.AssetDatabase.CreateAsset(menu, folderPath + "/ExpressionsMenu.asset");
            UnityEditor.AssetDatabase.CreateAsset(prms, folderPath + "/ExpressionParameters.asset");

            var dt = descriptor.GetType();
            dt.GetField("customExpressions")?.SetValue(descriptor, true);
            dt.GetField("expressionsMenu")?.SetValue(descriptor, menu);
            dt.GetField("expressionParameters")?.SetValue(descriptor, prms);
            UnityEditor.EditorUtility.SetDirty(descriptor);
            return true;
        }
    }
}
