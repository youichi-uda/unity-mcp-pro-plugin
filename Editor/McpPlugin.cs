using UnityEngine;
using UnityEditor;

namespace UnityMcpPro
{
    [InitializeOnLoad]
    public class McpPlugin : EditorWindow
    {
        private static McpPlugin _instance;
        private static WebSocketServer _wsServer;
        private static CommandRouter _router;
        private static bool _initialized;

        static McpPlugin()
        {
            // Use update callback as primary initialization trigger
            // This is more reliable than delayCall across domain reloads
            EditorApplication.update += OnEditorUpdate;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        [MenuItem("Window/Unity MCP Pro")]
        public static void ShowWindow()
        {
            _instance = GetWindow<McpPlugin>("Unity MCP Pro");
        }

        private static void OnEditorUpdate()
        {
            if (!_initialized)
            {
                Initialize();
            }
        }

        private static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            _router = new CommandRouter();
            RegisterCommands();

            _wsServer = new WebSocketServer(_router);
            _wsServer.Start();

            Debug.Log("[MCP] Unity MCP Pro plugin initialized");
        }

        private static void RegisterCommands()
        {
            // MVP (26 tools)
            ProjectCommands.Register(_router);
            SceneCommands.Register(_router);
            GameObjectCommands.Register(_router);
            ScriptCommands.Register(_router);
            EditorCommands.Register(_router);

            // Tier 1 (29 tools)
            PrefabCommands.Register(_router);
            MaterialCommands.Register(_router);
            PhysicsCommands.Register(_router);
            LightingCommands.Register(_router);
            UICommands.Register(_router);

            // Tier 2 (22 tools)
            AnimationCommands.Register(_router);
            BuildCommands.Register(_router);
            BatchCommands.Register(_router);
            AudioCommands.Register(_router);

            // Tier 3 (15 tools)
            AnalysisCommands.Register(_router);
            NavigationCommands.Register(_router);
            ParticleCommands.Register(_router);
            PackageCommands.Register(_router);
            TerrainCommands.Register(_router);

            // Debug (5 tools)
            DebugCommands.Register(_router);

            // Input Simulation (4 + 4 tools)
            InputCommands.Register(_router);

            // Screenshot & Visual (4 tools)
            ScreenshotCommands.Register(_router);

            // Runtime Extended (7 tools)
            RuntimeCommands.Register(_router);

            // Testing & QA (6 tools)
            TestingCommands.Register(_router);
        }

        private static void OnBeforeAssemblyReload()
        {
            _wsServer?.Stop();
            _wsServer = null;
            _router = null;
            _initialized = false;
        }

        private void OnDestroy()
        {
            // Window closed, but keep server running
        }

        private void OnGUI()
        {
            GUILayout.Label("Unity MCP Pro", EditorStyles.boldLabel);
            GUILayout.Space(10);

            bool connected = _wsServer != null && _wsServer.IsConnected;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Status:");
            var style = new GUIStyle(EditorStyles.label);
            style.normal.textColor = connected ? Color.green : Color.yellow;
            GUILayout.Label(connected ? "Connected" : "Waiting for MCP server...", style);
            EditorGUILayout.EndHorizontal();

            if (_wsServer != null)
            {
                EditorGUILayout.LabelField("Port", _wsServer.Port.ToString());
            }

            GUILayout.Space(10);

            if (GUILayout.Button("Restart Connection"))
            {
                _wsServer?.Stop();
                _wsServer = null;
                _router = null;
                _initialized = false;
                Initialize();
            }
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }
    }
}
