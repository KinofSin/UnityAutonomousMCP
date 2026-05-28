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
