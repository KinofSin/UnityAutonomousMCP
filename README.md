# Unity Autonomous Agent MCP

🤖 **A comprehensive autonomous agent framework for Unity 2022.3.22f1** that combines the power of Model Context Protocol (MCP) with advanced AI decision-making capabilities.

## 🌟 Key Features

### 🧠 Autonomous Agent Capabilities
- **Intelligent Task Planning**: AI-driven task decomposition and execution planning
- **Decision Making**: Context-aware decision trees with learning capabilities
- **Self-Improvement**: Continuous learning from user interactions and outcomes
- **Multi-Agent Coordination**: Support for multiple autonomous agents working together

### 🎮 Unity Integration
- **Full Editor Control**: Complete Unity Editor automation and manipulation
- **Runtime AI**: In-game autonomous NPC behavior and debugging
- **Asset Management**: Intelligent asset creation, optimization, and organization
- **Scene Management**: Automated scene setup, optimization, and testing

### 🔧 Advanced MCP Features
- **Extensible Architecture**: Plugin-based system for custom tools and handlers
- **Real-time Communication**: Low-latency TCP/IP communication with external AI services
- **Multi-Provider Support**: Compatible with Claude, GPT, Gemini, and custom LLM providers
- **Resource Management**: Intelligent resource allocation and optimization

## 🏗️ Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    Unity Autonomous Agent MCP              │
├─────────────────────────────────────────────────────────────┤
│  ┌─────────────────┐  ┌─────────────────┐  ┌──────────────┐ │
│  │   Agent Core    │  │   Task Planner  │  │ Decision     │ │
│  │                 │  │                 │  │ Engine       │ │
│  │ - State Mgmt    │  │ - Task Decomp   │  │ - ML Models  │ │
│  │ - Learning      │  │ - Priority      │  │ - Context    │ │
│  │ - Memory        │  │ - Scheduling    │  │ - Reasoning  │ │
│  └─────────────────┘  └─────────────────┘  └──────────────┘ │
├─────────────────────────────────────────────────────────────┤
│  ┌─────────────────┐  ┌─────────────────┐  ┌──────────────┐ │
│  │  MCP Server     │  │  Unity Bridge   │  │ Plugin       │ │
│  │                 │  │                 │  │ System       │ │
│  │ - Protocol      │  │ - Editor API    │  │ - Custom     │ │
│  │ - Handlers      │  │ - Runtime API   │  │ Tools        │ │
│  │ - Transport     │  │ - Asset Mgmt    │  │ - Extensions │ │
│  └─────────────────┘  └─────────────────┘  └──────────────┘ │
├─────────────────────────────────────────────────────────────┤
│                    Unity Engine (2022.3.22f1)              │
└─────────────────────────────────────────────────────────────┘
```

## 📋 Requirements

- **Unity 2022.3.22f1** or later (tested up to Unity 6.1)
- **.NET/C# 9.0**
- **Node.js 18.0.0+** with npm (for TypeScript server)
- **Python 3.8+** (for ML/AI components)
- **External LLM Provider** (Claude, OpenAI, Gemini, or custom)

## 🚀 Quick Start

> Re-analysis details and capability gaps are documented in `docs/capability-matrix.md`.

### 1. Installation

```bash
# Clone the repository
git clone https://github.com/KinofSin/UnityAutonomousMCP.git
cd UnityAutonomousMCP

# Install Unity Package
# In Unity Editor: Window > Package Manager > Add package from git URL
# Enter: file:///path/to/UnityAutonomousMCP/com.autonomous-unity.mcp
```

### 2. Setup TypeScript Server

```bash
npm install
npm run build
npm run smoke
```

`npm run smoke` validates planner + executor behavior (including `run_tests` -> `get_test_job` polling) for both successful and failed test-job terminal paths, without requiring a live Unity Editor.

### 3. Run modes

```bash
# Autonomous bootstrap (local dry-run using mock Unity bridge)
npm run dev -- -- "inspect scene and update scripts"

# MCP stdio mode (for Claude/Cursor/Windsurf MCP client config)
npm run dev -- --mcp

# Force mock bridge explicitly
npm run dev -- --mock -- "inspect scene and update scripts"
```

### 4. Transport bridge configuration (real Unity connection)

Unity package host (inside Unity Editor) serves both transports:

- HTTP endpoint: `POST /mcp/tool` on `UNITY_HTTP_PORT` (default `8080`)
- TCP endpoint: newline-delimited JSON on `UNITY_TCP_PORT` (default `8081`)

Node-side bridge environment variables:

```bash
UNITY_TRANSPORT=http       # http | tcp | mock
UNITY_HOST=127.0.0.1
UNITY_HTTP_PORT=8080
UNITY_TCP_PORT=8081
UNITY_TIMEOUT_MS=10000
UNITY_TEST_POLL_ATTEMPTS=30
UNITY_TEST_POLL_INTERVAL_MS=1000
UNITY_TEST_POLL_TIMEOUT_MS=120000
```

### 5. Configure Claude Desktop

```json
{
  "mcpServers": {
    "unity-autonomous-agent": {
      "command": "node",
      "args": ["path/to/server/dist/index.js"],
      "env": {
        "UNITY_HOST": "localhost",
        "UNITY_TRANSPORT": "http",
        "UNITY_HTTP_PORT": "8080",
        "UNITY_TCP_PORT": "8081",
        "AI_PROVIDER": "anthropic",
        "AI_API_KEY": "your-api-key"
      }
    }
  }
}
```

### 6. Start Using

1. Open Unity Project
2. Navigate to `Edit > Preferences > Autonomous Agent MCP`
3. Configure host + HTTP/TCP ports
4. Click "Connect" to start Unity transport host
5. Start MCP server (`npm run dev -- --mcp`)
6. Begin interacting with AI through your preferred MCP client

## ✅ Current implementation status (this repo)

- **Capability matrix + gap analysis**: `docs/capability-matrix.md`
- **Autonomous planner core**: `server/src/planner.ts`
- **Policy guardrails**: `server/src/policy.ts`
- **Plan executor**: `server/src/executor.ts`
- **MCP server tools** (`autonomous_plan`, `unity_tool_call`, `list_capabilities`): `server/src/mcpServer.ts`
- **Real Unity bridge transports (HTTP/TCP + env-driven mode)**: `server/src/unityBridge.ts`
- **Concrete step contracts for autonomous_plan**: `server/src/contracts.ts`
- **Unity 2022.3.22f1 package scaffold**: `com.autonomous-unity.mcp/`
  - Editor settings/provider: `Editor/AutonomousMcpSettingsProvider.cs`
  - Real transport host: `Editor/AutonomousMcpTransportHost.cs`
  - Tool dispatcher: `Editor/AutonomousMcpToolDispatcher.cs`
  - Runtime entry component: `Runtime/AutonomousMcpRuntime.cs`

## 🔁 End-to-end autonomous_plan contract mapping

`autonomous_plan` now emits concrete Unity tool payloads:

1. `read_console` → `{ level, limit }`
2. `manage_scene` → `{ action: "inspect_active_scene" }`
3. `manage_script` → `{ action: "create_or_update", scriptPath, contents }` *(when goal implies code changes)*
4. `validate_script` → `{ strict }` *(after script edits)*
5. `run_tests` → `{ mode }` *(when goal mentions tests; returns `jobId`)*
6. `get_test_job` → `{ jobId }` *(executor polls until `completed` or `failed`)*
7. `batch_execute` → `{ operations: [{ tool: "manage_scene", params: { action: "save_active_scene" } }] }`

### Test execution flow

1. `run_tests` starts an async Unity Test Runner job (`editmode` or `playmode`)
2. Server receives `{ jobId, status: "queued" }`
3. Executor automatically calls `get_test_job` until terminal state
4. Final test summary (passed/failed/skipped + per-test details) is included in execution results

### Test polling policy limits

- `UNITY_TEST_POLL_ATTEMPTS`: max polling iterations for `get_test_job` (clamped 1..300)
- `UNITY_TEST_POLL_INTERVAL_MS`: delay between polls in milliseconds (clamped 100..60000)
- `UNITY_TEST_POLL_TIMEOUT_MS`: overall timeout for polling cycle (clamped 1000..1800000)

## 🤖 Autonomous Agent Features

### Intelligent Task Planning
- **Goal Decomposition**: Break complex tasks into manageable subtasks
- **Priority Management**: Dynamic task prioritization based on context
- **Resource Allocation**: Optimal distribution of system resources
- **Dependency Resolution**: Handle task dependencies and conflicts

### Learning & Adaptation
- **Pattern Recognition**: Learn from user behavior and preferences
- **Performance Optimization**: Improve efficiency over time
- **Error Recovery**: Learn from mistakes and avoid repetition
- **Context Awareness**: Adapt to project-specific requirements

### Multi-Agent Coordination
- **Agent Communication**: Coordinate multiple specialized agents
- **Task Distribution**: Distribute workload across agent instances
- **Conflict Resolution**: Handle competing priorities and resource conflicts
- **Collaborative Problem Solving**: Combine multiple agent perspectives

## 🔧 MCP Tools & Capabilities

### 🎮 Unity Editor Tools
- **Scene Management**: Create, modify, optimize scenes automatically
- **Asset Operations**: Intelligent asset creation and organization
- **GameObject Control**: Advanced object manipulation and optimization
- **Component Management**: Dynamic component addition and configuration
- **Build Automation**: Automated build processes and optimization

### 🧩 Advanced Development Tools
- **Code Generation**: AI-assisted script writing and optimization
- **Testing Automation**: Automated test creation and execution
- **Performance Analysis**: Real-time performance monitoring and optimization
- **Debugging Assistant**: Intelligent error detection and resolution
- **Documentation Generation**: Auto-generate technical documentation

### 🎯 Runtime AI Features
- **NPC Behavior**: Dynamic, intelligent NPC behavior systems
- **Game Balance**: Automated game balance testing and adjustment
- **Player Analytics**: Real-time player behavior analysis
- **Dynamic Difficulty**: Adaptive difficulty adjustment systems
- **Content Generation**: Procedural content creation and optimization

## 🔌 Plugin System

Create custom plugins to extend functionality:

```csharp
[AutonomousPlugin("custom-tool")]
public class CustomToolPlugin : IAutonomousTool
{
    public async Task<ToolResult> ExecuteAsync(ToolContext context)
    {
        // Your custom tool logic
        return new ToolResult { Success = true, Data = result };
    }
}
```

## 📊 Performance & Monitoring

### Real-time Metrics
- **Agent Performance**: Monitor agent efficiency and accuracy
- **Resource Usage**: Track CPU, memory, and network utilization
- **Task Completion**: Measure task success rates and timing
- **Learning Progress**: Track agent improvement over time

### Optimization Features
- **Caching System**: Intelligent caching for frequently used data
- **Batch Processing**: Optimize multiple operations together
- **Predictive Loading**: Anticipate and preload required resources
- **Adaptive Performance**: Adjust performance based on system capabilities

## 🛡️ Security & Safety

### Safety Measures
- **Sandboxed Execution**: Isolate agent operations from critical systems
- **Permission System**: Granular control over agent capabilities
- **Audit Logging**: Complete audit trail of all agent actions
- **Rollback Capability**: Undo system for agent modifications

### Best Practices
- **Regular Backups**: Automatic backup before major operations
- **Validation Checks**: Verify operation safety before execution
- **User Confirmation**: Require confirmation for destructive operations
- **Error Handling**: Comprehensive error recovery mechanisms

## 🤝 Contributing

We welcome contributions! Please see our [Contributing Guide](CONTRIBUTING.md) for details.

### Development Setup
1. Clone repository
2. Install dependencies (`npm install` in server directory)
3. Open Unity project
4. Enable Developer Mode in preferences
5. Start contributing!

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- Built upon the excellent foundations of [Unity-MCP](https://github.com/IvanMurzak/Unity-MCP) and [UnityMCP](https://github.com/isuzu-shiranui/UnityMCP)
- Inspired by the latest advances in autonomous agents and AI decision-making
- Community feedback and contributions have been invaluable

---

**🚀 Ready to transform your Unity development with AI-powered autonomy?** 

Start building intelligent, self-improving Unity projects today!
