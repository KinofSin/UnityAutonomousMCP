/**
 * VRChat Unity Install Guide — structured install/setup/test data for 60+ tools.
 * Sourced from VRChat_Unity_Complete_Install_Guide.md (3356 lines).
 * Queryable by tool name, section, or free-text search.
 */

export interface InstallEntry {
  id: string;
  name: string;
  section: string;
  type: "vpm" | "manual" | "asset_store" | "bundled" | "curated";
  author?: string;
  cost: "free" | "paid" | string;
  vpmRepoUrl?: string;
  url?: string;
  packageSearchName?: string;
  install: string[];
  setup?: string[];
  test?: string[];
  notes?: string[];
  commonIssues?: string[];
}

// ── Global Prerequisites ──
export const GLOBAL_PREREQUISITES = {
  title: "Global Prerequisites — Complete Before Any Tool Installation",
  steps: [
    {
      step: "Install Unity Hub",
      instructions: [
        "Go to https://unity.com/download",
        "Download Unity Hub, run installer, accept defaults",
        "Open Unity Hub after install",
      ],
    },
    {
      step: "Install Unity 2022.3.22f1",
      instructions: [
        "Unity Hub → Installs → Install Editor → Archive tab → download archive link",
        "Find Unity 2022.x → scroll to 2022.3.22 → click Unity Hub button",
        "Check: Microsoft Visual Studio Community, Android Build Support (expand → Android SDK & NDK Tools + OpenJDK)",
        "Click Install (takes 20–60 minutes)",
      ],
    },
    {
      step: "Install VRChat Creator Companion (VCC)",
      instructions: [
        "Go to https://vrchat.com/home/download → Creator Companion section",
        "Download and run installer",
        "Open VCC → locate Unity install → sign in to VRChat account",
      ],
    },
    {
      step: "(Optional) Install ALCOM",
      instructions: [
        "Go to https://github.com/anatawa12/vrc-get/releases/latest",
        "Download _windows_x64.exe → run installer",
        "Set Unity path to C:\\Program Files\\Unity\\Hub\\Editor\\2022.3.22f1\\Editor\\Unity.exe",
      ],
    },
    {
      step: "Create a VRChat Unity Project",
      instructions: [
        "Open VCC/ALCOM → Projects → Create New Project",
        "Select Avatar or World template",
        "Name project (no spaces/special chars), e.g. C:\\VRC\\MyAvatars_2022",
        "Click Create Project → Open Project (first open takes 2–5 min)",
      ],
    },
  ],
  vpmInstallFlow: [
    "Open VCC → Settings (gear icon) → Packages tab → Add Repository → paste URL → Add",
    "Go to Projects → click Manage next to your project",
    "Find the package in the list → click + → Apply Changes",
    "Unity reimports when you open the project",
  ],
  unitypackageInstallFlow: [
    "Download the .unitypackage file from source (Booth, Gumroad, GitHub Releases)",
    "Have your Unity project open",
    "Drag the .unitypackage into Unity's Project panel (or double-click it)",
    "Import dialog appears → leave all checked → click Import",
    "Wait for progress bar to complete",
  ],
  testingFlow: [
    "Select avatar root GameObject in Hierarchy",
    "Press Play (▶)",
    "Gesture Manager / Av3Emulator activates automatically",
    "Use Gesture Manager panel: set hand gestures, trigger parameters, navigate expression menus",
    "Press Stop (■) to exit Play mode",
  ],
};

// ── VPM Repository Reference ──
export const VPM_REPOS: { author: string; url: string; packages: string }[] = [
  { author: "bd_ (Modular Avatar + NDMF)", url: "https://vpm.nadena.dev/vpm.json", packages: "Modular Avatar, NDMF" },
  { author: "anatawa12", url: "https://vpm.anatawa12.com/vpm.json", packages: "AAO, ContinuousAvatarUploader, Actual Performance Window" },
  { author: "lilxyzw", url: "https://lilxyzw.github.io/vpm-repos/vpm.json", packages: "lilToon, lilAvatarUtils, lilycalInventory, lilNDMFMeshSimplifier, lilMaterialConverter" },
  { author: "Poiyomi", url: "https://poiyomi.github.io/vpm/index.json", packages: "Poiyomi Toon Shader" },
  { author: "Haï~", url: "https://hai-vr.github.io/vpm-listing/index.json", packages: "CGE, AAC, FaceTra, Starmesh, Prefabulous, Blendshape Viewer, Visual Expressions Editor, Denormalized Avatar Exporter" },
  { author: "d4rkc0d3r", url: "https://d4rkc0d3r.github.io/vpm-repos/main.json", packages: "d4rkAvatarOptimizer" },
  { author: "kurotu", url: "https://kurotu.github.io/vpm-repos/vpm.json", packages: "VRCQuestTools" },
  { author: "Narazaka", url: "https://vpm.narazaka.net/index.json", packages: "AvatarMenuCreator" },
  { author: "AwA VR Tools", url: "https://awa-vr.github.io/vrc-tools-vpm/index.json", packages: "VRC SDK+, Preset Creator, Parameter Renamer, Sitting Fix, Controller Cleaner, HierarchyPlus" },
  { author: "orels1", url: "https://orels1.github.io/orels-Unity-Shaders/index.json", packages: "ORL Shaders" },
  { author: "PiMaker", url: "https://vpm.pimaker.at/index.json", packages: "LTCGI" },
  { author: "CyanLaser", url: "https://CyanLaser.github.io/CyanTrigger/index.json", packages: "CyanTrigger" },
  { author: "VRCFury", url: "https://vcc.vrcfury.com/", packages: "VRCFury" },
  { author: "Razgriz", url: "https://vpm.razgriz.one/", packages: "RATS" },
  { author: "Suzuryg", url: "https://suzuryg.github.io/vpm-repos/vpm.json", packages: "FaceEmo" },
  { author: "Azukimochi", url: "https://azukimochi.github.io/vpm-repos/index.json", packages: "Light Limit Changer for MA" },
  { author: "Pumkin", url: "https://rurre.github.io/vpm/index.json", packages: "Pumkin's Avatar Tools" },
  { author: "gatosyocora", url: "https://vpm.gatosyocora.net/index.json", packages: "MeshDeleterWithTexture" },
  { author: "WhiteFlare", url: "https://whiteflare.github.io/vpm-repos/vpm.json", packages: "Unlit WF Shaders" },
  { author: "c-colloid", url: "https://c-colloid.github.io/PBReplacer-VPM/", packages: "PBReplacer" },
  { author: "Varneon", url: "https://varneon.github.io/vpm-repos/index.json", packages: "VUdon ecosystem" },
  { author: "needon", url: "https://k4584587.github.io/Modular-Auto-Toggle/", packages: "Modular Auto Toggle" },
  { author: "VRLabs", url: "https://vrlabs.dev/packages", packages: "Marker, World Constraint, Ragdoll, Contact Tracker, Collision Detection, Avatars 3.0 Manager, HierarchyPlus" },
  { author: "ArchiTech (ProTV)", url: "https://vpm.techanon.dev/", packages: "ProTV" },
  { author: "Pokeyi", url: "https://pokeyi.dev/vpm-packages/", packages: "Udon Tools" },
  { author: "Guribo", url: "https://guribo.github.io/udon-voice-utils/index.json", packages: "Udon Voice Utils" },
  { author: "AudioLink", url: "https://llealloo.github.io/vrc-udon-audio-link/index.json", packages: "AudioLink" },
  { author: "BlendShare", url: "https://Tr1turbo.github.io/BlendShare/", packages: "BlendShare" },
  { author: "BefuddledLabs", url: "https://BefuddledLabs.github.io/OpenSyncDance/", packages: "OpenSyncDance" },
];

// ── Tool Install Entries ──
export const INSTALL_ENTRIES: InstallEntry[] = [
  // Section 1: Non-Destructive Frameworks
  {
    id: "ndmf", name: "NDMF", section: "Non-Destructive Frameworks",
    type: "vpm", author: "bd_", cost: "free",
    vpmRepoUrl: "https://vpm.nadena.dev/vpm.json",
    packageSearchName: "Non-Destructive Modular Framework",
    install: [
      "VCC/ALCOM → Settings → Packages → Add Repository → paste: https://vpm.nadena.dev/vpm.json",
      "Project → Manage → search 'Non-Destructive Modular Framework' → + → Apply",
      "Note: NDMF installs automatically as dependency of Modular Avatar",
    ],
    setup: ["No direct setup — NDMF is infrastructure"],
    test: ["Top menu: ndmf → Manual Bake Avatar — if menu exists, NDMF is installed"],
  },
  {
    id: "modular_avatar", name: "Modular Avatar (MA)", section: "Non-Destructive Frameworks",
    type: "vpm", author: "bd_", cost: "free",
    vpmRepoUrl: "https://vpm.nadena.dev/vpm.json",
    packageSearchName: "Modular Avatar",
    install: [
      "VCC → Settings → Packages → Add Repository → paste: https://vpm.nadena.dev/vpm.json → Add",
      "Project → Manage → search 'Modular Avatar' → + → Apply Changes",
    ],
    setup: [
      "INSTALL MA-COMPATIBLE OUTFIT: Import outfit → drag outfit prefab onto avatar root in Hierarchy",
      "Outfit should have MA Merge Armature component. If Merge Target is blank, drag avatar's Armature into it",
      "MANUAL TOGGLE: Select object → Add Component → MA Object Toggle",
      "Create Empty child of avatar root → Add MA Menu Item (Control Type: Toggle, Parameter: e.g. hat_toggle)",
      "Add MA Menu Installer to same empty. Drag as child of avatar root. Ctrl+S",
    ],
    test: [
      "Select avatar root → Play → Gesture Manager → Expression Menu",
      "Toggle should appear in menu — click it to show/hide object",
      "MANUAL BAKE TEST: ndmf → Manual Bake Avatar → inspect baked copy → delete when done",
    ],
  },
  {
    id: "vrcfury", name: "VRCFury", section: "Non-Destructive Frameworks",
    type: "vpm", author: "VRCFury Team", cost: "free",
    vpmRepoUrl: "https://vcc.vrcfury.com/",
    packageSearchName: "VRCFury",
    install: [
      "VCC → Settings → Packages → Add Repository → paste: https://vcc.vrcfury.com/ → Add",
      "Project → Manage → search 'VRCFury' → + → Apply Changes",
    ],
    setup: [
      "Drag VRCFury prefab onto avatar root in Hierarchy",
      "In Inspector: VRCFury component shows features list",
      "Armature Link: verify Link To field matches avatar bone name",
      "Toggle: pre-filled, rename if needed",
      "Full Controller: referenced controller file, leave as-is",
    ],
    test: [
      "Select avatar root → Play → Gesture Manager → Expression Menu",
      "Find VRCFury-added menu items → click toggles → verify objects appear/disappear",
    ],
  },

  // Section 2: Avatar Optimizers
  {
    id: "aao", name: "AAO: Avatar Optimizer", section: "Avatar Optimizers",
    type: "vpm", author: "anatawa12", cost: "free",
    vpmRepoUrl: "https://vpm.anatawa12.com/vpm.json",
    packageSearchName: "Avatar Optimizer",
    install: [
      "VCC → Add Repository → https://vpm.anatawa12.com/vpm.json",
      "Project → Manage → search 'Avatar Optimizer' → + → Apply",
    ],
    setup: [
      "AUTO OPTIMIZATION: Select avatar root → Add Component → 'AAO Trace And Optimize'",
      "Leave defaults: ✅ Remove Unused Objects, ✅ Optimize Animator. Ctrl+S",
      "MESH MERGING: Select SkinnedMeshRenderer → Add Component → 'AAO Merge Skinned Mesh' → Add Source → drag other meshes",
      "REMOVE HIDDEN BODY: Select body mesh → Add Component → 'AAO Remove Mesh By BlendShape' → Add BlendShape → select clothing blendshape → Threshold: 0.5",
    ],
    test: [
      "ndmf → Manual Bake Avatar → check baked mesh polygon counts (should be lower)",
      "Window → Analysis → Avatar Performance Stats to verify rank improved",
      "Install Actual Performance Window for post-optimization metrics",
    ],
  },
  {
    id: "d4rk", name: "d4rkAvatarOptimizer", section: "Avatar Optimizers",
    type: "vpm", author: "d4rkc0d3r", cost: "free",
    vpmRepoUrl: "https://d4rkc0d3r.github.io/vpm-repos/main.json",
    packageSearchName: "d4rkAvatarOptimizer",
    install: [
      "VCC → Add Repository → https://d4rkc0d3r.github.io/vpm-repos/main.json",
      "Project → Manage → search 'd4rkAvatarOptimizer' → + → Apply",
    ],
    setup: [
      "⚠️ IF USING POIYOMI: Lock ALL materials first! Thry → Lock All Unlocked Materials",
      "Select avatar root → Add Component → 'd4rkAvatarOptimizer'",
      "Settings: ✅ Merge Skinned Meshes, ✅ Merge Static Meshes with Same Material",
      "✅ Merge Same Dimension Textures, ✅ Remove Unused Components/GameObjects",
      "✅ Write Properties as Static Values. Ctrl+S",
    ],
    test: [
      "d4rkAvatarOptimizer → Show Optimizer Preview (before/after draw calls, materials, polygons)",
      "ndmf → Manual Bake Avatar for full test → delete baked copy",
    ],
    notes: ["Poiyomi materials MUST be locked before running d4rk — failure to lock causes shader errors"],
  },
  {
    id: "lilndmfmeshsimplifier", name: "lilNDMFMeshSimplifier", section: "Avatar Optimizers",
    type: "vpm", author: "lilxyzw", cost: "free",
    vpmRepoUrl: "https://lilxyzw.github.io/vpm-repos/vpm.json",
    packageSearchName: "lilNDMFMeshSimplifier",
    install: [
      "VCC → Add Repository → https://lilxyzw.github.io/vpm-repos/vpm.json",
      "Project → Manage → search 'lilNDMFMeshSimplifier' → + → Apply",
    ],
    setup: [
      "Select SkinnedMeshRenderer → Add Component → 'lilNDMFMeshSimplifier'",
      "Set Quality slider: 1.0 = no reduction, 0.7 = 30% reduction (good default), 0.5 = 50% (aggressive)",
      "⚠️ For face meshes: do NOT go below 0.85 — facial topology matters for expressions",
    ],
    test: ["ndmf → Manual Bake Avatar → check baked mesh triangle count vs original → delete baked copy"],
  },

  // Section 3: Shaders
  {
    id: "liltoon", name: "lilToon", section: "Shaders",
    type: "vpm", author: "lilxyzw", cost: "free",
    vpmRepoUrl: "https://lilxyzw.github.io/vpm-repos/vpm.json",
    packageSearchName: "lilToon",
    install: [
      "VCC → Add Repository → https://lilxyzw.github.io/vpm-repos/vpm.json",
      "Project → Manage → search 'lilToon' → + → Apply",
    ],
    setup: [
      "Select material in Project → Inspector → Shader dropdown → search 'lilToon'",
      "Select: lilToon (opaque), lilToonTransparent (glass), lilToonOutline, lilToonFur",
      "PRESET: Click Preset button (list icon) → browse categories → Apply",
      "BASIC TOON: Rendering Mode: Opaque, Main Texture in Main Color section",
      "Shading: Shadow Color 1&2 (darker/blue), Shadow Border: 0.5, Shadow Blur: adjust",
      "Outline: ✅ Enable Outline → Width: 0.08 → Color: dark/black",
    ],
    test: ["Press Play → inspect avatar in Scene view → lilToon shading visible"],
  },
  {
    id: "poiyomi", name: "Poiyomi Toon Shader", section: "Shaders",
    type: "vpm", author: "Poiyomi", cost: "free",
    vpmRepoUrl: "https://poiyomi.github.io/vpm/index.json",
    packageSearchName: "Poiyomi Toon",
    install: [
      "VCC → Add Repository → https://poiyomi.github.io/vpm/index.json",
      "Project → Manage → search 'Poiyomi Toon' → + → Apply",
    ],
    setup: [
      "Select material → Shader dropdown → search 'poiyomi' → select '.poiyomi/Poiyomi Toon'",
      "LOCK MATERIAL (required for d4rk + performance): padlock icon at top → click → 'Locked' indicator",
      "To edit: unlock → edit → re-lock",
      "Shading → Toon → Toon1 or Toon2Gradient (most common toon looks)",
      "Main section: set Albedo texture. Shadow: Shadow Tint + Shadow Border",
      "Rim Lighting: ✅ enable → set color/range. Emission: ✅ enable for glowing parts",
    ],
    test: ["Press Play → inspect in Scene view"],
    notes: ["ThryEditor (shader inspector) installs automatically with Poiyomi"],
  },
  {
    id: "scss", name: "SCSS (Silent's Cel Shading)", section: "Shaders",
    type: "manual", author: "Silent", cost: "free",
    url: "https://gitlab.com/s-ilent/SCSS/-/releases",
    install: [
      "Go to https://gitlab.com/s-ilent/SCSS/-/releases",
      "Download latest .unitypackage → drag into Unity Project panel → Import",
    ],
    setup: [
      "Select material → Shader → search 'Silent's Cel Shading' → select Cel Shading Shader",
      "Tone Map Texture: draw gradient or use built-in preset",
      "Crosstone: ✅ enable for anime-style colored shadows → set Shadow 2nd Color",
      "Rim Lighting: expand → enable → set color",
    ],
  },
  {
    id: "orl", name: "ORL Shaders", section: "Shaders",
    type: "vpm", author: "orels1", cost: "free",
    vpmRepoUrl: "https://orels1.github.io/orels-Unity-Shaders/index.json",
    packageSearchName: "ORL Shaders",
    install: [
      "VCC → Add Repository → https://orels1.github.io/orels-Unity-Shaders/index.json",
      "Project → Manage → search 'ORL Shaders' → + → Apply",
    ],
    setup: [
      "Select material → Shader → search 'ORL'",
      "ORL/Toon/Lit Toon for avatar toon, ORL/PBR/Layered Material for world, ORL/VFX/Laser for lasers",
    ],
  },

  // Section 4: Shader Utilities
  {
    id: "lilavatarutils", name: "lilAvatarUtils", section: "Shader Utilities",
    type: "vpm", author: "lilxyzw", cost: "free",
    vpmRepoUrl: "https://lilxyzw.github.io/vpm-repos/vpm.json",
    packageSearchName: "lilAvatarUtils",
    install: [
      "VCC → Add Repository → https://lilxyzw.github.io/vpm-repos/vpm.json (same as lilToon)",
      "Project → Manage → search 'lilAvatarUtils' → + → Apply",
    ],
    setup: [
      "TEXTURE VRAM: lilToon → lilAvatarUtils → Textures tab → select avatar → Scan",
      "LIGHTING PREVIEW: Lighting tab → select avatar → Show Preview (6 lighting conditions)",
      "ANIMATION TRACKER: Animations tab → select avatar → Scan (shows all clips and targets)",
    ],
  },
  {
    id: "lilmaterialconverter", name: "lilMaterialConverter", section: "Shader Utilities",
    type: "vpm", author: "lilxyzw", cost: "free",
    vpmRepoUrl: "https://lilxyzw.github.io/vpm-repos/vpm.json",
    install: [
      "Included with lilToon from the lilxyzw VPM repo",
    ],
    setup: [
      "lilToon → Convert Materials → select avatar root",
      "Source Shader: Standard (or current) → Target Shader: lilToon",
      "Click Convert All (textures reassigned automatically)",
    ],
  },

  // Section 5: Toggle Systems
  {
    id: "lilycalinventory", name: "lilycalInventory", section: "Toggle Systems",
    type: "vpm", author: "lilxyzw", cost: "free",
    vpmRepoUrl: "https://lilxyzw.github.io/vpm-repos/vpm.json",
    packageSearchName: "lilycalInventory",
    install: [
      "VCC → Add Repository → https://lilxyzw.github.io/vpm-repos/vpm.json",
      "Project → Manage → search 'lilycalInventory' → + → Apply",
    ],
    setup: [
      "SIMPLE TOGGLE: Select object to toggle → Add Component → 'LI Menu Item'",
      "Set Menu Name, Object (auto-filled), Default State (True/False). Done.",
      "OUTFIT GROUP (mutual exclusion): Create Empty 'OutfitGroup' → Add Component → 'LI Costume'",
      "Drag all outfit objects into Costumes list, set Name for each, Default Index = 0",
    ],
    test: [
      "Play → Gesture Manager → Expression Menu → find toggles",
      "Click toggle → object shows/hides. Outfit group: selecting one deactivates others",
    ],
  },
  {
    id: "avatarmenucreator", name: "AvatarMenuCreator for MA", section: "Toggle Systems",
    type: "vpm", author: "Narazaka", cost: "free",
    vpmRepoUrl: "https://vpm.narazaka.net/index.json",
    packageSearchName: "AvatarMenuCreator",
    install: [
      "VCC → Add Repository → https://vpm.narazaka.net/index.json",
      "Project → Manage → search 'AvatarMenuCreator' → + → Apply",
    ],
    setup: [
      "Select avatar root → Narazaka → AvatarMenuCreator",
      "Click + (Add Menu Item) → set Type (Toggle/Radial/Sub-Menu), Name, drag Objects, Parameter Name",
      "When done click Generate → MA components created automatically",
    ],
    test: ["Play → Gesture Manager → Expression Menu → navigate menu tree → verify all items work"],
  },

  // Section 6: Facial Expressions & Animation
  {
    id: "faceemo", name: "FaceEmo", section: "Facial Expressions",
    type: "vpm", author: "Suzuryg", cost: "free",
    vpmRepoUrl: "https://suzuryg.github.io/vpm-repos/vpm.json",
    packageSearchName: "FaceEmo",
    install: [
      "VCC → Add Repository → https://suzuryg.github.io/vpm-repos/vpm.json",
      "Project → Manage → search 'FaceEmo' → + → Apply (deps install automatically)",
    ],
    setup: [
      "Suzuryg → FaceEmo → Open Editor → Set Avatar (drag avatar root)",
      "CREATE EXPRESSION: + Add Expression → name it → Edit → adjust blendshape sliders → Save",
      "ASSIGN TO GESTURE: In gesture grid (8x8 left×right), click cell → select expression",
      "Configure FBT/non-FBT columns if using FBT",
      "Click Generate (or Apply with Modular Avatar). Ctrl+S",
    ],
    test: [
      "Play → Gesture Manager → manually set hand gestures (Fist, Point, etc.)",
      "Watch avatar face change to assigned expression for each gesture combination",
    ],
  },
  {
    id: "cge", name: "ComboGestureExpressions (CGE)", section: "Facial Expressions",
    type: "vpm", author: "Haï~", cost: "free",
    vpmRepoUrl: "https://hai-vr.github.io/vpm-listing/index.json",
    packageSearchName: "ComboGestureExpressions",
    install: [
      "VCC → Add Repository → https://hai-vr.github.io/vpm-listing/index.json",
      "Project → Manage → search 'ComboGestureExpressions' → + → Apply",
    ],
    setup: [
      "Select avatar root → Add Component → 'Combo Gesture Compiler'",
      "Gesture Layer: assign Animator Controller. Facial Animations Container: click Create",
      "Double-click container → CGE editor → 64-cell gesture grid",
      "For each cell: assign animation clip (or create with + → drag blendshape sliders → Save)",
      "Back on component → Force Regenerate Animator. Ctrl+S",
    ],
    test: ["Play → Gesture Manager → set hand gestures → verify face expressions change"],
  },
  {
    id: "rats", name: "RATS", section: "Animation Tools",
    type: "vpm", author: "Razgriz", cost: "free",
    vpmRepoUrl: "https://vpm.razgriz.one/",
    packageSearchName: "RATS",
    install: [
      "VCC → Add Repository → https://vpm.razgriz.one/",
      "Project → Manage → search 'RATS' → + → Apply",
    ],
    setup: [
      "COLOR-CODE: Open Animator window → double-click controller → right-click state → color picker (RATS adds this)",
      "Automatic features (no setup): actual parameter names in transitions, grid snapping, keyboard shortcuts",
    ],
  },

  // Section 7: Quest Conversion
  {
    id: "vrcquesttools", name: "VRCQuestTools", section: "Quest Conversion",
    type: "vpm", author: "kurotu", cost: "free",
    vpmRepoUrl: "https://kurotu.github.io/vpm-repos/vpm.json",
    packageSearchName: "VRCQuestTools",
    install: [
      "VCC → Add Repository → https://kurotu.github.io/vpm-repos/vpm.json",
      "Project → Manage → search 'VRCQuestTools' → + → Apply",
    ],
    setup: [
      "Select PC avatar root → VRCQuestTools → Convert Avatar for Quest",
      "✅ Generate Textures (bakes toon shaders to flat textures)",
      "Texture Size: 1024 body / 512 accessories. ✅ Compress Textures. ✅ Remove Unsupported Components",
      "Click Convert → '[AvatarName] (Quest)' appears in Hierarchy",
      "UPLOAD BOTH: Upload PC version, note Avatar ID. File → Build Settings → Android → Switch Platform",
      "Select Quest avatar → paste same Avatar ID → Build & Publish",
    ],
    test: [
      "PC: Play → Gesture Manager → verify expressions/toggles",
      "Quest: Switch to Android → select Quest avatar → Play → verify Quest shaders, no errors",
    ],
    notes: ["Quest limits: VRChat/Mobile/* shaders only, 10k polys for Good rank"],
  },

  // Section 9: Physics
  {
    id: "vrcphysbone", name: "VRCPhysBone", section: "Physics",
    type: "bundled", cost: "free",
    install: ["Already included in VRChat SDK — no separate install needed"],
    setup: [
      "Expand avatar armature → find root bone of hair/tail/clothing chain → select it",
      "Add Component → 'VRC Phys Bone'",
      "Pull: 0.2 (return-to-rest), Spring: 0.3 (bounce), Stiffness: 0.0 (free-flowing)",
      "Gravity: 0.1 (downward), Gravity Falloff: 0.8, Radius: 0.02 (collision radius), Immobile: 0.2",
      "Collision: click + to add collision spheres for head/neck to prevent clipping",
    ],
    test: [
      "Play → in Scene view click and drag on hair/tail — should jiggle and spring back",
      "PhysBone debug panel in Av3Emulator shows grab state",
    ],
    notes: [
      "DynamicBone → PhysBone converter: VRChatSDK → Utilities → Convert DynamicBones To PhysBones",
      "Conversion is approximate — manual tuning still required",
    ],
  },
  {
    id: "pbreplacer", name: "PBReplacer", section: "Physics",
    type: "vpm", author: "c-colloid", cost: "free",
    vpmRepoUrl: "https://c-colloid.github.io/PBReplacer-VPM/",
    packageSearchName: "PBReplacer",
    install: [
      "VCC → Add Repository → https://c-colloid.github.io/PBReplacer-VPM/",
      "Project → Manage → search 'PBReplacer' → + → Apply",
    ],
    setup: ["c-colloid → PBReplacer → drag avatar root → Scan → review/adjust → Replace All"],
  },

  // Section 10: Texture Tools
  {
    id: "textranstool", name: "TexTransTool (TTT)", section: "Texture Tools",
    type: "vpm", author: "Reina_Sakiria", cost: "free",
    url: "https://booth.pm/ja/items/4833984",
    install: [
      "Go to https://booth.pm/ja/items/4833984 → download VPM or .unitypackage",
      "VPM: add repo URL to VCC → install 'TexTransTool'",
      ".unitypackage: drag into Unity → Import",
    ],
    setup: [
      "DECAL: Create Empty child of avatar root → Add Component → 'TTT Decal'",
      "Set Decal Texture (PNG with alpha), Target Renderer, Decal Scale",
      "Position the decal object on mesh surface in Scene view",
      "ATLAS: Add Component → 'TTT Texture Atlas' → drag SkinnedMeshRenderers → Atlas Resolution: 2048x2048",
    ],
    test: ["ndmf → Manual Bake Avatar → inspect baked textures/materials → delete baked copy"],
  },

  // Section 11: Emulators
  {
    id: "av3emulator", name: "Av3Emulator", section: "Emulators",
    type: "curated", author: "Lyuma", cost: "free",
    url: "https://github.com/lyuma/Av3Emulator/releases/latest",
    install: [
      "OPTION A: VCC → Manage project → find 'Av3Emulator' in Curated packages → + → Apply",
      "OPTION B: Download .unitypackage from GitHub releases → drag into Unity → Import",
    ],
    setup: [
      "Activates AUTOMATICALLY when entering Play mode with VRChat avatar in scene",
      "Panel: Window → Lyuma Av3 Emulator",
      "Gesture Left/Right dropdowns: Neutral, Fist, HandOpen, FingerPoint, Victory, RockNRoll, HandGun, ThumbsUp",
      "Parameters section: lists all avatar parameters with current values + manual override",
    ],
    test: [
      "Play → set Gesture Left: Fist, Gesture Right: Peace → watch avatar face change",
      "Click Bool parameters to toggle, Float parameters get sliders",
    ],
  },
  {
    id: "gesturemanager", name: "Gesture Manager", section: "Emulators",
    type: "manual", author: "BlackStartx", cost: "free",
    url: "https://github.com/BlackStartx/VRC-Gesture-Manager/releases/latest",
    install: [
      "Download .unitypackage from GitHub releases → drag into Unity → Import",
    ],
    setup: [
      "Play → Window → Gesture Manager (or appears automatically)",
      "Left/Right Hand gesture dropdowns. Expression Menu button: opens menu navigator",
      "Click any menu item to activate — exactly like clicking in VRChat",
      "Navigate sub-menus, toggles show checkmarks, radial puppets show circular slider",
    ],
    test: [
      "Install BOTH Gesture Manager and Av3Emulator for best testing",
      "Gesture Manager for: expression menus, hand gestures",
      "Av3Emulator for: parameter monitoring, OSC simulation, FBT testing",
    ],
  },

  // Section 12: Outfit Fitting
  {
    id: "kisetene", name: "KiseteNe for MA", section: "Outfit Fitting",
    type: "manual", author: "Sayamame-beans", cost: "free",
    url: "https://github.com/Sayamame-beans/KiseteNe-for-MA/releases/latest",
    install: ["Download .unitypackage from GitHub releases → drag into Unity → Import"],
    setup: [
      "Import both base avatar and the outfit (designed for different avatar)",
      "Place both in Hierarchy → Sayamame → KiseteNe for MA",
      "Source Avatar: avatar outfit was designed for. Target Avatar: your avatar. Outfit Root: outfit object",
      "Click Fit → MA components generated for bone mapping differences. Ctrl+S",
    ],
    test: ["Play → verify outfit follows avatar movement without deformation artifacts"],
  },

  // Section 13: Lighting
  {
    id: "lightlimitchanger", name: "Light Limit Changer for MA", section: "Lighting",
    type: "vpm", author: "Azukimochi", cost: "free",
    vpmRepoUrl: "https://azukimochi.github.io/vpm-repos/index.json",
    packageSearchName: "Light Limit Changer",
    install: [
      "VCC → Add Repository → https://azukimochi.github.io/vpm-repos/index.json",
      "Project → Manage → search 'Light Limit Changer' → + → Apply",
    ],
    setup: [
      "Select avatar root → Add Component → 'Light Limit Changer'",
      "Min Brightness: 0.1, Max Brightness: 1.5, Include Target Shader: your shader (lilToon/Poiyomi)",
      "Auto-generates MA components + 2 expression menu sliders",
    ],
    test: [
      "Play → Gesture Manager → Expression Menu → Light Limit sub-menu",
      "Min slider to 0 → avatar darkens but not fully black. Max slider to 2 → avatar brightens above normal",
    ],
  },

  // Section 14: Audio/Video
  {
    id: "audiolink", name: "AudioLink", section: "Audio & Video",
    type: "vpm", author: "llealloo", cost: "free",
    vpmRepoUrl: "https://llealloo.github.io/vrc-udon-audio-link/index.json",
    packageSearchName: "AudioLink",
    install: [
      "VCC → Add Repository → https://llealloo.github.io/vrc-udon-audio-link/index.json",
      "Project → Manage → search 'AudioLink' → + → Apply",
    ],
    setup: [
      "WORLD: Hierarchy → right-click → AudioLink → Add AudioLink to Scene",
      "Set Audio Source (drag audio source object). Set Theme Colors.",
      "AVATAR lilToon: material inspector → AudioLink section → ✅ Enable → map properties to bands",
      "AVATAR Poiyomi: Unlock material → AudioLink tab → ✅ Enable → map properties → Lock material",
    ],
    test: [
      "Play mode with world scene → play Audio Source → AudioLink → Open AudioLink Monitor",
      "AudioLink-reactive materials should pulse/glow in sync with audio",
    ],
  },

  // Section 15: World Tools
  {
    id: "cyantrigger", name: "CyanTrigger", section: "World Udon Tools",
    type: "vpm", author: "CyanLaser", cost: "free",
    vpmRepoUrl: "https://CyanLaser.github.io/CyanTrigger/index.json",
    packageSearchName: "CyanTrigger",
    install: [
      "VCC → Add Repository → https://CyanLaser.github.io/CyanTrigger/index.json",
      "Project → Manage → search 'CyanTrigger' → + → Apply",
    ],
    setup: [
      "Select world GameObject (button etc) → Add Component → 'CyanTrigger'",
      "+ Add Event → OnInteract → + Add Action → e.g. GameObject.SetActive or AudioSource.PlayOneShot",
    ],
    test: ["Play with ClientSim → walk to object → press E to interact → action fires"],
  },

  // Section 16: Editor UI
  {
    id: "pumkins_avatar_tools", name: "Pumkin's Avatar Tools", section: "Editor Tools",
    type: "vpm", author: "Pumkin", cost: "free",
    vpmRepoUrl: "https://rurre.github.io/vpm/index.json",
    packageSearchName: "Pumkin's Avatar Tools",
    install: [
      "VCC → Add Repository → https://rurre.github.io/vpm/index.json",
      "Project → Manage → search 'Pumkin's Avatar Tools' → + → Apply",
    ],
    setup: [
      "COPY COMPONENTS: Tools → Pumkin's Avatar Tools → Copy From: source avatar, Copy To: target avatar",
      "Check: ✅ PhysBones, ✅ Colliders, ✅ Viseme Blendshapes, ✅ View Position → Copy Selected",
      "AUTO VISEMES: Select avatar → Edit Viewpoint and Visemes → Auto Detect Visemes → verify mapping → Apply",
    ],
  },

  // Section 19: VRLabs Gimmick Prefabs
  {
    id: "vrlabs_marker", name: "VRLabs Marker", section: "Gimmick Prefabs",
    type: "vpm", cost: "free",
    vpmRepoUrl: "https://vrlabs.dev/packages",
    packageSearchName: "Marker",
    install: [
      "VCC → Add Repository → https://vrlabs.dev/packages",
      "Project → Manage → search 'Marker' → + → Apply",
    ],
    setup: [
      "Drag Marker prefab onto avatar root",
      "Pen Bone: drag RightIndexDistal (or finger tip bone). Menu Path: default Marker/",
      "Prefab includes MA components — auto-installs into expression menu",
    ],
    test: ["Play → Expression Menu → Marker → Enable Draw Mode → move hand → colored lines appear"],
  },

  // Section 20: Dreadrith Tools
  {
    id: "dreadscripts", name: "DreadScripts Collection", section: "Dreadrith Tools",
    type: "manual", author: "Dreadrith", cost: "free",
    url: "https://github.com/Dreadrith/DreadScripts/releases/latest",
    install: ["Download .unitypackage from GitHub releases → drag into Unity → Import"],
    setup: [
      "PB/DB CONVERTER: DreadScripts → PB/DB Converter → select direction → Convert",
      "MISSING SCRIPT FINDER: DreadScripts → Find Missing Scripts → scans scene",
      "BULK PROPERTY CHANGER: DreadScripts → Bulk Property Changer → drag materials → set property → Apply",
    ],
  },

  // Section 21: Performance
  {
    id: "actual_performance_window", name: "Actual Performance Window", section: "Performance Analysis",
    type: "vpm", author: "anatawa12", cost: "free",
    vpmRepoUrl: "https://vpm.anatawa12.com/vpm.json",
    packageSearchName: "Actual Performance Window",
    install: [
      "VCC → Add Repository → https://vpm.anatawa12.com/vpm.json (same as AAO)",
      "Project → Manage → search 'Actual Performance Window' → + → Apply",
    ],
    setup: [
      "Window → Actual Performance Window → select avatar root → Calculate",
      "Shows POST-OPTIMIZATION: Polygon Count, Bone Count, Material Count, Skinned Mesh Count, PhysBone Count",
      "Overall Performance Rank: Excellent / Good / Medium / Poor / Very Poor",
    ],
  },

  // Section 23: Locomotion
  {
    id: "gogoloco", name: "GoGo Loco", section: "Locomotion",
    type: "manual", author: "Franada", cost: "free",
    url: "https://booth.pm/en/items/3290806",
    install: [
      "Go to https://booth.pm/en/items/3290806 → download .unitypackage → drag into Unity → Import All",
    ],
    setup: [
      "NEW VERSION (MA): Drag 'GoGo Loco (MA)' prefab onto avatar root",
      "Avatar Root: auto-filled. Write Defaults: match your FX layer's setting",
      "Quest Support: ✅ if needed. Ctrl+S",
      "OLD VERSION: Merge GoGo Loco FX layer manually using Avatars 3.0 Manager",
    ],
    test: [
      "Play → Gesture Manager → Expression Menu → GoGo Loco menu",
      "Test: Sitting, Lying Down, Floating, Height Adjust (radial puppet)",
    ],
    commonIssues: [
      "T-posing during locomotion: Write Defaults mismatch — match GoGo Loco to your FX layer",
      "Height adjust doesn't work in FBT: verify FBT mode enabled in GoGo Loco settings",
    ],
  },

  // Section 26: Avatars 3.0 Manager
  {
    id: "av3manager", name: "Avatars 3.0 Manager", section: "SDK Helpers",
    type: "vpm", cost: "free",
    vpmRepoUrl: "https://vrlabs.dev/packages",
    packageSearchName: "Avatars 3.0 Manager",
    install: ["VCC → VRLabs repo → search 'Avatars 3.0 Manager' → install"],
    setup: [
      "MERGE FX: VRLabs → Avatars 3.0 Manager → Avatar: drag avatar root → FX Layer tab → Merge Animator Controller",
      "Drag source controller → Merge. Repeat for Parameters and Expression Menu",
      "Used for tools that don't use MA and give you raw FX controllers to merge",
    ],
  },
];

// ── Common Errors & Fixes ──
export const COMMON_ERRORS = [
  {
    error: "Script X is missing after importing",
    cause: "Script in different namespace or dependency not installed",
    fix: [
      "Check Console for full error (Window → General → Console)",
      "Look for 'The type or namespace X could not be found' — names the missing package",
      "Find and install that package from VPM or GitHub",
      "If persists: Assets → Reimport All",
    ],
  },
  {
    error: "VCC shows 'Failed to resolve dependencies'",
    cause: "Two packages require conflicting versions of a shared dependency",
    fix: [
      "In VCC, look for orange/red highlighted packages",
      "Try selecting an older version of the conflicting package",
      "If NDMF version is the conflict: update ALL tools to latest simultaneously",
    ],
  },
  {
    error: "Avatar looks correct in edit mode but wrong/broken in Play mode",
    cause: "An NDMF build-time transformation is failing",
    fix: [
      "Select avatar root → ndmf → Manual Bake Avatar",
      "Check Console for NDMF error messages (red)",
      "Error names which plugin is failing — check that tool's settings",
    ],
  },
  {
    error: "Av3Emulator says 'No Avatar Descriptor found'",
    cause: "Avatar root doesn't have VRCAvatarDescriptor, or no avatar in scene",
    fix: [
      "Select avatar root → Add Component → VRCAvatarDescriptor",
      "Set View Position (click View button to set eye height)",
    ],
  },
];

/**
 * Query the install guide by tool name, section, or search term.
 */
export function queryInstallGuide(query: {
  tool_name?: string;
  section?: string;
  search?: string;
  list_sections?: boolean;
  get_prerequisites?: boolean;
  get_repos?: boolean;
  get_errors?: boolean;
}): unknown {
  if (query.get_prerequisites) {
    return GLOBAL_PREREQUISITES;
  }

  if (query.get_repos) {
    return { repoCount: VPM_REPOS.length, repos: VPM_REPOS };
  }

  if (query.get_errors) {
    return { errorCount: COMMON_ERRORS.length, errors: COMMON_ERRORS };
  }

  if (query.list_sections) {
    const sections = [...new Set(INSTALL_ENTRIES.map((e) => e.section))];
    return {
      sections: sections.map((s) => ({
        section: s,
        toolCount: INSTALL_ENTRIES.filter((e) => e.section === s).length,
        tools: INSTALL_ENTRIES.filter((e) => e.section === s).map((e) => e.name),
      })),
    };
  }

  if (query.tool_name) {
    const needle = query.tool_name.toLowerCase();
    const entry = INSTALL_ENTRIES.find(
      (e) =>
        e.name.toLowerCase().includes(needle) ||
        e.id.toLowerCase().includes(needle) ||
        (e.packageSearchName && e.packageSearchName.toLowerCase().includes(needle))
    );
    if (entry) return entry;
    return { error: `Tool '${query.tool_name}' not found in install guide. Use list_sections=true to browse.` };
  }

  if (query.section) {
    const needle = query.section.toLowerCase();
    const entries = INSTALL_ENTRIES.filter((e) => e.section.toLowerCase().includes(needle));
    if (entries.length > 0) return { section: entries[0].section, tools: entries };
    return { error: `Section '${query.section}' not found. Use list_sections=true to browse.` };
  }

  if (query.search) {
    const needle = query.search.toLowerCase();
    const results = INSTALL_ENTRIES.filter(
      (e) =>
        e.name.toLowerCase().includes(needle) ||
        e.id.toLowerCase().includes(needle) ||
        e.section.toLowerCase().includes(needle) ||
        e.install.some((s) => s.toLowerCase().includes(needle)) ||
        (e.setup && e.setup.some((s) => s.toLowerCase().includes(needle))) ||
        (e.notes && e.notes.some((s) => s.toLowerCase().includes(needle)))
    );
    return { query: query.search, resultCount: results.length, results: results.slice(0, 10) };
  }

  return {
    totalTools: INSTALL_ENTRIES.length,
    sections: [...new Set(INSTALL_ENTRIES.map((e) => e.section))],
    vpmRepoCount: VPM_REPOS.length,
    hint: "Use tool_name, section, search, get_prerequisites, get_repos, or get_errors params.",
  };
}
