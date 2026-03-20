# Unity MCP Pro — Plugin

Unity editor plugin that connects AI assistants (Claude, Cursor, Windsurf, VS Code Copilot) to the Unity editor via WebSocket.

**280+ tools across 50 categories** — scene management, GameObjects, scripts, prefabs, physics, lighting, animation, materials, terrain, particles, audio, UI, build pipeline, input simulation, screenshots, testing, 2D, timeline, splines, ECS, netcode, XR, and more.

## Features

- **Full Undo/Redo** — Every AI operation goes through Unity's Undo system (Ctrl+Z)
- **Production-grade WebSocket** — Heartbeat, auto-reconnect with exponential backoff, port scanning (6605–6609)
- **Smart Type Parsing** — Automatic conversion of strings to Vector3, Color, Quaternion, etc.
- **Domain Reload Safe** — Survives script recompilation without losing connection state
- **Server Setup Window** — Built-in editor window for configuring MCP server and IDE integration
- **Unity 2021.3+** — Supports Unity 2022, 2023, and Unity 6 (Built-in, URP, HDRP)

## 50 Tool Categories

| Category | # | Category | # |
|----------|---|----------|---|
| Project | 7 | Animation | 7 |
| Scene | 6 | Animation Extended | 5 |
| GameObject | 11 | UI (Canvas) | 6 |
| Script | 6 | Audio | 5 |
| Editor | 5 | Particle | 5 |
| Prefab | 6 | Navigation | 5 |
| Material & Shader | 6 | Terrain | 4 |
| Physics | 6 | Build Pipeline | 5 |
| Lighting | 5 | Batch Operations | 6 |
| Analysis & Profiling | 10 | Package Manager | 6 |
| Input Simulation | 8 | Debug | 5 |
| Runtime Extended | 7 | Screenshot & Visual | 4 |
| Testing & QA | 6 | 2D Tools | 6 |
| Controller | 4 | Timeline | 5 |
| Environment | 6 | Spline | 5 |
| Optimization | 7 | Shader Graph | 5 |
| Camera / Cinemachine | 6 | Visual Scripting | 5 |
| Post-Processing | 5 | Profiler | 4 |
| AI Tools | 4 | Benchmark | 4 |
| Game Systems | 5 | Playthrough | 4 |
| Import Settings | 5 | Watch / Monitor | 3 |
| Multi-Scene | 4 | Undo History | 3 |
| Scene View Camera | 4 | Addressables | 5 |
| Custom Editor | 4 | Localization | 4 |
| Rigging | 4 | ECS / DOTS | 4 |
| Netcode | 4 | XR / VR | 4 |

## Installation

### Option 1: Unity Package Manager (Git URL)

1. Open Unity → **Window** → **Package Manager**
2. Click **+** → **Add package from git URL...**
3. Enter:
   ```
   https://github.com/youichi-uda/unity-mcp-pro-plugin.git
   ```

### Option 2: Clone to your project

```bash
cd YourUnityProject/Packages
git clone https://github.com/youichi-uda/unity-mcp-pro-plugin.git com.unity-mcp-pro
```

### Option 3: Download and copy

Download this repository and copy it into your project's `Packages/com.unity-mcp-pro/` directory.

## Setup

This plugin is the **Unity-side component** of Unity MCP Pro. To use it with AI assistants, you also need the MCP server.

1. **Install this plugin** (see above)
2. **Get the MCP server** — Available at [unity-mcp.abyo.net](https://unity-mcp.abyo.net/)
3. **Build the MCP server**:
   ```bash
   cd server && npm install && npm run build
   ```
4. **Configure your AI client** (or use the built-in **Window → Unity MCP Pro → Server Setup**):
   ```json
   {
     "mcpServers": {
       "unity-mcp-pro": {
         "command": "node",
         "args": ["/path/to/server/build/index.js"]
       }
     }
   }
   ```
5. **Open Unity** — The plugin auto-connects when the editor starts. Check **Window → Unity MCP Pro** for connection status.

## Architecture

```
AI Assistant  ←—stdio/MCP—→  Node.js MCP Server  ←—WebSocket—→  Unity Editor Plugin (this repo)
```

The plugin runs a WebSocket client inside the Unity editor that connects to the MCP server on `127.0.0.1:6605–6609`. All tool calls are dispatched through the `CommandRouter` to domain-specific command handlers.

## Multi-Editor Support

Run multiple Unity editors simultaneously on ports `6605`-`6609`. Set the `UNITY_MCP_PORT` environment variable to target a specific instance.

## Running Tests

1. Open Unity → **Window** → **General** → **Test Runner**
2. Select **EditMode** tab
3. Click **Run All**

## Requirements

- Unity 2021.3 LTS or later
- Node.js 18+ (for the MCP server)
- Any MCP-compatible AI client

## Links

- [Website](https://unity-mcp.abyo.net/)
- [Unity Asset Store](https://u3d.as/3TYe) — AI Bridge for Unity (MCP Pro)
- [MCP Server (itch.io)](https://y1uda.itch.io/unity-mcp-pro)
- [Discord](https://discord.gg/TMH2648mS2)

## License

MIT License — see [LICENSE](LICENSE)
