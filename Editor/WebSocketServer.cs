using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace UnityMcpPro
{
    /// <summary>
    /// WebSocket client using raw TcpClient for Unity Editor compatibility.
    /// System.Net.WebSockets.ClientWebSocket is unreliable in the Unity Editor.
    /// </summary>
    public class WebSocketServer
    {
        private const int BASE_PORT = 6605;
        private const int MAX_PORT = 6609;
        private const float RECONNECT_INTERVAL = 3f;
        private const int BUFFER_SIZE = 65536;

        private TcpClient _tcp;
        private NetworkStream _stream;
        private Thread _receiveThread;
        private readonly CommandRouter _router;
        private readonly ConcurrentQueue<string> _incomingMessages = new ConcurrentQueue<string>();
        private double _lastReconnectAttempt;
        private volatile bool _running;
        private volatile bool _connected;
        private volatile bool _connecting;
        private int _port = BASE_PORT;
        private readonly object _sendLock = new object();

        public bool IsConnected => _connected;
        public int Port => _port;

        public WebSocketServer(CommandRouter router)
        {
            _router = router;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _lastReconnectAttempt = EditorApplication.timeSinceStartup;
            EditorApplication.update += Update;
            TryConnect();
        }

        public void Stop()
        {
            _running = false;
            _connecting = false;
            EditorApplication.update -= Update;
            Disconnect();
        }

        private void Update()
        {
            // Process incoming messages on main thread
            while (_incomingMessages.TryDequeue(out string message))
            {
                ProcessMessage(message);
            }

            // Auto-reconnect
            if (_running && !_connected && !_connecting)
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
                    for (int port = BASE_PORT; port <= MAX_PORT; port++)
                    {
                        if (!_running) break;
                        try
                        {
                            var tcp = new TcpClient();
                            tcp.Connect("127.0.0.1", port);

                            if (PerformWebSocketHandshake(tcp, port))
                            {
                                Disconnect(); // Clean up old connection
                                _tcp = tcp;
                                _stream = tcp.GetStream();
                                _port = port;
                                _connected = true;

                                // Start receive thread
                                _receiveThread = new Thread(ReceiveLoop)
                                {
                                    IsBackground = true,
                                    Name = "MCP-WebSocket-Receive"
                                };
                                _receiveThread.Start();

                                EditorApplication.delayCall += () =>
                                    Debug.Log($"[MCP] Connected to MCP server on port {port}");
                                return;
                            }
                            else
                            {
                                tcp.Close();
                            }
                        }
                        catch (Exception)
                        {
                            // Try next port
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

        private void Disconnect()
        {
            _connected = false;
            try { _stream?.Close(); } catch { }
            try { _tcp?.Close(); } catch { }
            _stream = null;
            _tcp = null;
        }

        private void ReceiveLoop()
        {
            var buffer = new byte[BUFFER_SIZE];
            var messageBuffer = new MemoryStream();

            try
            {
                while (_running && _connected && _tcp?.Connected == true)
                {
                    // Read WebSocket frame header
                    int b0 = ReadByte();
                    int b1 = ReadByte();
                    if (b0 < 0 || b1 < 0) break;

                    bool fin = (b0 & 0x80) != 0;
                    int opcode = b0 & 0x0F;
                    bool masked = (b1 & 0x80) != 0;
                    long payloadLen = b1 & 0x7F;

                    if (payloadLen == 126)
                    {
                        int h = ReadByte();
                        int l = ReadByte();
                        if (h < 0 || l < 0) break;
                        payloadLen = (h << 8) | l;
                    }
                    else if (payloadLen == 127)
                    {
                        var lenBytes = ReadBytes(8);
                        if (lenBytes == null) break;
                        payloadLen = 0;
                        for (int i = 0; i < 8; i++)
                            payloadLen = (payloadLen << 8) | lenBytes[i];
                    }

                    byte[] maskKey = null;
                    if (masked)
                    {
                        maskKey = ReadBytes(4);
                        if (maskKey == null) break;
                    }

                    // Read payload
                    byte[] payload = null;
                    if (payloadLen > 0)
                    {
                        payload = ReadBytes((int)payloadLen);
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
                                _incomingMessages.Enqueue(text);
                            }
                            break;

                        case 0x8: // Close
                            SendCloseFrame();
                            _connected = false;
                            return;

                        case 0x9: // Ping
                            SendPongFrame(payload);
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
                    EditorApplication.delayCall += () =>
                        Debug.LogWarning($"[MCP] WebSocket receive error: {ex.Message}");
                }
            }
            finally
            {
                bool wasConnected = _connected;
                _connected = false;
                if (wasConnected && _running)
                {
                    EditorApplication.delayCall += () =>
                        Debug.Log("[MCP] Disconnected from MCP server");
                }
            }
        }

        private int ReadByte()
        {
            try
            {
                return _stream.ReadByte();
            }
            catch
            {
                return -1;
            }
        }

        private byte[] ReadBytes(int count)
        {
            var buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = _stream.Read(buffer, offset, count - offset);
                if (read <= 0) return null;
                offset += read;
            }
            return buffer;
        }

        private void SendWebSocketFrame(byte opcode, byte[] payload)
        {
            if (!_connected || _stream == null) return;

            lock (_sendLock)
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
                        for (int i = 7; i >= 0; i--)
                            frame.WriteByte((byte)((len >> (i * 8)) & 0xFF));
                    }

                    // Generate mask key
                    var maskKey = new byte[4];
                    using (var rng = RandomNumberGenerator.Create())
                        rng.GetBytes(maskKey);
                    frame.Write(maskKey, 0, 4);

                    // Masked payload
                    if (payload != null && payload.Length > 0)
                    {
                        var masked = new byte[payload.Length];
                        for (int i = 0; i < payload.Length; i++)
                            masked[i] = (byte)(payload[i] ^ maskKey[i % 4]);
                        frame.Write(masked, 0, masked.Length);
                    }

                    var frameBytes = frame.ToArray();
                    _stream.Write(frameBytes, 0, frameBytes.Length);
                    _stream.Flush();
                }
                catch (Exception)
                {
                    _connected = false;
                }
            }
        }

        private void SendCloseFrame()
        {
            SendWebSocketFrame(0x8, new byte[] { 0x03, 0xE8 }); // 1000 normal closure
        }

        private void SendPongFrame(byte[] payload)
        {
            SendWebSocketFrame(0xA, payload);
        }

        private void SendTextFrame(string text)
        {
            SendWebSocketFrame(0x1, Encoding.UTF8.GetBytes(text));
        }

        private void ProcessMessage(string message)
        {
            try
            {
                var request = JsonHelper.Deserialize<JsonRpcRequest>(message);

                if (request.method == "ping")
                {
                    SendTextFrame("{\"jsonrpc\":\"2.0\",\"method\":\"pong\",\"params\":{}}");
                    return;
                }

                _router.Dispatch(request, (response) =>
                {
                    SendTextFrame(response);
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP] Error processing message: {ex.Message}");
            }
        }
    }
}
