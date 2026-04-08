using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR_WIN
using System.Runtime.InteropServices;
#endif
#if HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace UnityMcpPro
{
    public class InputCommands : BaseCommand
    {
        // Track which keys are currently "held" by simulation
#if UNITY_EDITOR_WIN
        private static HashSet<ushort> _heldScanCodes = new HashSet<ushort>();
#elif HAS_INPUT_SYSTEM
        private static HashSet<Key> _heldInputKeys = new HashSet<Key>();
#endif

        public static void Register(CommandRouter router)
        {
            router.Register("simulate_key", SimulateKey);
            router.Register("simulate_mouse", SimulateMouse);
            router.Register("simulate_axis", SimulateAxis);
            router.Register("get_input_state", GetInputState);
            router.Register("simulate_sequence", SimulateSequence);
            router.Register("start_recording", StartRecording);
            router.Register("stop_recording", StopRecording);
            router.Register("replay_recording", ReplayRecording);
        }

        // Input recording state
        private static bool _isRecording;
        private static float _recordStartTime;
        private static List<Dictionary<string, object>> _recordedEvents = new List<Dictionary<string, object>>();

#if UNITY_EDITOR_WIN
        // =================================================================
        // Win32 API declarations (Windows Editor only)
        // =================================================================
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUTUNION u;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [System.Runtime.InteropServices.FieldOffset(0)] public KEYBDINPUT ki;
            [System.Runtime.InteropServices.FieldOffset(0)] public MOUSEINPUT mi;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const uint INPUT_KEYBOARD = 1;
        private const uint INPUT_MOUSE = 0;
        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        private static ushort MapVKToScan(ushort vk)
        {
            return (ushort)MapVirtualKey(vk, 0); // MAPVK_VK_TO_VSC
        }
#endif

        // =================================================================
        // simulate_key
        // =================================================================
        private static object SimulateKey(Dictionary<string, object> p)
        {
            string keyName = GetStringParam(p, "key");
            string action = GetStringParam(p, "action", "tap");
            float duration = GetFloatParam(p, "duration", 0.1f);

            if (string.IsNullOrEmpty(keyName))
                throw new ArgumentException("key is required (e.g. 'Space', 'W', 'A', 'LeftArrow')");

#if UNITY_EDITOR_WIN
            ushort vk = MapKeyNameToVK(keyName);
            ushort scan = MapVKToScan(vk);

            switch (action.ToLower())
            {
                case "press":
                    SendKeyDown(vk, scan);
                    _heldScanCodes.Add(scan);
                    return Success($"Key '{keyName}' pressed (held down)");

                case "release":
                    SendKeyUp(vk, scan);
                    _heldScanCodes.Remove(scan);
                    return Success($"Key '{keyName}' released");

                case "tap":
                    SendKeyDown(vk, scan);
                    _heldScanCodes.Add(scan);
                    float releaseTime = (float)UnityEditor.EditorApplication.timeSinceStartup + duration;
                    var capturedVk = vk;
                    var capturedScan = scan;
                    UnityEditor.EditorApplication.CallbackFunction releaseCallback = null;
                    releaseCallback = () =>
                    {
                        if (UnityEditor.EditorApplication.timeSinceStartup >= releaseTime)
                        {
                            SendKeyUp(capturedVk, capturedScan);
                            _heldScanCodes.Remove(capturedScan);
                            UnityEditor.EditorApplication.update -= releaseCallback;
                        }
                    };
                    UnityEditor.EditorApplication.update += releaseCallback;
                    return Success($"Key '{keyName}' tapped (duration: {duration}s)");

                default:
                    throw new ArgumentException($"Unknown action '{action}'. Use 'press', 'release', or 'tap'.");
            }
#elif HAS_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null)
                throw new InvalidOperationException("No keyboard device found. Ensure a keyboard is connected and Input System is active.");

            Key key = MapKeyNameToInputSystemKey(keyName);

            switch (action.ToLower())
            {
                case "press":
                    _heldInputKeys.Add(key);
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(_heldInputKeys.ToArray()));
                    return Success($"Key '{keyName}' pressed (held down)");

                case "release":
                    _heldInputKeys.Remove(key);
                    InputSystem.QueueStateEvent(keyboard, _heldInputKeys.Count > 0
                        ? new KeyboardState(_heldInputKeys.ToArray())
                        : new KeyboardState());
                    return Success($"Key '{keyName}' released");

                case "tap":
                    _heldInputKeys.Add(key);
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(_heldInputKeys.ToArray()));
                    float tapReleaseTime = (float)UnityEditor.EditorApplication.timeSinceStartup + duration;
                    var capturedKey = key;
                    UnityEditor.EditorApplication.CallbackFunction tapCallback = null;
                    tapCallback = () =>
                    {
                        if (UnityEditor.EditorApplication.timeSinceStartup >= tapReleaseTime)
                        {
                            _heldInputKeys.Remove(capturedKey);
                            InputSystem.QueueStateEvent(Keyboard.current, _heldInputKeys.Count > 0
                                ? new KeyboardState(_heldInputKeys.ToArray())
                                : new KeyboardState());
                            UnityEditor.EditorApplication.update -= tapCallback;
                        }
                    };
                    UnityEditor.EditorApplication.update += tapCallback;
                    return Success($"Key '{keyName}' tapped (duration: {duration}s)");

                default:
                    throw new ArgumentException($"Unknown action '{action}'. Use 'press', 'release', or 'tap'.");
            }
#else
            throw new PlatformNotSupportedException(
                "simulate_key is not supported on this platform without the New Input System package (com.unity.inputsystem).");
#endif
        }

        // =================================================================
        // simulate_mouse
        // =================================================================
        private static object SimulateMouse(Dictionary<string, object> p)
        {
            string action = GetStringParam(p, "action", "click");
            float x = GetFloatParam(p, "x", 0);
            float y = GetFloatParam(p, "y", 0);
            string button = GetStringParam(p, "button", "left");

#if UNITY_EDITOR_WIN
            switch (action.ToLower())
            {
                case "move":
                    SendMouseMove((int)x, (int)y);
                    return Success($"Mouse moved by ({x}, {y})");

                case "click":
                    GetMouseButtonFlags(button, out uint downFlag, out uint upFlag);
                    SendMouseButton(downFlag);
                    UnityEditor.EditorApplication.delayCall += () => SendMouseButton(upFlag);
                    return Success($"Mouse {button} clicked");

                case "press":
                    GetMouseButtonFlags(button, out uint df, out _);
                    SendMouseButton(df);
                    return Success($"Mouse {button} pressed");

                case "release":
                    GetMouseButtonFlags(button, out _, out uint uf);
                    SendMouseButton(uf);
                    return Success($"Mouse {button} released");

                case "scroll":
                    SendMouseScroll((int)y);
                    return Success($"Mouse scrolled {y}");

                default:
                    throw new ArgumentException($"Unknown action '{action}'.");
            }
#elif HAS_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null)
                throw new InvalidOperationException("No mouse device found.");

            switch (action.ToLower())
            {
                case "move":
                    bool relative = p.ContainsKey("relative") && Convert.ToBoolean(p["relative"]);
                    var targetPos = relative
                        ? mouse.position.ReadValue() + new UnityEngine.Vector2(x, y)
                        : new UnityEngine.Vector2(x, y);
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = targetPos });
                    return Success($"Mouse moved to ({targetPos.x}, {targetPos.y})");

                case "click":
                    var clickMask = GetMouseButtonMaskInputSystem(button);
                    InputSystem.QueueStateEvent(mouse, new MouseState { buttons = clickMask });
                    UnityEditor.EditorApplication.delayCall += () =>
                        InputSystem.QueueStateEvent(Mouse.current, new MouseState { buttons = 0 });
                    return Success($"Mouse {button} clicked");

                case "press":
                    InputSystem.QueueStateEvent(mouse, new MouseState { buttons = GetMouseButtonMaskInputSystem(button) });
                    return Success($"Mouse {button} pressed");

                case "release":
                    InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 0 });
                    return Success($"Mouse {button} released");

                case "scroll":
                    InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new UnityEngine.Vector2(0, y) });
                    return Success($"Mouse scrolled {y}");

                default:
                    throw new ArgumentException($"Unknown action '{action}'.");
            }
#else
            throw new PlatformNotSupportedException(
                "simulate_mouse is not supported on this platform without the New Input System package (com.unity.inputsystem).");
#endif
        }

        // =================================================================
        // simulate_axis
        // =================================================================
        private static object SimulateAxis(Dictionary<string, object> p)
        {
            float horizontal = GetFloatParam(p, "horizontal", 0);
            float vertical = GetFloatParam(p, "vertical", 0);
            float duration = GetFloatParam(p, "duration", 0.1f);

#if UNITY_EDITOR_WIN
            var keysToPress = new List<(ushort vk, ushort scan)>();
            var keysToRelease = new List<(ushort vk, ushort scan)>();

            ushort vkA = 0x41, vkD = 0x44, vkW = 0x57, vkS = 0x53;
            ushort scA = MapVKToScan(vkA), scD = MapVKToScan(vkD);
            ushort scW = MapVKToScan(vkW), scS = MapVKToScan(vkS);

            if (horizontal < -0.1f) { keysToPress.Add((vkA, scA)); keysToRelease.Add((vkD, scD)); }
            else if (horizontal > 0.1f) { keysToPress.Add((vkD, scD)); keysToRelease.Add((vkA, scA)); }
            else { keysToRelease.Add((vkA, scA)); keysToRelease.Add((vkD, scD)); }

            if (vertical < -0.1f) { keysToPress.Add((vkS, scS)); keysToRelease.Add((vkW, scW)); }
            else if (vertical > 0.1f) { keysToPress.Add((vkW, scW)); keysToRelease.Add((vkS, scS)); }
            else { keysToRelease.Add((vkW, scW)); keysToRelease.Add((vkS, scS)); }

            foreach (var (vk, scan) in keysToRelease) { SendKeyUp(vk, scan); _heldScanCodes.Remove(scan); }
            foreach (var (vk, scan) in keysToPress) { SendKeyDown(vk, scan); _heldScanCodes.Add(scan); }

            if (duration > 0)
            {
                float releaseTime = (float)UnityEditor.EditorApplication.timeSinceStartup + duration;
                var pressed = new List<(ushort vk, ushort scan)>(keysToPress);
                UnityEditor.EditorApplication.CallbackFunction cb = null;
                cb = () =>
                {
                    if (UnityEditor.EditorApplication.timeSinceStartup >= releaseTime)
                    {
                        foreach (var (vk, scan) in pressed) { SendKeyUp(vk, scan); _heldScanCodes.Remove(scan); }
                        UnityEditor.EditorApplication.update -= cb;
                    }
                };
                UnityEditor.EditorApplication.update += cb;
            }

            var pressedNames = new List<string>();
            foreach (var (vk, _) in keysToPress) pressedNames.Add(((char)vk).ToString());

            return Success(new Dictionary<string, object>
            {
                { "horizontal", horizontal },
                { "vertical", vertical },
                { "duration", duration },
                { "keysPressed", string.Join(", ", pressedNames) }
            });
#elif HAS_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null)
                throw new InvalidOperationException("No keyboard device found.");

            // Release opposing keys first, then press the requested ones
            var toRelease = new List<Key>();
            var toPress = new List<Key>();

            if (horizontal < -0.1f) { toPress.Add(Key.A); toRelease.Add(Key.D); }
            else if (horizontal > 0.1f) { toPress.Add(Key.D); toRelease.Add(Key.A); }
            else { toRelease.Add(Key.A); toRelease.Add(Key.D); }

            if (vertical < -0.1f) { toPress.Add(Key.S); toRelease.Add(Key.W); }
            else if (vertical > 0.1f) { toPress.Add(Key.W); toRelease.Add(Key.S); }
            else { toRelease.Add(Key.W); toRelease.Add(Key.S); }

            foreach (var k in toRelease) _heldInputKeys.Remove(k);
            foreach (var k in toPress) _heldInputKeys.Add(k);

            InputSystem.QueueStateEvent(keyboard, _heldInputKeys.Count > 0
                ? new KeyboardState(_heldInputKeys.ToArray())
                : new KeyboardState());

            if (duration > 0)
            {
                float releaseTime = (float)UnityEditor.EditorApplication.timeSinceStartup + duration;
                var pressed = new List<Key>(toPress);
                UnityEditor.EditorApplication.CallbackFunction cb = null;
                cb = () =>
                {
                    if (UnityEditor.EditorApplication.timeSinceStartup >= releaseTime)
                    {
                        foreach (var k in pressed) _heldInputKeys.Remove(k);
                        InputSystem.QueueStateEvent(Keyboard.current, _heldInputKeys.Count > 0
                            ? new KeyboardState(_heldInputKeys.ToArray())
                            : new KeyboardState());
                        UnityEditor.EditorApplication.update -= cb;
                    }
                };
                UnityEditor.EditorApplication.update += cb;
            }

            return Success(new Dictionary<string, object>
            {
                { "horizontal", horizontal },
                { "vertical", vertical },
                { "duration", duration },
                { "keysPressed", string.Join(", ", toPress) }
            });
#else
            throw new PlatformNotSupportedException(
                "simulate_axis is not supported on this platform without the New Input System package (com.unity.inputsystem).");
#endif
        }

        // =================================================================
        // get_input_state
        // =================================================================
        private static object GetInputState(Dictionary<string, object> p)
        {
            var result = new Dictionary<string, object>();
            var pressedKeys = new List<string>();

#if UNITY_EDITOR_WIN
            var keyChecks = new (string name, int vk)[] {
                ("W", 0x57), ("A", 0x41), ("S", 0x53), ("D", 0x44),
                ("Space", 0x20), ("Enter", 0x0D), ("Escape", 0x1B),
                ("Up", 0x26), ("Down", 0x28), ("Left", 0x25), ("Right", 0x27),
                ("Shift", 0x10), ("Ctrl", 0x11), ("Alt", 0x12),
                ("R", 0x52), ("E", 0x45), ("Q", 0x51), ("F", 0x46), ("Tab", 0x09)
            };
            foreach (var (name, vk) in keyChecks)
            {
                if ((GetAsyncKeyState(vk) & 0x8000) != 0)
                    pressedKeys.Add(name);
            }
            result["heldBySim"] = _heldScanCodes.Count;
#elif HAS_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                var keyChecks = new (string name, Key key)[] {
                    ("W", Key.W), ("A", Key.A), ("S", Key.S), ("D", Key.D),
                    ("Space", Key.Space), ("Enter", Key.Enter), ("Escape", Key.Escape),
                    ("Up", Key.UpArrow), ("Down", Key.DownArrow), ("Left", Key.LeftArrow), ("Right", Key.RightArrow),
                    ("Shift", Key.LeftShift), ("Ctrl", Key.LeftCtrl), ("Alt", Key.LeftAlt),
                    ("R", Key.R), ("E", Key.E), ("Q", Key.Q), ("F", Key.F), ("Tab", Key.Tab)
                };
                foreach (var (name, key) in keyChecks)
                {
                    if (keyboard[key].isPressed)
                        pressedKeys.Add(name);
                }
            }
            result["heldBySim"] = _heldInputKeys.Count;
#else
            if (UnityEditor.EditorApplication.isPlaying)
            {
                var keyChecks = new (string name, KeyCode kc)[] {
                    ("W", KeyCode.W), ("A", KeyCode.A), ("S", KeyCode.S), ("D", KeyCode.D),
                    ("Space", KeyCode.Space), ("Enter", KeyCode.Return), ("Escape", KeyCode.Escape),
                    ("Up", KeyCode.UpArrow), ("Down", KeyCode.DownArrow),
                    ("Left", KeyCode.LeftArrow), ("Right", KeyCode.RightArrow),
                    ("Shift", KeyCode.LeftShift), ("R", KeyCode.R), ("E", KeyCode.E),
                    ("Q", KeyCode.Q), ("F", KeyCode.F), ("Tab", KeyCode.Tab)
                };
                foreach (var (name, kc) in keyChecks)
                {
                    try { if (Input.GetKey(kc)) pressedKeys.Add(name); } catch { }
                }
            }
            result["heldBySim"] = 0;
#endif
            result["pressedKeys"] = pressedKeys;

            if (UnityEditor.EditorApplication.isPlaying)
            {
                try
                {
                    result["oldInput_horizontal"] = Input.GetAxisRaw("Horizontal");
                    result["oldInput_vertical"] = Input.GetAxisRaw("Vertical");
                    result["oldInput_space"] = Input.GetKey(KeyCode.Space);
                }
                catch { }
            }

            return result;
        }

#if UNITY_EDITOR_WIN
        // =================================================================
        // Win32 SendInput helpers
        // =================================================================
        private static void SendKeyDown(ushort vk, ushort scan)
        {
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUTUNION
                {
                    ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = KEYEVENTF_KEYDOWN, time = 0, dwExtraInfo = IntPtr.Zero }
                }
            };
            SendInput(1, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
        }

        private static void SendKeyUp(ushort vk, ushort scan)
        {
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUTUNION
                {
                    ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero }
                }
            };
            SendInput(1, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
        }

        private static void SendMouseMove(int dx, int dy)
        {
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                u = new INPUTUNION
                {
                    mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = MOUSEEVENTF_MOVE, mouseData = 0, time = 0, dwExtraInfo = IntPtr.Zero }
                }
            };
            SendInput(1, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
        }

        private static void SendMouseButton(uint flags)
        {
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                u = new INPUTUNION
                {
                    mi = new MOUSEINPUT { dx = 0, dy = 0, dwFlags = flags, mouseData = 0, time = 0, dwExtraInfo = IntPtr.Zero }
                }
            };
            SendInput(1, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
        }

        private static void SendMouseScroll(int amount)
        {
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                u = new INPUTUNION
                {
                    mi = new MOUSEINPUT { dx = 0, dy = 0, dwFlags = MOUSEEVENTF_WHEEL, mouseData = (uint)amount, time = 0, dwExtraInfo = IntPtr.Zero }
                }
            };
            SendInput(1, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
        }

        private static void GetMouseButtonFlags(string button, out uint downFlag, out uint upFlag)
        {
            switch (button.ToLower())
            {
                case "left":   downFlag = MOUSEEVENTF_LEFTDOWN;   upFlag = MOUSEEVENTF_LEFTUP;   break;
                case "right":  downFlag = MOUSEEVENTF_RIGHTDOWN;  upFlag = MOUSEEVENTF_RIGHTUP;  break;
                case "middle": downFlag = MOUSEEVENTF_MIDDLEDOWN; upFlag = MOUSEEVENTF_MIDDLEUP; break;
                default: throw new ArgumentException($"Unknown mouse button '{button}'");
            }
        }
#endif

#if HAS_INPUT_SYSTEM && !UNITY_EDITOR_WIN
        private static byte GetMouseButtonMaskInputSystem(string button)
        {
            switch (button.ToLower())
            {
                case "left":   return (byte)MouseButton.Left;
                case "right":  return (byte)MouseButton.Right;
                case "middle": return (byte)MouseButton.Middle;
                default: throw new ArgumentException($"Unknown mouse button '{button}'");
            }
        }
#endif

        // =================================================================
        // simulate_sequence
        // =================================================================
        private static object SimulateSequence(Dictionary<string, object> p)
        {
            var actions = p.ContainsKey("actions") ? p["actions"] as List<object> : null;
            if (actions == null || actions.Count == 0)
                throw new ArgumentException("actions is required");

            int executed = 0;
            float totalDelay = 0;

            foreach (var actionObj in actions)
            {
                var action = actionObj as Dictionary<string, object>;
                if (action == null) continue;

                string type = action.ContainsKey("type") ? action["type"].ToString() : "wait";
                float duration = action.ContainsKey("duration") ? Convert.ToSingle(action["duration"]) : 0.1f;

                switch (type.ToLower())
                {
                    case "key":   SimulateKey(action);   break;
                    case "mouse": SimulateMouse(action); break;
                    case "axis":  SimulateAxis(action);  break;
                    case "wait":
                        System.Threading.Thread.Sleep((int)(duration * 1000));
                        totalDelay += duration;
                        break;
                }
                executed++;
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "executed", executed },
                { "totalActions", actions.Count },
                { "totalDelay", totalDelay }
            };
        }

        // =================================================================
        // Input Recording
        // =================================================================
        private static object StartRecording(Dictionary<string, object> p)
        {
            if (_isRecording)
                return Success("Recording already in progress");

            _recordedEvents.Clear();
            _recordStartTime = (float)UnityEditor.EditorApplication.timeSinceStartup;
            _isRecording = true;
            UnityEditor.EditorApplication.update += RecordingPollCallback;

            return new Dictionary<string, object>
            {
                { "success", true },
                { "message", "Input recording started" },
                { "startTime", _recordStartTime }
            };
        }

        private static void RecordingPollCallback()
        {
            if (!_isRecording)
            {
                UnityEditor.EditorApplication.update -= RecordingPollCallback;
                return;
            }

            float elapsed = (float)UnityEditor.EditorApplication.timeSinceStartup - _recordStartTime;

#if UNITY_EDITOR_WIN
            var keyChecks = new (string name, int vk)[] {
                ("W", 0x57), ("A", 0x41), ("S", 0x53), ("D", 0x44),
                ("Space", 0x20), ("Enter", 0x0D), ("Escape", 0x1B),
                ("R", 0x52), ("E", 0x45), ("Q", 0x51), ("F", 0x46)
            };
            foreach (var (name, vk) in keyChecks)
            {
                if ((GetAsyncKeyState(vk) & 0x8000) != 0)
                    RecordKeyEvent(name, elapsed);
            }
#elif HAS_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                var keyChecks = new (string name, Key key)[] {
                    ("W", Key.W), ("A", Key.A), ("S", Key.S), ("D", Key.D),
                    ("Space", Key.Space), ("Enter", Key.Enter), ("Escape", Key.Escape),
                    ("R", Key.R), ("E", Key.E), ("Q", Key.Q), ("F", Key.F)
                };
                foreach (var (name, key) in keyChecks)
                {
                    if (keyboard[key].isPressed)
                        RecordKeyEvent(name, elapsed);
                }
            }
#else
            if (UnityEditor.EditorApplication.isPlaying)
            {
                var keyChecks = new (string name, KeyCode kc)[] {
                    ("W", KeyCode.W), ("A", KeyCode.A), ("S", KeyCode.S), ("D", KeyCode.D),
                    ("Space", KeyCode.Space), ("Enter", KeyCode.Return), ("Escape", KeyCode.Escape),
                    ("R", KeyCode.R), ("E", KeyCode.E), ("Q", KeyCode.Q), ("F", KeyCode.F)
                };
                foreach (var (name, kc) in keyChecks)
                {
                    try { if (Input.GetKey(kc)) RecordKeyEvent(name, elapsed); } catch { }
                }
            }
#endif
        }

        private static void RecordKeyEvent(string name, float elapsed)
        {
            _recordedEvents.Add(new Dictionary<string, object>
            {
                { "type", "key" },
                { "timestamp", Math.Round(elapsed, 3) },
                { "data", new Dictionary<string, object> { { "key", name }, { "action", "press" } } }
            });
        }

        private static object StopRecording(Dictionary<string, object> p)
        {
            if (!_isRecording)
                return new Dictionary<string, object> { { "success", false }, { "message", "No recording in progress" } };

            _isRecording = false;
            UnityEditor.EditorApplication.update -= RecordingPollCallback;
            float duration = (float)UnityEditor.EditorApplication.timeSinceStartup - _recordStartTime;

            return new Dictionary<string, object>
            {
                { "success", true },
                { "duration", Math.Round(duration, 3) },
                { "eventCount", _recordedEvents.Count },
                { "recording", _recordedEvents.ToArray() }
            };
        }

        private static object ReplayRecording(Dictionary<string, object> p)
        {
            var recording = p.ContainsKey("recording") ? p["recording"] as List<object> : null;
            float speed = 1f;
            if (p.ContainsKey("speed")) speed = Convert.ToSingle(p["speed"]);

            if (recording == null || recording.Count == 0)
                throw new ArgumentException("recording is required");

            int replayed = 0;
            float startTime = (float)UnityEditor.EditorApplication.timeSinceStartup;

            foreach (var eventObj in recording)
            {
                var evt = eventObj as Dictionary<string, object>;
                if (evt == null) continue;

                float timestamp = evt.ContainsKey("timestamp") ? Convert.ToSingle(evt["timestamp"]) : 0f;
                float targetTime = startTime + timestamp / speed;
                while ((float)UnityEditor.EditorApplication.timeSinceStartup < targetTime)
                    System.Threading.Thread.Sleep(10);

                string type = evt.ContainsKey("type") ? evt["type"].ToString() : "";
                var data = evt.ContainsKey("data") ? evt["data"] as Dictionary<string, object> : new Dictionary<string, object>();

                switch (type)
                {
                    case "key":   if (data != null) SimulateKey(data);   break;
                    case "mouse": if (data != null) SimulateMouse(data); break;
                }
                replayed++;
            }

            return new Dictionary<string, object>
            {
                { "success", true },
                { "replayed", replayed },
                { "totalEvents", recording.Count },
                { "speed", speed }
            };
        }

        // =================================================================
        // Key name → Virtual Key code (Windows)
        // =================================================================
        private static ushort MapKeyNameToVK(string name)
        {
            switch (name.ToLower())
            {
                case "space": return 0x20;
                case "enter": case "return": return 0x0D;
                case "escape": case "esc": return 0x1B;
                case "tab": return 0x09;
                case "backspace": return 0x08;
                case "delete": return 0x2E;
                case "leftshift": case "lshift": case "shift": return 0x10;
                case "rightshift": case "rshift": return 0xA1;
                case "leftctrl": case "lctrl": case "ctrl": return 0x11;
                case "leftalt": case "lalt": case "alt": return 0x12;
                case "uparrow": case "up": return 0x26;
                case "downarrow": case "down": return 0x28;
                case "leftarrow": case "left": return 0x25;
                case "rightarrow": case "right": return 0x27;
                case "w": return 0x57;
                case "a": return 0x41;
                case "s": return 0x53;
                case "d": return 0x44;
                case "r": return 0x52;
                case "e": return 0x45;
                case "q": return 0x51;
                case "f": return 0x46;
                case "1": return 0x31;
                case "2": return 0x32;
                case "3": return 0x33;
                case "4": return 0x34;
                case "5": return 0x35;
                default:
                    if (name.Length == 1 && char.IsLetterOrDigit(name[0]))
                        return (ushort)char.ToUpper(name[0]);
                    throw new ArgumentException($"Unknown key '{name}'. Examples: Space, W, A, S, D, Enter, Escape, LeftArrow");
            }
        }

#if HAS_INPUT_SYSTEM && !UNITY_EDITOR_WIN
        // =================================================================
        // Key name → Input System Key (Linux / macOS)
        // =================================================================
        private static Key MapKeyNameToInputSystemKey(string name)
        {
            switch (name.ToLower())
            {
                case "space": return Key.Space;
                case "enter": case "return": return Key.Enter;
                case "escape": case "esc": return Key.Escape;
                case "tab": return Key.Tab;
                case "backspace": return Key.Backspace;
                case "delete": return Key.Delete;
                case "leftshift": case "lshift": case "shift": return Key.LeftShift;
                case "rightshift": case "rshift": return Key.RightShift;
                case "leftctrl": case "lctrl": case "ctrl": return Key.LeftCtrl;
                case "rightctrl": case "rctrl": return Key.RightCtrl;
                case "leftalt": case "lalt": case "alt": return Key.LeftAlt;
                case "rightalt": case "ralt": return Key.RightAlt;
                case "uparrow": case "up": return Key.UpArrow;
                case "downarrow": case "down": return Key.DownArrow;
                case "leftarrow": case "left": return Key.LeftArrow;
                case "rightarrow": case "right": return Key.RightArrow;
                case "w": return Key.W;
                case "a": return Key.A;
                case "s": return Key.S;
                case "d": return Key.D;
                case "r": return Key.R;
                case "e": return Key.E;
                case "q": return Key.Q;
                case "f": return Key.F;
                case "1": return Key.Digit1;
                case "2": return Key.Digit2;
                case "3": return Key.Digit3;
                case "4": return Key.Digit4;
                case "5": return Key.Digit5;
                default:
                    if (name.Length == 1)
                    {
                        char c = char.ToUpper(name[0]);
                        if (System.Enum.TryParse<Key>(c.ToString(), out Key k)) return k;
                        if (c >= '0' && c <= '9' && System.Enum.TryParse<Key>("Digit" + c, out Key dk)) return dk;
                    }
                    throw new ArgumentException($"Unknown key '{name}'. Examples: Space, W, A, S, D, Enter, Escape, LeftArrow");
            }
        }
#endif
    }
}
