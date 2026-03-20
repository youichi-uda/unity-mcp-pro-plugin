using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace UnityMcpPro
{
    public class AICommands : BaseCommand
    {
        public static void Register(CommandRouter router)
        {
            router.Register("create_state_machine", CreateStateMachine);
            router.Register("create_waypoint_system", CreateWaypointSystem);
            router.Register("setup_ai_agent", SetupAIAgent);
            router.Register("create_patrol_route", CreatePatrolRoute);
        }

        #region create_state_machine

        private static object CreateStateMachine(Dictionary<string, object> p)
        {
            ThrowIfPlaying("create_state_machine");

            string scriptPath = GetStringParam(p, "script_path", "Assets/Scripts/AI/StateMachine.cs");
            string[] states = GetStringListParam(p, "states");
            string initialState = GetStringParam(p, "initial_state");

            if (states == null || states.Length == 0)
                throw new ArgumentException("At least one state is required in the 'states' array");

            if (!scriptPath.StartsWith("Assets/"))
                scriptPath = "Assets/" + scriptPath;
            if (!scriptPath.EndsWith(".cs"))
                scriptPath += ".cs";

            if (string.IsNullOrEmpty(initialState))
                initialState = states[0];

            string content = GenerateStateMachineScript(states, initialState);
            string fullPath = Path.Combine(Application.dataPath.Replace("/Assets", ""), scriptPath);

            string dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(fullPath, content);
            AssetDatabase.Refresh();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "message", $"State machine script created at {scriptPath}" },
                { "path", scriptPath },
                { "states", states },
                { "initial_state", initialState }
            };
        }

        private static string GenerateStateMachineScript(string[] states, string initialState)
        {
            var sb = new StringBuilder();

            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();

            // IState interface
            sb.AppendLine("#region IState Interface");
            sb.AppendLine();
            sb.AppendLine("/// <summary>");
            sb.AppendLine("/// Interface for all states in the finite state machine.");
            sb.AppendLine("/// </summary>");
            sb.AppendLine("public interface IState");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>Called when entering this state.</summary>");
            sb.AppendLine("    void Enter();");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>Called every frame while in this state.</summary>");
            sb.AppendLine("    void Execute();");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>Called when exiting this state.</summary>");
            sb.AppendLine("    void Exit();");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("#endregion");
            sb.AppendLine();

            // StateMachine class
            sb.AppendLine("#region StateMachine");
            sb.AppendLine();
            sb.AppendLine("/// <summary>");
            sb.AppendLine("/// Finite state machine that manages state transitions.");
            sb.AppendLine("/// </summary>");
            sb.AppendLine("public class StateMachine : MonoBehaviour");
            sb.AppendLine("{");
            sb.AppendLine("    [SerializeField] private bool debugLog = false;");
            sb.AppendLine();
            sb.AppendLine("    private Dictionary<Type, IState> _states = new Dictionary<Type, IState>();");
            sb.AppendLine("    private IState _currentState;");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>The currently active state.</summary>");
            sb.AppendLine("    public IState CurrentState => _currentState;");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>Event fired when the state changes. Passes (oldState, newState).</summary>");
            sb.AppendLine("    public event Action<IState, IState> OnStateChanged;");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Register a state instance with the state machine.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public void AddState(IState state)");
            sb.AppendLine("    {");
            sb.AppendLine("        _states[state.GetType()] = state;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Transition to a new state by type.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public void ChangeState<T>() where T : IState");
            sb.AppendLine("    {");
            sb.AppendLine("        if (!_states.TryGetValue(typeof(T), out var newState))");
            sb.AppendLine("        {");
            sb.AppendLine("            Debug.LogError($\"State {typeof(T).Name} not registered in StateMachine.\");");
            sb.AppendLine("            return;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        var oldState = _currentState;");
            sb.AppendLine("        _currentState?.Exit();");
            sb.AppendLine();
            sb.AppendLine("        if (debugLog)");
            sb.AppendLine("            Debug.Log($\"[StateMachine] {oldState?.GetType().Name ?? \"None\"} -> {newState.GetType().Name}\");");
            sb.AppendLine();
            sb.AppendLine("        _currentState = newState;");
            sb.AppendLine("        _currentState.Enter();");
            sb.AppendLine("        OnStateChanged?.Invoke(oldState, _currentState);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Check if the current state is of the given type.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public bool IsInState<T>() where T : IState");
            sb.AppendLine("    {");
            sb.AppendLine("        return _currentState != null && _currentState.GetType() == typeof(T);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private void Update()");
            sb.AppendLine("    {");
            sb.AppendLine("        _currentState?.Execute();");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("#endregion");
            sb.AppendLine();

            // Individual state classes
            sb.AppendLine("#region State Implementations");
            sb.AppendLine();

            foreach (string state in states)
            {
                string safeName = state.Replace(" ", "");
                sb.AppendLine($"/// <summary>");
                sb.AppendLine($"/// {safeName} state implementation.");
                sb.AppendLine($"/// </summary>");
                sb.AppendLine($"public class {safeName}State : IState");
                sb.AppendLine("{");
                sb.AppendLine($"    private readonly StateMachine _stateMachine;");
                sb.AppendLine();
                sb.AppendLine($"    public {safeName}State(StateMachine stateMachine)");
                sb.AppendLine("    {");
                sb.AppendLine("        _stateMachine = stateMachine;");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine("    /// <summary>Called when entering this state.</summary>");
                sb.AppendLine("    public void Enter()");
                sb.AppendLine("    {");
                sb.AppendLine($"        Debug.Log(\"Entering {safeName} state\");");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine("    /// <summary>Called every frame while in this state.</summary>");
                sb.AppendLine("    public void Execute()");
                sb.AppendLine("    {");
                sb.AppendLine($"        // TODO: Implement {safeName} behavior");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine("    /// <summary>Called when exiting this state.</summary>");
                sb.AppendLine("    public void Exit()");
                sb.AppendLine("    {");
                sb.AppendLine($"        Debug.Log(\"Exiting {safeName} state\");");
                sb.AppendLine("    }");
                sb.AppendLine("}");
                sb.AppendLine();
            }

            sb.AppendLine("#endregion");
            sb.AppendLine();

            // Setup helper
            sb.AppendLine("#region Setup Helper");
            sb.AppendLine();
            sb.AppendLine("/// <summary>");
            sb.AppendLine("/// Initializes the state machine with all states on Start.");
            sb.AppendLine("/// Attach this component alongside StateMachine.");
            sb.AppendLine("/// </summary>");
            sb.AppendLine("public class StateMachineSetup : MonoBehaviour");
            sb.AppendLine("{");
            sb.AppendLine("    private void Start()");
            sb.AppendLine("    {");
            sb.AppendLine("        var sm = GetComponent<StateMachine>();");
            sb.AppendLine("        if (sm == null)");
            sb.AppendLine("        {");
            sb.AppendLine("            Debug.LogError(\"StateMachine component not found.\");");
            sb.AppendLine("            return;");
            sb.AppendLine("        }");
            sb.AppendLine();

            foreach (string state in states)
            {
                string safeName = state.Replace(" ", "");
                sb.AppendLine($"        sm.AddState(new {safeName}State(sm));");
            }

            sb.AppendLine();
            sb.AppendLine($"        sm.ChangeState<{initialState.Replace(" ", "")}State>();");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("#endregion");

            return sb.ToString();
        }

        #endregion

        #region create_waypoint_system

        private static object CreateWaypointSystem(Dictionary<string, object> p)
        {
            ThrowIfPlaying("create_waypoint_system");

            string name = GetStringParam(p, "name", "WaypointPath");
            string[] waypointStrs = GetStringListParam(p, "waypoints");
            bool loop = GetBoolParam(p, "loop", true);
            string color = GetStringParam(p, "color", "green");

            if (waypointStrs == null || waypointStrs.Length == 0)
                throw new ArgumentException("At least one waypoint position is required");

            // Create parent GameObject
            var parentGo = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(parentGo, "Create WaypointPath");

            // Create child waypoint GameObjects
            var positions = new List<object>();
            for (int i = 0; i < waypointStrs.Length; i++)
            {
                Vector3 pos = TypeParser.ParseVector3(waypointStrs[i]);
                var wpGo = new GameObject($"Waypoint_{i}");
                wpGo.transform.SetParent(parentGo.transform);
                wpGo.transform.position = pos;
                Undo.RegisterCreatedObjectUndo(wpGo, "Create Waypoint");

                positions.Add(new Dictionary<string, object>
                {
                    { "name", wpGo.name },
                    { "position", $"{pos.x},{pos.y},{pos.z}" }
                });
            }

            // Generate and write the WaypointPath script
            string scriptPath = "Assets/Scripts/AI/WaypointPath.cs";
            string content = GenerateWaypointPathScript(loop, color);
            string fullPath = Path.Combine(Application.dataPath.Replace("/Assets", ""), scriptPath);

            string dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(fullPath, content);
            AssetDatabase.Refresh();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "message", $"Waypoint path '{name}' created with {waypointStrs.Length} waypoints" },
                { "gameObject", GetGameObjectPath(parentGo) },
                { "script_path", scriptPath },
                { "waypoints", positions },
                { "loop", loop }
            };
        }

        private static string GenerateWaypointPathScript(bool loop, string color)
        {
            var sb = new StringBuilder();

            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            sb.AppendLine("/// <summary>");
            sb.AppendLine("/// Manages a series of waypoints for AI patrol paths.");
            sb.AppendLine("/// Child transforms are automatically collected as waypoints.");
            sb.AppendLine("/// </summary>");
            sb.AppendLine("public class WaypointPath : MonoBehaviour");
            sb.AppendLine("{");
            sb.AppendLine("    #region Fields");
            sb.AppendLine();
            sb.AppendLine($"    [SerializeField] private bool loop = {(loop ? "true" : "false")};");
            sb.AppendLine($"    [SerializeField] private Color gizmoColor = Color.{color};");
            sb.AppendLine("    [SerializeField] private float gizmoSphereRadius = 0.3f;");
            sb.AppendLine();
            sb.AppendLine("    private List<Transform> _waypoints = new List<Transform>();");
            sb.AppendLine("    private int _currentIndex = 0;");
            sb.AppendLine();
            sb.AppendLine("    #endregion");
            sb.AppendLine();
            sb.AppendLine("    #region Properties");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>Whether the path loops back to the start.</summary>");
            sb.AppendLine("    public bool Loop => loop;");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>Total number of waypoints.</summary>");
            sb.AppendLine("    public int Count => _waypoints.Count;");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>Current waypoint index.</summary>");
            sb.AppendLine("    public int CurrentIndex => _currentIndex;");
            sb.AppendLine();
            sb.AppendLine("    #endregion");
            sb.AppendLine();
            sb.AppendLine("    #region Unity Callbacks");
            sb.AppendLine();
            sb.AppendLine("    private void Awake()");
            sb.AppendLine("    {");
            sb.AppendLine("        CollectWaypoints();");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    #endregion");
            sb.AppendLine();
            sb.AppendLine("    #region Public Methods");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Collect all child transforms as waypoints.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public void CollectWaypoints()");
            sb.AppendLine("    {");
            sb.AppendLine("        _waypoints.Clear();");
            sb.AppendLine("        foreach (Transform child in transform)");
            sb.AppendLine("        {");
            sb.AppendLine("            _waypoints.Add(child);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Get the current waypoint position.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public Vector3 GetCurrentWaypoint()");
            sb.AppendLine("    {");
            sb.AppendLine("        if (_waypoints.Count == 0) return transform.position;");
            sb.AppendLine("        return _waypoints[_currentIndex].position;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Advance to the next waypoint and return its position.");
            sb.AppendLine("    /// Returns Vector3.zero if at the end and not looping.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public Vector3 GetNextWaypoint()");
            sb.AppendLine("    {");
            sb.AppendLine("        if (_waypoints.Count == 0) return transform.position;");
            sb.AppendLine();
            sb.AppendLine("        _currentIndex++;");
            sb.AppendLine("        if (_currentIndex >= _waypoints.Count)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (loop)");
            sb.AppendLine("                _currentIndex = 0;");
            sb.AppendLine("            else");
            sb.AppendLine("                _currentIndex = _waypoints.Count - 1;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        return _waypoints[_currentIndex].position;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Reset to the first waypoint.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public void Reset()");
            sb.AppendLine("    {");
            sb.AppendLine("        _currentIndex = 0;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Check if we have reached the end of a non-looping path.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public bool IsAtEnd()");
            sb.AppendLine("    {");
            sb.AppendLine("        return !loop && _currentIndex >= _waypoints.Count - 1;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    #endregion");
            sb.AppendLine();
            sb.AppendLine("    #region Gizmos");
            sb.AppendLine();
            sb.AppendLine("    private void OnDrawGizmos()");
            sb.AppendLine("    {");
            sb.AppendLine("        var points = new List<Transform>();");
            sb.AppendLine("        foreach (Transform child in transform)");
            sb.AppendLine("            points.Add(child);");
            sb.AppendLine();
            sb.AppendLine("        if (points.Count == 0) return;");
            sb.AppendLine();
            sb.AppendLine("        Gizmos.color = gizmoColor;");
            sb.AppendLine();
            sb.AppendLine("        for (int i = 0; i < points.Count; i++)");
            sb.AppendLine("        {");
            sb.AppendLine("            Gizmos.DrawSphere(points[i].position, gizmoSphereRadius);");
            sb.AppendLine();
            sb.AppendLine("            if (i < points.Count - 1)");
            sb.AppendLine("                Gizmos.DrawLine(points[i].position, points[i + 1].position);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        if (loop && points.Count > 1)");
            sb.AppendLine("            Gizmos.DrawLine(points[points.Count - 1].position, points[0].position);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    #endregion");
            sb.AppendLine("}");

            return sb.ToString();
        }

        #endregion

        #region setup_ai_agent

        private static object SetupAIAgent(Dictionary<string, object> p)
        {
            ThrowIfPlaying("setup_ai_agent");

            string targetPath = GetStringParam(p, "target");
            string patrolPath = GetStringParam(p, "patrol_path");
            float detectionRange = GetFloatParam(p, "detection_range", 10f);
            float fieldOfView = GetFloatParam(p, "field_of_view", 120f);
            float agentSpeed = GetFloatParam(p, "agent_speed", 3.5f);
            string scriptPath = GetStringParam(p, "script_path", "Assets/Scripts/AI/AIAgent.cs");

            if (string.IsNullOrEmpty(targetPath))
                throw new ArgumentException("'target' is required");

            var go = FindGameObject(targetPath);

            // Add NavMeshAgent if not present
            var agent = go.GetComponent<NavMeshAgent>();
            if (agent == null)
                agent = Undo.AddComponent<NavMeshAgent>(go);
            else
                RecordUndo(agent, "Setup AI Agent NavMeshAgent");

            agent.speed = agentSpeed;

            // Generate and write the AIAgent script
            if (!scriptPath.StartsWith("Assets/"))
                scriptPath = "Assets/" + scriptPath;
            if (!scriptPath.EndsWith(".cs"))
                scriptPath += ".cs";

            string content = GenerateAIAgentScript(detectionRange, fieldOfView, agentSpeed);
            string fullPath = Path.Combine(Application.dataPath.Replace("/Assets", ""), scriptPath);

            string dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(fullPath, content);

            // Ensure WaypointPath.cs exists (AIAgent depends on it)
            string waypointScriptPath = "Assets/Scripts/AI/WaypointPath.cs";
            string waypointFullPath = Path.Combine(Application.dataPath.Replace("/Assets", ""), waypointScriptPath);
            if (!File.Exists(waypointFullPath))
            {
                string waypointDir = Path.GetDirectoryName(waypointFullPath);
                if (!Directory.Exists(waypointDir))
                    Directory.CreateDirectory(waypointDir);
                File.WriteAllText(waypointFullPath, GenerateWaypointPathScript(true, "cyan"));
            }

            AssetDatabase.Refresh();

            var result = new Dictionary<string, object>
            {
                { "success", true },
                { "message", $"AI agent configured on '{go.name}'" },
                { "gameObject", GetGameObjectPath(go) },
                { "script_path", scriptPath },
                { "detection_range", detectionRange },
                { "field_of_view", fieldOfView },
                { "agent_speed", agentSpeed }
            };

            if (!string.IsNullOrEmpty(patrolPath))
                result["patrol_path"] = patrolPath;

            return result;
        }

        private static string GenerateAIAgentScript(float detectionRange, float fieldOfView, float agentSpeed)
        {
            var sb = new StringBuilder();

            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using UnityEngine.AI;");
            sb.AppendLine("using UnityEngine.Events;");
            sb.AppendLine();
            sb.AppendLine("/// <summary>");
            sb.AppendLine("/// AI agent with patrol, detection, and chase behavior using NavMeshAgent.");
            sb.AppendLine("/// </summary>");
            sb.AppendLine("[RequireComponent(typeof(NavMeshAgent))]");
            sb.AppendLine("public class AIAgent : MonoBehaviour");
            sb.AppendLine("{");
            sb.AppendLine("    #region Enums");
            sb.AppendLine();
            sb.AppendLine("    public enum AIState");
            sb.AppendLine("    {");
            sb.AppendLine("        Idle,");
            sb.AppendLine("        Patrol,");
            sb.AppendLine("        Chase,");
            sb.AppendLine("        ReturnToPatrol");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    #endregion");
            sb.AppendLine();
            sb.AppendLine("    #region Serialized Fields");
            sb.AppendLine();
            sb.AppendLine("    [Header(\"Detection\")]");
            sb.AppendLine($"    [SerializeField] private float detectionRange = {detectionRange}f;");
            sb.AppendLine($"    [SerializeField] private float fieldOfView = {fieldOfView}f;");
            sb.AppendLine("    [SerializeField] private LayerMask detectionMask = ~0;");
            sb.AppendLine("    [SerializeField] private string playerTag = \"Player\";");
            sb.AppendLine();
            sb.AppendLine("    [Header(\"Movement\")]");
            sb.AppendLine($"    [SerializeField] private float patrolSpeed = {agentSpeed}f;");
            sb.AppendLine($"    [SerializeField] private float chaseSpeed = {agentSpeed * 1.5f}f;");
            sb.AppendLine("    [SerializeField] private float waypointReachedDistance = 0.5f;");
            sb.AppendLine();
            sb.AppendLine("    [Header(\"References\")]");
            sb.AppendLine("    [SerializeField] private WaypointPath patrolPath;");
            sb.AppendLine();
            sb.AppendLine("    [Header(\"Events\")]");
            sb.AppendLine("    [SerializeField] private UnityEvent onPlayerDetected;");
            sb.AppendLine("    [SerializeField] private UnityEvent onPlayerLost;");
            sb.AppendLine();
            sb.AppendLine("    #endregion");
            sb.AppendLine();
            sb.AppendLine("    #region Private Fields");
            sb.AppendLine();
            sb.AppendLine("    private NavMeshAgent _agent;");
            sb.AppendLine("    private AIState _currentState = AIState.Idle;");
            sb.AppendLine("    private Transform _detectedPlayer;");
            sb.AppendLine("    private Vector3 _lastKnownPosition;");
            sb.AppendLine("    private float _loseSightTimer;");
            sb.AppendLine("    private const float LoseSightDelay = 2f;");
            sb.AppendLine();
            sb.AppendLine("    #endregion");
            sb.AppendLine();
            sb.AppendLine("    #region Properties");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>Current AI state.</summary>");
            sb.AppendLine("    public AIState CurrentState => _currentState;");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>Whether the agent currently detects the player.</summary>");
            sb.AppendLine("    public bool IsPlayerDetected => _detectedPlayer != null;");
            sb.AppendLine();
            sb.AppendLine("    #endregion");
            sb.AppendLine();
            sb.AppendLine("    #region Unity Callbacks");
            sb.AppendLine();
            sb.AppendLine("    private void Awake()");
            sb.AppendLine("    {");
            sb.AppendLine("        _agent = GetComponent<NavMeshAgent>();");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private void Start()");
            sb.AppendLine("    {");
            sb.AppendLine("        if (patrolPath != null && patrolPath.Count > 0)");
            sb.AppendLine("            SetState(AIState.Patrol);");
            sb.AppendLine("        else");
            sb.AppendLine("            SetState(AIState.Idle);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private void Update()");
            sb.AppendLine("    {");
            sb.AppendLine("        DetectPlayer();");
            sb.AppendLine();
            sb.AppendLine("        switch (_currentState)");
            sb.AppendLine("        {");
            sb.AppendLine("            case AIState.Idle:");
            sb.AppendLine("                UpdateIdle();");
            sb.AppendLine("                break;");
            sb.AppendLine("            case AIState.Patrol:");
            sb.AppendLine("                UpdatePatrol();");
            sb.AppendLine("                break;");
            sb.AppendLine("            case AIState.Chase:");
            sb.AppendLine("                UpdateChase();");
            sb.AppendLine("                break;");
            sb.AppendLine("            case AIState.ReturnToPatrol:");
            sb.AppendLine("                UpdateReturnToPatrol();");
            sb.AppendLine("                break;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    #endregion");
            sb.AppendLine();
            sb.AppendLine("    #region State Updates");
            sb.AppendLine();
            sb.AppendLine("    private void UpdateIdle()");
            sb.AppendLine("    {");
            sb.AppendLine("        if (_detectedPlayer != null)");
            sb.AppendLine("            SetState(AIState.Chase);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private void UpdatePatrol()");
            sb.AppendLine("    {");
            sb.AppendLine("        if (_detectedPlayer != null)");
            sb.AppendLine("        {");
            sb.AppendLine("            SetState(AIState.Chase);");
            sb.AppendLine("            return;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        if (patrolPath == null) return;");
            sb.AppendLine();
            sb.AppendLine("        if (!_agent.hasPath || _agent.remainingDistance <= waypointReachedDistance)");
            sb.AppendLine("        {");
            sb.AppendLine("            Vector3 next = patrolPath.GetNextWaypoint();");
            sb.AppendLine("            _agent.SetDestination(next);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private void UpdateChase()");
            sb.AppendLine("    {");
            sb.AppendLine("        if (_detectedPlayer != null)");
            sb.AppendLine("        {");
            sb.AppendLine("            _lastKnownPosition = _detectedPlayer.position;");
            sb.AppendLine("            _agent.SetDestination(_lastKnownPosition);");
            sb.AppendLine("            _loseSightTimer = 0f;");
            sb.AppendLine("        }");
            sb.AppendLine("        else");
            sb.AppendLine("        {");
            sb.AppendLine("            _loseSightTimer += Time.deltaTime;");
            sb.AppendLine("            _agent.SetDestination(_lastKnownPosition);");
            sb.AppendLine();
            sb.AppendLine("            if (_loseSightTimer >= LoseSightDelay || _agent.remainingDistance <= waypointReachedDistance)");
            sb.AppendLine("            {");
            sb.AppendLine("                onPlayerLost?.Invoke();");
            sb.AppendLine("                if (patrolPath != null)");
            sb.AppendLine("                    SetState(AIState.ReturnToPatrol);");
            sb.AppendLine("                else");
            sb.AppendLine("                    SetState(AIState.Idle);");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private void UpdateReturnToPatrol()");
            sb.AppendLine("    {");
            sb.AppendLine("        if (_detectedPlayer != null)");
            sb.AppendLine("        {");
            sb.AppendLine("            SetState(AIState.Chase);");
            sb.AppendLine("            return;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        if (!_agent.hasPath || _agent.remainingDistance <= waypointReachedDistance)");
            sb.AppendLine("            SetState(AIState.Patrol);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    #endregion");
            sb.AppendLine();
            sb.AppendLine("    #region Detection");
            sb.AppendLine();
            sb.AppendLine("    private void DetectPlayer()");
            sb.AppendLine("    {");
            sb.AppendLine("        _detectedPlayer = null;");
            sb.AppendLine();
            sb.AppendLine("        var colliders = Physics.OverlapSphere(transform.position, detectionRange, detectionMask);");
            sb.AppendLine("        foreach (var col in colliders)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (!col.CompareTag(playerTag)) continue;");
            sb.AppendLine();
            sb.AppendLine("            Vector3 dirToPlayer = (col.transform.position - transform.position).normalized;");
            sb.AppendLine("            float angle = Vector3.Angle(transform.forward, dirToPlayer);");
            sb.AppendLine();
            sb.AppendLine("            if (angle <= fieldOfView * 0.5f)");
            sb.AppendLine("            {");
            sb.AppendLine("                if (!Physics.Linecast(transform.position + Vector3.up, col.transform.position + Vector3.up, ~detectionMask))");
            sb.AppendLine("                {");
            sb.AppendLine("                    _detectedPlayer = col.transform;");
            sb.AppendLine();
            sb.AppendLine("                    if (_currentState != AIState.Chase)");
            sb.AppendLine("                        onPlayerDetected?.Invoke();");
            sb.AppendLine();
            sb.AppendLine("                    break;");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    #endregion");
            sb.AppendLine();
            sb.AppendLine("    #region State Management");
            sb.AppendLine();
            sb.AppendLine("    private void SetState(AIState newState)");
            sb.AppendLine("    {");
            sb.AppendLine("        _currentState = newState;");
            sb.AppendLine();
            sb.AppendLine("        switch (newState)");
            sb.AppendLine("        {");
            sb.AppendLine("            case AIState.Patrol:");
            sb.AppendLine("                _agent.speed = patrolSpeed;");
            sb.AppendLine("                if (patrolPath != null)");
            sb.AppendLine("                    _agent.SetDestination(patrolPath.GetCurrentWaypoint());");
            sb.AppendLine("                break;");
            sb.AppendLine("            case AIState.Chase:");
            sb.AppendLine("                _agent.speed = chaseSpeed;");
            sb.AppendLine("                break;");
            sb.AppendLine("            case AIState.ReturnToPatrol:");
            sb.AppendLine("                _agent.speed = patrolSpeed;");
            sb.AppendLine("                if (patrolPath != null)");
            sb.AppendLine("                    _agent.SetDestination(patrolPath.GetCurrentWaypoint());");
            sb.AppendLine("                break;");
            sb.AppendLine("            case AIState.Idle:");
            sb.AppendLine("                _agent.ResetPath();");
            sb.AppendLine("                break;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    #endregion");
            sb.AppendLine();
            sb.AppendLine("    #region Gizmos");
            sb.AppendLine();
            sb.AppendLine("    private void OnDrawGizmosSelected()");
            sb.AppendLine("    {");
            sb.AppendLine("        // Detection range");
            sb.AppendLine("        Gizmos.color = new Color(1f, 0f, 0f, 0.15f);");
            sb.AppendLine("        Gizmos.DrawWireSphere(transform.position, detectionRange);");
            sb.AppendLine();
            sb.AppendLine("        // Field of view");
            sb.AppendLine("        Gizmos.color = Color.yellow;");
            sb.AppendLine("        Vector3 leftDir = Quaternion.Euler(0, -fieldOfView * 0.5f, 0) * transform.forward;");
            sb.AppendLine("        Vector3 rightDir = Quaternion.Euler(0, fieldOfView * 0.5f, 0) * transform.forward;");
            sb.AppendLine("        Gizmos.DrawRay(transform.position, leftDir * detectionRange);");
            sb.AppendLine("        Gizmos.DrawRay(transform.position, rightDir * detectionRange);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    #endregion");
            sb.AppendLine("}");

            return sb.ToString();
        }

        #endregion

        #region create_patrol_route

        private static object CreatePatrolRoute(Dictionary<string, object> p)
        {
            ThrowIfPlaying("create_patrol_route");

            string targetPath = GetStringParam(p, "target");
            string[] waypointObjects = GetStringListParam(p, "waypoint_objects");
            string[] waypointPositions = GetStringListParam(p, "waypoint_positions");
            float waitTime = GetFloatParam(p, "wait_time", 2f);

            if (string.IsNullOrEmpty(targetPath))
                throw new ArgumentException("'target' is required");

            if ((waypointObjects == null || waypointObjects.Length == 0) &&
                (waypointPositions == null || waypointPositions.Length == 0))
                throw new ArgumentException("Provide either 'waypoint_objects' or 'waypoint_positions'");

            var go = FindGameObject(targetPath);

            // Add NavMeshAgent if not present
            var agent = go.GetComponent<NavMeshAgent>();
            if (agent == null)
                agent = Undo.AddComponent<NavMeshAgent>(go);

            // Collect waypoint transforms
            var waypointList = new List<object>();

            if (waypointObjects != null && waypointObjects.Length > 0)
            {
                foreach (string wpPath in waypointObjects)
                {
                    var wpGo = FindGameObject(wpPath);
                    waypointList.Add(new Dictionary<string, object>
                    {
                        { "name", wpGo.name },
                        { "position", $"{wpGo.transform.position.x},{wpGo.transform.position.y},{wpGo.transform.position.z}" }
                    });
                }
            }

            if (waypointPositions != null && waypointPositions.Length > 0)
            {
                // Create a container for generated waypoints
                var container = new GameObject($"{go.name}_PatrolWaypoints");
                Undo.RegisterCreatedObjectUndo(container, "Create Patrol Waypoints");

                for (int i = 0; i < waypointPositions.Length; i++)
                {
                    Vector3 pos = TypeParser.ParseVector3(waypointPositions[i]);
                    var wpGo = new GameObject($"PatrolPoint_{i}");
                    wpGo.transform.SetParent(container.transform);
                    wpGo.transform.position = pos;
                    Undo.RegisterCreatedObjectUndo(wpGo, "Create Patrol Point");

                    waypointList.Add(new Dictionary<string, object>
                    {
                        { "name", wpGo.name },
                        { "position", waypointPositions[i] }
                    });
                }
            }

            // Generate and write the PatrolBehavior script
            string scriptPath = "Assets/Scripts/AI/PatrolBehavior.cs";
            string content = GeneratePatrolBehaviorScript(waitTime);
            string fullPath = Path.Combine(Application.dataPath.Replace("/Assets", ""), scriptPath);

            string dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(fullPath, content);
            AssetDatabase.Refresh();

            return new Dictionary<string, object>
            {
                { "success", true },
                { "message", $"Patrol route created for '{go.name}' with {waypointList.Count} waypoints" },
                { "gameObject", GetGameObjectPath(go) },
                { "script_path", scriptPath },
                { "waypoints", waypointList },
                { "wait_time", waitTime }
            };
        }

        private static string GeneratePatrolBehaviorScript(float waitTime)
        {
            var sb = new StringBuilder();

            sb.AppendLine("using System.Collections;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using UnityEngine.AI;");
            sb.AppendLine("using UnityEngine.Events;");
            sb.AppendLine();
            sb.AppendLine("/// <summary>");
            sb.AppendLine("/// Simple patrol behavior that cycles through waypoints using NavMeshAgent.");
            sb.AppendLine("/// </summary>");
            sb.AppendLine("[RequireComponent(typeof(NavMeshAgent))]");
            sb.AppendLine("public class PatrolBehavior : MonoBehaviour");
            sb.AppendLine("{");
            sb.AppendLine("    #region Serialized Fields");
            sb.AppendLine();
            sb.AppendLine("    [Header(\"Patrol Settings\")]");
            sb.AppendLine("    [SerializeField] private List<Transform> waypoints = new List<Transform>();");
            sb.AppendLine($"    [SerializeField] private float waitTimeAtWaypoint = {waitTime}f;");
            sb.AppendLine("    [SerializeField] private bool loop = true;");
            sb.AppendLine("    [SerializeField] private float arrivalDistance = 0.5f;");
            sb.AppendLine();
            sb.AppendLine("    [Header(\"Events\")]");
            sb.AppendLine("    [SerializeField] private UnityEvent<int> onWaypointReached;");
            sb.AppendLine("    [SerializeField] private UnityEvent onPatrolComplete;");
            sb.AppendLine();
            sb.AppendLine("    #endregion");
            sb.AppendLine();
            sb.AppendLine("    #region Private Fields");
            sb.AppendLine();
            sb.AppendLine("    private NavMeshAgent _agent;");
            sb.AppendLine("    private int _currentWaypointIndex = 0;");
            sb.AppendLine("    private bool _isWaiting = false;");
            sb.AppendLine("    private bool _isPatrolling = false;");
            sb.AppendLine();
            sb.AppendLine("    #endregion");
            sb.AppendLine();
            sb.AppendLine("    #region Properties");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>Whether the agent is currently patrolling.</summary>");
            sb.AppendLine("    public bool IsPatrolling => _isPatrolling;");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>Current waypoint index.</summary>");
            sb.AppendLine("    public int CurrentWaypointIndex => _currentWaypointIndex;");
            sb.AppendLine();
            sb.AppendLine("    #endregion");
            sb.AppendLine();
            sb.AppendLine("    #region Unity Callbacks");
            sb.AppendLine();
            sb.AppendLine("    private void Awake()");
            sb.AppendLine("    {");
            sb.AppendLine("        _agent = GetComponent<NavMeshAgent>();");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private void Start()");
            sb.AppendLine("    {");
            sb.AppendLine("        if (waypoints.Count > 0)");
            sb.AppendLine("            StartPatrol();");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private void Update()");
            sb.AppendLine("    {");
            sb.AppendLine("        if (!_isPatrolling || _isWaiting || waypoints.Count == 0) return;");
            sb.AppendLine();
            sb.AppendLine("        if (!_agent.pathPending && _agent.remainingDistance <= arrivalDistance)");
            sb.AppendLine("        {");
            sb.AppendLine("            onWaypointReached?.Invoke(_currentWaypointIndex);");
            sb.AppendLine("            StartCoroutine(WaitAndMoveNext());");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    #endregion");
            sb.AppendLine();
            sb.AppendLine("    #region Public Methods");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Start or resume the patrol.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public void StartPatrol()");
            sb.AppendLine("    {");
            sb.AppendLine("        _isPatrolling = true;");
            sb.AppendLine("        MoveToCurrentWaypoint();");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Stop patrolling and halt the agent.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public void StopPatrol()");
            sb.AppendLine("    {");
            sb.AppendLine("        _isPatrolling = false;");
            sb.AppendLine("        _agent.ResetPath();");
            sb.AppendLine("        StopAllCoroutines();");
            sb.AppendLine("        _isWaiting = false;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Set waypoints at runtime.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public void SetWaypoints(List<Transform> newWaypoints)");
            sb.AppendLine("    {");
            sb.AppendLine("        waypoints = newWaypoints;");
            sb.AppendLine("        _currentWaypointIndex = 0;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    #endregion");
            sb.AppendLine();
            sb.AppendLine("    #region Private Methods");
            sb.AppendLine();
            sb.AppendLine("    private void MoveToCurrentWaypoint()");
            sb.AppendLine("    {");
            sb.AppendLine("        if (waypoints.Count == 0 || _currentWaypointIndex >= waypoints.Count) return;");
            sb.AppendLine("        _agent.SetDestination(waypoints[_currentWaypointIndex].position);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private IEnumerator WaitAndMoveNext()");
            sb.AppendLine("    {");
            sb.AppendLine("        _isWaiting = true;");
            sb.AppendLine("        yield return new WaitForSeconds(waitTimeAtWaypoint);");
            sb.AppendLine("        _isWaiting = false;");
            sb.AppendLine();
            sb.AppendLine("        _currentWaypointIndex++;");
            sb.AppendLine("        if (_currentWaypointIndex >= waypoints.Count)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (loop)");
            sb.AppendLine("            {");
            sb.AppendLine("                _currentWaypointIndex = 0;");
            sb.AppendLine("            }");
            sb.AppendLine("            else");
            sb.AppendLine("            {");
            sb.AppendLine("                _isPatrolling = false;");
            sb.AppendLine("                onPatrolComplete?.Invoke();");
            sb.AppendLine("                yield break;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        MoveToCurrentWaypoint();");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    #endregion");
            sb.AppendLine();
            sb.AppendLine("    #region Gizmos");
            sb.AppendLine();
            sb.AppendLine("    private void OnDrawGizmosSelected()");
            sb.AppendLine("    {");
            sb.AppendLine("        if (waypoints == null || waypoints.Count == 0) return;");
            sb.AppendLine();
            sb.AppendLine("        Gizmos.color = Color.cyan;");
            sb.AppendLine("        for (int i = 0; i < waypoints.Count; i++)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (waypoints[i] == null) continue;");
            sb.AppendLine("            Gizmos.DrawSphere(waypoints[i].position, 0.3f);");
            sb.AppendLine();
            sb.AppendLine("            int next = (i + 1) % waypoints.Count;");
            sb.AppendLine("            if (next != 0 || loop)");
            sb.AppendLine("            {");
            sb.AppendLine("                if (waypoints[next] != null)");
            sb.AppendLine("                    Gizmos.DrawLine(waypoints[i].position, waypoints[next].position);");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    #endregion");
            sb.AppendLine("}");

            return sb.ToString();
        }

        #endregion
    }
}
