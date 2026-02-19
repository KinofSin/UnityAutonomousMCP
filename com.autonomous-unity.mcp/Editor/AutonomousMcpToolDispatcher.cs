using System;
using System.Collections.Generic;
using System.IO;
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
                    "manage_script",
                    "validate_script",
                    "run_tests",
                    "get_test_job",
                    "batch_execute"
                },
                supportedActions = new
                {
                    manage_scene = new[] { "inspect_active_scene", "save_active_scene" },
                    manage_gameobject = new[] { "create", "create_empty", "create_primitive", "find", "find_by_name", "find_contains", "set_transform", "get_world_transform", "reparent", "get_children", "get_parent", "get_full_hierarchy", "set_active", "rename", "destroy" },
                    manage_script = new[] { "create_or_update" }
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
