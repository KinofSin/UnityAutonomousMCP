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
                    var saved = EditorSceneManager.SaveScene(scene);
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
                case "create_primitive":
                    return HandleCreateGameObject(args, action);
                case "find":
                case "find_by_name":
                    return HandleFindGameObject(args, action);
                case "destroy":
                    return HandleDestroyGameObject(args, action);
                case "set_transform":
                    return HandleSetGameObjectTransform(args, action);
                default:
                    return Error($"Unsupported manage_gameobject action '{action}'.");
            }
        }

        private static AutonomousMcpToolResponse HandleCreateGameObject(JObject args, string action)
        {
            var name = args.Value<string>("name") ?? "AgentGameObject";
            var primitiveTypeRaw = args.Value<string>("primitiveType") ?? args.Value<string>("primitive_type");

            GameObject created;
            if (!string.IsNullOrWhiteSpace(primitiveTypeRaw) && Enum.TryParse(primitiveTypeRaw, true, out PrimitiveType primitiveType))
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
            if (!string.IsNullOrWhiteSpace(parentName))
            {
                var parent = GameObject.Find(parentName);
                if (parent != null)
                {
                    created.transform.SetParent(parent.transform, false);
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
                position = created.transform.position,
                rotation = created.transform.eulerAngles,
                scale = created.transform.localScale
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
                instanceId = target.GetInstanceID(),
                activeSelf = target.activeSelf,
                position = target.transform.position,
                rotation = target.transform.eulerAngles,
                scale = target.transform.localScale
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
            UnityEngine.Object.DestroyImmediate(target);

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

            target.transform.position = ReadVector3(args["position"], target.transform.position);
            var euler = ReadVector3(args["rotation"], target.transform.eulerAngles);
            target.transform.rotation = Quaternion.Euler(euler);
            target.transform.localScale = ReadVector3(args["scale"], target.transform.localScale);

            return Success(JToken.FromObject(new
            {
                action,
                name = target.name,
                instanceId = target.GetInstanceID(),
                position = target.transform.position,
                rotation = target.transform.eulerAngles,
                scale = target.transform.localScale
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
