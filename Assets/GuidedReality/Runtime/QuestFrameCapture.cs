using System;
using System.Threading;
using System.Threading.Tasks;
using Meta.XR;
using UnityEngine;
using UnityEngine.Rendering;

namespace GuidedReality
{
    public sealed class QuestFrameCapture : MonoBehaviour
    {
        public PassthroughCameraAccess CameraAccess;
        public EnvironmentRaycastManager Depth;
        public async Task<FrameSnapshot> Capture(RuntimeSettings settings, CancellationToken cancel)
        {
            float deadline = Time.realtimeSinceStartup + 3;
            while (!CameraAccess.IsPlaying || !CameraAccess.IsUpdatedThisFrame)
            {
                cancel.ThrowIfCancellationRequested();
                if (Time.realtimeSinceStartup > deadline) throw new Exception("Camera not ready. Grant headset camera permission and use Quest 3/3S.");
                await Task.Yield();
            }
            if (!EnvironmentRaycastManager.IsSupported) throw new Exception("Environment depth is unavailable.");
            var f = new FrameSnapshot {
                CameraTimestamp = CameraAccess.Timestamp, CapturedAt = Time.realtimeSinceStartup,
                CameraPose = CameraAccess.GetCameraPose(), Width = CameraAccess.CurrentResolution.x,
                Height = CameraAccess.CurrentResolution.y };
            Vector3 LocalRay(Vector2 uv)
            {
                Vector3 d = Quaternion.Inverse(f.CameraPose.rotation) * CameraAccess.ViewportPointToRay(uv, f.CameraPose).direction;
                return d / d.z;
            }
            f.Ray00 = LocalRay(Vector2.zero); f.Ray10 = LocalRay(Vector2.right); f.Ray01 = LocalRay(Vector2.up);
            // Enqueue after PCA's native texture update. Blocking Blit/ReadPixels can return the previous frame.
            var completion = new TaskCompletionSource<byte[]>();
            AsyncGPUReadback.Request(CameraAccess.GetTexture(), 0, TextureFormat.RGBA32, readback => {
                if (readback.hasError) completion.TrySetException(new Exception("Camera GPU readback failed."));
                else completion.TrySetResult(readback.GetData<byte>().ToArray());
            });
            // Enforce the new baseline even when an older device config still requests 41x31.
            int cols = Mathf.Clamp(settings.depthGridColumns, 81, 161), rows = Mathf.Clamp(settings.depthGridRows, 61, 121);
            var depthTimer = System.Diagnostics.Stopwatch.StartNew();
            f.Depth.Capacity = cols * rows;
            f.DepthGridColumns = cols; f.DepthGridRows = rows;
            for (int y=0;y<rows;y++) for (int x=0;x<cols;x++)
            {
                var uv = new Vector2((x+.5f)/cols, (y+.5f)/rows);
                if (Depth.Raycast(f.RayAt(uv), out var hit, 5f) && hit.point != Vector3.zero)
                    f.Depth.Add(new DepthSample { Viewport=uv, Point=hit.point, Normal=hit.normal });
            }
            depthTimer.Stop();
            Debug.Log($"GuidedReality: Captured depth {cols}x{rows}, {f.Depth.Count}/{cols*rows} valid samples, {depthTimer.Elapsed.TotalMilliseconds:F1} ms. No depth-image interpolation.");
            byte[] rgba = await completion.Task;
            cancel.ThrowIfCancellationRequested();
            
            var image = new Texture2D(f.Width, f.Height, TextureFormat.RGBA32, false);
            try { image.LoadRawTextureData(rgba); image.Apply(); f.Jpeg = image.EncodeToJPG(90); }
            finally { Destroy(image); }
            await CaptureDebugLog.SaveCapture(f);
            if (f.Depth.Count < 3) throw new Exception("Depth not ready. Grant spatial-data permission, look around, then press A again.");
            return f;
        }
    }
}
