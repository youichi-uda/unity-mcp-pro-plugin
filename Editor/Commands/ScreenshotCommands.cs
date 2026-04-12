using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnityMcpPro
{
    public class ScreenshotCommands : BaseCommand
    {
        // Only touched from the main thread: WebSocketServer queues inbound
        // commands onto EditorApplication.update, the same loop the capture
        // callback runs on. No locking needed as long as that invariant holds.
        private static readonly Dictionary<string, CaptureSession> _captureSessions
            = new Dictionary<string, CaptureSession>();

        private class CaptureSession
        {
            public List<Dictionary<string, object>> frames = new List<Dictionary<string, object>>();
            public int targetCount;
            public int width;
            public int height;
            public int quality;
            public string format;
            public float interval;
            public float nextCapture;
            public bool complete;
            public string source;
            public double createdAt;
            public string error;
        }

        public static void Register(CommandRouter router)
        {
            router.Register("get_editor_screenshot", GetEditorScreenshot);
            router.Register("get_game_screenshot", GetGameScreenshot);
            router.Register("compare_screenshots", CompareScreenshots);
            router.Register("capture_frames", CaptureFrames);
            router.Register("get_captured_frames", GetCapturedFrames);
        }

        private static byte[] EncodeTexture(Texture2D tex, string format, int quality)
        {
            if (format == "jpg" || format == "jpeg")
                return tex.EncodeToJPG(quality);
            return tex.EncodeToPNG();
        }

        private static string GetMimeType(string format)
        {
            if (format == "jpg" || format == "jpeg")
                return "image/jpeg";
            return "image/png";
        }

        private static object GetEditorScreenshot(Dictionary<string, object> p)
        {
            int width = GetIntParam(p, "width", 320);
            int height = GetIntParam(p, "height", 240);
            string format = GetStringParam(p, "format", "jpg");
            int quality = GetIntParam(p, "quality", 75);

            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
                throw new InvalidOperationException("No active Scene view found");

            var camera = sceneView.camera;
            if (camera == null)
                throw new InvalidOperationException("Scene view camera not available");

            var rt = new RenderTexture(width, height, 24);
            var prevRT = camera.targetTexture;
            camera.targetTexture = rt;
            camera.Render();
            camera.targetTexture = prevRT;

            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            byte[] imageData = EncodeTexture(tex, format, quality);
            string base64 = Convert.ToBase64String(imageData);

            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);

            return new Dictionary<string, object>
            {
                { "image", base64 },
                { "width", width },
                { "height", height },
                { "format", format },
                { "mimeType", GetMimeType(format) },
                { "source", "scene_view" }
            };
        }

        private static object GetGameScreenshot(Dictionary<string, object> p)
        {
            int width = GetIntParam(p, "width", 320);
            int height = GetIntParam(p, "height", 240);
            string format = GetStringParam(p, "format", "jpg");
            int quality = GetIntParam(p, "quality", 75);

            Texture2D tex;
            string source;

            if (Application.isPlaying)
            {
                // Read directly from the Game View's internal RenderTexture.
                // ScreenCapture.CaptureScreenshotAsTexture() requires end-of-frame
                // timing; called synchronously from the MCP handler it reads the
                // editor's framebuffer instead, producing chrome and artifacts.
                tex = CaptureGameViewTexture();
                if (tex != null)
                {
                    source = "game_view";
                }
                else
                {
                    // Fallback: render main camera to a temporary RT.
                    // This won't include ScreenSpaceOverlay UI but avoids artifacts.
                    tex = RenderCameraToTexture(width, height);
                    source = "camera_render";
                }
            }
            else
            {
                tex = RenderCameraToTexture(width, height);
                source = "game_camera";
            }

            // Resize to requested dimensions if the captured size differs
            if (tex.width != width || tex.height != height)
            {
                var resizeRt = new RenderTexture(width, height, 0);
                Graphics.Blit(tex, resizeRt);
                RenderTexture.active = resizeRt;
                var resizedTex = new Texture2D(width, height, TextureFormat.RGB24, false);
                resizedTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                resizedTex.Apply();
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(tex);
                resizeRt.Release();
                UnityEngine.Object.DestroyImmediate(resizeRt);
                tex = resizedTex;
            }

            byte[] imageData = EncodeTexture(tex, format, quality);
            string base64 = Convert.ToBase64String(imageData);
            UnityEngine.Object.DestroyImmediate(tex);

            return new Dictionary<string, object>
            {
                { "image", base64 },
                { "width", width },
                { "height", height },
                { "format", format },
                { "mimeType", GetMimeType(format) },
                { "source", source }
            };
        }

        /// <summary>
        /// Reads the Game View's backing RenderTexture via reflection.
        /// Returns the full composited frame (including ScreenSpaceOverlay UI)
        /// at the Game View's native resolution, or null on failure.
        /// </summary>
        private static Texture2D CaptureGameViewTexture()
        {
            try
            {
                var assembly = typeof(EditorWindow).Assembly;
                var gameViewType = assembly.GetType("UnityEditor.GameView");
                if (gameViewType == null) return null;

                // Probe for an existing Game View before calling GetWindow,
                // which would otherwise create one and surprise the user.
                EditorWindow gameView = null;
                var existing = Resources.FindObjectsOfTypeAll(gameViewType);
                if (existing != null && existing.Length > 0)
                    gameView = existing[0] as EditorWindow;
                if (gameView == null) return null;

                // PlayModeView (base of GameView) holds m_TargetTexture —
                // the RenderTexture that Unity's rendering pipeline outputs to.
                FieldInfo field = null;
                for (var t = gameViewType; t != null && field == null; t = t.BaseType)
                    field = t.GetField("m_TargetTexture",
                        BindingFlags.NonPublic | BindingFlags.Instance);

                var rt = field?.GetValue(gameView) as RenderTexture;
                if (rt == null || !rt.IsCreated() || rt.width == 0 || rt.height == 0)
                    return null;

                var prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prevActive;

                return tex;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MCP] Game View texture capture failed, using fallback: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Renders the main camera (or first available) to a temporary RenderTexture.
        /// Does not capture ScreenSpaceOverlay canvases.
        /// </summary>
        private static Texture2D RenderCameraToTexture(int width, int height)
        {
            Camera gameCamera = Camera.main;
            if (gameCamera == null)
            {
                var cameras = Camera.allCameras;
                if (cameras.Length > 0)
                    gameCamera = cameras[0];
            }
            if (gameCamera == null)
                throw new InvalidOperationException("No camera found in the scene");

            var rt = new RenderTexture(width, height, 24);
            var prevRT = gameCamera.targetTexture;
            gameCamera.targetTexture = rt;
            gameCamera.Render();
            gameCamera.targetTexture = prevRT;

            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);

            return tex;
        }

        private static object CompareScreenshots(Dictionary<string, object> p)
        {
            string imageA = GetStringParam(p, "image_a");
            string imageB = GetStringParam(p, "image_b");
            int threshold = GetIntParam(p, "threshold", 10);

            if (string.IsNullOrEmpty(imageA) || string.IsNullOrEmpty(imageB))
                throw new ArgumentException("Both image_a and image_b are required");

            byte[] bytesA = Convert.FromBase64String(imageA);
            byte[] bytesB = Convert.FromBase64String(imageB);

            var texA = new Texture2D(2, 2);
            var texB = new Texture2D(2, 2);
            texA.LoadImage(bytesA);
            texB.LoadImage(bytesB);

            int width = Math.Min(texA.width, texB.width);
            int height = Math.Min(texA.height, texB.height);

            var pixelsA = texA.GetPixels32();
            var pixelsB = texB.GetPixels32();
            int totalPixels = width * height;
            int differentPixels = 0;

            var diffTex = new Texture2D(width, height, TextureFormat.RGB24, false);
            var diffPixels = new Color32[totalPixels];

            for (int i = 0; i < totalPixels; i++)
            {
                int rDiff = Math.Abs(pixelsA[i].r - pixelsB[i].r);
                int gDiff = Math.Abs(pixelsA[i].g - pixelsB[i].g);
                int bDiff = Math.Abs(pixelsA[i].b - pixelsB[i].b);
                int maxDiff = Math.Max(rDiff, Math.Max(gDiff, bDiff));

                if (maxDiff > threshold)
                {
                    differentPixels++;
                    diffPixels[i] = new Color32(255, 0, 0, 255);
                }
                else
                {
                    diffPixels[i] = new Color32(
                        (byte)((pixelsA[i].r + pixelsB[i].r) / 4),
                        (byte)((pixelsA[i].g + pixelsB[i].g) / 4),
                        (byte)((pixelsA[i].b + pixelsB[i].b) / 4),
                        255
                    );
                }
            }

            diffTex.SetPixels32(diffPixels);
            diffTex.Apply();
            byte[] diffJpg = diffTex.EncodeToJPG(75);
            string diffBase64 = Convert.ToBase64String(diffJpg);

            UnityEngine.Object.DestroyImmediate(texA);
            UnityEngine.Object.DestroyImmediate(texB);
            UnityEngine.Object.DestroyImmediate(diffTex);

            float percent = totalPixels > 0 ? (float)differentPixels / totalPixels * 100f : 0f;

            return new Dictionary<string, object>
            {
                { "totalPixels", totalPixels },
                { "differentPixels", differentPixels },
                { "differencePercent", Math.Round(percent, 2) },
                { "identical", differentPixels == 0 },
                { "threshold", threshold },
                { "diffImage", diffBase64 }
            };
        }

        private static object CaptureFrames(Dictionary<string, object> p)
        {
            int frameCount = GetIntParam(p, "frame_count", 5);
            float interval = GetFloatParam(p, "interval", 0.5f);
            int width = GetIntParam(p, "width", 320);
            int height = GetIntParam(p, "height", 240);
            int quality = GetIntParam(p, "quality", 60);
            string format = GetStringParam(p, "format", "jpg");

            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Play mode is required for frame capture");

            // Purge sessions older than 60 seconds to prevent memory leaks
            PurgeOldSessions(60.0);

            string captureId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var session = new CaptureSession
            {
                targetCount = frameCount,
                width = width,
                height = height,
                quality = quality,
                format = format,
                interval = interval,
                nextCapture = (float)EditorApplication.timeSinceStartup,
                createdAt = EditorApplication.timeSinceStartup
            };
            _captureSessions[captureId] = session;

            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                if (session.complete)
                {
                    EditorApplication.update -= callback;
                    return;
                }

                if (!EditorApplication.isPlaying)
                {
                    session.complete = true;
                    EditorApplication.update -= callback;
                    return;
                }

                float now = (float)EditorApplication.timeSinceStartup;
                if (now < session.nextCapture)
                    return;

                // If the capture body throws (e.g. no camera for the fallback
                // path) we must mark the session complete and unsubscribe,
                // otherwise the exception keeps firing every editor tick.
                try
                {
                    Texture2D tex = CaptureGameViewTexture();
                    if (tex != null)
                    {
                        session.source = "game_view";
                    }
                    else
                    {
                        tex = RenderCameraToTexture(width, height);
                        session.source = "camera_render";
                    }

                    if (tex.width != width || tex.height != height)
                    {
                        var resizeRt = new RenderTexture(width, height, 0);
                        Graphics.Blit(tex, resizeRt);
                        RenderTexture.active = resizeRt;
                        var resizedTex = new Texture2D(width, height, TextureFormat.RGB24, false);
                        resizedTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                        resizedTex.Apply();
                        RenderTexture.active = null;
                        UnityEngine.Object.DestroyImmediate(tex);
                        resizeRt.Release();
                        UnityEngine.Object.DestroyImmediate(resizeRt);
                        tex = resizedTex;
                    }

                    byte[] imageData = EncodeTexture(tex, format, quality);
                    UnityEngine.Object.DestroyImmediate(tex);

                    session.frames.Add(new Dictionary<string, object>
                    {
                        { "frame", session.frames.Count },
                        { "timestamp", now },
                        { "image", Convert.ToBase64String(imageData) },
                        { "mimeType", GetMimeType(format) }
                    });

                    session.nextCapture = now + interval;

                    if (session.frames.Count >= session.targetCount)
                    {
                        session.complete = true;
                        EditorApplication.update -= callback;
                    }
                }
                catch (Exception e)
                {
                    session.error = e.Message;
                    session.complete = true;
                    EditorApplication.update -= callback;
                    Debug.LogError($"[MCP] Frame capture failed: {e.Message}");
                }
            };

            EditorApplication.update += callback;

            return new Dictionary<string, object>
            {
                { "status", "capturing" },
                { "capture_id", captureId },
                { "frameCount", frameCount },
                { "interval", interval },
                { "width", width },
                { "height", height },
                { "format", format },
                { "message", $"Capturing {frameCount} frames at {interval}s intervals. Use get_captured_frames with capture_id \"{captureId}\" to retrieve results." }
            };
        }

        private static object GetCapturedFrames(Dictionary<string, object> p)
        {
            string captureId = GetStringParam(p, "capture_id");
            if (string.IsNullOrEmpty(captureId))
                throw new ArgumentException("capture_id is required");

            if (!_captureSessions.TryGetValue(captureId, out var session))
                throw new ArgumentException($"No capture session found with id \"{captureId}\"");

            var result = new Dictionary<string, object>
            {
                { "capture_id", captureId },
                { "status", session.error != null ? "error" : (session.complete ? "complete" : "capturing") },
                { "capturedCount", session.frames.Count },
                { "targetCount", session.targetCount },
                { "width", session.width },
                { "height", session.height },
                { "format", session.format },
                { "source", session.source ?? "pending" },
                // Always expose whatever frames have been captured so far —
                // callers polling a long-running session can stream results
                // instead of waiting for completion.
                { "frames", session.frames }
            };

            if (session.error != null)
                result["error"] = session.error;

            if (session.complete)
                _captureSessions.Remove(captureId);

            return result;
        }

        private static void PurgeOldSessions(double maxAgeSeconds)
        {
            // Only evict sessions that have already finished — in-flight
            // captures can legitimately outlive the TTL on long runs
            // (e.g. frame_count=200, interval=1s).
            var stale = new List<string>();
            double now = EditorApplication.timeSinceStartup;
            foreach (var kv in _captureSessions)
            {
                if (kv.Value.complete && now - kv.Value.createdAt > maxAgeSeconds)
                    stale.Add(kv.Key);
            }
            foreach (var id in stale)
                _captureSessions.Remove(id);
        }
    }
}
