using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AutonomousMcp.Editor
{
    internal static class AutonomousMcpToolDispatcher
    {
        private const int MaxBatchDepth = 3;

        public static AutonomousMcpToolResponse Dispatch(AutonomousMcpEnvelope envelope)
        {
            return AutonomousMcpMainThread.Invoke(() => DispatchOnMainThread(envelope, 0));
        }

        private static AutonomousMcpToolResponse DispatchOnMainThread(AutonomousMcpEnvelope envelope, int depth)
        {
            if (envelope == null || string.IsNullOrWhiteSpace(envelope.tool))
            {
                return Error("Missing tool name.");
            }

            var args = envelope.@params ?? new JObject();

            try
            {
                switch (envelope.tool)
                {
                    case "health_check":
                        return HandleHealthCheck(args);
                    case "read_console":
                        return HandleReadConsole(args);
                    case "manage_scene":
                        return HandleManageScene(args);
                    case "manage_gameobject":
                        return HandleManageGameObject(args);
                    case "manage_script":
                        return HandleManageScript(args);
                    case "validate_script":
                        return HandleValidateScript(args);
                    case "run_tests":
                        return HandleRunTests(args);
                    case "get_test_job":
                        return HandleGetTestJob(args);
                    case "manage_component":
                        return HandleManageComponent(args);
                    case "execute_menu_item":
                        return HandleExecuteMenuItem(args);
                    case "manage_asset":
                        return HandleManageAsset(args);
                    case "manage_editor":
                        return HandleManageEditor(args);
                    case "read_script":
                        return HandleReadScript(args);
                    case "capture_screenshot":
                        return HandleCaptureScreenshot(args);
                    case "manage_animator":
                        return HandleManageAnimator(args);
                    case "manage_material":
                        return HandleManageMaterial(args);
                    case "execute_csharp":
                        return HandleExecuteCSharp(args);
                    case "search_hierarchy":
                        return HandleSearchHierarchy(args);
                    case "get_project_structure":
                        return HandleGetProjectStructure(args);
                    case "manage_prefab":
                        return HandleManagePrefab(args);
                    case "manage_selection":
                        return HandleManageSelection(args);
                    case "manage_layer_tag":
                        return HandleManageLayerTag(args);
                    case "get_compilation_errors":
                        return HandleGetCompilationErrors(args);
                    case "manage_project_settings":
                        return HandleManageProjectSettings(args);
                    case "get_installed_packages":
                        return HandleGetInstalledPackages(args);
                    case "list_shaders":
                        return HandleListShaders(args);
                    case "get_asset_info":
                        return HandleGetAssetInfo(args);
                    case "scan_armature":
                        return HandleScanArmature(args);
                    case "scan_avatar":
                        return HandleScanAvatar(args);
                    case "manage_scriptable_object":
                        return HandleManageScriptableObject(args);
                    case "manage_texture":
                        return HandleManageTexture(args);
                    case "refresh_unity":
                        return HandleRefreshUnity(args);
                    case "batch_execute":
                        return HandleBatchExecute(args, depth);
                    default:
                        return Error($"Unsupported tool '{envelope.tool}'.");
                }
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
        }

        private static AutonomousMcpToolResponse HandleReadConsole(JObject args)
        {
            var level = args.Value<string>("level") ?? "all";
            var limit = Math.Max(1, Math.Min(args.Value<int?>("limit") ?? 200, 1000));
            var logs = AutonomousMcpLogStore.Read(level, limit);

            return Success(JToken.FromObject(new
            {
                level,
                limit,
                count = logs.Count,
                entries = logs
            }));
        }

        private static AutonomousMcpToolResponse HandleHealthCheck(JObject args)
        {
            var scene = SceneManager.GetActiveScene();

            return Success(JToken.FromObject(new
            {
                ok = true,
                package = "com.autonomous.unity.mcp",
                unityVersion = Application.unityVersion,
                projectPath = Application.dataPath,
                activeScene = new
                {
                    name = scene.name,
                    path = scene.path,
                    isDirty = scene.isDirty
                },
                editor = new
                {
                    isPlaying = EditorApplication.isPlaying,
                    isCompiling = EditorApplication.isCompiling,
                    isUpdating = EditorApplication.isUpdating
                },
                supportedTools = new[]
                {
                    "health_check",
                    "read_console",
                    "manage_scene",
                    "manage_gameobject",
                    "manage_component",
                    "manage_script",
                    "manage_asset",
                    "manage_editor",
                    "manage_animator",
                    "manage_material",
                    "read_script",
                    "execute_menu_item",
                    "capture_screenshot",
                    "execute_csharp",
                    "search_hierarchy",
                    "get_project_structure",
                    "manage_prefab",
                    "manage_selection",
                    "manage_layer_tag",
                    "get_compilation_errors",
                    "manage_project_settings",
                    "get_installed_packages",
                    "list_shaders",
                    "get_asset_info",
                    "scan_armature",
                    "scan_avatar",
                    "validate_script",
                    "run_tests",
                    "get_test_job",
                    "manage_scriptable_object",
                    "manage_texture",
                    "refresh_unity",
                    "batch_execute"
                },
                supportedActions = new
                {
                    manage_scene = new[] { "inspect_active_scene", "save_active_scene", "open_scene", "list_scenes" },
                    manage_gameobject = new[] { "create", "create_empty", "create_primitive", "find", "find_by_name", "find_contains", "set_transform", "get_world_transform", "reparent", "get_children", "get_parent", "get_full_hierarchy", "set_active", "rename", "destroy" },
                    manage_component = new[] { "add", "remove", "get_all", "get_properties", "set_property" },
                    manage_script = new[] { "create_or_update" },
                    manage_asset = new[] { "find", "instantiate_prefab" },
                    manage_editor = new[] { "enter_play_mode", "exit_play_mode", "pause", "step", "undo", "redo" },
                    manage_animator = new[] { "get_parameters", "set_parameter", "get_layers", "get_states", "get_current_state" },
                    manage_material = new[] { "get", "get_properties", "set_property", "list_materials" },
                    capture_screenshot = new[] { "scene", "game" },
                    manage_prefab = new[] { "get_status", "open", "apply_overrides", "revert_overrides", "unpack" },
                    manage_selection = new[] { "get", "set", "clear", "focus" },
                    manage_layer_tag = new[] { "get", "set_layer", "set_tag", "list_layers", "list_tags", "list_sorting_layers" },
                    manage_project_settings = new[] { "get_player_settings", "set_player_setting", "get_quality_settings", "get_physics_settings", "get_time_settings" },
                    manage_scriptable_object = new[] { "find", "get_properties", "set_property", "create", "list_fields" },
                    manage_texture = new[] { "get_import_settings", "set_import_settings", "get_info", "find_textures" }
                }
            }));
        }

        private static AutonomousMcpToolResponse HandleManageScene(JObject args)
        {
            var action = args.Value<string>("action") ?? "inspect_active_scene";
            var scene = SceneManager.GetActiveScene();

            switch (action)
            {
                case "inspect_active_scene":
                {
                    var roots = scene.GetRootGameObjects();
                    var rootNames = new List<string>(roots.Length);
                    foreach (var root in roots)
                    {
                        rootNames.Add(root.name);
                    }

                    return Success(JToken.FromObject(new
                    {
                        action,
                        name = scene.name,
                        path = scene.path,
                        isDirty = scene.isDirty,
                        rootCount = roots.Length,
                        roots = rootNames
                    }));
                }
                case "save_active_scene":
                {
                    if (string.IsNullOrWhiteSpace(scene.path))
                    {
                        return Error("Active scene has no path yet. Save it once in Unity before calling save_active_scene.");
                    }

                    var saved = EditorSceneManager.SaveScene(scene, scene.path, false);
                    return Success(JToken.FromObject(new
                    {
                        action,
                        saved,
                        path = scene.path
                    }));
                }
                case "open_scene":
                {
                    var scenePath = args.Value<string>("path") ?? args.Value<string>("scene_path") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(scenePath))
                        return Error("open_scene requires a non-empty path (e.g. 'Assets/Scenes/MyScene.unity').");

                    if (scene.isDirty)
                    {
                        var saveFirst = args.Value<bool?>("save_first") ?? true;
                        if (saveFirst && !string.IsNullOrWhiteSpace(scene.path))
                            EditorSceneManager.SaveScene(scene, scene.path, false);
                    }

                    var opened = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    return Success(JToken.FromObject(new
                    {
                        action,
                        name = opened.name,
                        path = opened.path,
                        isLoaded = opened.isLoaded
                    }));
                }
                case "list_scenes":
                {
                    var guids = AssetDatabase.FindAssets("t:Scene");
                    var scenes = new JArray();
                    foreach (var guid in guids)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        scenes.Add(JToken.FromObject(new { guid, path }));
                    }

                    return Success(JToken.FromObject(new
                    {
                        action,
                        count = scenes.Count,
                        scenes
                    }));
                }
                default:
                    return Error($"Unsupported manage_scene action '{action}'.");
            }
        }

        private static AutonomousMcpToolResponse HandleManageScript(JObject args)
        {
            var action = args.Value<string>("action") ?? "";
            if (action != "create_or_update")
            {
                return Error($"Unsupported manage_script action '{action}'.");
            }

            var scriptPath = args.Value<string>("scriptPath") ?? string.Empty;
            var contents = args.Value<string>("contents") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(scriptPath) ||
                !(scriptPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                  scriptPath.StartsWith("Packages/", StringComparison.Ordinal)))
            {
                return Error("scriptPath must start with 'Assets/' or 'Packages/'.");
            }

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
            {
                return Error("Failed to resolve project root.");
            }

            var fullPath = Path.Combine(projectRoot, scriptPath.Replace('/', Path.DirectorySeparatorChar));
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, contents);
            AssetDatabase.ImportAsset(scriptPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();

            return Success(JToken.FromObject(new
            {
                action,
                scriptPath,
                bytes = contents.Length
            }));
        }

        private static AutonomousMcpToolResponse HandleManageGameObject(JObject args)
        {
            var action = args.Value<string>("action") ?? string.Empty;

            switch (action)
            {
                case "create":
                case "create_empty":
                case "create_primitive":
                    return HandleCreateGameObject(args, action);
                case "find":
                case "find_by_name":
                    return HandleFindGameObject(args, action);
                case "find_contains":
                    return HandleFindContainsGameObject(args, action);
                case "reparent":
                    return HandleReparentGameObject(args, action);
                case "get_children":
                    return HandleGetChildren(args, action);
                case "get_parent":
                    return HandleGetParent(args, action);
                case "get_full_hierarchy":
                    return HandleGetFullHierarchy(action);
                case "set_active":
                    return HandleSetActive(args, action);
                case "rename":
                    return HandleRename(args, action);
                case "destroy":
                    return HandleDestroyGameObject(args, action);
                case "set_transform":
                    return HandleSetGameObjectTransform(args, action);
                case "get_world_transform":
                    return HandleGetWorldTransform(args, action);
                default:
                    return Error($"Unsupported manage_gameobject action '{action}'.");
            }
        }

        private static AutonomousMcpToolResponse HandleCreateGameObject(JObject args, string action)
        {
            var name = args.Value<string>("name") ?? "AgentGameObject";
            var primitiveTypeRaw = args.Value<string>("primitiveType") ?? args.Value<string>("primitive_type");

            GameObject created;
            if (action == "create_empty")
            {
                created = new GameObject(name);
            }
            else if (!string.IsNullOrWhiteSpace(primitiveTypeRaw) && Enum.TryParse(primitiveTypeRaw, true, out PrimitiveType primitiveType))
            {
                created = GameObject.CreatePrimitive(primitiveType);
                created.name = name;
            }
            else if (action == "create_primitive")
            {
                return Error("create_primitive requires a valid primitiveType (Cube, Sphere, Capsule, Cylinder, Plane, Quad).");
            }
            else
            {
                created = new GameObject(name);
            }

            var parentName = args.Value<string>("parent");
            var worldPositionStays = args.Value<bool?>("worldPositionStays") ?? false;
            if (!string.IsNullOrWhiteSpace(parentName))
            {
                var parent = GameObject.Find(parentName);
                if (parent != null)
                {
                    created.transform.SetParent(parent.transform, worldPositionStays);
                }
            }

            created.transform.position = ReadVector3(args["position"], created.transform.position);
            var euler = ReadVector3(args["rotation"], created.transform.eulerAngles);
            created.transform.rotation = Quaternion.Euler(euler);
            created.transform.localScale = ReadVector3(args["scale"], created.transform.localScale);

            return Success(JToken.FromObject(new
            {
                action,
                name = created.name,
                instanceId = created.GetInstanceID(),
                position = ToVectorPayload(created.transform.position),
                rotation = ToVectorPayload(created.transform.eulerAngles),
                scale = ToVectorPayload(created.transform.localScale)
            }));
        }

        private static AutonomousMcpToolResponse HandleFindGameObject(JObject args, string action)
        {
            var name = args.Value<string>("name") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                return Error("find/find_by_name requires a non-empty name.");
            }

            var target = GameObject.Find(name);
            if (target == null)
            {
                return Success(JToken.FromObject(new
                {
                    action,
                    found = false,
                    name
                }));
            }

            return Success(JToken.FromObject(new
            {
                action,
                found = true,
                name = target.name,
                fullPath = GetFullPath(target.transform),
                instanceId = target.GetInstanceID(),
                activeSelf = target.activeSelf,
                position = ToVectorPayload(target.transform.position),
                rotation = ToVectorPayload(target.transform.eulerAngles),
                scale = ToVectorPayload(target.transform.localScale)
            }));
        }

        private static AutonomousMcpToolResponse HandleFindContainsGameObject(JObject args, string action)
        {
            var query = args.Value<string>("query") ?? args.Value<string>("name") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                return Error("find_contains requires a non-empty query.");
            }

            var matches = new JArray();
            foreach (var gameObject in EnumerateSceneGameObjects())
            {
                if (gameObject.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                matches.Add(JToken.FromObject(new
                {
                    name = gameObject.name,
                    fullPath = GetFullPath(gameObject.transform),
                    instanceId = gameObject.GetInstanceID(),
                    activeSelf = gameObject.activeSelf
                }));
            }

            return Success(JToken.FromObject(new
            {
                action,
                query,
                count = matches.Count,
                matches
            }));
        }

        private static AutonomousMcpToolResponse HandleReparentGameObject(JObject args, string action)
        {
            var child = ResolveGameObject(args);
            if (child == null)
            {
                return Error("reparent requires a valid child target by instanceId or name.");
            }

            var parentName = args.Value<string>("parent_name") ?? args.Value<string>("parent") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(parentName))
            {
                return Error("reparent requires parent_name (or parent).");
            }

            var parent = GameObject.Find(parentName);
            if (parent == null)
            {
                return Error($"Parent GameObject '{parentName}' not found.");
            }

            var worldPositionStays = args.Value<bool?>("worldPositionStays") ?? true;
            Undo.SetTransformParent(child.transform, parent.transform, "MCP: Reparent GameObject");
            child.transform.SetParent(parent.transform, worldPositionStays);

            return Success(JToken.FromObject(new
            {
                action,
                child = child.name,
                parent = parent.name,
                worldPositionStays,
                fullPath = GetFullPath(child.transform)
            }));
        }

        private static AutonomousMcpToolResponse HandleGetChildren(JObject args, string action)
        {
            var target = ResolveGameObject(args);
            if (target == null)
            {
                return Error("get_children requires a valid target by instanceId or name.");
            }

            var recursive = args.Value<bool?>("recursive") ?? false;
            var children = new JArray();

            if (recursive)
            {
                AddChildEntriesRecursive(target.transform, children);
            }
            else
            {
                foreach (Transform child in target.transform)
                {
                    children.Add(JToken.FromObject(new
                    {
                        name = child.name,
                        instanceId = child.gameObject.GetInstanceID(),
                        fullPath = GetFullPath(child),
                        localPosition = ToVectorPayload(child.localPosition),
                        localRotation = ToVectorPayload(child.localEulerAngles),
                        localScale = ToVectorPayload(child.localScale)
                    }));
                }
            }

            return Success(JToken.FromObject(new
            {
                action,
                name = target.name,
                recursive,
                count = children.Count,
                children
            }));
        }

        private static AutonomousMcpToolResponse HandleGetParent(JObject args, string action)
        {
            var target = ResolveGameObject(args);
            if (target == null)
            {
                return Error("get_parent requires a valid target by instanceId or name.");
            }

            var parent = target.transform.parent;
            return Success(JToken.FromObject(new
            {
                action,
                name = target.name,
                hasParent = parent != null,
                parent = parent == null
                    ? null
                    : new
                    {
                        name = parent.name,
                        instanceId = parent.gameObject.GetInstanceID(),
                        fullPath = GetFullPath(parent)
                    }
            }));
        }

        private static AutonomousMcpToolResponse HandleGetFullHierarchy(string action)
        {
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var hierarchy = new JArray();
            foreach (var root in roots)
            {
                hierarchy.Add(BuildHierarchyNode(root.transform));
            }

            return Success(JToken.FromObject(new
            {
                action,
                scene = scene.name,
                rootCount = roots.Length,
                hierarchy
            }));
        }

        private static AutonomousMcpToolResponse HandleSetActive(JObject args, string action)
        {
            var target = ResolveGameObject(args);
            if (target == null)
            {
                return Error("set_active requires a valid target by instanceId or name.");
            }

            var active = args.Value<bool?>("active");
            if (!active.HasValue)
            {
                return Error("set_active requires an 'active' boolean parameter.");
            }

            Undo.RecordObject(target, "MCP: Set GameObject Active");
            target.SetActive(active.Value);

            return Success(JToken.FromObject(new
            {
                action,
                name = target.name,
                instanceId = target.GetInstanceID(),
                activeSelf = target.activeSelf
            }));
        }

        private static AutonomousMcpToolResponse HandleRename(JObject args, string action)
        {
            var target = ResolveGameObject(args);
            if (target == null)
            {
                return Error("rename requires a valid target by instanceId or name.");
            }

            var newName = args.Value<string>("new_name") ?? args.Value<string>("newName") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(newName))
            {
                return Error("rename requires new_name (or newName).");
            }

            var oldName = target.name;
            Undo.RecordObject(target, "MCP: Rename GameObject");
            target.name = newName;

            return Success(JToken.FromObject(new
            {
                action,
                oldName,
                newName,
                instanceId = target.GetInstanceID(),
                fullPath = GetFullPath(target.transform)
            }));
        }

        private static AutonomousMcpToolResponse HandleDestroyGameObject(JObject args, string action)
        {
            var target = ResolveGameObject(args);
            if (target == null)
            {
                return Error("destroy requires a valid target by instanceId or name.");
            }

            var targetName = target.name;
            var instanceId = target.GetInstanceID();
            Undo.DestroyObjectImmediate(target);

            return Success(JToken.FromObject(new
            {
                action,
                destroyed = true,
                name = targetName,
                instanceId
            }));
        }

        private static AutonomousMcpToolResponse HandleSetGameObjectTransform(JObject args, string action)
        {
            var target = ResolveGameObject(args);
            if (target == null)
            {
                return Error("set_transform requires a valid target by instanceId or name.");
            }

            var space = (args.Value<string>("space") ?? "world").Trim().ToLowerInvariant();
            Undo.RecordObject(target.transform, "MCP: Set Transform");

            if (space == "local")
            {
                target.transform.localPosition = ReadVector3(args["position"], target.transform.localPosition);
                var localEuler = ReadVector3(args["rotation"], target.transform.localEulerAngles);
                target.transform.localRotation = Quaternion.Euler(localEuler);
            }
            else
            {
                target.transform.position = ReadVector3(args["position"], target.transform.position);
                var worldEuler = ReadVector3(args["rotation"], target.transform.eulerAngles);
                target.transform.rotation = Quaternion.Euler(worldEuler);
            }

            target.transform.localScale = ReadVector3(args["scale"], target.transform.localScale);

            return Success(JToken.FromObject(new
            {
                action,
                space,
                name = target.name,
                instanceId = target.GetInstanceID(),
                position = ToVectorPayload(target.transform.position),
                rotation = ToVectorPayload(target.transform.eulerAngles),
                scale = ToVectorPayload(target.transform.localScale)
            }));
        }

        private static AutonomousMcpToolResponse HandleGetWorldTransform(JObject args, string action)
        {
            var target = ResolveGameObject(args);
            if (target == null)
            {
                return Error("get_world_transform requires a valid target by instanceId or name.");
            }

            return Success(JToken.FromObject(new
            {
                action,
                name = target.name,
                instanceId = target.GetInstanceID(),
                position = ToVectorPayload(target.transform.position),
                rotation = ToVectorPayload(target.transform.eulerAngles),
                lossyScale = ToVectorPayload(target.transform.lossyScale),
                fullPath = GetFullPath(target.transform)
            }));
        }

        private static GameObject ResolveGameObject(JObject args)
        {
            var instanceId = args.Value<int?>("instanceId");
            if (instanceId.HasValue)
            {
                var byId = EditorUtility.InstanceIDToObject(instanceId.Value) as GameObject;
                if (byId != null)
                {
                    return byId;
                }
            }

            var name = args.Value<string>("name") ?? args.Value<string>("target") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return GameObject.Find(name);
            }

            return null;
        }

        private static Vector3 ReadVector3(JToken token, Vector3 fallback)
        {
            if (token == null)
            {
                return fallback;
            }

            if (token is JObject obj)
            {
                var x = obj.Value<float?>("x") ?? fallback.x;
                var y = obj.Value<float?>("y") ?? fallback.y;
                var z = obj.Value<float?>("z") ?? fallback.z;
                return new Vector3(x, y, z);
            }

            if (token is JArray arr && arr.Count >= 3)
            {
                var x = arr[0]?.Value<float?>() ?? fallback.x;
                var y = arr[1]?.Value<float?>() ?? fallback.y;
                var z = arr[2]?.Value<float?>() ?? fallback.z;
                return new Vector3(x, y, z);
            }

            return fallback;
        }

        private static object ToVectorPayload(Vector3 vector)
        {
            return new
            {
                x = vector.x,
                y = vector.y,
                z = vector.z
            };
        }

        private static IEnumerable<GameObject> EnumerateSceneGameObjects()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                yield break;
            }

            var roots = scene.GetRootGameObjects();
            foreach (var root in roots)
            {
                yield return root;

                foreach (Transform descendant in root.transform.GetComponentsInChildren<Transform>(true))
                {
                    if (descendant == root.transform)
                    {
                        continue;
                    }

                    yield return descendant.gameObject;
                }
            }
        }

        private static string GetFullPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            var segments = new List<string>();
            var current = transform;
            while (current != null)
            {
                segments.Add(current.name);
                current = current.parent;
            }

            segments.Reverse();
            return string.Join("/", segments);
        }

        private static JToken BuildHierarchyNode(Transform transform)
        {
            var children = new JArray();
            foreach (Transform child in transform)
            {
                children.Add(BuildHierarchyNode(child));
            }

            return JToken.FromObject(new
            {
                name = transform.name,
                instanceId = transform.gameObject.GetInstanceID(),
                fullPath = GetFullPath(transform),
                activeSelf = transform.gameObject.activeSelf,
                activeInHierarchy = transform.gameObject.activeInHierarchy,
                children
            });
        }

        private static void AddChildEntriesRecursive(Transform parent, JArray children)
        {
            foreach (Transform child in parent)
            {
                children.Add(JToken.FromObject(new
                {
                    name = child.name,
                    instanceId = child.gameObject.GetInstanceID(),
                    fullPath = GetFullPath(child),
                    localPosition = ToVectorPayload(child.localPosition),
                    localRotation = ToVectorPayload(child.localEulerAngles),
                    localScale = ToVectorPayload(child.localScale)
                }));

                AddChildEntriesRecursive(child, children);
            }
        }

        // ───────── manage_component ─────────

        private static AutonomousMcpToolResponse HandleManageComponent(JObject args)
        {
            var action = args.Value<string>("action") ?? string.Empty;

            switch (action)
            {
                case "add":
                    return HandleAddComponent(args);
                case "remove":
                    return HandleRemoveComponent(args);
                case "get_all":
                    return HandleGetAllComponents(args);
                case "get_properties":
                    return HandleGetComponentProperties(args);
                case "set_property":
                    return HandleSetComponentProperty(args);
                default:
                    return Error($"Unsupported manage_component action '{action}'.");
            }
        }

        private static AutonomousMcpToolResponse HandleAddComponent(JObject args)
        {
            var target = ResolveGameObject(args);
            if (target == null)
                return Error("add requires a valid target by instanceId or name.");

            var typeName = args.Value<string>("component_type") ?? args.Value<string>("componentType") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(typeName))
                return Error("add requires a non-empty component_type.");

            var type = ResolveType(typeName);
            if (type == null)
                return Error($"Type '{typeName}' not found. Use fully-qualified name if ambiguous.");

            if (target.GetComponent(type) != null)
            {
                return Success(JToken.FromObject(new
                {
                    action = "add",
                    name = target.name,
                    componentType = type.FullName,
                    alreadyPresent = true
                }));
            }

            Undo.AddComponent(target, type);

            return Success(JToken.FromObject(new
            {
                action = "add",
                name = target.name,
                instanceId = target.GetInstanceID(),
                componentType = type.FullName,
                added = true
            }));
        }

        private static AutonomousMcpToolResponse HandleRemoveComponent(JObject args)
        {
            var target = ResolveGameObject(args);
            if (target == null)
                return Error("remove requires a valid target by instanceId or name.");

            var typeName = args.Value<string>("component_type") ?? args.Value<string>("componentType") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(typeName))
                return Error("remove requires a non-empty component_type.");

            var type = ResolveType(typeName);
            if (type == null)
                return Error($"Type '{typeName}' not found.");

            var component = target.GetComponent(type);
            if (component == null)
                return Error($"Component '{typeName}' not found on '{target.name}'.");

            Undo.DestroyObjectImmediate(component);

            return Success(JToken.FromObject(new
            {
                action = "remove",
                name = target.name,
                componentType = type.FullName,
                removed = true
            }));
        }

        private static AutonomousMcpToolResponse HandleGetAllComponents(JObject args)
        {
            var target = ResolveGameObject(args);
            if (target == null)
                return Error("get_all requires a valid target by instanceId or name.");

            var components = target.GetComponents<Component>();
            var list = new JArray();

            foreach (var component in components)
            {
                if (component == null)
                {
                    list.Add(JToken.FromObject(new { type = "(missing script)", instanceId = 0 }));
                    continue;
                }

                list.Add(JToken.FromObject(new
                {
                    type = component.GetType().FullName,
                    instanceId = component.GetInstanceID(),
                    enabled = (component is Behaviour b) ? (bool?)b.enabled : null
                }));
            }

            return Success(JToken.FromObject(new
            {
                action = "get_all",
                name = target.name,
                count = list.Count,
                components = list
            }));
        }

        private static AutonomousMcpToolResponse HandleGetComponentProperties(JObject args)
        {
            var target = ResolveGameObject(args);
            if (target == null)
                return Error("get_properties requires a valid target by instanceId or name.");

            var typeName = args.Value<string>("component_type") ?? args.Value<string>("componentType") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(typeName))
                return Error("get_properties requires a non-empty component_type.");

            var type = ResolveType(typeName);
            if (type == null)
                return Error($"Type '{typeName}' not found.");

            var component = target.GetComponent(type);
            if (component == null)
                return Error($"Component '{typeName}' not found on '{target.name}'.");

            var so = new SerializedObject(component);
            var props = new JArray();
            var iterator = so.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                props.Add(JToken.FromObject(new
                {
                    name = iterator.name,
                    displayName = iterator.displayName,
                    type = iterator.propertyType.ToString(),
                    value = ReadSerializedPropertyValue(iterator),
                    editable = iterator.editable
                }));
            }

            so.Dispose();

            return Success(JToken.FromObject(new
            {
                action = "get_properties",
                gameObject = target.name,
                componentType = type.FullName,
                count = props.Count,
                properties = props
            }));
        }

        private static AutonomousMcpToolResponse HandleSetComponentProperty(JObject args)
        {
            var target = ResolveGameObject(args);
            if (target == null)
                return Error("set_property requires a valid target by instanceId or name.");

            var typeName = args.Value<string>("component_type") ?? args.Value<string>("componentType") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(typeName))
                return Error("set_property requires a non-empty component_type.");

            var type = ResolveType(typeName);
            if (type == null)
                return Error($"Type '{typeName}' not found.");

            var component = target.GetComponent(type);
            if (component == null)
                return Error($"Component '{typeName}' not found on '{target.name}'.");

            var propertyName = args.Value<string>("property") ?? args.Value<string>("property_name") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(propertyName))
                return Error("set_property requires a non-empty property name.");

            var so = new SerializedObject(component);
            var prop = so.FindProperty(propertyName);
            if (prop == null)
            {
                so.Dispose();
                return Error($"Property '{propertyName}' not found on '{typeName}'.");
            }

            var valueToken = args["value"];
            if (valueToken == null)
            {
                so.Dispose();
                return Error("set_property requires a 'value' parameter.");
            }

            bool written = WriteSerializedPropertyValue(prop, valueToken);
            if (!written)
            {
                so.Dispose();
                return Error($"Could not write value to property '{propertyName}' (type: {prop.propertyType}).");
            }

            so.ApplyModifiedProperties();
            so.Dispose();

            return Success(JToken.FromObject(new
            {
                action = "set_property",
                gameObject = target.name,
                componentType = type.FullName,
                property = propertyName,
                written = true
            }));
        }

        private static object ReadSerializedPropertyValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer: return prop.intValue;
                case SerializedPropertyType.Boolean: return prop.boolValue;
                case SerializedPropertyType.Float: return prop.floatValue;
                case SerializedPropertyType.String: return prop.stringValue;
                case SerializedPropertyType.Enum: return prop.enumNames.Length > prop.enumValueIndex && prop.enumValueIndex >= 0 ? prop.enumNames[prop.enumValueIndex] : prop.enumValueIndex.ToString();
                case SerializedPropertyType.Color:
                    var c = prop.colorValue;
                    return new { r = c.r, g = c.g, b = c.b, a = c.a };
                case SerializedPropertyType.Vector2:
                    var v2 = prop.vector2Value;
                    return new { x = v2.x, y = v2.y };
                case SerializedPropertyType.Vector3:
                    var v3 = prop.vector3Value;
                    return new { x = v3.x, y = v3.y, z = v3.z };
                case SerializedPropertyType.Vector4:
                    var v4 = prop.vector4Value;
                    return new { x = v4.x, y = v4.y, z = v4.z, w = v4.w };
                case SerializedPropertyType.ObjectReference:
                    var obj = prop.objectReferenceValue;
                    return obj != null ? new { name = obj.name, type = obj.GetType().Name, instanceId = obj.GetInstanceID() } : (object)null;
                case SerializedPropertyType.LayerMask: return prop.intValue;
                case SerializedPropertyType.ArraySize: return prop.intValue;
                default: return $"({prop.propertyType})";
            }
        }

        private static bool WriteSerializedPropertyValue(SerializedProperty prop, JToken valueToken)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    prop.intValue = valueToken.Value<int>();
                    return true;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = valueToken.Value<bool>();
                    return true;
                case SerializedPropertyType.Float:
                    prop.floatValue = valueToken.Value<float>();
                    return true;
                case SerializedPropertyType.String:
                    prop.stringValue = valueToken.Value<string>();
                    return true;
                case SerializedPropertyType.Enum:
                    if (valueToken.Type == JTokenType.Integer)
                    {
                        prop.enumValueIndex = valueToken.Value<int>();
                    }
                    else
                    {
                        var enumStr = valueToken.Value<string>() ?? string.Empty;
                        int idx = Array.IndexOf(prop.enumNames, enumStr);
                        if (idx < 0) return false;
                        prop.enumValueIndex = idx;
                    }
                    return true;
                case SerializedPropertyType.Color:
                    if (valueToken is JObject cObj)
                    {
                        prop.colorValue = new Color(
                            cObj.Value<float?>("r") ?? 0, cObj.Value<float?>("g") ?? 0,
                            cObj.Value<float?>("b") ?? 0, cObj.Value<float?>("a") ?? 1);
                        return true;
                    }
                    return false;
                case SerializedPropertyType.Vector2:
                    if (valueToken is JObject v2Obj)
                    {
                        prop.vector2Value = new Vector2(
                            v2Obj.Value<float?>("x") ?? 0, v2Obj.Value<float?>("y") ?? 0);
                        return true;
                    }
                    return false;
                case SerializedPropertyType.Vector3:
                    if (valueToken is JObject v3Obj)
                    {
                        prop.vector3Value = new Vector3(
                            v3Obj.Value<float?>("x") ?? 0, v3Obj.Value<float?>("y") ?? 0,
                            v3Obj.Value<float?>("z") ?? 0);
                        return true;
                    }
                    return false;
                case SerializedPropertyType.Vector4:
                    if (valueToken is JObject v4Obj)
                    {
                        prop.vector4Value = new Vector4(
                            v4Obj.Value<float?>("x") ?? 0, v4Obj.Value<float?>("y") ?? 0,
                            v4Obj.Value<float?>("z") ?? 0, v4Obj.Value<float?>("w") ?? 0);
                        return true;
                    }
                    return false;
                case SerializedPropertyType.ObjectReference:
                    var instanceId = valueToken.Value<int?>();
                    if (instanceId.HasValue)
                    {
                        prop.objectReferenceValue = EditorUtility.InstanceIDToObject(instanceId.Value);
                        return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        private static Type ResolveType(string typeName)
        {
            // Try direct lookup first
            var type = Type.GetType(typeName);
            if (type != null) return type;

            // Search all loaded assemblies
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(typeName);
                if (type != null) return type;
            }

            // Fuzzy: search by short name (last segment)
            var shortName = typeName.Contains('.') ? typeName : typeName;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = asm.GetTypes().FirstOrDefault(t =>
                        string.Equals(t.Name, shortName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(t.FullName, typeName, StringComparison.OrdinalIgnoreCase));
                    if (type != null) return type;
                }
                catch { /* skip assemblies that throw on GetTypes() */ }
            }

            return null;
        }

        // ───────── execute_menu_item ─────────

        private static AutonomousMcpToolResponse HandleExecuteMenuItem(JObject args)
        {
            var menuPath = args.Value<string>("menu_path") ?? args.Value<string>("menuPath") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(menuPath))
                return Error("execute_menu_item requires a non-empty menu_path (e.g. 'Tools/My Tool').");

            var result = EditorApplication.ExecuteMenuItem(menuPath);

            return Success(JToken.FromObject(new
            {
                menuPath,
                executed = result
            }));
        }

        // ───────── manage_asset ─────────

        private static AutonomousMcpToolResponse HandleManageAsset(JObject args)
        {
            var action = args.Value<string>("action") ?? string.Empty;

            switch (action)
            {
                case "find":
                    return HandleFindAssets(args);
                case "instantiate_prefab":
                    return HandleInstantiatePrefab(args);
                default:
                    return Error($"Unsupported manage_asset action '{action}'.");
            }
        }

        private static AutonomousMcpToolResponse HandleFindAssets(JObject args)
        {
            var filter = args.Value<string>("filter") ?? string.Empty;
            var searchFolder = args.Value<string>("folder") ?? args.Value<string>("search_folder") ?? "Assets";
            var limit = Math.Max(1, Math.Min(args.Value<int?>("limit") ?? 50, 200));

            string[] guids;
            if (!string.IsNullOrWhiteSpace(searchFolder))
                guids = AssetDatabase.FindAssets(filter, new[] { searchFolder });
            else
                guids = AssetDatabase.FindAssets(filter);

            var results = new JArray();
            int count = 0;
            foreach (var guid in guids)
            {
                if (count >= limit) break;
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var assetType = AssetDatabase.GetMainAssetTypeAtPath(path);
                results.Add(JToken.FromObject(new
                {
                    guid,
                    path,
                    type = assetType?.Name ?? "Unknown"
                }));
                count++;
            }

            return Success(JToken.FromObject(new
            {
                action = "find",
                filter,
                folder = searchFolder,
                totalFound = guids.Length,
                returned = results.Count,
                assets = results
            }));
        }

        private static AutonomousMcpToolResponse HandleInstantiatePrefab(JObject args)
        {
            var assetPath = args.Value<string>("asset_path") ?? args.Value<string>("assetPath") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(assetPath))
                return Error("instantiate_prefab requires a non-empty asset_path.");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
                return Error($"No prefab found at '{assetPath}'.");

            Transform parentTransform = null;
            var parentName = args.Value<string>("parent") ?? args.Value<string>("parent_name");
            if (!string.IsNullOrWhiteSpace(parentName))
            {
                var parentGo = GameObject.Find(parentName);
                if (parentGo != null) parentTransform = parentGo.transform;
            }

            var instance = parentTransform != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, parentTransform)
                : (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            instance.transform.localPosition = ReadVector3(args["position"], Vector3.zero);
            var euler = ReadVector3(args["rotation"], Vector3.zero);
            instance.transform.localRotation = Quaternion.Euler(euler);
            instance.transform.localScale = ReadVector3(args["scale"], Vector3.one);

            Undo.RegisterCreatedObjectUndo(instance, "MCP: Instantiate Prefab");

            return Success(JToken.FromObject(new
            {
                action = "instantiate_prefab",
                assetPath,
                name = instance.name,
                instanceId = instance.GetInstanceID(),
                fullPath = GetFullPath(instance.transform)
            }));
        }

        // ───────── manage_editor ─────────

        private static AutonomousMcpToolResponse HandleManageEditor(JObject args)
        {
            var action = args.Value<string>("action") ?? string.Empty;

            switch (action)
            {
                case "enter_play_mode":
                    EditorApplication.isPlaying = true;
                    return Success(JToken.FromObject(new { action, isPlaying = true }));

                case "exit_play_mode":
                    EditorApplication.isPlaying = false;
                    return Success(JToken.FromObject(new { action, isPlaying = false }));

                case "pause":
                    EditorApplication.isPaused = !EditorApplication.isPaused;
                    return Success(JToken.FromObject(new { action, isPaused = EditorApplication.isPaused }));

                case "step":
                    EditorApplication.Step();
                    return Success(JToken.FromObject(new { action, stepped = true }));

                case "undo":
                    Undo.PerformUndo();
                    return Success(JToken.FromObject(new { action, performed = true }));

                case "redo":
                    Undo.PerformRedo();
                    return Success(JToken.FromObject(new { action, performed = true }));

                default:
                    return Error($"Unsupported manage_editor action '{action}'.");
            }
        }

        // ───────── read_script ─────────

        private static AutonomousMcpToolResponse HandleReadScript(JObject args)
        {
            var scriptPath = args.Value<string>("scriptPath") ?? args.Value<string>("script_path") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(scriptPath) ||
                !(scriptPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                  scriptPath.StartsWith("Packages/", StringComparison.Ordinal)))
                return Error("read_script requires scriptPath starting with 'Assets/' or 'Packages/'.");

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
                return Error("Failed to resolve project root.");

            var fullPath = Path.Combine(projectRoot, scriptPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                return Error($"File not found: '{scriptPath}'.");

            var contents = File.ReadAllText(fullPath);
            var lineCount = contents.Split('\n').Length;

            return Success(JToken.FromObject(new
            {
                scriptPath,
                lineCount,
                sizeBytes = contents.Length,
                contents
            }));
        }

        // ───────── capture_screenshot ─────────

        private static AutonomousMcpToolResponse HandleCaptureScreenshot(JObject args)
        {
            var source = (args.Value<string>("source") ?? "scene").Trim().ToLowerInvariant();
            var width = args.Value<int?>("width") ?? 512;
            var height = args.Value<int?>("height") ?? 512;
            width = Math.Clamp(width, 64, 2048);
            height = Math.Clamp(height, 64, 2048);

            Texture2D tex = null;

            try
            {
                if (source == "game")
                {
                    var gameView = EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"), false, null, false);
                    if (gameView != null)
                    {
                        gameView.Repaint();
                        var rt = new RenderTexture(width, height, 24);
                        var cam = Camera.main;
                        if (cam == null)
                        {
                            var allCams = Camera.allCameras;
                            if (allCams.Length > 0) cam = allCams[0];
                        }

                        if (cam != null)
                        {
                            var prevRT = cam.targetTexture;
                            cam.targetTexture = rt;
                            cam.Render();
                            cam.targetTexture = prevRT;

                            RenderTexture.active = rt;
                            tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                            tex.Apply();
                            RenderTexture.active = null;
                            UnityEngine.Object.DestroyImmediate(rt);
                        }
                        else
                        {
                            UnityEngine.Object.DestroyImmediate(rt);
                            return Error("No camera found for game view capture.");
                        }
                    }
                    else
                    {
                        return Error("Game view not available.");
                    }
                }
                else
                {
                    var sceneView = SceneView.lastActiveSceneView;
                    if (sceneView == null)
                        return Error("No active SceneView found.");

                    var cam = sceneView.camera;
                    if (cam == null)
                        return Error("SceneView camera not available.");

                    var rt = new RenderTexture(width, height, 24);
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = null;

                    RenderTexture.active = rt;
                    tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    UnityEngine.Object.DestroyImmediate(rt);
                }

                if (tex == null)
                    return Error("Failed to capture screenshot.");

                var png = tex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(tex);
                var base64 = Convert.ToBase64String(png);

                // Optionally save to file
                var savePath = args.Value<string>("save_path") ?? args.Value<string>("savePath");
                if (!string.IsNullOrWhiteSpace(savePath))
                {
                    var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? "";
                    var fullSavePath = savePath.StartsWith("Assets/", StringComparison.Ordinal)
                        ? Path.Combine(projectRoot, savePath.Replace('/', Path.DirectorySeparatorChar))
                        : savePath;
                    var dir = Path.GetDirectoryName(fullSavePath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllBytes(fullSavePath, png);
                }

                return Success(JToken.FromObject(new
                {
                    source,
                    width,
                    height,
                    sizeBytes = png.Length,
                    savedTo = string.IsNullOrWhiteSpace(savePath) ? null : savePath,
                    base64png = base64
                }));
            }
            catch (Exception ex)
            {
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                return Error($"Screenshot capture failed: {ex.Message}");
            }
        }

        // ───────── manage_animator ─────────

        private static AutonomousMcpToolResponse HandleManageAnimator(JObject args)
        {
            var action = args.Value<string>("action") ?? string.Empty;

            switch (action)
            {
                case "get_parameters":
                    return HandleGetAnimatorParameters(args);
                case "set_parameter":
                    return HandleSetAnimatorParameter(args);
                case "get_layers":
                    return HandleGetAnimatorLayers(args);
                case "get_states":
                    return HandleGetAnimatorStates(args);
                case "get_current_state":
                    return HandleGetCurrentAnimatorState(args);
                default:
                    return Error($"Unsupported manage_animator action '{action}'.");
            }
        }

        private static Animator ResolveAnimator(JObject args)
        {
            var target = ResolveGameObject(args);
            if (target == null) return null;
            return target.GetComponent<Animator>() ?? target.GetComponentInChildren<Animator>();
        }

        private static UnityEditor.Animations.AnimatorController ResolveAnimatorController(Animator animator)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return null;
            // Handle AnimatorOverrideController
            var rac = animator.runtimeAnimatorController;
            if (rac is AnimatorOverrideController aoc)
                rac = aoc.runtimeAnimatorController;
            return rac as UnityEditor.Animations.AnimatorController;
        }

        private static AutonomousMcpToolResponse HandleGetAnimatorParameters(JObject args)
        {
            var animator = ResolveAnimator(args);
            if (animator == null)
                return Error("No Animator found on the target GameObject.");

            var controller = ResolveAnimatorController(animator);
            if (controller == null)
                return Error("No AnimatorController assigned to the Animator.");

            var paramList = new JArray();
            foreach (var param in controller.parameters)
            {
                object currentValue = null;
                if (EditorApplication.isPlaying)
                {
                    switch (param.type)
                    {
                        case AnimatorControllerParameterType.Float: currentValue = animator.GetFloat(param.name); break;
                        case AnimatorControllerParameterType.Int: currentValue = animator.GetInteger(param.name); break;
                        case AnimatorControllerParameterType.Bool: currentValue = animator.GetBool(param.name); break;
                        case AnimatorControllerParameterType.Trigger: currentValue = "(trigger)"; break;
                    }
                }
                else
                {
                    switch (param.type)
                    {
                        case AnimatorControllerParameterType.Float: currentValue = param.defaultFloat; break;
                        case AnimatorControllerParameterType.Int: currentValue = param.defaultInt; break;
                        case AnimatorControllerParameterType.Bool: currentValue = param.defaultBool; break;
                        case AnimatorControllerParameterType.Trigger: currentValue = "(trigger)"; break;
                    }
                }

                paramList.Add(JToken.FromObject(new
                {
                    name = param.name,
                    type = param.type.ToString(),
                    value = currentValue
                }));
            }

            return Success(JToken.FromObject(new
            {
                action = "get_parameters",
                gameObject = (ResolveGameObject(args))?.name,
                controllerName = controller.name,
                count = paramList.Count,
                parameters = paramList
            }));
        }

        private static AutonomousMcpToolResponse HandleSetAnimatorParameter(JObject args)
        {
            var animator = ResolveAnimator(args);
            if (animator == null)
                return Error("No Animator found on the target GameObject.");

            var paramName = args.Value<string>("parameter") ?? args.Value<string>("param_name") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(paramName))
                return Error("set_parameter requires a non-empty parameter name.");

            var controller = ResolveAnimatorController(animator);
            if (controller == null)
                return Error("No AnimatorController assigned.");

            AnimatorControllerParameter targetParam = null;
            foreach (var p in controller.parameters)
            {
                if (string.Equals(p.name, paramName, StringComparison.Ordinal))
                {
                    targetParam = p;
                    break;
                }
            }

            if (targetParam == null)
                return Error($"Parameter '{paramName}' not found on animator controller.");

            var valueToken = args["value"];

            if (EditorApplication.isPlaying)
            {
                switch (targetParam.type)
                {
                    case AnimatorControllerParameterType.Float:
                        animator.SetFloat(paramName, valueToken?.Value<float>() ?? 0f);
                        break;
                    case AnimatorControllerParameterType.Int:
                        animator.SetInteger(paramName, valueToken?.Value<int>() ?? 0);
                        break;
                    case AnimatorControllerParameterType.Bool:
                        animator.SetBool(paramName, valueToken?.Value<bool>() ?? false);
                        break;
                    case AnimatorControllerParameterType.Trigger:
                        animator.SetTrigger(paramName);
                        break;
                }
            }
            else
            {
                // Edit mode: modify the default value on the controller asset
                switch (targetParam.type)
                {
                    case AnimatorControllerParameterType.Float:
                        targetParam.defaultFloat = valueToken?.Value<float>() ?? 0f;
                        break;
                    case AnimatorControllerParameterType.Int:
                        targetParam.defaultInt = valueToken?.Value<int>() ?? 0;
                        break;
                    case AnimatorControllerParameterType.Bool:
                        targetParam.defaultBool = valueToken?.Value<bool>() ?? false;
                        break;
                }
                EditorUtility.SetDirty(controller);
            }

            return Success(JToken.FromObject(new
            {
                action = "set_parameter",
                parameter = paramName,
                type = targetParam.type.ToString(),
                isPlaying = EditorApplication.isPlaying
            }));
        }

        private static AutonomousMcpToolResponse HandleGetAnimatorLayers(JObject args)
        {
            var animator = ResolveAnimator(args);
            if (animator == null)
                return Error("No Animator found on the target GameObject.");

            var controller = ResolveAnimatorController(animator);
            if (controller == null)
                return Error("No AnimatorController assigned.");

            var layerList = new JArray();
            for (int i = 0; i < controller.layers.Length; i++)
            {
                var layer = controller.layers[i];
                layerList.Add(JToken.FromObject(new
                {
                    index = i,
                    name = layer.name,
                    defaultWeight = layer.defaultWeight,
                    blendingMode = layer.blendingMode.ToString(),
                    stateCount = layer.stateMachine.states.Length
                }));
            }

            return Success(JToken.FromObject(new
            {
                action = "get_layers",
                controllerName = controller.name,
                count = layerList.Count,
                layers = layerList
            }));
        }

        private static AutonomousMcpToolResponse HandleGetAnimatorStates(JObject args)
        {
            var animator = ResolveAnimator(args);
            if (animator == null)
                return Error("No Animator found on the target GameObject.");

            var controller = ResolveAnimatorController(animator);
            if (controller == null)
                return Error("No AnimatorController assigned.");

            var layerIndex = args.Value<int?>("layer_index") ?? args.Value<int?>("layerIndex") ?? 0;
            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
                return Error($"Layer index {layerIndex} out of range (0..{controller.layers.Length - 1}).");

            var sm = controller.layers[layerIndex].stateMachine;
            var stateList = new JArray();

            foreach (var childState in sm.states)
            {
                var state = childState.state;
                var transitions = new JArray();
                foreach (var t in state.transitions)
                {
                    transitions.Add(JToken.FromObject(new
                    {
                        destination = t.destinationState?.name ?? "(exit)",
                        hasExitTime = t.hasExitTime,
                        duration = t.duration,
                        conditionCount = t.conditions.Length
                    }));
                }

                stateList.Add(JToken.FromObject(new
                {
                    name = state.name,
                    tag = state.tag,
                    speed = state.speed,
                    motion = state.motion?.name,
                    transitionCount = state.transitions.Length,
                    transitions
                }));
            }

            return Success(JToken.FromObject(new
            {
                action = "get_states",
                controllerName = controller.name,
                layerIndex,
                layerName = controller.layers[layerIndex].name,
                count = stateList.Count,
                states = stateList
            }));
        }

        private static AutonomousMcpToolResponse HandleGetCurrentAnimatorState(JObject args)
        {
            if (!EditorApplication.isPlaying)
                return Error("get_current_state requires Play mode.");

            var animator = ResolveAnimator(args);
            if (animator == null)
                return Error("No Animator found on the target GameObject.");

            var layerIndex = args.Value<int?>("layer_index") ?? args.Value<int?>("layerIndex") ?? 0;
            var stateInfo = animator.GetCurrentAnimatorStateInfo(layerIndex);

            return Success(JToken.FromObject(new
            {
                action = "get_current_state",
                layerIndex,
                normalizedTime = stateInfo.normalizedTime,
                length = stateInfo.length,
                speed = stateInfo.speed,
                isLooping = stateInfo.loop,
                tagHash = stateInfo.tagHash
            }));
        }

        // ───────── manage_material ─────────

        private static AutonomousMcpToolResponse HandleManageMaterial(JObject args)
        {
            var action = args.Value<string>("action") ?? string.Empty;

            switch (action)
            {
                case "get":
                    return HandleGetMaterial(args);
                case "get_properties":
                    return HandleGetMaterialProperties(args);
                case "set_property":
                    return HandleSetMaterialProperty(args);
                case "list_materials":
                    return HandleListMaterials(args);
                default:
                    return Error($"Unsupported manage_material action '{action}'.");
            }
        }

        private static AutonomousMcpToolResponse HandleGetMaterial(JObject args)
        {
            var target = ResolveGameObject(args);
            if (target == null)
                return Error("get requires a valid target by instanceId or name.");

            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
                return Error($"No Renderer found on '{target.name}'.");

            var materialIndex = args.Value<int?>("material_index") ?? args.Value<int?>("materialIndex") ?? 0;
            var materials = renderer.sharedMaterials;
            if (materialIndex < 0 || materialIndex >= materials.Length)
                return Error($"Material index {materialIndex} out of range (0..{materials.Length - 1}).");

            var mat = materials[materialIndex];
            if (mat == null)
                return Error("Material at that index is null.");

            return Success(JToken.FromObject(new
            {
                action = "get",
                gameObject = target.name,
                materialIndex,
                materialName = mat.name,
                shader = mat.shader?.name,
                renderQueue = mat.renderQueue,
                passCount = mat.passCount,
                instanceId = mat.GetInstanceID()
            }));
        }

        private static AutonomousMcpToolResponse HandleGetMaterialProperties(JObject args)
        {
            var target = ResolveGameObject(args);
            if (target == null)
                return Error("get_properties requires a valid target by instanceId or name.");

            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
                return Error($"No Renderer found on '{target.name}'.");

            var materialIndex = args.Value<int?>("material_index") ?? args.Value<int?>("materialIndex") ?? 0;
            var materials = renderer.sharedMaterials;
            if (materialIndex < 0 || materialIndex >= materials.Length)
                return Error($"Material index {materialIndex} out of range.");

            var mat = materials[materialIndex];
            if (mat == null) return Error("Material is null.");

            var shader = mat.shader;
            var propList = new JArray();
            int propCount = shader.GetPropertyCount();

            for (int i = 0; i < propCount; i++)
            {
                var propName = shader.GetPropertyName(i);
                var propType = shader.GetPropertyType(i);
                object value = null;

                switch (propType)
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                        var c = mat.GetColor(propName);
                        value = new { r = c.r, g = c.g, b = c.b, a = c.a };
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                        value = mat.GetFloat(propName);
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Vector:
                        var v = mat.GetVector(propName);
                        value = new { x = v.x, y = v.y, z = v.z, w = v.w };
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
                        var tex = mat.GetTexture(propName);
                        value = tex != null ? tex.name : null;
                        break;
#if UNITY_2021_1_OR_NEWER
                    case UnityEngine.Rendering.ShaderPropertyType.Int:
                        value = mat.GetInteger(propName);
                        break;
#endif
                }

                propList.Add(JToken.FromObject(new
                {
                    name = propName,
                    displayName = shader.GetPropertyDescription(i),
                    type = propType.ToString(),
                    value
                }));
            }

            return Success(JToken.FromObject(new
            {
                action = "get_properties",
                gameObject = target.name,
                materialName = mat.name,
                shader = shader.name,
                count = propList.Count,
                properties = propList
            }));
        }

        private static AutonomousMcpToolResponse HandleSetMaterialProperty(JObject args)
        {
            var target = ResolveGameObject(args);
            if (target == null)
                return Error("set_property requires a valid target by instanceId or name.");

            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
                return Error($"No Renderer found on '{target.name}'.");

            var materialIndex = args.Value<int?>("material_index") ?? args.Value<int?>("materialIndex") ?? 0;
            var materials = renderer.sharedMaterials;
            if (materialIndex < 0 || materialIndex >= materials.Length)
                return Error($"Material index {materialIndex} out of range.");

            var mat = materials[materialIndex];
            if (mat == null) return Error("Material is null.");

            var propName = args.Value<string>("property") ?? args.Value<string>("property_name") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(propName))
                return Error("set_property requires a non-empty property name.");

            if (!mat.HasProperty(propName))
                return Error($"Material '{mat.name}' has no property '{propName}'.");

            var valueToken = args["value"];
            if (valueToken == null)
                return Error("set_property requires a 'value' parameter.");

            Undo.RecordObject(mat, "MCP: Set Material Property");

            var shader = mat.shader;
            int propIdx = shader.FindPropertyIndex(propName);
            if (propIdx < 0)
                return Error($"Property '{propName}' not found on shader.");

            var propType = shader.GetPropertyType(propIdx);
            bool written = false;

            switch (propType)
            {
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                    if (valueToken is JObject cObj)
                    {
                        mat.SetColor(propName, new Color(
                            cObj.Value<float?>("r") ?? 0, cObj.Value<float?>("g") ?? 0,
                            cObj.Value<float?>("b") ?? 0, cObj.Value<float?>("a") ?? 1));
                        written = true;
                    }
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Float:
                case UnityEngine.Rendering.ShaderPropertyType.Range:
                    mat.SetFloat(propName, valueToken.Value<float>());
                    written = true;
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    if (valueToken is JObject vObj)
                    {
                        mat.SetVector(propName, new Vector4(
                            vObj.Value<float?>("x") ?? 0, vObj.Value<float?>("y") ?? 0,
                            vObj.Value<float?>("z") ?? 0, vObj.Value<float?>("w") ?? 0));
                        written = true;
                    }
                    break;
#if UNITY_2021_1_OR_NEWER
                case UnityEngine.Rendering.ShaderPropertyType.Int:
                    mat.SetInteger(propName, valueToken.Value<int>());
                    written = true;
                    break;
#endif
            }

            if (!written)
                return Error($"Could not write to property '{propName}' (type: {propType}).");

            EditorUtility.SetDirty(mat);

            return Success(JToken.FromObject(new
            {
                action = "set_property",
                gameObject = target.name,
                materialName = mat.name,
                property = propName,
                written = true
            }));
        }

        private static AutonomousMcpToolResponse HandleListMaterials(JObject args)
        {
            var target = ResolveGameObject(args);
            if (target == null)
                return Error("list_materials requires a valid target by instanceId or name.");

            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
                return Error($"No Renderer found on '{target.name}'.");

            var matList = new JArray();
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                var m = materials[i];
                matList.Add(JToken.FromObject(new
                {
                    index = i,
                    name = m?.name ?? "(null)",
                    shader = m?.shader?.name ?? "(null)",
                    instanceId = m?.GetInstanceID() ?? 0
                }));
            }

            return Success(JToken.FromObject(new
            {
                action = "list_materials",
                gameObject = target.name,
                count = matList.Count,
                materials = matList
            }));
        }

        // ───────── execute_csharp ─────────

        private static AutonomousMcpToolResponse HandleExecuteCSharp(JObject args)
        {
            var code = args.Value<string>("code") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(code))
                return Error("execute_csharp requires non-empty 'code'.");

            var returnResult = args.Value<bool?>("return_result") ?? true;

            try
            {
                var fullCode = @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

public static class __McpEval
{
    public static object Run()
    {
        " + code + @"
    }
}";
                var provider = new Microsoft.CSharp.CSharpCodeProvider();
                var parameters = new System.CodeDom.Compiler.CompilerParameters
                {
                    GenerateInMemory = true,
                    GenerateExecutable = false,
                    TreatWarningsAsErrors = false
                };

                // Add references to all loaded assemblies
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (!asm.IsDynamic && !string.IsNullOrEmpty(asm.Location))
                            parameters.ReferencedAssemblies.Add(asm.Location);
                    }
                    catch { /* skip problematic assemblies */ }
                }

                var results = provider.CompileAssemblyFromSource(parameters, fullCode);
                if (results.Errors.HasErrors)
                {
                    var errors = new List<string>();
                    foreach (System.CodeDom.Compiler.CompilerError error in results.Errors)
                    {
                        if (!error.IsWarning)
                            errors.Add($"Line {error.Line}: {error.ErrorText}");
                    }
                    return Error($"Compilation failed:\n{string.Join("\n", errors)}");
                }

                var type = results.CompiledAssembly.GetType("__McpEval");
                var method = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
                var result = method.Invoke(null, null);

                if (returnResult && result != null)
                {
                    // Try to serialize complex objects
                    string resultStr;
                    try
                    {
                        resultStr = JToken.FromObject(result).ToString();
                    }
                    catch
                    {
                        resultStr = result.ToString();
                    }

                    return Success(JToken.FromObject(new
                    {
                        executed = true,
                        resultType = result.GetType().FullName,
                        result = resultStr
                    }));
                }

                return Success(JToken.FromObject(new
                {
                    executed = true,
                    resultType = "void",
                    result = (string)null
                }));
            }
            catch (TargetInvocationException tie)
            {
                var inner = tie.InnerException ?? tie;
                return Error($"Runtime error: {inner.GetType().Name}: {inner.Message}\n{inner.StackTrace}");
            }
            catch (Exception ex)
            {
                return Error($"execute_csharp failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // ───────── search_hierarchy ─────────

        private static AutonomousMcpToolResponse HandleSearchHierarchy(JObject args)
        {
            var namePattern = args.Value<string>("name_pattern") ?? args.Value<string>("namePattern");
            var componentType = args.Value<string>("component_type") ?? args.Value<string>("componentType");
            var tag = args.Value<string>("tag");
            var layer = args.Value<string>("layer");
            var activeOnly = args.Value<bool?>("active_only") ?? false;
            var includeComponents = args.Value<bool?>("include_components") ?? false;
            var includeInactive = args.Value<bool?>("include_inactive") ?? true;
            var limit = Math.Max(1, Math.Min(args.Value<int?>("limit") ?? 100, 500));

            Regex nameRegex = null;
            if (!string.IsNullOrWhiteSpace(namePattern))
            {
                try { nameRegex = new Regex(namePattern, RegexOptions.IgnoreCase); }
                catch { return Error($"Invalid regex pattern: '{namePattern}'."); }
            }

            Type filterType = null;
            if (!string.IsNullOrWhiteSpace(componentType))
            {
                filterType = ResolveType(componentType);
                if (filterType == null)
                    return Error($"Component type '{componentType}' not found.");
            }

            int? layerMask = null;
            if (!string.IsNullOrWhiteSpace(layer))
            {
                int layerIndex = LayerMask.NameToLayer(layer);
                if (layerIndex >= 0) layerMask = layerIndex;
                else if (int.TryParse(layer, out int parsed)) layerMask = parsed;
                else return Error($"Layer '{layer}' not found.");
            }

            var matches = new JArray();
            int count = 0;

            foreach (var go in EnumerateSceneGameObjects())
            {
                if (count >= limit) break;

                if (activeOnly && !go.activeInHierarchy) continue;
                if (!includeInactive && !go.activeSelf) continue;

                if (nameRegex != null && !nameRegex.IsMatch(go.name)) continue;

                if (filterType != null && go.GetComponent(filterType) == null) continue;

                if (!string.IsNullOrWhiteSpace(tag) && !go.CompareTag(tag)) continue;

                if (layerMask.HasValue && go.layer != layerMask.Value) continue;

                var entry = new JObject
                {
                    ["name"] = go.name,
                    ["fullPath"] = GetFullPath(go.transform),
                    ["instanceId"] = go.GetInstanceID(),
                    ["activeSelf"] = go.activeSelf,
                    ["activeInHierarchy"] = go.activeInHierarchy,
                    ["layer"] = LayerMask.LayerToName(go.layer),
                    ["tag"] = go.tag
                };

                if (includeComponents)
                {
                    var comps = new JArray();
                    foreach (var c in go.GetComponents<Component>())
                    {
                        if (c == null) { comps.Add("(missing script)"); continue; }
                        comps.Add(c.GetType().Name);
                    }
                    entry["components"] = comps;
                }

                matches.Add(entry);
                count++;
            }

            return Success(JToken.FromObject(new
            {
                filters = new
                {
                    namePattern,
                    componentType,
                    tag,
                    layer,
                    activeOnly,
                    includeComponents
                },
                count = matches.Count,
                matches
            }));
        }

        // ───────── get_project_structure ─────────

        private static AutonomousMcpToolResponse HandleGetProjectStructure(JObject args)
        {
            var rootPath = args.Value<string>("path") ?? "Assets";
            var maxDepth = Math.Max(1, Math.Min(args.Value<int?>("depth") ?? 3, 10));
            var extensions = args.Value<string>("extensions"); // comma-separated, e.g. ".cs,.shader"
            var includeMeta = args.Value<bool?>("include_meta") ?? false;

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
                return Error("Failed to resolve project root.");

            var fullRootPath = Path.Combine(projectRoot, rootPath.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(fullRootPath))
                return Error($"Directory not found: '{rootPath}'.");

            HashSet<string> extFilter = null;
            if (!string.IsNullOrWhiteSpace(extensions))
            {
                extFilter = new HashSet<string>(
                    extensions.Split(',').Select(e => e.Trim().ToLowerInvariant()),
                    StringComparer.OrdinalIgnoreCase);
            }

            var tree = BuildDirectoryTree(fullRootPath, projectRoot, maxDepth, 0, extFilter, includeMeta);

            return Success(JToken.FromObject(new
            {
                rootPath,
                maxDepth,
                extensions,
                tree
            }));
        }

        private static JToken BuildDirectoryTree(string dirPath, string projectRoot, int maxDepth, int currentDepth,
            HashSet<string> extFilter, bool includeMeta)
        {
            var dirName = Path.GetFileName(dirPath);
            var relativePath = dirPath.Substring(projectRoot.Length + 1).Replace('\\', '/');
            var node = new JObject
            {
                ["name"] = dirName,
                ["path"] = relativePath,
                ["type"] = "directory"
            };

            try
            {
                var files = Directory.GetFiles(dirPath);
                var filteredFiles = new JArray();
                int fileCount = 0;

                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    if (!includeMeta && fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;

                    if (extFilter != null)
                    {
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        if (!extFilter.Contains(ext)) continue;
                    }

                    filteredFiles.Add(JToken.FromObject(new
                    {
                        name = fileName,
                        extension = Path.GetExtension(file),
                        size = new FileInfo(file).Length
                    }));
                    fileCount++;
                }

                node["fileCount"] = fileCount;

                if (currentDepth < maxDepth)
                {
                    node["files"] = filteredFiles;

                    var subdirs = Directory.GetDirectories(dirPath);
                    var children = new JArray();
                    foreach (var subdir in subdirs)
                    {
                        var subDirName = Path.GetFileName(subdir);
                        if (subDirName.StartsWith(".", StringComparison.Ordinal)) continue;
                        children.Add(BuildDirectoryTree(subdir, projectRoot, maxDepth, currentDepth + 1, extFilter, includeMeta));
                    }
                    node["directories"] = children;
                }
                else
                {
                    var subdirCount = Directory.GetDirectories(dirPath).Length;
                    node["directoryCount"] = subdirCount;
                }
            }
            catch (Exception ex)
            {
                node["error"] = ex.Message;
            }

            return node;
        }

        // ───────── manage_prefab ─────────

        private static AutonomousMcpToolResponse HandleManagePrefab(JObject args)
        {
            var action = args.Value<string>("action") ?? string.Empty;

            switch (action)
            {
                case "get_status":
                {
                    var target = ResolveGameObject(args);
                    if (target == null)
                        return Error("get_status requires a valid target by instanceId or name.");

                    var prefabType = PrefabUtility.GetPrefabAssetType(target);
                    var prefabStatus = PrefabUtility.GetPrefabInstanceStatus(target);
                    var isPartOfPrefab = PrefabUtility.IsPartOfAnyPrefab(target);
                    var hasOverrides = PrefabUtility.HasPrefabInstanceAnyOverrides(target, false);
                    var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(target);

                    return Success(JToken.FromObject(new
                    {
                        action,
                        gameObject = target.name,
                        prefabAssetType = prefabType.ToString(),
                        prefabInstanceStatus = prefabStatus.ToString(),
                        isPartOfPrefab,
                        hasOverrides,
                        prefabAssetPath = prefabPath
                    }));
                }

                case "open":
                {
                    var assetPath = args.Value<string>("asset_path") ?? args.Value<string>("assetPath") ?? string.Empty;
                    GameObject prefabRoot = null;

                    if (!string.IsNullOrWhiteSpace(assetPath))
                    {
                        prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                    }
                    else
                    {
                        var target = ResolveGameObject(args);
                        if (target != null)
                        {
                            assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(target);
                            if (!string.IsNullOrWhiteSpace(assetPath))
                                prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                        }
                    }

                    if (prefabRoot == null)
                        return Error("Could not resolve prefab. Provide asset_path or a valid prefab instance name/instanceId.");

                    AssetDatabase.OpenAsset(prefabRoot);

                    return Success(JToken.FromObject(new
                    {
                        action,
                        assetPath,
                        opened = true
                    }));
                }

                case "apply_overrides":
                {
                    var target = ResolveGameObject(args);
                    if (target == null)
                        return Error("apply_overrides requires a valid target by instanceId or name.");

                    var outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(target);
                    if (outermost == null)
                        return Error($"'{target.name}' is not a prefab instance.");

                    PrefabUtility.ApplyPrefabInstance(outermost, InteractionMode.UserAction);

                    return Success(JToken.FromObject(new
                    {
                        action,
                        gameObject = outermost.name,
                        applied = true
                    }));
                }

                case "revert_overrides":
                {
                    var target = ResolveGameObject(args);
                    if (target == null)
                        return Error("revert_overrides requires a valid target by instanceId or name.");

                    var outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(target);
                    if (outermost == null)
                        return Error($"'{target.name}' is not a prefab instance.");

                    PrefabUtility.RevertPrefabInstance(outermost, InteractionMode.UserAction);

                    return Success(JToken.FromObject(new
                    {
                        action,
                        gameObject = outermost.name,
                        reverted = true
                    }));
                }

                case "unpack":
                {
                    var target = ResolveGameObject(args);
                    if (target == null)
                        return Error("unpack requires a valid target by instanceId or name.");

                    var completely = args.Value<bool?>("completely") ?? false;

                    if (completely)
                        PrefabUtility.UnpackPrefabInstance(target, PrefabUnpackMode.Completely, InteractionMode.UserAction);
                    else
                        PrefabUtility.UnpackPrefabInstance(target, PrefabUnpackMode.OutermostRoot, InteractionMode.UserAction);

                    return Success(JToken.FromObject(new
                    {
                        action,
                        gameObject = target.name,
                        unpacked = true,
                        completely
                    }));
                }

                default:
                    return Error($"Unsupported manage_prefab action '{action}'. Supported: get_status, open, apply_overrides, revert_overrides, unpack.");
            }
        }

        // ───────── manage_selection ─────────

        private static AutonomousMcpToolResponse HandleManageSelection(JObject args)
        {
            var action = args.Value<string>("action") ?? "get";

            switch (action)
            {
                case "get":
                {
                    var selected = Selection.gameObjects;
                    var list = new JArray();
                    foreach (var go in selected)
                    {
                        list.Add(JToken.FromObject(new
                        {
                            name = go.name,
                            instanceId = go.GetInstanceID(),
                            fullPath = GetFullPath(go.transform)
                        }));
                    }

                    var activeObj = Selection.activeGameObject;
                    return Success(JToken.FromObject(new
                    {
                        action,
                        count = list.Count,
                        activeObject = activeObj != null ? new
                        {
                            name = activeObj.name,
                            instanceId = activeObj.GetInstanceID(),
                            fullPath = GetFullPath(activeObj.transform)
                        } : null,
                        selection = list
                    }));
                }

                case "set":
                {
                    var names = args["names"] as JArray;
                    var instanceIds = args["instanceIds"] as JArray;
                    var objects = new List<UnityEngine.Object>();

                    if (instanceIds != null)
                    {
                        foreach (var id in instanceIds)
                        {
                            var obj = EditorUtility.InstanceIDToObject(id.Value<int>());
                            if (obj != null) objects.Add(obj);
                        }
                    }
                    else if (names != null)
                    {
                        foreach (var nameToken in names)
                        {
                            var go = GameObject.Find(nameToken.Value<string>());
                            if (go != null) objects.Add(go);
                        }
                    }
                    else
                    {
                        // Single object
                        var target = ResolveGameObject(args);
                        if (target != null) objects.Add(target);
                    }

                    Selection.objects = objects.ToArray();

                    return Success(JToken.FromObject(new
                    {
                        action,
                        selectedCount = objects.Count
                    }));
                }

                case "clear":
                {
                    Selection.objects = new UnityEngine.Object[0];
                    return Success(JToken.FromObject(new { action, cleared = true }));
                }

                case "focus":
                {
                    var target = ResolveGameObject(args);
                    if (target != null)
                        Selection.activeGameObject = target;

                    if (SceneView.lastActiveSceneView != null)
                    {
                        SceneView.lastActiveSceneView.FrameSelected();
                    }

                    return Success(JToken.FromObject(new
                    {
                        action,
                        focused = target?.name
                    }));
                }

                default:
                    return Error($"Unsupported manage_selection action '{action}'. Supported: get, set, clear, focus.");
            }
        }

        // ───────── manage_layer_tag ─────────

        private static AutonomousMcpToolResponse HandleManageLayerTag(JObject args)
        {
            var action = args.Value<string>("action") ?? string.Empty;

            switch (action)
            {
                case "get":
                {
                    var target = ResolveGameObject(args);
                    if (target == null)
                        return Error("get requires a valid target by instanceId or name.");

                    return Success(JToken.FromObject(new
                    {
                        action,
                        gameObject = target.name,
                        layer = LayerMask.LayerToName(target.layer),
                        layerIndex = target.layer,
                        tag = target.tag
                    }));
                }

                case "set_layer":
                {
                    var target = ResolveGameObject(args);
                    if (target == null)
                        return Error("set_layer requires a valid target by instanceId or name.");

                    var layerName = args.Value<string>("layer") ?? string.Empty;
                    var layerIndex = args.Value<int?>("layer_index");
                    var recursive = args.Value<bool?>("recursive") ?? false;

                    int resolvedLayer;
                    if (layerIndex.HasValue)
                    {
                        resolvedLayer = layerIndex.Value;
                    }
                    else if (!string.IsNullOrWhiteSpace(layerName))
                    {
                        resolvedLayer = LayerMask.NameToLayer(layerName);
                        if (resolvedLayer < 0)
                            return Error($"Layer '{layerName}' not found.");
                    }
                    else
                    {
                        return Error("set_layer requires 'layer' (name) or 'layer_index' (int).");
                    }

                    Undo.RecordObject(target, "MCP: Set Layer");
                    target.layer = resolvedLayer;

                    if (recursive)
                    {
                        foreach (Transform child in target.GetComponentsInChildren<Transform>(true))
                        {
                            Undo.RecordObject(child.gameObject, "MCP: Set Layer Recursive");
                            child.gameObject.layer = resolvedLayer;
                        }
                    }

                    return Success(JToken.FromObject(new
                    {
                        action,
                        gameObject = target.name,
                        layer = LayerMask.LayerToName(resolvedLayer),
                        layerIndex = resolvedLayer,
                        recursive
                    }));
                }

                case "set_tag":
                {
                    var target = ResolveGameObject(args);
                    if (target == null)
                        return Error("set_tag requires a valid target by instanceId or name.");

                    var newTag = args.Value<string>("tag") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(newTag))
                        return Error("set_tag requires a non-empty 'tag' value.");

                    Undo.RecordObject(target, "MCP: Set Tag");
                    target.tag = newTag;

                    return Success(JToken.FromObject(new
                    {
                        action,
                        gameObject = target.name,
                        tag = target.tag
                    }));
                }

                case "list_layers":
                {
                    var layers = new JArray();
                    for (int i = 0; i < 32; i++)
                    {
                        var name = LayerMask.LayerToName(i);
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            layers.Add(JToken.FromObject(new
                            {
                                index = i,
                                name
                            }));
                        }
                    }

                    return Success(JToken.FromObject(new
                    {
                        action,
                        count = layers.Count,
                        layers
                    }));
                }

                case "list_tags":
                {
                    var tags = UnityEditorInternal.InternalEditorUtility.tags;
                    return Success(JToken.FromObject(new
                    {
                        action,
                        count = tags.Length,
                        tags
                    }));
                }

                case "list_sorting_layers":
                {
                    var sortingLayers = new JArray();
                    foreach (var sl in SortingLayer.layers)
                    {
                        sortingLayers.Add(JToken.FromObject(new
                        {
                            id = sl.id,
                            name = sl.name,
                            value = sl.value
                        }));
                    }

                    return Success(JToken.FromObject(new
                    {
                        action,
                        count = sortingLayers.Count,
                        sortingLayers
                    }));
                }

                default:
                    return Error($"Unsupported manage_layer_tag action '{action}'. Supported: get, set_layer, set_tag, list_layers, list_tags, list_sorting_layers.");
            }
        }

        // ───────── get_compilation_errors ─────────

        private static AutonomousMcpToolResponse HandleGetCompilationErrors(JObject args)
        {
            var includeWarnings = args.Value<bool?>("include_warnings") ?? false;

            // Force a refresh to get latest state
            AssetDatabase.Refresh();

            var messages = new JArray();

            // Use CompilationPipeline if available
            try
            {
                var pipelineType = Type.GetType("UnityEditor.Compilation.CompilationPipeline, UnityEditor");
                if (pipelineType != null)
                {
                    var getMessagesMethod = pipelineType.GetMethod("GetCompilerMessages",
                        BindingFlags.Public | BindingFlags.Static);
                    if (getMessagesMethod != null)
                    {
                        // CompilationPipeline.GetCompilerMessages() not available in all versions
                        // Fall through to console-based approach
                    }
                }
            }
            catch { /* fall through */ }

            // Read compilation errors from console log entries
            var logs = AutonomousMcpLogStore.Read("all", 500);
            foreach (var logObj in logs)
            {
                var logToken = JToken.FromObject(logObj);
                var logLevel = logToken.Value<string>("level") ?? "";
                var logMessage = logToken.Value<string>("message") ?? "";

                if (logLevel == "error" ||
                    (includeWarnings && logLevel == "warning"))
                {
                    // Try to parse file:line from the message
                    string file = null;
                    int? line = null;

                    // Pattern: "Assets/Scripts/MyScript.cs(42,10): error CS1234: ..."
                    var match = Regex.Match(logMessage, @"^([\w/\\\.]+)\((\d+),?\d*\):\s*(error|warning)\s+(\w+):\s*(.*)$");
                    if (match.Success)
                    {
                        file = match.Groups[1].Value;
                        line = int.Parse(match.Groups[2].Value);
                    }

                    messages.Add(JToken.FromObject(new
                    {
                        level = logLevel,
                        message = logMessage,
                        file,
                        line
                    }));
                }
            }

            return Success(JToken.FromObject(new
            {
                isCompiling = EditorApplication.isCompiling,
                hasErrors = messages.Count > 0,
                count = messages.Count,
                messages
            }));
        }

        // ───────── manage_project_settings ─────────

        private static AutonomousMcpToolResponse HandleManageProjectSettings(JObject args)
        {
            var action = args.Value<string>("action") ?? string.Empty;

            switch (action)
            {
                case "get_player_settings":
                {
                    return Success(JToken.FromObject(new
                    {
                        action,
                        companyName = PlayerSettings.companyName,
                        productName = PlayerSettings.productName,
                        bundleVersion = PlayerSettings.bundleVersion,
                        defaultIsFullScreen = PlayerSettings.defaultIsNativeResolution,
                        runInBackground = PlayerSettings.runInBackground,
                        colorSpace = PlayerSettings.colorSpace.ToString(),
                        apiCompatibilityLevel = PlayerSettings.GetApiCompatibilityLevel(EditorUserBuildSettings.selectedBuildTargetGroup).ToString(),
                        scriptingBackend = PlayerSettings.GetScriptingBackend(EditorUserBuildSettings.selectedBuildTargetGroup).ToString(),
                        targetPlatform = EditorUserBuildSettings.activeBuildTarget.ToString()
                    }));
                }

                case "set_player_setting":
                {
                    var setting = args.Value<string>("setting") ?? string.Empty;
                    var value = args.Value<string>("value") ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(setting))
                        return Error("set_player_setting requires a non-empty 'setting' name.");

                    switch (setting.ToLowerInvariant())
                    {
                        case "companyname":
                            PlayerSettings.companyName = value;
                            break;
                        case "productname":
                            PlayerSettings.productName = value;
                            break;
                        case "bundleversion":
                            PlayerSettings.bundleVersion = value;
                            break;
                        case "runinbackground":
                            PlayerSettings.runInBackground = bool.Parse(value);
                            break;
                        default:
                            return Error($"Setting '{setting}' is not supported. Supported: companyName, productName, bundleVersion, runInBackground.");
                    }

                    return Success(JToken.FromObject(new
                    {
                        action,
                        setting,
                        value,
                        applied = true
                    }));
                }

                case "get_quality_settings":
                {
                    var names = QualitySettings.names;
                    var current = QualitySettings.GetQualityLevel();

                    return Success(JToken.FromObject(new
                    {
                        action,
                        currentLevel = current,
                        currentName = names.Length > current ? names[current] : "Unknown",
                        levels = names,
                        shadowDistance = QualitySettings.shadowDistance,
                        pixelLightCount = QualitySettings.pixelLightCount,
                        antiAliasing = QualitySettings.antiAliasing,
                        vSyncCount = QualitySettings.vSyncCount
                    }));
                }

                case "get_physics_settings":
                {
                    return Success(JToken.FromObject(new
                    {
                        action,
                        gravity = new { x = Physics.gravity.x, y = Physics.gravity.y, z = Physics.gravity.z },
                        defaultSolverIterations = Physics.defaultSolverIterations,
                        defaultSolverVelocityIterations = Physics.defaultSolverVelocityIterations,
                        bounceThreshold = Physics.bounceThreshold,
                        defaultContactOffset = Physics.defaultContactOffset,
                        sleepThreshold = Physics.sleepThreshold,
                        autoSimulation = Physics.autoSimulation
                    }));
                }

                case "get_time_settings":
                {
                    return Success(JToken.FromObject(new
                    {
                        action,
                        fixedDeltaTime = Time.fixedDeltaTime,
                        maximumDeltaTime = Time.maximumDeltaTime,
                        timeScale = Time.timeScale,
                        maximumParticleDeltaTime = Time.maximumParticleDeltaTime
                    }));
                }

                default:
                    return Error($"Unsupported manage_project_settings action '{action}'. Supported: get_player_settings, set_player_setting, get_quality_settings, get_physics_settings, get_time_settings.");
            }
        }

        // ───────── get_installed_packages ─────────

        private static AutonomousMcpToolResponse HandleGetInstalledPackages(JObject args)
        {
            var includeBuiltin = args.Value<bool?>("include_builtin") ?? false;

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
                return Error("Failed to resolve project root.");

            // Known VRC ecosystem packages with descriptions
            var knownPackages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {"com.vrchat.avatars", "VRChat Avatars SDK"},
                {"com.vrchat.worlds", "VRChat Worlds SDK"},
                {"com.vrchat.base", "VRChat Base SDK"},
                {"com.vrchat.core.vpm-resolver", "VPM Resolver"},
                {"nadena.dev.modular-avatar", "Modular Avatar — non-destructive avatar assembly framework"},
                {"nadena.dev.ndmf", "NDMF — Non-Destructive Modular Framework (build pipeline)"},
                {"com.anatawa12.avatar-optimizer", "AAO: Avatar Optimizer — Trace & Optimize, mesh merging"},
                {"com.github.d4rkc0d3r.d4rkAvatarOptimizer", "d4rkAvatarOptimizer — cross-shader material merging"},
                {"jp.lilxyzw.liltoon", "lilToon — keyword-efficient toon shader"},
                {"com.poiyomi.toon", "Poiyomi Toon Shader — feature-dense avatar shader"},
                {"jp.lilxyzw.lilycalinventory", "lilycalInventory — toggle/inventory system with material optimization"},
                {"jp.lilxyzw.avatarutils", "lilAvatarUtils — avatar inspection and batch editing"},
                {"jp.lilxyzw.editortoolbox", "lilEditorToolbox — editor QOL extensions"},
                {"jp.lilxyzw.ndmfmeshsimplifier", "lilNDMFMeshSimplifier — non-destructive polygon reducer"},
                {"jp.lilxyzw.materialconverter", "lilMaterialConverter — cross-shader material conversion"},
                {"com.vrcfury.vrcfury", "VRCFury — non-destructive avatar assembly (SPS, toggles, armature link)"},
                {"dev.suzuryg.face-emo", "FaceEmo — facial expression creation + gesture mapping"},
                {"com.hai-vr.combo-gesture-expressions", "ComboGestureExpressions — gesture-to-expression mapping"},
                {"dev.hai-vr.animator-as-code", "Animator As Code — programmatic animator generation"},
                {"com.hai-vr.blendshape-viewer", "Blendshape Viewer — visual blendshape browser"},
                {"dev.hai-vr.denormalized-avatar-exporter", "Denormalized Avatar Exporter — VTubing export"},
                {"dev.hai-vr.prefabulous", "Prefabulous — NDMF utility components"},
                {"com.github.thry.editor", "ThryEditor — shader inspector + locking (powers Poiyomi UI)"},
                {"com.llealloo.audiolink", "AudioLink — audio reactive system for shaders/Udon"},
                {"com.pimaker.ltcgi", "LTCGI — realtime area light reflections"},
                {"com.blackstartx.gesturemanager", "GestureManager — avatar interaction emulator"},
                {"com.lyuma.av3emulator", "Av3Emulator — VRC Avatars 3.0 PlayableGraph emulator"},
                {"com.rrazgriz.rats", "RATS — Animator window QOL improvements"},
                {"com.vrlabs.hierarchyplus", "HierarchyPlus — component icons in hierarchy"},
                {"com.franada.gogoloco", "GoGo Loco — sit/lie/fly locomotion system"},
                {"com.kurotu.vrcquesttools", "VRCQuestTools — PC→Quest automated conversion"},
                {"com.narazaka.avatarmenucreator", "AvatarMenuCreator for MA — wizard-style toggle/menu creation"},
                {"dev.logilabo.virtuallens2", "VirtualLens2 — VRC photography camera (drone, DoF)"},
                {"com.reina-sakiria.textranstool", "TexTransTool — non-destructive texture atlas/decals"},
                {"com.autonomous.unity.mcp", "Autonomous Unity MCP — this package"},
                {"com.onevr.vrworldtoolkit", "VRWorld Toolkit — world QA analysis"},
                {"com.vrchat.clientsim", "ClientSim — in-editor world testing"},
                {"com.cyanlaser.cyantrigger", "CyanTrigger — visual Udon scripting"},
                {"com.acchosen.vr-stage-lighting", "VRSL — VR Stage Lighting (DMX, AudioLink)"},
                {"com.c-colloid.pbreplacer", "PBReplacer — batch PhysBone setting replacement"},
                {"com.azukimochi.light-limit-changer", "Light Limit Changer for MA — avatar brightness control"},
            };

            var packages = new JArray();

            // Read manifest.json
            var manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
            if (File.Exists(manifestPath))
            {
                try
                {
                    var manifestJson = JObject.Parse(File.ReadAllText(manifestPath));
                    var deps = manifestJson["dependencies"] as JObject;
                    if (deps != null)
                    {
                        foreach (var prop in deps.Properties())
                        {
                            if (!includeBuiltin && prop.Name.StartsWith("com.unity.modules.", StringComparison.Ordinal))
                                continue;

                            var version = prop.Value.ToString();
                            var isGit = version.Contains("github.com") || version.Contains(".git");
                            var isLocal = version.StartsWith("file:", StringComparison.Ordinal);

                            knownPackages.TryGetValue(prop.Name, out var description);

                            packages.Add(JToken.FromObject(new
                            {
                                name = prop.Name,
                                version,
                                source = isGit ? "git" : isLocal ? "local" : "registry",
                                description = description ?? "",
                                isVrcEcosystem = knownPackages.ContainsKey(prop.Name)
                            }));
                        }
                    }
                }
                catch (Exception ex)
                {
                    return Error($"Failed to parse manifest.json: {ex.Message}");
                }
            }

            // Detect additional packages via their components in loaded assemblies
            var detectedFrameworks = new JArray();
            string[] frameworkTypes = {
                "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor",
                "nadena.dev.modular_avatar.core.ModularAvatarMergeArmature",
                "VF.Model.VRCFury",
                "Anatawa12.AvatarOptimizer.TraceAndOptimize",
                "lilycalInventory.Runtime.LIToggleItem",
                "d4rkAvatarOptimizer",
            };
            foreach (var typeName in frameworkTypes)
            {
                var t = ResolveType(typeName);
                if (t != null)
                {
                    detectedFrameworks.Add(JToken.FromObject(new
                    {
                        type = t.FullName,
                        assembly = t.Assembly.GetName().Name
                    }));
                }
            }

            return Success(JToken.FromObject(new
            {
                packageCount = packages.Count,
                packages,
                detectedFrameworks
            }));
        }

        // ───────── list_shaders ─────────

        private static AutonomousMcpToolResponse HandleListShaders(JObject args)
        {
            var filter = args.Value<string>("filter") ?? "";
            var limit = Math.Max(1, Math.Min(args.Value<int?>("limit") ?? 100, 500));
            var includeProperties = args.Value<bool?>("include_properties") ?? false;
            var includeBuiltin = args.Value<bool?>("include_builtin") ?? false;

            // Known VRC shader families
            var knownShaderFamilies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {".poiyomi/", "Poiyomi Toon Shader — feature-dense, AudioLink, grabpass (Pro)"},
                {"lilToon", "lilToon — keyword-efficient, one-click presets, fur/gem variants"},
                {"Silent's Cel Shading", "SCSS — PBR-based cel shading, Crosstone shadows"},
                {"Xiexe/", "XSToon — PBR toon, halftone/stippling modes"},
                {"orels1/", "ORL Shaders — PBR/Toon hybrid, Shader Generator"},
                {"Mochie/", "Mochie's Shaders — Water, Particle, Screenspace"},
                {"Sunao/", "Sunao — zero shader keywords"},
                {"WhiteFlare/", "Unlit WF — unlit shader for accessories"},
                {"VRChat/", "VRChat SDK built-in shaders"},
            };

            var shaders = new JArray();
            int count = 0;

            // Use ShaderUtil to enumerate shaders
            var shaderUtilType = typeof(UnityEditor.ShaderUtil);
            var getCountMethod = shaderUtilType.GetMethod("GetShaderCount",
                BindingFlags.NonPublic | BindingFlags.Static);
            var getNameMethod = shaderUtilType.GetMethod("GetShaderNameByIndex",
                BindingFlags.NonPublic | BindingFlags.Static);

            if (getCountMethod != null && getNameMethod != null)
            {
                int totalShaders = (int)getCountMethod.Invoke(null, null);

                for (int i = 0; i < totalShaders && count < limit; i++)
                {
                    var shaderName = (string)getNameMethod.Invoke(null, new object[] { i });
                    if (string.IsNullOrEmpty(shaderName)) continue;

                    if (!includeBuiltin && (shaderName.StartsWith("Hidden/", StringComparison.Ordinal) ||
                        shaderName.StartsWith("Legacy Shaders/", StringComparison.Ordinal) ||
                        shaderName.StartsWith("GUI/", StringComparison.Ordinal) ||
                        shaderName.StartsWith("UI/", StringComparison.Ordinal)))
                        continue;

                    if (!string.IsNullOrWhiteSpace(filter) &&
                        shaderName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    // Identify family
                    string family = null;
                    string familyDescription = null;
                    foreach (var kv in knownShaderFamilies)
                    {
                        if (shaderName.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            family = kv.Key.TrimEnd('/');
                            familyDescription = kv.Value;
                            break;
                        }
                    }

                    var entry = new JObject
                    {
                        ["name"] = shaderName,
                        ["family"] = family,
                        ["familyDescription"] = familyDescription
                    };

                    if (includeProperties)
                    {
                        var shader = Shader.Find(shaderName);
                        if (shader != null)
                        {
                            int propCount = shader.GetPropertyCount();
                            entry["propertyCount"] = propCount;

                            var props = new JArray();
                            for (int p = 0; p < Math.Min(propCount, 50); p++)
                            {
                                props.Add(JToken.FromObject(new
                                {
                                    name = shader.GetPropertyName(p),
                                    type = shader.GetPropertyType(p).ToString(),
                                    description = shader.GetPropertyDescription(p)
                                }));
                            }
                            entry["properties"] = props;
                        }
                    }

                    shaders.Add(entry);
                    count++;
                }
            }

            return Success(JToken.FromObject(new
            {
                filter,
                count = shaders.Count,
                shaders
            }));
        }

        // ───────── get_asset_info ─────────

        private static AutonomousMcpToolResponse HandleGetAssetInfo(JObject args)
        {
            var assetPath = args.Value<string>("asset_path") ?? args.Value<string>("assetPath") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(assetPath))
                return Error("get_asset_info requires a non-empty asset_path.");

            var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (mainAsset == null)
                return Error($"No asset found at '{assetPath}'.");

            var assetType = mainAsset.GetType();
            var info = new JObject
            {
                ["assetPath"] = assetPath,
                ["type"] = assetType.Name,
                ["fullType"] = assetType.FullName,
                ["name"] = mainAsset.name,
                ["instanceId"] = mainAsset.GetInstanceID()
            };

            // Type-specific deep inspection
            if (mainAsset is GameObject prefab)
            {
                // Prefab inspection
                var components = new JArray();
                foreach (var comp in prefab.GetComponentsInChildren<Component>(true))
                {
                    if (comp == null) { components.Add("(missing script)"); continue; }
                    components.Add(comp.GetType().FullName);
                }

                var boneCount = prefab.GetComponentsInChildren<Transform>(true).Length;

                info["isPrefab"] = true;
                info["childCount"] = prefab.transform.childCount;
                info["totalTransforms"] = boneCount;
                info["componentTypes"] = new JArray(
                    components.Select(c => c.ToString()).Distinct().OrderBy(c => c).ToArray());

                // Check for VRC/MA/VRCFury components
                var vrcComponents = new JArray();
                foreach (var comp in prefab.GetComponentsInChildren<Component>(true))
                {
                    if (comp == null) continue;
                    var typeName = comp.GetType().FullName ?? "";
                    if (typeName.Contains("VRC") || typeName.Contains("VRCFury") ||
                        typeName.Contains("ModularAvatar") || typeName.Contains("nadena") ||
                        typeName.Contains("lilycal") || typeName.Contains("Anatawa12") ||
                        typeName.Contains("PhysBone"))
                    {
                        vrcComponents.Add(JToken.FromObject(new
                        {
                            type = comp.GetType().Name,
                            fullType = typeName,
                            gameObject = comp.gameObject.name,
                            path = GetFullPath(comp.transform)
                        }));
                    }
                }
                if (vrcComponents.Count > 0)
                    info["vrcEcosystemComponents"] = vrcComponents;

                // Build hierarchy tree (limited depth)
                info["hierarchy"] = BuildHierarchyNode(prefab.transform);
            }
            else if (mainAsset is Material mat)
            {
                info["shader"] = mat.shader?.name;
                info["renderQueue"] = mat.renderQueue;
                info["passCount"] = mat.passCount;

                // List texture properties
                var textures = new JArray();
                if (mat.shader != null)
                {
                    int propCount = mat.shader.GetPropertyCount();
                    for (int i = 0; i < propCount; i++)
                    {
                        if (mat.shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Texture)
                        {
                            var propName = mat.shader.GetPropertyName(i);
                            var tex = mat.GetTexture(propName);
                            textures.Add(JToken.FromObject(new
                            {
                                property = propName,
                                texture = tex?.name,
                                width = (tex is Texture2D t2d) ? (int?)t2d.width : null,
                                height = (tex is Texture2D t2dh) ? (int?)t2dh.height : null
                            }));
                        }
                    }
                }
                info["textures"] = textures;
            }
            else if (mainAsset is Texture2D tex2d)
            {
                info["width"] = tex2d.width;
                info["height"] = tex2d.height;
                info["format"] = tex2d.format.ToString();
                info["mipmapCount"] = tex2d.mipmapCount;
                info["isReadable"] = tex2d.isReadable;

                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer != null)
                {
                    info["maxTextureSize"] = importer.maxTextureSize;
                    info["textureCompression"] = importer.textureCompression.ToString();
                    info["sRGB"] = importer.sRGBTexture;
                    info["alphaSource"] = importer.alphaSource.ToString();
                }
            }
            else if (mainAsset is RuntimeAnimatorController rac)
            {
                var controller = rac as UnityEditor.Animations.AnimatorController;
                if (controller != null)
                {
                    info["layerCount"] = controller.layers.Length;
                    info["parameterCount"] = controller.parameters.Length;

                    var layers = new JArray();
                    foreach (var layer in controller.layers)
                    {
                        layers.Add(JToken.FromObject(new
                        {
                            name = layer.name,
                            stateCount = layer.stateMachine.states.Length,
                            defaultWeight = layer.defaultWeight
                        }));
                    }
                    info["layers"] = layers;

                    var parameters = new JArray();
                    foreach (var param in controller.parameters)
                    {
                        parameters.Add(JToken.FromObject(new
                        {
                            name = param.name,
                            type = param.type.ToString()
                        }));
                    }
                    info["parameters"] = parameters;
                }
            }
            else if (mainAsset is AnimationClip clip)
            {
                info["length"] = clip.length;
                info["frameRate"] = clip.frameRate;
                info["isLooping"] = clip.isLooping;
                info["isHumanMotion"] = clip.humanMotion;

                var bindings = AnimationUtility.GetCurveBindings(clip);
                info["curveCount"] = bindings.Length;
                var bindingList = new JArray();
                foreach (var binding in bindings.Take(50))
                {
                    bindingList.Add(JToken.FromObject(new
                    {
                        path = binding.path,
                        propertyName = binding.propertyName,
                        type = binding.type.Name
                    }));
                }
                info["bindings"] = bindingList;
            }

            return Success(info);
        }

        // ───────── scan_armature ─────────

        private static AutonomousMcpToolResponse HandleScanArmature(JObject args)
        {
            var target = ResolveGameObject(args);
            if (target == null)
                return Error("scan_armature requires a valid target by instanceId or name.");

            // Find the Armature root
            Transform armatureRoot = null;
            foreach (Transform child in target.transform)
            {
                var nameLower = child.name.ToLowerInvariant();
                if (nameLower == "armature" || nameLower.Contains("armature") ||
                    nameLower == "skeleton" || nameLower.Contains("root"))
                {
                    armatureRoot = child;
                    break;
                }
            }

            if (armatureRoot == null)
            {
                // Try first child with children as armature
                foreach (Transform child in target.transform)
                {
                    if (child.childCount > 0 && child.GetComponent<SkinnedMeshRenderer>() == null)
                    {
                        armatureRoot = child;
                        break;
                    }
                }
            }

            if (armatureRoot == null)
                return Error($"No armature root found on '{target.name}'. Expected child named 'Armature' or similar.");

            // Traverse bone hierarchy
            var allBones = armatureRoot.GetComponentsInChildren<Transform>(true);
            int boneCount = allBones.Length;
            int maxDepth = 0;

            var boneTree = BuildBoneTree(armatureRoot, 0, ref maxDepth);

            // Check for humanoid rig mapping
            var animator = target.GetComponent<Animator>();
            JToken humanoidMapping = null;
            bool isHumanoid = false;

            if (animator != null && animator.avatar != null && animator.avatar.isHuman)
            {
                isHumanoid = true;
                var mappings = new JObject();
                foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if (bone == HumanBodyBones.LastBone) continue;
                    var boneTransform = animator.GetBoneTransform(bone);
                    if (boneTransform != null)
                    {
                        mappings[bone.ToString()] = boneTransform.name;
                    }
                }
                humanoidMapping = mappings;
            }

            // Find PhysBone chains
            var physBoneChains = new JArray();
            var physBoneType = ResolveType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone");
            if (physBoneType != null)
            {
                var physBones = target.GetComponentsInChildren(physBoneType, true);
                foreach (var pb in physBones)
                {
                    var so = new SerializedObject(pb);
                    var rootTransformProp = so.FindProperty("rootTransform");
                    Transform pbRoot = rootTransformProp?.objectReferenceValue as Transform ?? pb.transform;
                    int chainLength = pbRoot.GetComponentsInChildren<Transform>(true).Length;

                    physBoneChains.Add(JToken.FromObject(new
                    {
                        gameObject = pb.gameObject.name,
                        rootBone = pbRoot.name,
                        path = GetFullPath(pbRoot),
                        chainBoneCount = chainLength
                    }));
                    so.Dispose();
                }
            }

            // Find SkinnedMeshRenderers and their bone references
            var meshes = target.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var meshInfo = new JArray();
            foreach (var smr in meshes)
            {
                meshInfo.Add(JToken.FromObject(new
                {
                    name = smr.name,
                    path = GetFullPath(smr.transform),
                    boneCount = smr.bones?.Length ?? 0,
                    vertexCount = smr.sharedMesh?.vertexCount ?? 0,
                    blendShapeCount = smr.sharedMesh?.blendShapeCount ?? 0,
                    materialCount = smr.sharedMaterials?.Length ?? 0,
                    rootBone = smr.rootBone?.name
                }));
            }

            return Success(JToken.FromObject(new
            {
                gameObject = target.name,
                armatureRoot = armatureRoot.name,
                boneCount,
                maxDepth,
                isHumanoid,
                humanoidMapping,
                physBoneChainCount = physBoneChains.Count,
                physBoneChains,
                skinnedMeshCount = meshInfo.Count,
                skinnedMeshes = meshInfo,
                boneTree
            }));
        }

        private static JToken BuildBoneTree(Transform bone, int depth, ref int maxDepth)
        {
            if (depth > maxDepth) maxDepth = depth;

            var children = new JArray();
            foreach (Transform child in bone)
            {
                children.Add(BuildBoneTree(child, depth + 1, ref maxDepth));
            }

            return JToken.FromObject(new
            {
                name = bone.name,
                depth,
                childCount = bone.childCount,
                children
            });
        }

        // ───────── scan_avatar ─────────

        private static AutonomousMcpToolResponse HandleScanAvatar(JObject args)
        {
            var target = ResolveGameObject(args);
            if (target == null)
                return Error("scan_avatar requires a valid target by instanceId or name.");

            var result = new JObject
            {
                ["gameObject"] = target.name,
                ["instanceId"] = target.GetInstanceID()
            };

            // Detect VRC Avatar Descriptor
            var descriptorType = ResolveType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            Component descriptor = null;
            if (descriptorType != null)
            {
                descriptor = target.GetComponent(descriptorType);
                if (descriptor != null)
                {
                    var so = new SerializedObject(descriptor);

                    // Basic descriptor info
                    result["hasAvatarDescriptor"] = true;

                    // Lip sync
                    var lipSyncProp = so.FindProperty("lipSync");
                    if (lipSyncProp != null)
                        result["lipSyncType"] = lipSyncProp.enumNames.Length > lipSyncProp.enumValueIndex && lipSyncProp.enumValueIndex >= 0
                            ? lipSyncProp.enumNames[lipSyncProp.enumValueIndex] : lipSyncProp.enumValueIndex.ToString();

                    var lipSyncMeshProp = so.FindProperty("VisemeSkinnedMesh");
                    if (lipSyncMeshProp?.objectReferenceValue != null)
                        result["visemeMesh"] = lipSyncMeshProp.objectReferenceValue.name;

                    // View position
                    var viewPosProp = so.FindProperty("ViewPosition");
                    if (viewPosProp != null)
                        result["viewPosition"] = new JObject
                        {
                            ["x"] = viewPosProp.vector3Value.x,
                            ["y"] = viewPosProp.vector3Value.y,
                            ["z"] = viewPosProp.vector3Value.z
                        };

                    // Expression parameters
                    var exprParamsProp = so.FindProperty("expressionParameters");
                    if (exprParamsProp?.objectReferenceValue != null)
                    {
                        var paramAsset = exprParamsProp.objectReferenceValue;
                        var paramSO = new SerializedObject(paramAsset);
                        var paramsList = paramSO.FindProperty("parameters");
                        if (paramsList != null && paramsList.isArray)
                        {
                            int paramCount = paramsList.arraySize;
                            int totalCost = 0;
                            var paramEntries = new JArray();
                            for (int i = 0; i < paramCount; i++)
                            {
                                var elem = paramsList.GetArrayElementAtIndex(i);
                                var paramName = elem.FindPropertyRelative("name")?.stringValue ?? "";
                                var paramType = elem.FindPropertyRelative("valueType");
                                int cost = 0;
                                string typeName = "Unknown";
                                if (paramType != null)
                                {
                                    // VRC parameter types: 0=Int(8), 1=Float(8), 2=Bool(1)
                                    switch (paramType.intValue)
                                    {
                                        case 0: cost = 8; typeName = "Int"; break;
                                        case 1: cost = 8; typeName = "Float"; break;
                                        case 2: cost = 1; typeName = "Bool"; break;
                                    }
                                }
                                totalCost += cost;
                                if (!string.IsNullOrEmpty(paramName))
                                {
                                    paramEntries.Add(JToken.FromObject(new
                                    {
                                        name = paramName,
                                        type = typeName,
                                        cost
                                    }));
                                }
                            }
                            result["expressionParameterCount"] = paramCount;
                            result["expressionParameterCost"] = totalCost;
                            result["expressionParameterBudget"] = 256;
                            result["expressionParameterRemaining"] = 256 - totalCost;
                            result["expressionParameters"] = paramEntries;
                        }
                        paramSO.Dispose();
                    }

                    // Expression menus
                    var exprMenuProp = so.FindProperty("expressionsMenu");
                    if (exprMenuProp?.objectReferenceValue != null)
                    {
                        result["hasExpressionsMenu"] = true;
                        result["expressionsMenuAsset"] = AssetDatabase.GetAssetPath(exprMenuProp.objectReferenceValue);
                    }

                    so.Dispose();
                }
                else
                {
                    result["hasAvatarDescriptor"] = false;
                    result["warning"] = "No VRCAvatarDescriptor found. This is required for VRChat avatars.";
                }
            }
            else
            {
                result["hasAvatarDescriptor"] = false;
                result["note"] = "VRChat SDK not detected in project.";
            }

            // Detect PhysBones
            var physBoneType = ResolveType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone");
            if (physBoneType != null)
            {
                var physBones = target.GetComponentsInChildren(physBoneType, true);
                result["physBoneCount"] = physBones.Length;

                var pbColliderType = ResolveType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider");
                if (pbColliderType != null)
                {
                    var colliders = target.GetComponentsInChildren(pbColliderType, true);
                    result["physBoneColliderCount"] = colliders.Length;
                }
            }

            // Detect Contact Receivers/Senders
            var contactReceiverType = ResolveType("VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver");
            var contactSenderType = ResolveType("VRC.SDK3.Dynamics.Contact.Components.VRCContactSender");
            if (contactReceiverType != null)
                result["contactReceiverCount"] = target.GetComponentsInChildren(contactReceiverType, true).Length;
            if (contactSenderType != null)
                result["contactSenderCount"] = target.GetComponentsInChildren(contactSenderType, true).Length;

            // Detect ecosystem frameworks
            var installedFrameworks = new JArray();

            // Modular Avatar
            var maType = ResolveType("nadena.dev.modular_avatar.core.AvatarTagComponent");
            if (maType != null)
            {
                var maComponents = target.GetComponentsInChildren(maType, true);
                if (maComponents.Length > 0)
                {
                    var maTypes = new JArray();
                    foreach (var c in maComponents)
                    {
                        var typeName = c.GetType().Name;
                        if (!maTypes.Any(t => t.ToString() == typeName))
                            maTypes.Add(typeName);
                    }
                    installedFrameworks.Add(JToken.FromObject(new
                    {
                        framework = "Modular Avatar",
                        componentCount = maComponents.Length,
                        componentTypes = maTypes
                    }));
                }
            }

            // VRCFury
            var vrcfType = ResolveType("VF.Model.VRCFury");
            if (vrcfType != null)
            {
                var vrcfComponents = target.GetComponentsInChildren(vrcfType, true);
                if (vrcfComponents.Length > 0)
                {
                    installedFrameworks.Add(JToken.FromObject(new
                    {
                        framework = "VRCFury",
                        componentCount = vrcfComponents.Length
                    }));
                }
            }

            // AAO (Avatar Optimizer)
            var aaoType = ResolveType("Anatawa12.AvatarOptimizer.TraceAndOptimize");
            if (aaoType != null)
            {
                var aaoComponents = target.GetComponentsInChildren(aaoType, true);
                installedFrameworks.Add(JToken.FromObject(new
                {
                    framework = "AAO: Avatar Optimizer",
                    hasTraceAndOptimize = aaoComponents.Length > 0
                }));
            }

            // lilycalInventory
            var liType = ResolveType("lilycalInventory.Runtime.LIToggleItem");
            if (liType == null) liType = ResolveType("LIToggleItem");
            if (liType != null)
            {
                var liComponents = target.GetComponentsInChildren(liType, true);
                if (liComponents.Length > 0)
                {
                    installedFrameworks.Add(JToken.FromObject(new
                    {
                        framework = "lilycalInventory",
                        toggleCount = liComponents.Length
                    }));
                }
            }

            result["installedFrameworks"] = installedFrameworks;

            // Mesh stats
            var skinnedMeshes = target.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var meshRenderers = target.GetComponentsInChildren<MeshRenderer>(true);
            int totalPolygons = 0;
            int totalMaterials = 0;
            int totalBlendShapes = 0;

            foreach (var smr in skinnedMeshes)
            {
                if (smr.sharedMesh != null)
                {
                    totalPolygons += smr.sharedMesh.triangles.Length / 3;
                    totalBlendShapes += smr.sharedMesh.blendShapeCount;
                }
                totalMaterials += smr.sharedMaterials?.Length ?? 0;
            }
            foreach (var mr in meshRenderers)
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf?.sharedMesh != null)
                    totalPolygons += mf.sharedMesh.triangles.Length / 3;
                totalMaterials += mr.sharedMaterials?.Length ?? 0;
            }

            result["meshStats"] = JToken.FromObject(new
            {
                skinnedMeshRendererCount = skinnedMeshes.Length,
                meshRendererCount = meshRenderers.Length,
                totalPolygons,
                totalMaterials,
                totalBlendShapes
            });

            // Bone count
            var allTransforms = target.GetComponentsInChildren<Transform>(true);
            result["totalBoneCount"] = allTransforms.Length;

            // Shader usage
            var shaderUsage = new Dictionary<string, int>();
            foreach (var smr in skinnedMeshes)
            {
                foreach (var mat in smr.sharedMaterials)
                {
                    if (mat?.shader != null)
                    {
                        var shaderName = mat.shader.name;
                        shaderUsage[shaderName] = shaderUsage.ContainsKey(shaderName) ? shaderUsage[shaderName] + 1 : 1;
                    }
                }
            }
            var shaderList = new JArray();
            foreach (var kv in shaderUsage.OrderByDescending(kv => kv.Value))
            {
                shaderList.Add(JToken.FromObject(new { shader = kv.Key, materialCount = kv.Value }));
            }
            result["shaderUsage"] = shaderList;

            return Success(result);
        }

        private static AutonomousMcpToolResponse HandleValidateScript(JObject args)
        {
            var strict = args.Value<bool?>("strict") ?? false;
            AssetDatabase.Refresh();

            return Success(JToken.FromObject(new
            {
                strict,
                isCompiling = EditorApplication.isCompiling,
                message = "Compilation refresh triggered."
            }));
        }

        private static AutonomousMcpToolResponse HandleRunTests(JObject args)
        {
            var mode = args.Value<string>("mode") ?? "editmode";

            try
            {
                var jobId = AutonomousMcpTestRunner.Run(mode);
                return Success(JToken.FromObject(new
                {
                    mode,
                    status = "queued",
                    jobId,
                    next = "Call get_test_job with jobId until status is completed/failed."
                }));
            }
            catch (Exception ex)
            {
                return Error($"Failed to start Unity Test Runner: {ex.Message}");
            }
        }

        private static AutonomousMcpToolResponse HandleGetTestJob(JObject args)
        {
            var jobId = args.Value<string>("jobId") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(jobId))
            {
                return Error("get_test_job requires a non-empty jobId.");
            }

            if (!AutonomousMcpTestJobs.TryGet(jobId, out var job))
            {
                return Error($"Unknown test job '{jobId}'.");
            }

            return Success(JToken.FromObject(new
            {
                job = job.Snapshot()
            }));
        }

        // ───────── manage_scriptable_object ─────────

        private static AutonomousMcpToolResponse HandleManageScriptableObject(JObject args)
        {
            var action = args.Value<string>("action") ?? string.Empty;

            switch (action)
            {
                case "find":
                    return HandleFindScriptableObjects(args);
                case "get_properties":
                    return HandleGetScriptableObjectProperties(args);
                case "set_property":
                    return HandleSetScriptableObjectProperty(args);
                case "create":
                    return HandleCreateScriptableObject(args);
                case "list_fields":
                    return HandleListScriptableObjectFields(args);
                default:
                    return Error($"Unsupported manage_scriptable_object action '{action}'.");
            }
        }

        private static AutonomousMcpToolResponse HandleFindScriptableObjects(JObject args)
        {
            var filter = args.Value<string>("filter") ?? args.Value<string>("search") ?? "t:ScriptableObject";
            if (!filter.Contains("t:")) filter = $"t:ScriptableObject {filter}";

            var guids = AssetDatabase.FindAssets(filter);
            var results = new JArray();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (asset == null) continue;

                results.Add(JToken.FromObject(new
                {
                    name = asset.name,
                    type = asset.GetType().Name,
                    fullType = asset.GetType().FullName,
                    path,
                    instanceId = asset.GetInstanceID()
                }));

                if (results.Count >= 100) break;
            }

            return Success(JToken.FromObject(new
            {
                action = "find",
                filter,
                count = results.Count,
                assets = results
            }));
        }

        private static ScriptableObject ResolveScriptableObject(JObject args)
        {
            var assetPath = args.Value<string>("asset_path") ?? args.Value<string>("path") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(assetPath))
                return AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);

            var instanceId = args.Value<int?>("instanceId") ?? args.Value<int?>("instance_id");
            if (instanceId.HasValue)
                return EditorUtility.InstanceIDToObject(instanceId.Value) as ScriptableObject;

            return null;
        }

        private static AutonomousMcpToolResponse HandleGetScriptableObjectProperties(JObject args)
        {
            var so = ResolveScriptableObject(args);
            if (so == null)
                return Error("get_properties requires a valid ScriptableObject (asset_path or instanceId).");

            var serialized = new SerializedObject(so);
            var propList = new JArray();
            var iterator = serialized.GetIterator();
            bool enter = true;

            while (iterator.NextVisible(enter))
            {
                enter = false;
                if (iterator.name == "m_Script") continue;

                propList.Add(JToken.FromObject(new
                {
                    name = iterator.name,
                    displayName = iterator.displayName,
                    type = iterator.propertyType.ToString(),
                    value = ReadSerializedPropertyValue(iterator),
                    depth = iterator.depth
                }));
            }

            return Success(JToken.FromObject(new
            {
                action = "get_properties",
                name = so.name,
                type = so.GetType().Name,
                path = AssetDatabase.GetAssetPath(so),
                count = propList.Count,
                properties = propList
            }));
        }

        private static AutonomousMcpToolResponse HandleSetScriptableObjectProperty(JObject args)
        {
            var so = ResolveScriptableObject(args);
            if (so == null)
                return Error("set_property requires a valid ScriptableObject (asset_path or instanceId).");

            var propName = args.Value<string>("property") ?? args.Value<string>("property_name") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(propName))
                return Error("set_property requires a non-empty property name.");

            var serialized = new SerializedObject(so);
            var prop = serialized.FindProperty(propName);
            if (prop == null)
                return Error($"Property '{propName}' not found on ScriptableObject '{so.name}'.");

            var valueToken = args["value"];
            if (valueToken == null)
                return Error("set_property requires a 'value' parameter.");

            Undo.RecordObject(so, "MCP: Set ScriptableObject Property");

            bool written = WriteSerializedPropertyValue(prop, valueToken);
            if (!written)
                return Error($"Could not write to property '{propName}' (type: {prop.propertyType}).");

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(so);

            return Success(JToken.FromObject(new
            {
                action = "set_property",
                name = so.name,
                property = propName,
                type = prop.propertyType.ToString()
            }));
        }

        private static AutonomousMcpToolResponse HandleCreateScriptableObject(JObject args)
        {
            var typeName = args.Value<string>("type") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(typeName))
                return Error("create requires a 'type' (e.g. 'MyScriptableObject' or full 'Namespace.MyScriptableObject').");

            var savePath = args.Value<string>("path") ?? args.Value<string>("asset_path") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(savePath))
                return Error("create requires a 'path' (e.g. 'Assets/Data/MyConfig.asset').");

            var type = ResolveType(typeName);
            if (type == null || !typeof(ScriptableObject).IsAssignableFrom(type))
                return Error($"Type '{typeName}' not found or not a ScriptableObject.");

            var instance = ScriptableObject.CreateInstance(type);
            if (instance == null)
                return Error($"Failed to create instance of '{typeName}'.");

            var directory = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            AssetDatabase.CreateAsset(instance, savePath);
            AssetDatabase.SaveAssets();

            return Success(JToken.FromObject(new
            {
                action = "create",
                type = type.FullName,
                path = savePath,
                instanceId = instance.GetInstanceID()
            }));
        }

        private static AutonomousMcpToolResponse HandleListScriptableObjectFields(JObject args)
        {
            var typeName = args.Value<string>("type") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(typeName))
                return Error("list_fields requires a 'type' name.");

            var type = ResolveType(typeName);
            if (type == null || !typeof(ScriptableObject).IsAssignableFrom(type))
                return Error($"Type '{typeName}' not found or not a ScriptableObject.");

            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => f.IsPublic || f.GetCustomAttribute<SerializeField>() != null)
                .ToArray();

            var fieldList = new JArray();
            foreach (var f in fields)
            {
                fieldList.Add(JToken.FromObject(new
                {
                    name = f.Name,
                    type = f.FieldType.Name,
                    fullType = f.FieldType.FullName,
                    isPublic = f.IsPublic,
                    hasSerializeField = f.GetCustomAttribute<SerializeField>() != null
                }));
            }

            return Success(JToken.FromObject(new
            {
                action = "list_fields",
                type = type.FullName,
                count = fieldList.Count,
                fields = fieldList
            }));
        }

        // ───────── manage_texture ─────────

        private static AutonomousMcpToolResponse HandleManageTexture(JObject args)
        {
            var action = args.Value<string>("action") ?? string.Empty;

            switch (action)
            {
                case "get_import_settings":
                    return HandleGetTextureImportSettings(args);
                case "set_import_settings":
                    return HandleSetTextureImportSettings(args);
                case "get_info":
                    return HandleGetTextureInfo(args);
                case "find_textures":
                    return HandleFindTextures(args);
                default:
                    return Error($"Unsupported manage_texture action '{action}'.");
            }
        }

        private static TextureImporter ResolveTextureImporter(JObject args)
        {
            var assetPath = args.Value<string>("asset_path") ?? args.Value<string>("path") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(assetPath)) return null;
            return AssetImporter.GetAtPath(assetPath) as TextureImporter;
        }

        private static AutonomousMcpToolResponse HandleGetTextureImportSettings(JObject args)
        {
            var importer = ResolveTextureImporter(args);
            if (importer == null)
                return Error("get_import_settings requires a valid texture asset_path.");

            var platformAndroid = importer.GetPlatformTextureSettings("Android");

            return Success(JToken.FromObject(new
            {
                action = "get_import_settings",
                path = importer.assetPath,
                textureType = importer.textureType.ToString(),
                textureShape = importer.textureShape.ToString(),
                sRGBTexture = importer.sRGBTexture,
                alphaSource = importer.alphaSource.ToString(),
                alphaIsTransparency = importer.alphaIsTransparency,
                maxTextureSize = importer.maxTextureSize,
                textureCompression = importer.textureCompression.ToString(),
                crunchedCompression = importer.crunchCompression,
                compressionQuality = importer.compressionQuality,
                filterMode = importer.filterMode.ToString(),
                wrapMode = importer.wrapMode.ToString(),
                anisoLevel = importer.anisoLevel,
                mipmapEnabled = importer.mipmapEnabled,
                isReadable = importer.isReadable,
                npotScale = importer.npotScale.ToString(),
                androidOverride = platformAndroid.overridden ? new
                {
                    maxSize = platformAndroid.maxTextureSize,
                    format = platformAndroid.format.ToString(),
                    compressionQuality = platformAndroid.compressionQuality
                } : null
            }));
        }

        private static AutonomousMcpToolResponse HandleSetTextureImportSettings(JObject args)
        {
            var importer = ResolveTextureImporter(args);
            if (importer == null)
                return Error("set_import_settings requires a valid texture asset_path.");

            bool changed = false;

            var maxSize = args.Value<int?>("max_texture_size") ?? args.Value<int?>("maxTextureSize");
            if (maxSize.HasValue) { importer.maxTextureSize = maxSize.Value; changed = true; }

            var compression = args.Value<string>("texture_compression") ?? args.Value<string>("compression");
            if (!string.IsNullOrEmpty(compression))
            {
                if (Enum.TryParse<TextureImporterCompression>(compression, true, out var comp))
                { importer.textureCompression = comp; changed = true; }
            }

            var crunch = args.Value<bool?>("crunch_compression") ?? args.Value<bool?>("crunchedCompression");
            if (crunch.HasValue) { importer.crunchCompression = crunch.Value; changed = true; }

            var quality = args.Value<int?>("compression_quality") ?? args.Value<int?>("compressionQuality");
            if (quality.HasValue) { importer.compressionQuality = quality.Value; changed = true; }

            var srgb = args.Value<bool?>("sRGB") ?? args.Value<bool?>("srgb");
            if (srgb.HasValue) { importer.sRGBTexture = srgb.Value; changed = true; }

            var readable = args.Value<bool?>("is_readable") ?? args.Value<bool?>("isReadable");
            if (readable.HasValue) { importer.isReadable = readable.Value; changed = true; }

            var mipmap = args.Value<bool?>("mipmap_enabled") ?? args.Value<bool?>("mipmapEnabled");
            if (mipmap.HasValue) { importer.mipmapEnabled = mipmap.Value; changed = true; }

            var filterStr = args.Value<string>("filter_mode") ?? args.Value<string>("filterMode");
            if (!string.IsNullOrEmpty(filterStr))
            {
                if (Enum.TryParse<FilterMode>(filterStr, true, out var fm))
                { importer.filterMode = fm; changed = true; }
            }

            var aniso = args.Value<int?>("aniso_level") ?? args.Value<int?>("anisoLevel");
            if (aniso.HasValue) { importer.anisoLevel = aniso.Value; changed = true; }

            var texType = args.Value<string>("texture_type") ?? args.Value<string>("textureType");
            if (!string.IsNullOrEmpty(texType))
            {
                if (Enum.TryParse<TextureImporterType>(texType, true, out var tt))
                { importer.textureType = tt; changed = true; }
            }

            if (!changed)
                return Error("No valid settings provided to change.");

            importer.SaveAndReimport();

            return Success(JToken.FromObject(new
            {
                action = "set_import_settings",
                path = importer.assetPath,
                settingsChanged = changed
            }));
        }

        private static AutonomousMcpToolResponse HandleGetTextureInfo(JObject args)
        {
            var assetPath = args.Value<string>("asset_path") ?? args.Value<string>("path") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(assetPath))
                return Error("get_info requires an asset_path.");

            var texture = AssetDatabase.LoadAssetAtPath<Texture>(assetPath);
            if (texture == null)
                return Error($"No Texture found at '{assetPath}'.");

            var tex2d = texture as Texture2D;

            return Success(JToken.FromObject(new
            {
                action = "get_info",
                path = assetPath,
                name = texture.name,
                type = texture.GetType().Name,
                width = texture.width,
                height = texture.height,
                filterMode = texture.filterMode.ToString(),
                wrapMode = texture.wrapMode.ToString(),
                anisoLevel = texture.anisoLevel,
                format = tex2d?.format.ToString(),
                mipmapCount = tex2d?.mipmapCount,
                isReadable = tex2d?.isReadable,
                instanceId = texture.GetInstanceID()
            }));
        }

        private static AutonomousMcpToolResponse HandleFindTextures(JObject args)
        {
            var filter = args.Value<string>("filter") ?? args.Value<string>("search") ?? "t:Texture2D";
            if (!filter.Contains("t:")) filter = $"t:Texture2D {filter}";

            var guids = AssetDatabase.FindAssets(filter);
            var results = new JArray();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var tex = AssetDatabase.LoadAssetAtPath<Texture>(path);
                if (tex == null) continue;

                results.Add(JToken.FromObject(new
                {
                    name = tex.name,
                    path,
                    width = tex.width,
                    height = tex.height,
                    type = tex.GetType().Name
                }));

                if (results.Count >= 100) break;
            }

            return Success(JToken.FromObject(new
            {
                action = "find_textures",
                filter,
                count = results.Count,
                textures = results
            }));
        }

        // ───────── refresh_unity ─────────

        private static AutonomousMcpToolResponse HandleRefreshUnity(JObject args)
        {
            var importAll = args.Value<bool?>("import_all") ?? false;

            if (importAll)
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            else
                AssetDatabase.Refresh();

            return Success(JToken.FromObject(new
            {
                action = "refresh",
                importAll,
                isCompiling = EditorApplication.isCompiling
            }));
        }

        // ───────── batch_execute ─────────

        private static AutonomousMcpToolResponse HandleBatchExecute(JObject args, int depth)
        {
            if (depth >= MaxBatchDepth)
            {
                return Error("batch_execute depth limit reached.");
            }

            var operations = args["operations"] as JArray;
            if (operations == null)
            {
                return Error("batch_execute requires operations array.");
            }

            var results = new JArray();
            foreach (var token in operations)
            {
                if (token is not JObject operation)
                {
                    results.Add(new JObject
                    {
                        ["success"] = false,
                        ["error"] = "Operation is not an object."
                    });
                    continue;
                }

                var tool = operation.Value<string>("tool") ?? string.Empty;
                var operationParams = operation["params"] as JObject ?? new JObject();

                var response = DispatchOnMainThread(new AutonomousMcpEnvelope
                {
                    requestId = string.Empty,
                    tool = tool,
                    @params = operationParams
                }, depth + 1);

                results.Add(JToken.FromObject(response));
            }

            return Success(new JObject
            {
                ["count"] = operations.Count,
                ["results"] = results
            });
        }

        private static AutonomousMcpToolResponse Success(JToken data)
        {
            return new AutonomousMcpToolResponse
            {
                success = true,
                data = data,
                error = string.Empty
            };
        }

        private static AutonomousMcpToolResponse Error(string message)
        {
            return new AutonomousMcpToolResponse
            {
                success = false,
                data = null,
                error = message
            };
        }
    }
}
