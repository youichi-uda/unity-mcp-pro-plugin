using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace UnityMcpPro
{
    /// <summary>
    /// WebSocket client that connects to multiple MCP server instances simultaneously.
    /// Each Claude session spawns its own MCP server on a different port (6605-6609).
    /// This client connects to ALL available servers so multiple sessions can control Unity.
    /// </summary>
    public class WebSocketServer
    {
        private const int BASE_PORT = 6605;
        private const int MAX_PORT = 6609;
        private const float RECONNECT_INTERVAL = 3f;
        private const int BUFFER_SIZE = 65536;
        private const int PROJECT_MISMATCH_CLOSE_CODE = 4001;
        private const double REJECTED_PORT_BACKOFF_SECONDS = 30.0;

        private readonly CommandRouter _router;
        private readonly ConcurrentDictionary<int, Connection> _connections = new ConcurrentDictionary<int, Connection>();
        private readonly ConcurrentDictionary<int, double> _rejectedUntil = new ConcurrentDictionary<int, double>();
        private readonly ConcurrentQueue<PendingAction> _mainThreadActions = new ConcurrentQueue<PendingAction>();
        // Thread-safe monotonic clock for rejection backoff (EditorApplication.timeSinceStartup is main-thread-only).
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private string _projectPath;
        private double _lastReconnectAttempt;
        private volatile bool _running;
        private volatile bool _connecting;

        public bool IsConnected => _connections.Values.Any(c => c.Connected);
        public int ConnectionCount => _connections.Values.Count(c => c.Connected);
        public IEnumerable<int> ConnectedPorts => _connections.Where(kv => kv.Value.Connected).Select(kv => kv.Key);

        // Legacy compat — returns first connected port or 0
        public int Port => _connections.Where(kv => kv.Value.Connected).Select(kv => kv.Key).FirstOrDefault();

        public WebSocketServer(CommandRouter router)
        {
            _router = router;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            // Application.dataPath only callable from main thread — cache for use in connect threads.
            // Project root is the directory containing Assets/ and ProjectSettings/.
            _projectPath = Path.GetDirectoryName(Application.dataPath);
            _lastReconnectAttempt = EditorApplication.timeSinceStartup;
            EditorApplication.update += Update;
            TryConnect();
        }

        public void Stop()
        {
            _running = false;
            _connecting = false;
            EditorApplication.update -= Update;
            DisconnectAll();
        }

        private void Update()
        {
            // Process main-thread actions (incoming messages + log calls)
            while (_mainThreadActions.TryDequeue(out var action))
            {
                action.Execute();
            }

            // Auto-reconnect: periodically scan for new MCP servers
            if (_running && !_connecting)
            {
                double now = EditorApplication.timeSinceStartup;
                if (now - _lastReconnectAttempt >= RECONNECT_INTERVAL)
                {
                    _lastReconnectAttempt = now;
                    TryConnect();
                }
            }
        }

        private void TryConnect()
        {
            if (_connecting) return;
            _connecting = true;

            var thread = new Thread(() =>
            {
                try
                {
                    double now = _clock.Elapsed.TotalSeconds;
                    for (int port = BASE_PORT; port <= MAX_PORT; port++)
                    {
                        if (!_running) break;

                        // Skip ports we're already connected to
                        if (_connections.TryGetValue(port, out var existing) && existing.Connected)
                            continue;

                        // Skip ports that recently rejected us (project mismatch)
                        if (_rejectedUntil.TryGetValue(port, out var rejectedUntil) && now < rejectedUntil)
                            continue;

                        try
                        {
                            var tcp = new TcpClient();
                            tcp.Connect("127.0.0.1", port);

                            if (PerformWebSocketHandshake(tcp, port))
                            {
                                // Remove old dead connection for this port if any
                                if (_connections.TryRemove(port, out var old))
                                {
                                    old.Connected = false;
                                    try { old.Stream?.Close(); } catch { }
                                    try { old.Tcp?.Close(); } catch { }
                                }

                                var conn = new Connection
                                {
                                    Port = port,
                                    Tcp = tcp,
                                    Stream = tcp.GetStream(),
                                    Connected = true
                                };

                                conn.ReceiveThread = new Thread(() => ReceiveLoop(conn))
                                {
                                    IsBackground = true,
                                    Name = $"MCP-WS-Recv-{port}"
                                };
                                conn.ReceiveThread.Start();

                                _connections[port] = conn;

                                // Send hello handshake so the server can confirm we are the right project.
                                // Server may close us with code 4001 if the project does not match.
                                SendHello(conn);

                                int p = port; // capture for closure
                                _mainThreadActions.Enqueue(new PendingAction(() =>
                                    Debug.Log($"[MCP] Connected to MCP server on port {p} (active connections: {ConnectionCount})")));
                            }
                            else
                            {
                                tcp.Close();
                            }
                        }
                        catch (Exception)
                        {
                            // Port not available, skip
                        }
                    }
                }
                finally
                {
                    _connecting = false;
                }
            })
            {
                IsBackground = true,
                Name = "MCP-WebSocket-Connect"
            };
            thread.Start();
        }

        private bool PerformWebSocketHandshake(TcpClient tcp, int port)
        {
            var stream = tcp.GetStream();
            tcp.ReceiveTimeout = 5000;

            // Generate WebSocket key
            var keyBytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(keyBytes);
            string wsKey = Convert.ToBase64String(keyBytes);

            // Send HTTP upgrade request
            string request =
                $"GET / HTTP/1.1\r\n" +
                $"Host: 127.0.0.1:{port}\r\n" +
                $"Upgrade: websocket\r\n" +
                $"Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Key: {wsKey}\r\n" +
                $"Sec-WebSocket-Version: 13\r\n" +
                $"\r\n";

            var requestBytes = Encoding.ASCII.GetBytes(request);
            stream.Write(requestBytes, 0, requestBytes.Length);
            stream.Flush();

            // Read HTTP response
            var responseBuilder = new StringBuilder();
            var buffer = new byte[1];
            int consecutiveNewlines = 0;

            while (consecutiveNewlines < 4)
            {
                int read = stream.Read(buffer, 0, 1);
                if (read == 0) return false;

                char c = (char)buffer[0];
                responseBuilder.Append(c);

                if (c == '\r' || c == '\n')
                    consecutiveNewlines++;
                else
                    consecutiveNewlines = 0;
            }

            string response = responseBuilder.ToString();

            // Verify 101 Switching Protocols
            if (!response.Contains("101"))
                return false;

            // Verify Sec-WebSocket-Accept
            string expectedAccept;
            using (var sha1 = SHA1.Create())
            {
                string combined = wsKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
                expectedAccept = Convert.ToBase64String(sha1.ComputeHash(Encoding.ASCII.GetBytes(combined)));
            }

            if (!response.Contains(expectedAccept))
                return false;

            tcp.ReceiveTimeout = 0; // Reset to blocking
            return true;
        }

        private void DisconnectAll()
        {
            foreach (var kv in _connections)
            {
                var conn = kv.Value;
                conn.Connected = false;
                try { conn.Stream?.Close(); } catch { }
                try { conn.Tcp?.Close(); } catch { }
            }
            _connections.Clear();
        }

        private void Disconnect(Connection conn)
        {
            conn.Connected = false;
            try { conn.Stream?.Close(); } catch { }
            try { conn.Tcp?.Close(); } catch { }
            _connections.TryRemove(conn.Port, out _);
        }

        private void ReceiveLoop(Connection conn)
        {
            var messageBuffer = new MemoryStream();

            try
            {
                while (_running && conn.Connected && conn.Tcp?.Connected == true)
                {
                    // Read WebSocket frame header
                    int b0 = ReadByte(conn);
                    int b1 = ReadByte(conn);
                    if (b0 < 0 || b1 < 0) break;

                    bool fin = (b0 & 0x80) != 0;
                    int opcode = b0 & 0x0F;
                    bool masked = (b1 & 0x80) != 0;
                    long payloadLen = b1 & 0x7F;

                    if (payloadLen == 126)
                    {
                        int h = ReadByte(conn);
                        int l = ReadByte(conn);
                        if (h < 0 || l < 0) break;
                        payloadLen = (h << 8) | l;
                    }
                    else if (payloadLen == 127)
                    {
                        var lenBytes = ReadBytes(conn, 8);
                        if (lenBytes == null) break;
                        payloadLen = 0;
                        for (int i = 0; i < 8; i++)
                            payloadLen = (payloadLen << 8) | lenBytes[i];
                    }

                    byte[] maskKey = null;
                    if (masked)
                    {
                        maskKey = ReadBytes(conn, 4);
                        if (maskKey == null) break;
                    }

                    // Read payload
                    byte[] payload = null;
                    if (payloadLen > 0)
                    {
                        payload = ReadBytes(conn, (int)payloadLen);
                        if (payload == null) break;

                        if (masked && maskKey != null)
                        {
                            for (int i = 0; i < payload.Length; i++)
                                payload[i] ^= maskKey[i % 4];
                        }
                    }

                    // Handle frame by opcode
                    switch (opcode)
                    {
                        case 0x1: // Text
                        case 0x0: // Continuation
                            if (payload != null)
                                messageBuffer.Write(payload, 0, payload.Length);
                            if (fin)
                            {
                                string text = Encoding.UTF8.GetString(messageBuffer.ToArray());
                                messageBuffer.SetLength(0);
                                _mainThreadActions.Enqueue(new PendingAction(() => ProcessMessage(conn, text)));
                            }
                            break;

                        case 0x8: // Close
                            // RFC 6455: payload optionally starts with 2-byte big-endian status code
                            int closeCode = 1005;
                            string closeReason = null;
                            if (payload != null && payload.Length >= 2)
                            {
                                closeCode = (payload[0] << 8) | payload[1];
                                if (payload.Length > 2)
                                    closeReason = Encoding.UTF8.GetString(payload, 2, payload.Length - 2);
                            }
                            if (closeCode == PROJECT_MISMATCH_CLOSE_CODE)
                            {
                                _rejectedUntil[conn.Port] = _clock.Elapsed.TotalSeconds + REJECTED_PORT_BACKOFF_SECONDS;
                                int p = conn.Port;
                                string reason = closeReason ?? "(no reason)";
                                _mainThreadActions.Enqueue(new PendingAction(() =>
                                    Debug.Log($"[MCP] Port {p} not for this project — backing off ({reason})")));
                            }
                            SendCloseFrame(conn);
                            conn.Connected = false;
                            return;

                        case 0x9: // Ping
                            SendPongFrame(conn, payload);
                            break;

                        case 0xA: // Pong
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (_running)
                {
                    string msg = ex.Message;
                    int port = conn.Port;
                    _mainThreadActions.Enqueue(new PendingAction(() =>
                        Debug.LogWarning($"[MCP] WebSocket error on port {port}: {msg}")));
                }
            }
            finally
            {
                bool wasConnected = conn.Connected;
                conn.Connected = false;
                _connections.TryRemove(conn.Port, out _);
                if (wasConnected && _running)
                {
                    int port = conn.Port;
                    int remaining = ConnectionCount;
                    _mainThreadActions.Enqueue(new PendingAction(() =>
                        Debug.Log($"[MCP] Disconnected from port {port} (active connections: {remaining})")));
                }
            }
        }

        private int ReadByte(Connection conn)
        {
            try
            {
                return conn.Stream.ReadByte();
            }
            catch
            {
                return -1;
            }
        }

        private byte[] ReadBytes(Connection conn, int count)
        {
            var buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = conn.Stream.Read(buffer, offset, count - offset);
                if (read <= 0) return null;
                offset += read;
            }
            return buffer;
        }

        private void SendWebSocketFrame(Connection conn, byte opcode, byte[] payload)
        {
            if (!conn.Connected || conn.Stream == null) return;

            lock (conn.SendLock)
            {
                try
                {
                    var frame = new MemoryStream();
                    frame.WriteByte((byte)(0x80 | opcode)); // FIN + opcode

                    // Client must mask frames
                    int len = payload?.Length ?? 0;
                    if (len < 126)
                        frame.WriteByte((byte)(0x80 | len));
                    else if (len < 65536)
                    {
                        frame.WriteByte(0x80 | 126);
                        frame.WriteByte((byte)(len >> 8));
                        frame.WriteByte((byte)(len & 0xFF));
                    }
                    else
                    {
                        frame.WriteByte(0x80 | 127);
                        long len64 = len;
                        for (int i = 7; i >= 0; i--)
                            frame.WriteByte((byte)((len64 >> (i * 8)) & 0xFF));
                    }

                    // Generate mask key
                    var maskKey = new byte[4];
                    using (var rng = RandomNumberGenerator.Create())
                        rng.GetBytes(maskKey);
                    frame.Write(maskKey, 0, 4);

                    // Masked payload
                    if (payload != null && payload.Length > 0)
                    {
                        var maskedPayload = new byte[payload.Length];
                        for (int i = 0; i < payload.Length; i++)
                            maskedPayload[i] = (byte)(payload[i] ^ maskKey[i % 4]);
                        frame.Write(maskedPayload, 0, maskedPayload.Length);
                    }

                    var frameBytes = frame.ToArray();
                    conn.Stream.Write(frameBytes, 0, frameBytes.Length);
                    conn.Stream.Flush();
                }
                catch (Exception)
                {
                    conn.Connected = false;
                }
            }
        }

        private void SendCloseFrame(Connection conn)
        {
            SendWebSocketFrame(conn, 0x8, new byte[] { 0x03, 0xE8 }); // 1000 normal closure
        }

        private void SendPongFrame(Connection conn, byte[] payload)
        {
            SendWebSocketFrame(conn, 0xA, payload);
        }

        private void SendTextFrame(Connection conn, string text)
        {
            SendWebSocketFrame(conn, 0x1, Encoding.UTF8.GetBytes(text));
        }

        private void SendHello(Connection conn)
        {
            // JSON-encode the path manually so backslashes (Windows) round-trip correctly.
            string escapedPath = (_projectPath ?? "")
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
            string hello = $"{{\"jsonrpc\":\"2.0\",\"method\":\"hello\",\"params\":{{\"projectPath\":\"{escapedPath}\"}}}}";
            SendTextFrame(conn, hello);
        }

        private void ProcessMessage(Connection conn, string message)
        {
            try
            {
                var request = JsonHelper.Deserialize<JsonRpcRequest>(message);

                if (request.method == "ping")
                {
                    SendTextFrame(conn, "{\"jsonrpc\":\"2.0\",\"method\":\"pong\",\"params\":{}}");
                    return;
                }

                _router.Dispatch(request, (response) =>
                {
                    SendTextFrame(conn, response);
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP] Error processing message on port {conn.Port}: {ex.Message}");
            }
        }

        /// <summary>Per-connection state</summary>
        private class Connection
        {
            public int Port;
            public TcpClient Tcp;
            public NetworkStream Stream;
            public Thread ReceiveThread;
            public volatile bool Connected;
            public readonly object SendLock = new object();
        }

        /// <summary>Action to execute on the main thread</summary>
        private class PendingAction
        {
            private readonly Action _action;
            public PendingAction(Action action) => _action = action;
            public void Execute() => _action?.Invoke();
        }
    }
}
