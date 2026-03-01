using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace UnityMcpPro
{
    public class InputCommands : BaseCommand
    {
        // Track which keys are currently "held" by simulation
        private static HashSet<ushort> _heldScanCodes = new HashSet<ushort>();

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

        // =================================================================
        // Win32 API declarations
        // =================================================================
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUTUNION u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
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

        // =================================================================
        // simulate_key — キーボードキーのプレス/リリース/タップ (Win32 SendInput)
        // =================================================================
        private static object SimulateKey(Dictionary<string, object> p)
        {
            string keyName = GetStringParam(p, "key");
            string action = GetStringParam(p, "action", "tap");
            float duration = GetFloatParam(p, "duration", 0.1f);

            if (string.IsNullOrEmpty(keyName))
                throw new ArgumentException("key is required (e.g. 'Space', 'W', 'A', 'LeftArrow')");

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
        }

        // =================================================================
        // simulate_mouse — マウス移動/クリック (Win32 SendInput)
        // =================================================================
        private static object SimulateMouse(Dictionary<string, object> p)
        {
            string action = GetStringParam(p, "action", "click");
            float x = GetFloatParam(p, "x", 0);
            float y = GetFloatParam(p, "y", 0);
            string button = GetStringParam(p, "button", "left");

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
        }

        // =================================================================
        // simulate_axis — 軸入力シミュレーション (WASD via Win32)
        // =================================================================
        private static object SimulateAxis(Dictionary<string, object> p)
        {
            float horizontal = GetFloatParam(p, "horizontal", 0);
            float vertical = GetFloatParam(p, "vertical", 0);
            float duration = GetFloatParam(p, "duration", 0.1f);

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
        }

        // =================================================================
        // get_input_state — 現在の入力状態を取得
        // =================================================================
        private static object GetInputState(Dictionary<string, object> p)
        {
            var result = new Dictionary<string, object>();

            // Check common keys via Win32 GetAsyncKeyState
            var pressedKeys = new List<string>();
            var keyChecks = new (string name, int vk)[] {
                ("W", 0x57), ("A", 0x41), ("S", 0x53), ("D", 0x44),
                ("Space", 0x20), ("Enter", 0x0D), ("Escape", 0x1B),
                ("Up", 0x26), ("Down", 0x28), ("Left", 0x25), ("Right", 0x27),
                ("Shift", 0x10), ("Ctrl", 0x11), ("Alt", 0x12),
                ("R", 0x52), ("E", 0x45), ("Q", 0x51), ("F", 0x46),
                ("Tab", 0x09)
            };
            foreach (var (name, vk) in keyChecks)
            {
                if ((GetAsyncKeyState(vk) & 0x8000) != 0)
                    pressedKeys.Add(name);
            }
            result["pressedKeys"] = pressedKeys;
            result["heldBySim"] = _heldScanCodes.Count;

            // Old Input Manager values (in play mode)
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
                    ki = new KEYBDINPUT
                    {
                        wVk = vk,
                        wScan = scan,
                        dwFlags = KEYEVENTF_KEYDOWN,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        private static void SendKeyUp(ushort vk, ushort scan)
        {
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUTUNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vk,
                        wScan = scan,
                        dwFlags = KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        private static void SendMouseMove(int dx, int dy)
        {
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                u = new INPUTUNION
                {
                    mi = new MOUSEINPUT
                    {
                        dx = dx, dy = dy,
                        dwFlags = MOUSEEVENTF_MOVE,
                        mouseData = 0, time = 0, dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        private static void SendMouseButton(uint flags)
        {
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                u = new INPUTUNION
                {
                    mi = new MOUSEINPUT
                    {
                        dx = 0, dy = 0,
                        dwFlags = flags,
                        mouseData = 0, time = 0, dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        private static void SendMouseScroll(int amount)
        {
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                u = new INPUTUNION
                {
                    mi = new MOUSEINPUT
                    {
                        dx = 0, dy = 0,
                        dwFlags = MOUSEEVENTF_WHEEL,
                        mouseData = (uint)amount, time = 0, dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        private static void GetMouseButtonFlags(string button, out uint downFlag, out uint upFlag)
        {
            switch (button.ToLower())
            {
                case "left": downFlag = MOUSEEVENTF_LEFTDOWN; upFlag = MOUSEEVENTF_LEFTUP; break;
                case "right": downFlag = MOUSEEVENTF_RIGHTDOWN; upFlag = MOUSEEVENTF_RIGHTUP; break;
                case "middle": downFlag = MOUSEEVENTF_MIDDLEDOWN; upFlag = MOUSEEVENTF_MIDDLEUP; break;
                default: throw new ArgumentException($"Unknown mouse button '{button}'");
            }
        }

        // =================================================================
        // Virtual Key code mapping
        // =================================================================
        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        private static ushort MapVKToScan(ushort vk)
        {
            return (ushort)MapVirtualKey(vk, 0); // MAPVK_VK_TO_VSC
        }

        // =================================================================
        // simulate_sequence — 入力シーケンスの一括実行
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
                    case "key":
                        SimulateKey(action);
                        break;
                    case "mouse":
                        SimulateMouse(action);
                        break;
                    case "axis":
                        SimulateAxis(action);
                        break;
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

            // Hook into update to poll key states
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

            // Poll common keys
            var keyChecks = new (string name, int vk)[] {
                ("W", 0x57), ("A", 0x41), ("S", 0x53), ("D", 0x44),
                ("Space", 0x20), ("Enter", 0x0D), ("Escape", 0x1B),
                ("R", 0x52), ("E", 0x45), ("Q", 0x51), ("F", 0x46)
            };

            foreach (var (name, vk) in keyChecks)
            {
                if ((GetAsyncKeyState(vk) & 0x8000) != 0)
                {
                    _recordedEvents.Add(new Dictionary<string, object>
                    {
                        { "type", "key" },
                        { "timestamp", Math.Round(elapsed, 3) },
                        { "data", new Dictionary<string, object> { { "key", name }, { "action", "press" } } }
                    });
                }
            }
        }

        private static object StopRecording(Dictionary<string, object> p)
        {
            if (!_isRecording)
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", "No recording in progress" }
                };

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
                float adjustedDelay = timestamp / speed;

                // Wait until the right time
                float targetTime = startTime + adjustedDelay;
                while ((float)UnityEditor.EditorApplication.timeSinceStartup < targetTime)
                    System.Threading.Thread.Sleep(10);

                string type = evt.ContainsKey("type") ? evt["type"].ToString() : "";
                var data = evt.ContainsKey("data") ? evt["data"] as Dictionary<string, object> : new Dictionary<string, object>();

                switch (type)
                {
                    case "key":
                        if (data != null) SimulateKey(data);
                        break;
                    case "mouse":
                        if (data != null) SimulateMouse(data);
                        break;
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
                    // Try single character
                    if (name.Length == 1 && char.IsLetterOrDigit(name[0]))
                        return (ushort)char.ToUpper(name[0]);
                    throw new ArgumentException($"Unknown key '{name}'. Examples: Space, W, A, S, D, Enter, Escape, LeftArrow");
            }
        }
    }
}
