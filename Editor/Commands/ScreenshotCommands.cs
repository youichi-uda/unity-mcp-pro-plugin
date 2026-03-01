using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityMcpPro
{
    public class ScreenshotCommands : BaseCommand
    {
        public static void Register(CommandRouter router)
        {
            router.Register("get_editor_screenshot", GetEditorScreenshot);
            router.Register("get_game_screenshot", GetGameScreenshot);
            router.Register("compare_screenshots", CompareScreenshots);
            router.Register("capture_frames", CaptureFrames);
        }

        private static object GetEditorScreenshot(Dictionary<string, object> p)
        {
            int width = GetIntParam(p, "width", 800);
            int height = GetIntParam(p, "height", 600);

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

            byte[] png = tex.EncodeToPNG();
            string base64 = Convert.ToBase64String(png);

            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);

            return new Dictionary<string, object>
            {
                { "image", base64 },
                { "width", width },
                { "height", height },
                { "source", "scene_view" }
            };
        }

        private static object GetGameScreenshot(Dictionary<string, object> p)
        {
            int width = GetIntParam(p, "width", 800);
            int height = GetIntParam(p, "height", 600);

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

            byte[] png = tex.EncodeToPNG();
            string base64 = Convert.ToBase64String(png);

            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);

            return new Dictionary<string, object>
            {
                { "image", base64 },
                { "width", width },
                { "height", height },
                { "source", "game_camera" },
                { "cameraName", gameCamera.name }
            };
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
            byte[] diffPng = diffTex.EncodeToPNG();
            string diffBase64 = Convert.ToBase64String(diffPng);

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
            int width = GetIntParam(p, "width", 400);
            int height = GetIntParam(p, "height", 300);

            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Play mode is required for frame capture");

            Camera cam = Camera.main;
            if (cam == null)
                throw new InvalidOperationException("No main camera found");

            var frames = new List<object>();
            int captured = 0;
            float nextCapture = (float)EditorApplication.timeSinceStartup;

            EditorApplication.CallbackFunction captureCallback = null;
            captureCallback = () =>
            {
                if (captured >= frameCount || !EditorApplication.isPlaying)
                {
                    EditorApplication.update -= captureCallback;
                    return;
                }

                float currentTime = (float)EditorApplication.timeSinceStartup;
                if (currentTime >= nextCapture)
                {
                    var rt = new RenderTexture(width, height, 24);
                    var prevRT = cam.targetTexture;
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = prevRT;

                    RenderTexture.active = rt;
                    var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;

                    byte[] png = tex.EncodeToPNG();

                    frames.Add(new Dictionary<string, object>
                    {
                        { "frame", captured },
                        { "timestamp", currentTime },
                        { "image", Convert.ToBase64String(png) }
                    });

                    UnityEngine.Object.DestroyImmediate(tex);
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);

                    captured++;
                    nextCapture = currentTime + interval;
                }
            };

            EditorApplication.update += captureCallback;

            // Return immediately with metadata; frames are captured async
            return new Dictionary<string, object>
            {
                { "status", "capturing" },
                { "frameCount", frameCount },
                { "interval", interval },
                { "width", width },
                { "height", height },
                { "message", $"Capturing {frameCount} frames at {interval}s intervals" }
            };
        }
    }
}
