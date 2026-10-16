using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace GuidedReality
{
    // Images remain in this app's Quest storage; logging errors must not interrupt inference.
    public static class CaptureDebugLog
    {
        public sealed class DepthImage
        {
            public int Width, Height;
            public float[] Pixels;
        }

        public static async Task SaveCapture(FrameSnapshot frame)
        {
            await Save(frame.DebugTimestamp + "_camera.jpg", frame.Jpeg);
            DepthImage depth;
            {
                // Preserve the actual captured grid; do not invent/interpolate missing samples.
                depth = new DepthImage { Width=frame.DepthGridColumns, Height=frame.DepthGridRows,
                    Pixels=new float[frame.DepthGridColumns * frame.DepthGridRows] };
                foreach (var sample in frame.Depth)
                {
                    int x=Mathf.Clamp(Mathf.FloorToInt(sample.Viewport.x * depth.Width), 0, depth.Width-1);
                    int y=Mathf.Clamp(Mathf.FloorToInt(sample.Viewport.y * depth.Height), 0, depth.Height-1);
                    var local=Quaternion.Inverse(frame.CameraPose.rotation) * (sample.Point-frame.CameraPose.position);
                    depth.Pixels[y * depth.Width + x]=local.z;
                }
            }
            Texture2D texture = null;
            try
            {
                texture=new Texture2D(depth.Width, depth.Height, TextureFormat.RGBA32, false, true);
                var pixels=new Color32[depth.Width * depth.Height];
                for (int i=0; i<pixels.Length; i++)
                {
                    float raw=depth.Pixels[i];
                    float meters=raw;
                    bool valid=meters > 0 && !float.IsNaN(meters) && !float.IsInfinity(meters);
                    byte gray=valid ? (byte)Mathf.Clamp(Mathf.RoundToInt(meters / 5f * 255f), 1, 255) : (byte)0;
                    pixels[i]=new Color32(gray, gray, gray, 255);
                }
                texture.SetPixels32(pixels); texture.Apply();
                await Save(frame.DebugTimestamp + "_depth.png", texture.EncodeToPNG());
            }
            catch (Exception e) { Debug.LogWarning("GuidedReality logs: depth save failed: " + e.Message); }
            finally { if (texture) UnityEngine.Object.Destroy(texture); }
        }

        public static Task SaveCrop(FrameSnapshot frame, byte[] png) => Save(frame.DebugTimestamp + "_cropped.png", png);

        static async Task Save(string filename, byte[] bytes)
        {
            string folder=Path.Combine(Application.persistentDataPath, "logs");
            try
            {
                await Task.Run(() => {
                    Directory.CreateDirectory(folder);
                    // Refuse to overwrite a previous capture, even if the device clock changes.
                    using var file=new FileStream(Path.Combine(folder, filename), FileMode.CreateNew, FileAccess.Write);
                    file.Write(bytes, 0, bytes.Length);
                });
                Debug.Log("GuidedReality logs: saved " + Path.Combine(folder, filename));
            }
            catch (Exception e) { Debug.LogWarning("GuidedReality logs: save failed: " + e.Message); }
        }
    }
}
