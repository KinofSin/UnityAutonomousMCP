# Unity MCP Re-Analysis (2026-02-16)

## Sources reviewed

1. IvanMurzak/Unity-MCP
2. isuzu-shiranui/UnityMCP
3. CoderGamester/mcp-unity
4. CoplayDev/unity-mcp

## Capability matrix

| Capability | IvanMurzak | isuzu | CoderGamester | CoplayDev | Gap for autonomous agent |
|---|---:|---:|---:|---:|---|
| Broad editor tools | High | Low-Med | Med-High | High | Mostly solved |
| Runtime AI bridge | High | Low | Low | Med | Needs policy/sandbox layer |
| Resources support | Med | Med | Med | High | Add richer runtime/project state resources |
| Prompt support | Med | High | Low | Low | Add autonomous planning prompts |
| Batch execution | Low-Med | Low | Low | High | Keep + enforce transactional safety |
| Multi-instance Unity routing | Low | Low | Low | High | Keep and improve |
| Script validation (Roslyn) | Low | Low | Low | High | Keep optional strict mode |
| Extensible handler architecture | Med | High | Med | Med | Keep, add capability metadata |
| Autonomous planning loop | None | None | None | None | **Missing** |
| Goal decomposition | None | None | None | None | **Missing** |
| Decision policy/guardrails | None | None | None | None | **Missing** |
| Recovery/retry strategy | Low | Low | Low | Med | Improve with explicit plan+rollback |
| Auditable execution graph | Low | Low | Low | Med | Add first-class execution traces |

## What was missed in first pass

1. **CoplayDev's breadth** is larger than initially captured (batch tools, strict validation, multi-instance routing).
2. **CoderGamester IDE package-cache integration** is useful for agent coding context.
3. Existing projects are strong at **tooling and transport**, weak at **autonomous agent control loops**.
4. Main missing piece across all reviewed MCPs: **policy-driven autonomous planner/executor with bounded authority**.

## Evolution strategy

Use existing MCP patterns as base and evolve with three autonomous layers:

1. **Planner**: goals -> ordered executable steps
2. **Policy Engine**: allow/deny/risk score for each step
3. **Executor**: transactional/batch execution + retries + rollback hooks

This repo now proceeds with that architecture for Unity 2022.3.22f1 compatibility.
