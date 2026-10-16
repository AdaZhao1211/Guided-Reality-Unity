using System;
using System.Collections.Generic;
using UnityEngine;

namespace GuidedReality
{
    [Serializable] public sealed class RuntimeSettings
    {
        public string geminiApiKey = "";
        public string geminiModel = "gemini-3.8-flash";
        public string samEndpoint = "http://127.0.0.1:8765";
        public string samToken = "";
        public int timeoutSeconds = 90;
        public int depthGridColumns = 81;
        public int depthGridRows = 61;
        public float maxCaptureAgeSeconds = 120;
    }
    [Serializable] public sealed class Detection
    {
        public bool found;
        public string label;
        public float[] box_2d;
        public void Validate()
        {
            if (!found) return;
            if (box_2d == null || box_2d.Length != 4) throw new Exception("Gemini returned an invalid box.");
            foreach (float v in box_2d)
                if (float.IsNaN(v) || float.IsInfinity(v) || v < 0 || v > 1000) throw new Exception("Gemini box is outside 0–1000.");
            if (box_2d[0] >= box_2d[2] || box_2d[1] >= box_2d[3]) throw new Exception("Gemini box has zero or reversed area.");
        }
    }
    [Serializable] public sealed class SegmentRequest
    {
        public string request_id;
        public int visual_type = 2;
        public string image_base64;
        public float[] box_2d;
    }
    [Serializable] public sealed class SegmentResponse
    {
        public string request_id;
        public int image_width, image_height;
        // Top-left image pixels: x, y, width, height. Right/bottom are exclusive.
        public int[] crop_rect;
        public string png_base64;
        public float score;
        public void Validate(FrameSnapshot frame)
        {
            if (request_id != frame.Id || image_width != frame.Width || image_height != frame.Height)
                throw new Exception("SAM reply belongs to a different frame.");
            if (crop_rect == null || crop_rect.Length != 4 || crop_rect[0] < 0 || crop_rect[1] < 0 ||
                crop_rect[2] <= 0 || crop_rect[3] <= 0 || (long)crop_rect[0] + crop_rect[2] > image_width ||
                (long)crop_rect[1] + crop_rect[3] > image_height || string.IsNullOrEmpty(png_base64))
                throw new Exception("SAM returned invalid crop dimensions.");
        }
    }
    public struct DepthSample
    {
        public Vector2 Viewport;
        public Vector3 Point, Normal;
    }
    public sealed class FrameSnapshot
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string DebugTimestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fffffff", System.Globalization.CultureInfo.InvariantCulture);
        public DateTime CameraTimestamp;
        public float CapturedAt;
        public int Width, Height;
        public int DepthGridColumns = 81, DepthGridRows = 61;
        public Pose CameraPose;
        public Vector3 Ray00, Ray10, Ray01; // camera-local directions with z == 1; frozen intrinsics
        public byte[] Jpeg;
        public readonly List<DepthSample> Depth = new List<DepthSample>();
        public Ray RayAt(Vector2 viewport)
        {
            var local = Ray00 + viewport.x * (Ray10 - Ray00) + viewport.y * (Ray01 - Ray00);
            return new Ray(CameraPose.position, CameraPose.rotation * local);
        }
    }
}
