/**
 * VRChat ecosystem knowledge base — 150+ tools, conventions, best practices.
 * Queryable by category for autonomous agents working with VRChat avatars/worlds.
 */

export interface VrcTool {
  name: string;
  packageId?: string;
  url?: string;
  creator?: string;
  license?: string;
  description: string;
  bestFeature?: string;
  category: string;
}

export interface VrcCategory {
  id: string;
  title: string;
  description: string;
  bestPick: string;
  tools: VrcTool[];
  conventions?: string[];
}

const categories: VrcCategory[] = [
  // ── 1. Non-Destructive Avatar Assembly ──
  {
    id: "assembly",
    title: "Non-Destructive Avatar Assembly Frameworks",
    description: "Top-level systems for drag-and-drop avatar assembly at build time without destructive editing.",
    bestPick: "Modular Avatar (MA) — ecosystem standard. VRCFury as alternative (SPS, monolithic component).",
    tools: [
      { name: "Modular Avatar (MA)", packageId: "nadena.dev.modular-avatar", url: "https://modular-avatar.nadena.dev", creator: "bd_", license: "MIT", category: "assembly", description: "De facto standard. Merge Armature, Merge Animator, Menu Installer, Parameter Sync, Bone Proxy, Shape Changer, Object Toggle. Virtually every Booth.pm accessory is MA-compatible.", bestFeature: "Merge Armature — eliminates manual bone weighting" },
      { name: "VRCFury", packageId: "com.vrcfury.vrcfury", url: "https://vrcfury.com", creator: "VRCFury team", license: "OSS", category: "assembly", description: "Alternative to MA. Monolithic component with Armature Link, Toggle, Blink/Viseme Controller, SPS (Super Power System). Single inspector window for all features.", bestFeature: "SPS — built-in penetration/contact system" },
    ],
    conventions: [
      "Mount clothing/accessory prefabs as children of avatar root",
      "MA Merge Armature requires matching bone names between clothing and avatar armature",
      "VRCFury Armature Link is equivalent to MA Merge Armature",
      "Don't mix MA and VRCFury on the same prefab unless you understand ordering",
      "Both produce identical results at build time — source hierarchy is never modified",
    ],
  },
  // ── 2. Build Pipeline ──
  {
    id: "pipeline",
    title: "Build Pipeline Frameworks (NDMF)",
    description: "Foundational build pipeline that other tools hook into. Runs at avatar build time.",
    bestPick: "NDMF — the only option. Infrastructure, not a choice.",
    tools: [
      { name: "NDMF", packageId: "nadena.dev.ndmf", url: "https://github.com/bdunderscore/ndmf", creator: "bd_", license: "MIT", category: "pipeline", description: "Non-Destructive Modular Framework. Orchestrates all registered plugins in phase order: Resolving → Generating → Transforming → Optimizing. Powers MA, AAO, d4rk, FaceEmo, lilycalInventory, and dozens more.", bestFeature: "Deterministic phase-ordered build pipeline with dependency resolution" },
    ],
    conventions: [
      "NDMF phases: Resolving → Generating → Transforming → Optimizing",
      "MA merges armatures in Generating phase, AAO optimizes in Optimizing phase",
      "Build log shows exactly what each plugin did — check it for debugging",
    ],
  },
  // ── 3. Avatar Optimizers ──
  {
    id: "optimization",
    title: "Avatar Optimizers",
    description: "Remove, merge, or transform components/bones/meshes/materials to reduce performance cost.",
    bestPick: "AAO (primary) + d4rkAvatarOptimizer (secondary for cross-shader merging).",
    tools: [
      { name: "AAO: Avatar Optimizer", packageId: "com.anatawa12.avatar-optimizer", url: "https://vpm.anatawa12.com/avatar-optimizer/en/", creator: "anatawa12", license: "MIT", category: "optimization", description: "Trace & Optimize (automatic unreachable content removal), Merge Skinned Mesh, Merge Bone, Remove Mesh By BlendShape, Actual Performance Window.", bestFeature: "Trace & Optimize — single component auto-removes all unreachable content" },
      { name: "d4rkAvatarOptimizer", packageId: "com.github.d4rkc0d3r.d4rkAvatarOptimizer", url: "https://github.com/d4rkc0d3r/d4rkAvatarOptimizer", creator: "d4rkc0d3r", license: "OSS", category: "optimization", description: "Cross-shader material merging via generated shader code. NaN toggles for zero-draw-call hiding. More aggressive than AAO.", bestFeature: "Cross-shader material merging — unique in ecosystem" },
      { name: "Polytool", url: "https://markcreator.gumroad.com/l/Polytool", creator: "Markcreator", license: "Paid", category: "optimization", description: "All-in-one manual optimizer: decimation, mesh merging, atlassing, bone reduction in single window. Quest presets.", bestFeature: "Unified visual workflow for beginners" },
    ],
    conventions: [
      "Add AAO Trace & Optimize component to avatar root — it auto-detects everything",
      "AAO reads MA-installed toggles to compute reachability — they compose correctly",
      "d4rk shader merging can alter visual results — check matcap/outline after",
      "Run both AAO then d4rk for maximum optimization",
      "Use Actual Performance Window to check post-NDMF rank (not Unity's default)",
    ],
  },
  // ── 4. Polygon Reduction ──
  {
    id: "mesh_reduction",
    title: "Polygon / Mesh Reduction Tools",
    description: "Reduce raw triangle count via decimation or manual deletion.",
    bestPick: "lilNDMFMeshSimplifier (free/non-destructive) or Mantis LOD (quality-critical).",
    tools: [
      { name: "lilNDMFMeshSimplifier", packageId: "jp.lilxyzw.ndmfmeshsimplifier", url: "https://github.com/lilxyzw/lilNDMFMeshSimplifier", creator: "lilxyzw", license: "MIT", category: "mesh_reduction", description: "Build-time non-destructive polygon reducer. Quality slider 0-1. NDMF-integrated.", bestFeature: "Non-destructive NDMF polygon reduction" },
      { name: "Meshia", url: "https://booth.pm/ja/items/6944218", creator: "RamType0", license: "MIT", category: "mesh_reduction", description: "Real-time previewing polygon reducer. Live scene view preview as you adjust slider.", bestFeature: "Real-time preview of decimation" },
      { name: "MeshDeleterWithTexture", url: "https://booth.pm/ja/items/1501527", creator: "gatosyocora", license: "Free", category: "mesh_reduction", description: "Paint on texture preview to select and delete polygons. Remove hidden geometry.", bestFeature: "Texture-painted polygon deletion" },
    ],
  },
  // ── 5. Shaders ──
  {
    id: "shaders",
    title: "Toon & Avatar Shaders",
    description: "Shaders controlling how avatar materials look. All target Built-In Render Pipeline (BiRP).",
    bestPick: "lilToon (primary/default) or Poiyomi (effects-heavy/Pro). Use SCSS for PBR correctness.",
    tools: [
      { name: "Poiyomi Toon Shader", packageId: "com.poiyomi.toon", url: "https://github.com/poiyomi/PoiyomiToonShader", creator: "Poiyomi", license: "MIT Free / Pro Patreon", category: "shaders", description: "Most feature-dense shader. AudioLink, grabpass (Pro), global masking, dissolve, fur (Pro), decals, emission. ThryEditor UI.", bestFeature: "Global masking + AudioLink combo" },
      { name: "lilToon", packageId: "jp.lilxyzw.liltoon", url: "https://github.com/lilxyzw/lilToon", creator: "lilxyzw", license: "MIT", category: "shaders", description: "Dominant in Japanese community. 3-colour shadow, SSS, fur/gem variants, one-click presets, keyword-efficient.", bestFeature: "Keyword-efficient architecture + one-click presets" },
      { name: "Silent's Cel Shading (SCSS)", url: "https://gitlab.com/s-ilent/SCSS", creator: "Silent", license: "MIT", category: "shaders", description: "PBR-based cel shading. Crosstone multi-shadow (4 tones), inventory system, shadow baking.", bestFeature: "Crosstone multi-shadow — finest shadow control" },
      { name: "ORL Shaders", url: "https://github.com/orels1/orels-Unity-Shaders", creator: "orels1", license: "MIT", category: "shaders", description: "PBR/Toon hybrid. Bakery SH support. ORL Shader Generator for custom shaders.", bestFeature: "Shader Generator — custom shaders without HLSL" },
      { name: "Sunao Shader", url: "https://booth.pm/ja/items/1723985", creator: "揚茄子研究所", license: "Free", category: "shaders", description: "Zero shader keywords. Avoids Unity 256-keyword limit.", bestFeature: "Zero keywords" },
    ],
    conventions: [
      "Poiyomi: lock shaders before upload (ThryEditor → Lock All)",
      "lilToon: use one-click presets then customize",
      "Shader swapping: use lilMaterialConverter to convert between Poiyomi↔lilToon",
      "Quest compatibility: lilToon has better keyword budget, Sunao uses zero keywords",
      "AudioLink requires world support — effects won't show in worlds without AudioLink",
      "Poiyomi Pro grabpass = glass/refraction (not available in any free shader)",
    ],
  },
  // ── 6. Shader Support Tools ──
  {
    id: "shader_tools",
    title: "Shader Editors, Lockers & Support Tools",
    description: "Better shader inspectors, property locking, texture packing, keyword analysis.",
    bestPick: "ThryEditor (mandatory with Poiyomi). VRAM Analyzer for performance.",
    tools: [
      { name: "ThryEditor", packageId: "com.github.thry.editor", url: "https://github.com/Thryrallo/ThryEditor", creator: "Thryrallo", license: "MIT", category: "shader_tools", description: "Custom shader inspector. Searchable properties, presets, shader locking, texture packer. Powers Poiyomi UI.", bestFeature: "Shader locking — compile locked material variants" },
      { name: "VRC Avatar Performance Tools", url: "https://github.com/Thryrallo/VRC-Avatar-Performance-Tools", creator: "Thryrallo", license: "OSS", category: "shader_tools", description: "VRAM analyzer. Per-texture breakdown of memory usage.", bestFeature: "Per-texture VRAM breakdown" },
      { name: "ScruffyShaderKeywordsUtility", url: "https://github.com/ScruffyRules/ScruffyShaderKeywordsUtility", creator: "ScruffyRules", license: "MIT", category: "shader_tools", description: "Removes unused shader keyword variants. Mitigates Unity 256-keyword limit.", bestFeature: "Keyword limit mitigation" },
    ],
  },
  // ── 7. Toggle / Inventory Systems ──
  {
    id: "toggles",
    title: "Toggle / Inventory / Expression Systems",
    description: "Automate creation of object toggles, blendshape sliders, material swaps, expression menus.",
    bestPick: "lilycalInventory — best toggle system with material optimization pass.",
    tools: [
      { name: "lilycalInventory", packageId: "jp.lilxyzw.lilycalinventory", url: "https://github.com/lilxyzw/lilycalInventory", creator: "lilxyzw", license: "Free", category: "toggles", description: "LI Toggle Item, Toggle Group (exclusive), Smooth Toggle, BlendShape, Material swap, Prop (world-fixed). Material optimization pass removes unreachable content.", bestFeature: "Toggle-aware material optimization pass" },
      { name: "AvatarMenuCreator for MA", packageId: "com.narazaka.avatarmenucreator", url: "https://booth.pm/ja/items/4419509", creator: "Narazaka", license: "Zlib", category: "toggles", description: "Wizard-style toggle/menu creator for MA. Bool/Int/Float parameter types.", bestFeature: "Beginner-friendly wizard UI" },
      { name: "VRCFury Toggle", category: "toggles", description: "Built into VRCFury component. Auto-creates animator layers, parameters, menus.", bestFeature: "Zero additional installation if using VRCFury" },
    ],
    conventions: [
      "lilycalInventory LI Toggle Item: add to the GameObject you want to toggle",
      "LI Toggle Group: only one item active at a time (outfit selection)",
      "For MA users: LI composes correctly with MA menu installer",
      "VRC expression parameter budget: 256 bits (Bool=1, Int/Float=8 each)",
    ],
  },
  // ── 8. Facial Expressions ──
  {
    id: "expressions",
    title: "Facial Expression & Gesture Management",
    description: "Map hand gestures to facial expressions, generate animator state machines.",
    bestPick: "FaceEmo (new projects) + GestureManager (testing).",
    tools: [
      { name: "FaceEmo", packageId: "dev.suzuryg.face-emo", url: "https://booth.pm/ja/items/4915091", creator: "Suzuryg", license: "Free", category: "expressions", description: "End-to-end: visual expression design + gesture assignment + animator generation + MA installation. Auto-handles blink/lipsync disable.", bestFeature: "Visual blendshape preview while editing" },
      { name: "ComboGestureExpressions (CGE)", url: "https://docs.hai-vr.dev/docs/products/combo-gesture-expressions", creator: "Haï~", license: "MIT", category: "expressions", description: "Maps expressions to gesture combos. Contact/PhysBone trigger support. Predecessor to FaceEmo.", bestFeature: "Contact/PhysBone-reactive expressions" },
      { name: "GestureManager", packageId: "com.blackstartx.gesturemanager", url: "https://github.com/BlackStartx/VRC-Gesture-Manager", creator: "BlackStartx", license: "OSS", category: "expressions", description: "Runtime emulator in Play Mode. Simulate gestures, test expression menus, PhysBones, OSC debug.", bestFeature: "Full avatar interaction simulation in Play Mode" },
    ],
    conventions: [
      "FaceEmo handles blink/lipsync disable automatically",
      "GestureManager requires Play Mode — not edit-time",
      "Use GestureManager to verify expressions before upload",
      "VRChat gestures: Idle, Fist, Open, Point, Peace, RockNRoll, Gun, Thumbs Up",
    ],
  },
  // ── 9. Locomotion ──
  {
    id: "locomotion",
    title: "Locomotion Systems",
    description: "Non-standard movement: sit, lie, fly, dash, height scaling.",
    bestPick: "GoGo Loco — no competition.",
    tools: [
      { name: "GoGo Loco", packageId: "com.franada.gogoloco", url: "https://gogoloco.net", creator: "Franada", license: "Free", category: "locomotion", description: "Sit anywhere, lie down, fly, dash, height scaling, prone, FBT overrides. PC+Quest. MA-compatible prefab.", bestFeature: "Sit and lie anywhere — baseline feature for modern avatars" },
    ],
    conventions: [
      "Drag GoGo Loco prefab as child of avatar root",
      "MA-compatible — installs automatically at build time",
      "Adds significant animator layers/parameters — budget accordingly",
    ],
  },
  // ── 10. Outfit Fitting ──
  {
    id: "outfit_fitting",
    title: "Outfit Fitting & Armature Matching",
    description: "Fit clothing designed for avatar A onto avatar B.",
    bestPick: "AvatarDresser (simple) → KiseteNe (non-MA) → Mochifitter (proportion mismatch).",
    tools: [
      { name: "AvatarDresser", url: "https://github.com/SophieBlueVR/AvatarDresser", creator: "SophieBlueVR", license: "MIT", category: "outfit_fitting", description: "One-click armature matching. Drag clothing, click Get Dressed. Produces MA output.", bestFeature: "One-click armature matching" },
      { name: "KiseteNe for MA", url: "https://github.com/Sayamame-beans/KiseteNe-for-MA", creator: "Sayamame-beans", license: "Free", category: "outfit_fitting", description: "Fits non-MA-compatible clothing. Handles bone renaming and scale adjustments.", bestFeature: "Fits non-MA-compatible clothing" },
      { name: "Module Creator", url: "https://github.com/Tliks/ModuleCreator", creator: "Tliks", license: "Free", category: "outfit_fitting", description: "Extract specific mesh sections from full avatar as independent modules.", bestFeature: "Mesh-level part extraction" },
    ],
    conventions: [
      "Bone names must match between clothing and avatar armature for MA Merge Armature",
      "Common bone names: Hips, Spine, Chest, UpperChest, Neck, Head, Shoulder_L/R, UpperArm_L/R, etc.",
      "Japanese bone names (日本語) need renaming to English for standard rigs",
      "For significant proportion differences, use Mochifitter's mesh deformation",
    ],
  },
  // ── 11. Blendshape Tools ──
  {
    id: "blendshapes",
    title: "Blendshape Tools",
    description: "Create, preview, share, transfer blendshapes.",
    bestPick: "Blendshape Viewer (browse) + FaceTra (create face tracking) + BlendShare (distribute).",
    tools: [
      { name: "Blendshape Viewer", packageId: "com.hai-vr.blendshape-viewer", url: "https://docs.hai-vr.dev/docs/products/blendshape-viewer", creator: "Haï~", license: "OSS", category: "blendshapes", description: "Thumbnail previews of all blendshapes. Click to activate.", bestFeature: "Auto-generated blendshape thumbnail gallery" },
      { name: "FaceTra Shape Creator", url: "https://docs.hai-vr.dev/docs/products/facetra-shape-creator", creator: "Haï~", license: "OSS", category: "blendshapes", description: "Create face tracking blendshapes by painting in Unity editor.", bestFeature: "Face tracking blendshapes without leaving Unity" },
      { name: "BlendShare", url: "https://Tr1turbo.github.io/BlendShare/", creator: "Tr1turbo", license: "Free", category: "blendshapes", description: "Distribute blendshape mods without redistributing protected mesh.", bestFeature: "License-respecting blendshape distribution" },
    ],
  },
  // ── 12. Texture/Material Tools ──
  {
    id: "textures",
    title: "Texture, Material & Atlas Tools",
    description: "Texture modification, atlassing, decals, material inspection.",
    bestPick: "TexTransTool (operations) + lilAvatarUtils (inspection).",
    tools: [
      { name: "TexTransTool (TTT)", packageId: "com.reina-sakiria.textranstool", url: "https://booth.pm/ja/items/4833984", creator: "Reina_Sakiria", license: "Free", category: "textures", description: "Non-destructive NDMF texture: atlas, decals, gradients, blending. Build-time processing.", bestFeature: "Non-destructive build-time decals" },
      { name: "lilAvatarUtils", packageId: "jp.lilxyzw.avatarutils", url: "https://github.com/lilxyzw/lilAvatarUtils", creator: "lilxyzw", license: "MIT", category: "textures", description: "Avatar inspection: all textures, materials, renderers, VRAM cost. Batch material editing. Lightbox previewer.", bestFeature: "Lightbox previewer — 6+ simulated lighting conditions" },
      { name: "lilMaterialConverter", packageId: "jp.lilxyzw.materialconverter", url: "https://github.com/lilxyzw/lilMaterialConverter", creator: "lilxyzw", license: "OSS", category: "textures", description: "Convert materials between shader formats (Poiyomi↔lilToon etc).", bestFeature: "Cross-shader material conversion" },
    ],
  },
  // ── 13. Physics ──
  {
    id: "physics",
    title: "Physics Tools (PhysBones)",
    description: "Manage VRChat PhysBone physics for hair, tails, clothing dynamics.",
    bestPick: "VRCPhysBones (mandatory) + PBReplacer (bulk config).",
    tools: [
      { name: "VRCPhysBone", category: "physics", description: "Native VRChat physics. Grab, pose, squish, spring, stretch. Only runtime physics in VRChat. Quest-compatible.", bestFeature: "Interactive grab and squish" },
      { name: "PBReplacer", packageId: "com.c-colloid.pbreplacer", url: "https://c-colloid.github.io/PBReplacer-VPM/", creator: "c-colloid", license: "Free", category: "physics", description: "Batch replace PhysBone settings across all components.", bestFeature: "Batch PhysBone setting replacement" },
    ],
    conventions: [
      "PhysBone parameters: pull, spring, stiffness, gravity, immobile",
      "Use PhysBoneCollider for collision with body/hands",
      "Quest: PhysBone count is limited — optimize chains",
      "Preview physics in Play Mode only (GestureManager/Av3Emulator)",
    ],
  },
  // ── 14. Animation Tooling ──
  {
    id: "animation",
    title: "Animation Tooling & Animator Workflow",
    description: "Create, edit, inspect animator controllers and animation clips.",
    bestPick: "RATS (always install) + AAC (tool building) + FaceEmo (expressions).",
    tools: [
      { name: "RATS", packageId: "com.rrazgriz.rats", url: "https://github.com/rrazgriz/RATS", creator: "Razgriz", license: "MIT", category: "animation", description: "Harmony-patched QOL for Animator window: custom state colors, readable text, snap-to-grid.", bestFeature: "Custom state colors for VRChat animator controllers" },
      { name: "Animator As Code (AAC)", url: "https://docs.hai-vr.dev/docs/products/animator-as-code", creator: "Haï~", license: "MIT", category: "animation", description: "Fluent C# API for generating animator controllers programmatically.", bestFeature: "Programmatic reproducible animator generation" },
    ],
  },
  // ── 15. Upload / SDK Workflow ──
  {
    id: "upload",
    title: "Avatar Upload & SDK Workflow",
    description: "Improve VRChat SDK upload flow.",
    bestPick: "AwA SDK+ (QOL) + Av3Emulator (testing) + ContinuousAvatarUploader (batch).",
    tools: [
      { name: "Av3Emulator", packageId: "com.lyuma.av3emulator", url: "https://github.com/lyuma/Av3Emulator", creator: "Lyuma", license: "MIT", category: "upload", description: "Full PlayableGraph simulation in Play Mode. Test avatar without uploading.", bestFeature: "Multi-layer PlayableGraph simulation" },
      { name: "ContinuousAvatarUploader", url: "https://github.com/anatawa12/ContinuousAvatarUploader", creator: "anatawa12", license: "MIT", category: "upload", description: "Batch upload multiple avatar variants with version tagging.", bestFeature: "Batch sequential uploads" },
    ],
  },
  // ── 16. Performance Analysis ──
  {
    id: "performance",
    title: "Performance Analysis & Ranking",
    description: "Identify bottlenecks and show expected VRChat performance rank.",
    bestPick: "Actual Performance Window + Kanameliser + lilAvatarUtils.",
    tools: [
      { name: "Actual Performance Window", url: "https://vpm.anatawa12.com/gists/ja/", creator: "anatawa12", license: "Free", category: "performance", description: "Post-NDMF performance rank. Shows what the built avatar actually ranks as.", bestFeature: "Post-optimization rank display" },
      { name: "Kanameliser Editor Plus", url: "https://github.com/kxn4t/kanameliser-editor-plus", creator: "kxn4t", license: "MIT", category: "performance", description: "Post-NDMF polygon counts per mesh. Ctrl+G EditorOnly toggle. Missing BlendShape Inserter.", bestFeature: "Post-NDMF per-mesh polygon counts" },
    ],
  },
  // ── 17. Quest Conversion ──
  {
    id: "quest",
    title: "Quest / Android Conversion",
    description: "Convert PC avatars to Quest-compatible.",
    bestPick: "VRCQuestTools (auto) + EasyQuestSwitch (fine control).",
    tools: [
      { name: "VRCQuestTools", packageId: "com.kurotu.vrcquesttools", url: "https://github.com/kurotu/VRCQuestTools", creator: "kurotu", license: "MIT", category: "quest", description: "Automated PC→Quest conversion. Shader baking, texture compression, component removal. NDMF-integrated.", bestFeature: "Automated shader baking to Quest-compatible flat-lit" },
    ],
    conventions: [
      "Quest shaders: only VRChat/Mobile/* shaders allowed",
      "Quest limits: 10k polygons (Good rank), limited material count",
      "VRCQuestTools bakes PC lighting into flat textures — manual touch-up often needed",
    ],
  },
  // ── 18. VRM Tools ──
  {
    id: "vrm",
    title: "VRM Import, Export & Conversion",
    description: "Import VRM avatars, convert to VRChat, export for VTubing.",
    bestPick: "UniVRM + VRM Converter for VRChat + Denormalized Avatar Exporter.",
    tools: [
      { name: "UniVRM", url: "https://github.com/vrm-c/UniVRM", creator: "VRM Consortium", license: "MIT", category: "vrm", description: "Official VRM implementation. VRM 0.x and 1.0 import/export.", bestFeature: "Full VRM spec compliance" },
      { name: "VRM Converter for VRChat", url: "https://github.com/esperecyan/VRMConverterForVRChat", creator: "esperecyan", license: "MPL-2.0", category: "vrm", description: "VRM→VRChat conversion: PhysBones, expressions, visemes, descriptor setup.", bestFeature: "Bidirectional VRM↔VRChat conversion" },
    ],
  },
  // ── 19. Lighting ──
  {
    id: "lighting",
    title: "Lighting Tools",
    description: "Lightmapping, avatar brightness control, stage lighting.",
    bestPick: "Bakery + VRC-Bakery-Adapter (worlds) + Light Limit Changer (avatars).",
    tools: [
      { name: "Light Limit Changer for MA", packageId: "com.azukimochi.light-limit-changer", url: "https://github.com/Azukimochi/LightLimitChangerForMA", creator: "Azukimochi", license: "Free", category: "lighting", description: "Expression menu brightness min/max control. Prevents avatar going full dark/bright. NDMF-integrated.", bestFeature: "Per-avatar brightness range from expression menu" },
      { name: "LTCGI", url: "https://github.com/PiMaker/ltcgi", creator: "PiMaker", license: "MIT", category: "lighting", description: "Realtime area light reflections. Video player screens cast light onto surfaces.", bestFeature: "Video player screen light reflection" },
      { name: "AudioLink", packageId: "com.llealloo.audiolink", url: "https://github.com/llealloo/vrc-udon-audio-link", creator: "llealloo", license: "MIT", category: "lighting", description: "Audio reactive system. Spectrum/bass/mid/treble data as texture for shaders/Udon.", bestFeature: "World-wide audio data broadcast" },
    ],
  },
  // ── 20. Package Management ──
  {
    id: "packages",
    title: "Package / VPM Management",
    description: "Manage VRChat packages and repositories.",
    bestPick: "ALCOM (power users) or VCC (beginners).",
    tools: [
      { name: "VCC (Creator Companion)", category: "packages", description: "Official VRChat GUI for VPM packages. Browse, install, manage.", bestFeature: "Official GUI" },
      { name: "ALCOM (vrc-get)", url: "https://github.com/anatawa12/vrc-get", creator: "anatawa12", license: "MIT", category: "packages", description: "Open-source VCC alternative with CLI. Faster package resolution.", bestFeature: "CLI for automation" },
    ],
  },
  // ── 21. Avatar Conventions & Best Practices ──
  {
    id: "avatar_conventions",
    title: "VRChat Avatar Conventions & Best Practices",
    description: "Common knowledge for working with VRChat avatars.",
    bestPick: "N/A — reference knowledge.",
    tools: [],
    conventions: [
      "Avatar root hierarchy: [AvatarRoot] → Armature → Hips → Spine → Chest → ... + Body mesh + clothing prefabs",
      "Standard humanoid bone chain: Hips → Spine → Chest → (UpperChest) → Neck → Head",
      "Arms: Shoulder → UpperArm → LowerArm → Hand → Fingers",
      "Legs: UpperLeg → LowerLeg → Foot → Toes",
      "VRC performance ranks: Excellent → Good → Medium → Poor → Very Poor",
      "Excellent (PC): ≤32k polys, ≤4 materials, ≤1 skinned mesh, ≤75 bones, ≤4 PhysBone components",
      "Good (PC): ≤70k polys, ≤8 materials, ≤2 skinned meshes, ≤150 bones, ≤16 PhysBone components",
      "Expression parameter budget: 256 bits total (Bool=1 bit, Int=8 bits, Float=8 bits)",
      "VRChat uses Unity Built-In Render Pipeline (BiRP) — NOT URP or HDRP",
      "All NDMF tools are non-destructive — source assets/hierarchy never modified",
      "Upload workflow: configure avatar → add MA/VRCFury prefabs → add AAO → build & publish",
      "Test before upload: Play Mode + GestureManager/Av3Emulator",
      "Booth.pm accessories: 99% ship as MA-compatible prefabs — mount as child of avatar root",
      "Armature naming: clothing bones MUST match avatar bone names for Merge Armature to work",
      "View Position: set at eye level in VRCAvatarDescriptor for correct VR perspective",
      "Visemes: set to Jaw Flap Blendshape or Viseme Blend Shape depending on avatar",
      "Standard visemes: vrc.v_sil, vrc.v_PP, vrc.v_FF, vrc.v_TH, vrc.v_DD, vrc.v_kk, vrc.v_SS, vrc.v_nn, vrc.v_RR, vrc.v_aa, vrc.v_E, vrc.v_I, vrc.v_O, vrc.v_U",
    ],
  },
];

/**
 * Query the knowledge base by category, tool name, or free-text search.
 */
export function queryKnowledgeBase(query: {
  category?: string;
  tool_name?: string;
  search?: string;
  list_categories?: boolean;
}): unknown {
  if (query.list_categories) {
    return {
      categories: categories.map((c) => ({
        id: c.id,
        title: c.title,
        description: c.description,
        bestPick: c.bestPick,
        toolCount: c.tools.length,
      })),
    };
  }

  if (query.category) {
    const cat = categories.find(
      (c) => c.id === query.category || c.title.toLowerCase().includes(query.category!.toLowerCase())
    );
    if (cat) return cat;
    return { error: `Category '${query.category}' not found. Use list_categories=true to see available categories.` };
  }

  if (query.tool_name) {
    const needle = query.tool_name.toLowerCase();
    for (const cat of categories) {
      const tool = cat.tools.find(
        (t) =>
          t.name.toLowerCase().includes(needle) ||
          (t.packageId && t.packageId.toLowerCase().includes(needle))
      );
      if (tool) return { ...tool, category: cat.id, categoryTitle: cat.title };
    }
    return { error: `Tool '${query.tool_name}' not found.` };
  }

  if (query.search) {
    const needle = query.search.toLowerCase();
    const results: unknown[] = [];

    for (const cat of categories) {
      // Search in tools
      for (const tool of cat.tools) {
        if (
          tool.name.toLowerCase().includes(needle) ||
          tool.description.toLowerCase().includes(needle) ||
          (tool.bestFeature && tool.bestFeature.toLowerCase().includes(needle))
        ) {
          results.push({ ...tool, category: cat.id });
        }
      }
      // Search in conventions
      if (cat.conventions) {
        for (const conv of cat.conventions) {
          if (conv.toLowerCase().includes(needle)) {
            results.push({ convention: conv, category: cat.id, categoryTitle: cat.title });
          }
        }
      }
    }

    return { query: query.search, resultCount: results.length, results: results.slice(0, 20) };
  }

  // Return overview
  return {
    totalCategories: categories.length,
    totalTools: categories.reduce((sum, c) => sum + c.tools.length, 0),
    categories: categories.map((c) => ({ id: c.id, title: c.title, toolCount: c.tools.length })),
    hint: "Use category, tool_name, or search params to query specific knowledge.",
  };
}
