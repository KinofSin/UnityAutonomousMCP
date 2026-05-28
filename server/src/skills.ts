export interface Skill {
  id: string;
  name: string;
  category: string;
  description: string;
  systemPrompt: string;
  recommendedTools: string[];
  requiredPackages: string[];
  examples?: string[];
}

const SKILLS: Skill[] = [
  {
    id: "vrchat-avatar",
    name: "VRChat avatar audit",
    category: "vrchat",
    description: "Inspect a VRChat avatar (descriptor, params, meshes, shaders) and surface common issues.",
    systemPrompt:
      "You are a VRChat avatar engineer. Use scan_avatar to capture the avatar; cross-reference against unity_validation and unity_optimization findings. Do not mutate the project without explicit user approval.",
    recommendedTools: ["scan_avatar", "scan_armature", "unity_validation", "unity_optimization"],
    requiredPackages: ["com.vrchat.avatars"],
    examples: [
      "Run scan_avatar, then unity_validation.audit_active_scene, then summarize per-mesh polycount.",
    ],
  },
  {
    id: "vrchat-upload-recipe",
    name: "VRChat upload pre-flight",
    category: "vrchat",
    description: "Pre-flight checklist before a VRChat avatar upload: scan, validate, optimize.",
    systemPrompt:
      "Run scan_avatar, unity_validation.audit_active_scene, and unity_optimization.scene_summary in order. Report findings grouped by severity (Block / Warn / Info). Never auto-fix without approval.",
    recommendedTools: ["scan_avatar", "unity_validation", "unity_optimization", "manage_checkpoint"],
    requiredPackages: ["com.vrchat.avatars"],
  },
  {
    id: "vrchat-quest",
    name: "VRChat Quest optimization",
    category: "vrchat",
    description: "Audit + suggest optimizations to make an avatar Quest-ready.",
    systemPrompt:
      "Use scan_avatar, then unity_optimization with actions mesh_audit, texture_audit, and oversized_textures. Group findings by severity (Block / Warn / Info) and suggest mitigations.",
    recommendedTools: ["scan_avatar", "unity_optimization", "unity_validation", "list_shaders"],
    requiredPackages: ["com.vrchat.avatars"],
  },
  {
    id: "vrcfury",
    name: "VRCFury workflow",
    category: "vrchat",
    description: "Author and inspect VRCFury components on VRChat avatars.",
    systemPrompt:
      "Use manage_component and scan_avatar to inspect VRCFury setups. Treat any change to existing VRCFury configs as destructive and require user approval.",
    recommendedTools: ["manage_component", "scan_avatar", "manage_prefab"],
    requiredPackages: ["com.vrcfury.vrcfury"],
  },
  {
    id: "modular-avatar",
    name: "Modular Avatar workflow",
    category: "vrchat",
    description: "Author Modular Avatar components and merge prefabs.",
    systemPrompt:
      "Use manage_component to add Modular Avatar components. Validate merges with scan_avatar after each change.",
    recommendedTools: ["manage_component", "scan_avatar", "manage_prefab"],
    requiredPackages: ["nadena.dev.modular-avatar"],
  },
  {
    id: "poiyomi",
    name: "Poiyomi shader workflow",
    category: "vrchat",
    description: "Inspect and tune Poiyomi materials on a VRChat avatar.",
    systemPrompt:
      "Use list_shaders, manage_material, and get_asset_info to inspect Poiyomi materials. Mutations require explicit approval.",
    recommendedTools: ["list_shaders", "manage_material", "get_asset_info"],
    requiredPackages: ["com.poiyomi.toon"],
  },
  {
    id: "cinemachine",
    name: "Cinemachine camera workflow",
    category: "unity-core",
    description: "Author Cinemachine virtual cameras and brain setups.",
    systemPrompt:
      "Use manage_component to add CinemachineBrain and CinemachineVirtualCamera. Inspect with get_component_properties.",
    recommendedTools: ["manage_component", "manage_gameobject", "manage_scene"],
    requiredPackages: ["com.unity.cinemachine"],
  },
  {
    id: "ui-toolkit",
    name: "Unity UI authoring",
    category: "unity-core",
    description: "Build canvases, text, buttons, and UI layouts.",
    systemPrompt:
      "Use manage_gameobject and manage_component to assemble UGUI hierarchies. Prefer Canvas + UI prefabs over ad-hoc setups.",
    recommendedTools: ["manage_gameobject", "manage_component", "manage_prefab"],
    requiredPackages: [],
  },
  {
    id: "physics",
    name: "Unity physics setup",
    category: "unity-core",
    description: "Configure rigidbodies, colliders, layers, and the physics matrix.",
    systemPrompt:
      "Use manage_component for Rigidbody/Collider tuning and manage_project_settings for the physics matrix. Validate via unity_validation.",
    recommendedTools: ["manage_component", "manage_project_settings", "manage_layer_tag"],
    requiredPackages: [],
  },
  {
    id: "performance",
    name: "Unity performance audit",
    category: "instruction",
    description: "Identify hot spots: triangle counts, texture sizes, draw calls.",
    systemPrompt:
      "Run unity_optimization with mesh_audit, texture_audit, draw_call_estimate. Surface the top offenders only.",
    recommendedTools: ["unity_optimization", "unity_profiler", "unity_validation"],
    requiredPackages: [],
  },
  {
    id: "mobile",
    name: "Mobile-target build hygiene",
    category: "instruction",
    description: "Audit settings for mobile/standalone target switches.",
    systemPrompt:
      "Use unity_build_manage to inspect defines and switch_target. Validate texture compression and shader support.",
    recommendedTools: ["unity_build_manage", "manage_project_settings", "list_shaders"],
    requiredPackages: [],
  },
  // ── Merged from the local Phase 0–7 catalog (12 skills not covered by the patch's set) ──
  {
    id: "vrchat-physbones",
    name: "VRChat PhysBones",
    category: "vrchat",
    description: "PhysBone chain setup, colliders, immobile types, allow grab/pose.",
    systemPrompt:
      "VRC PhysBone replaces DynamicBone for VRChat. Use Pull (0.2 default), Stiffness, Drag for natural motion. Set ImmobileType=Animation for clothing that should not slide. Allow Grab + Allow Pose for interactivity. Avoid >50 PhysBone components per avatar.",
    recommendedTools: ["scan_armature", "manage_component", "get_vrc_knowledge"],
    requiredPackages: ["com.vrchat.avatars"],
  },
  {
    id: "vrchat-expression-params",
    name: "VRChat Expression Parameters",
    category: "vrchat",
    description: "VRCExpressionParameters budgeting, sync vs local, parameter cost.",
    systemPrompt:
      "256-bit budget. Bool=1, Int/Float=8 bits. Unsync purely cosmetic params (faceblend, BOOP, PAT). Synced params are visible to remote players. Always show current/256 and the largest synced parameters when asked to audit.",
    recommendedTools: ["scan_avatar", "manage_scriptable_object"],
    requiredPackages: ["com.vrchat.avatars"],
  },
  {
    id: "osc-faceTracking",
    name: "OSC Face Tracking (VRCFT)",
    category: "vrchat",
    description: "VRCFaceTracking + ARKit/MediaPipe param mapping + OSC.",
    systemPrompt:
      "VRCFaceTracking runs as a desktop companion app over OSC port 9000/9001. Avatar must declare matching FT_ float parameters in the expression menu. ARKit blendshape names: BrowDownLeft, EyeBlinkLeft, etc. Use UnifiedExpressions when possible. Consult get_install_guide for VRCFT for the latest VPM repo.",
    recommendedTools: ["get_install_guide", "manage_component", "manage_scriptable_object"],
    requiredPackages: ["com.vrchat.face-tracking"],
  },
  {
    id: "fbt-stacks",
    name: "Full-Body Tracking Stacks",
    category: "vrchat",
    description: "SlimeVR, Vive trackers, Space Calibrator, IMU drift correction.",
    systemPrompt:
      "SlimeVR is the open-source FBT tracker stack with auto-calibration. Space Calibrator aligns OpenVR-driven trackers (Vive, Tundra) with non-OpenVR (SlimeVR, Mocopi). Always calibrate playspace first, then arm/leg sync after a few frames of motion. See get_install_guide section 'FBT Stacks'.",
    recommendedTools: ["get_install_guide", "get_vrc_knowledge"],
    requiredPackages: [],
  },
  {
    id: "unity-animator",
    name: "Unity Animator State Machines",
    category: "unity-core",
    description: "AnimatorController layers, states, transitions, parameters.",
    systemPrompt:
      "Use manage_animator.get_layers / get_states / set_parameter. Float/Int/Bool/Trigger. Avoid transition Has Exit Time when responsiveness matters. Default state must exist on every layer.",
    recommendedTools: ["manage_animator", "get_asset_info"],
    requiredPackages: [],
  },
  {
    id: "unity-navmesh",
    name: "Unity NavMesh + Agents",
    category: "unity-core",
    description: "Bake nav surfaces, agents, off-mesh links.",
    systemPrompt:
      "Use unity_navmesh.bake after geometry is final. NavMeshAgent needs Radius/Height matching capsule. For dynamic obstacles use NavMeshObstacle with carve=true. Modern projects can switch to com.unity.ai.navigation package for runtime baking.",
    recommendedTools: ["unity_navmesh", "manage_component"],
    requiredPackages: [],
  },
  {
    id: "unity-terrain",
    name: "Unity Terrain",
    category: "unity-core",
    description: "Terrain creation, sculpting heights, terrain layers.",
    systemPrompt:
      "Default heightmap resolution 513, alphamap 512. Flat terrains start with SetHeights to zeros. Use Terrain Layers for ground textures (tiled by tileSize).",
    recommendedTools: ["unity_terrain", "manage_component"],
    requiredPackages: [],
  },
  {
    id: "unity-timeline",
    name: "Unity Timeline Sequencing",
    category: "unity-core",
    description: "PlayableDirector, tracks, signals, control clips.",
    systemPrompt:
      "PlayableDirector + TimelineAsset are the entry points. unity_timeline.detect first. Tracks: AnimationTrack, AudioTrack, SignalTrack, ControlTrack.",
    recommendedTools: ["unity_timeline", "manage_component"],
    requiredPackages: ["com.unity.timeline"],
  },
  {
    id: "unity-testrunner",
    name: "Unity Test Framework / TDD",
    category: "unity-core",
    description: "EditMode/PlayMode tests, NUnit assertions, CI integration.",
    systemPrompt:
      "Use run_tests then poll get_test_job until terminal. EditMode tests live in Editor asmdef with com.unity.test-framework reference + UnityEditor + nunit.framework + UnityEngine.TestRunner. PlayMode tests need [UnityTest] attribute returning IEnumerator.",
    recommendedTools: ["run_tests", "get_test_job"],
    requiredPackages: ["com.unity.test-framework"],
  },
  {
    id: "csharp-pro",
    name: "Modern C# Patterns",
    category: "instruction",
    description: "Records, pattern matching, Span<T>, value tuples, SOLID.",
    systemPrompt:
      "Target C# 9.0+. Use records for immutable DTOs, init-only setters, switch expressions, pattern matching. Avoid LINQ in hot paths. Prefer Span<T>/Memory<T> for buffers. Apply Single Responsibility per class.",
    recommendedTools: ["manage_script", "validate_script", "inspect_type"],
    requiredPackages: [],
  },
  {
    id: "unity-async",
    name: "Unity Async / Main-Thread Rules",
    category: "instruction",
    description: "Coroutines vs Tasks, CancellationToken, Unity API main-thread constraint.",
    systemPrompt:
      "Unity API calls MUST run on the main thread. Use awaitable Tasks with await Task.Yield() to resume on main thread in Unity 2023+. Otherwise use coroutines or UniTask. ALWAYS pass a CancellationToken. Never async void unless required by event handler.",
    recommendedTools: ["manage_script", "execute_csharp"],
    requiredPackages: [],
  },
  {
    id: "unity-collection-pool",
    name: "Zero-GC Collections",
    category: "instruction",
    description: "ListPool, DictionaryPool, HashSetPool, ArrayPool patterns.",
    systemPrompt:
      "Use UnityEngine.Pool.ListPool<T>.Get() / Release() to eliminate GC allocations in Update/FixedUpdate. ArrayPool<T>.Shared for byte buffers. Reuse Material/MaterialPropertyBlock to avoid renderer.material allocations.",
    recommendedTools: ["manage_script"],
    requiredPackages: [],
  },
];

export function listSkills(opts?: { category?: string; search?: string }): Skill[] {
  const category = opts?.category?.toLowerCase();
  const search = opts?.search?.toLowerCase();
  return SKILLS.filter((skill: Skill) => {
    if (category && skill.category.toLowerCase() !== category) return false;
    if (search) {
      const haystack = `${skill.id} ${skill.name} ${skill.description} ${skill.systemPrompt}`.toLowerCase();
      if (!haystack.includes(search)) return false;
    }
    return true;
  });
}

export function findSkill(id: string): Skill | null {
  return SKILLS.find((skill: Skill) => skill.id === id) ?? null;
}

export type SkillInvocation =
  | { ok: true; inject: string }
  | { ok: false; error: string };

export function invokeSkill(id: string): SkillInvocation {
  const skill = findSkill(id);
  if (!skill) {
    return { ok: false, error: `Skill '${id}' not found. Use list_skills to discover ids.` };
  }
  const recommended = skill.recommendedTools.length > 0
    ? `\n\nRecommended tools:\n- ${skill.recommendedTools.join("\n- ")}`
    : "";
  const required = skill.requiredPackages.length > 0
    ? `\n\nRequired packages:\n- ${skill.requiredPackages.join("\n- ")}`
    : "";
  const examples = skill.examples && skill.examples.length > 0
    ? `\n\nExamples:\n- ${skill.examples.join("\n- ")}`
    : "";
  return {
    ok: true,
    inject: `# Skill: ${skill.name}\n\n${skill.systemPrompt}${recommended}${required}${examples}`,
  };
}
