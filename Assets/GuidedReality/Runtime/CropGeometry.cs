using System;
using UnityEngine;

namespace GuidedReality
{
    public static class CropGeometry
    {
        public static Vector2 PixelToViewport(float x, float y, int width, int height) => new Vector2(x / width, 1f - y / height);

        // Swift drawAnimation uses crop-center depth and a plane parallel to the saved camera.
        // Quest has a sampled grid: use the valid sample nearest that center inside the crop.
        public static Vector3[] Project(FrameSnapshot frame, SegmentResponse crop, Texture2D mask)
        {
            crop.Validate(frame);
            int[] r = crop.crop_rect;
            float centerX=r[0]+r[2]*.5f, centerY=r[1]+r[3]*.5f;
            Vector3 forward=frame.CameraPose.rotation * Vector3.forward;
            float bestDistance=float.PositiveInfinity, depth=0;
            foreach (var sample in frame.Depth)
            {
                float px=sample.Viewport.x * frame.Width;
                float py=(1-sample.Viewport.y) * frame.Height;
                if (px<r[0] || px>=r[0]+r[2] || py<r[1] || py>=r[1]+r[3]) continue;
                float z=Vector3.Dot(sample.Point-frame.CameraPose.position, forward);
                if (float.IsNaN(z) || float.IsInfinity(z) || z<.1f || z>5f) continue;
                float distance=(px-centerX)*(px-centerX)+(py-centerY)*(py-centerY);
                if (distance<bestDistance) { bestDistance=distance; depth=z; }
            }
            if (depth<=0) throw new Exception("No captured depth near the keyboard crop. Move closer and press A again.");
            var uv=new[] {
                PixelToViewport(r[0],r[1]+r[3],frame.Width,frame.Height),
                PixelToViewport(r[0]+r[2],r[1]+r[3],frame.Width,frame.Height),
                PixelToViewport(r[0]+r[2],r[1],frame.Width,frame.Height),
                PixelToViewport(r[0],r[1],frame.Width,frame.Height) };
            var corners=new Vector3[4];
            for (int i=0;i<4;i++)
            {
                // Frozen rays have camera-local z=1: pixel extent * depth / focal length.
                var local=frame.Ray00+uv[i].x*(frame.Ray10-frame.Ray00)+uv[i].y*(frame.Ray01-frame.Ray00);
                corners[i]=frame.CameraPose.position+frame.CameraPose.rotation*(local*depth);
            }
            if (Vector3.Distance(corners[0],corners[1])>2f || Vector3.Distance(corners[0],corners[3])>2f)
                throw new Exception("Projected crop is implausibly large.");
            return corners;
        }

        // Match the RGB transform in the Swift app's combined.py server; preserve SAM alpha.
        public static void ApplyBlueTint(Texture2D image)
        {
            var pixels=image.GetPixels32();
            for (int i=0;i<pixels.Length;i++)
            {
                var c=pixels[i];
                byte gray=(byte)Mathf.Clamp(Mathf.RoundToInt(.299f*c.r+.587f*c.g+.114f*c.b),0,255);
                pixels[i]=c.a==0 ? new Color32(0,0,0,0) : new Color32(0,gray,(byte)Mathf.Min(c.b*3,255),c.a);
            }
            image.SetPixels32(pixels); image.Apply();
        }
    }
}
