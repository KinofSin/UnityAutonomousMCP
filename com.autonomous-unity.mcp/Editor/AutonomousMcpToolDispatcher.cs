using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
                    "validate_script",
                    "run_tests",
                    "get_test_job",
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
                    capture_screenshot = new[] { "scene", "game" }
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

            if (string.IsNullOrWhiteSpace(scriptPath) || !scriptPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return Error("scriptPath must start with 'Assets/'.");
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
            if (string.IsNullOrWhiteSpace(scriptPath) || !scriptPath.StartsWith("Assets/", StringComparison.Ordinal))
                return Error("read_script requires scriptPath starting with 'Assets/'.");

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
