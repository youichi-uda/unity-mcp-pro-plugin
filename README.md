# Unity MCP Pro — Plugin

Unity editor plugin that connects AI assistants (Claude, Cursor, Windsurf) to the Unity editor via WebSocket.

**145 tools across 24 categories** — scene management, GameObjects, scripts, prefabs, physics, lighting, animation, materials, terrain, particles, audio, UI, build pipeline, input simulation, screenshots, testing, and more.

## Features

- **Full Undo/Redo** — Every AI operation goes through Unity's Undo system (Ctrl+Z)
- **Production-grade WebSocket** — Heartbeat, auto-reconnect with exponential backoff, port scanning (6605–6609)
- **Smart Type Parsing** — Automatic conversion of strings to Vector3, Color, Quaternion, etc.
- **Domain Reload Safe** — Survives script recompilation without losing connection state
- **Unity 2021.3+** — Supports Unity 2022, 2023, and Unity 6 (Built-in, URP, HDRP)

## 24 Tool Categories

| Category | Tools | Category | Tools |
|----------|-------|----------|-------|
| Project | 7 | Animation | 7 |
| Scene | 6 | UI (Canvas) | 6 |
| GameObject | 11 | Audio | 5 |
| Script | 6 | Particle | 5 |
| Editor | 5 | Navigation | 5 |
| Prefab | 6 | Terrain | 4 |
| Material & Shader | 6 | Build Pipeline | 5 |
| Physics | 6 | Batch Operations | 6 |
| Lighting | 5 | Package Manager | 4 |
| Analysis & Profiling | 10 | Debug | 5 |
| Input Simulation | 8 | Screenshot & Visual | 4 |
| Runtime Extended | 7 | Testing & QA | 6 |

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
4. **Configure your AI client** — Add to `.mcp.json`:
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

## Running Tests

1. Open Unity → **Window** → **General** → **Test Runner**
2. Select **EditMode** tab
3. Click **Run All**

Tests cover: `BaseCommand` parameter helpers, `CommandRouter` dispatch, `TypeParser` (Vector/Color parsing), and `JsonHelper` (serialization/deserialization).

## Requirements

- Unity 2021.3 LTS or later
- Node.js 18+ (for the MCP server)
- Any MCP-compatible AI client

## Links

- [Website](https://unity-mcp.abyo.net/)
- [MCP Server (itch.io)](https://y1uda.itch.io/unity-mcp-pro)
- [Discord](https://discord.gg/F4gR739y)

## License

MIT License — see [LICENSE](LICENSE)
