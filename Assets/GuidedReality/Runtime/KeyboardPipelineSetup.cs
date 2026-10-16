using Meta.XR;
using Meta.XR.EnvironmentDepth;
using UnityEngine;

namespace GuidedReality
{
    // Attach to the scene: reuse the user's working camera rig and passthrough.
    public sealed class KeyboardPipelineSetup : MonoBehaviour
    {
        public Material CropMaterial;
        public TextAsset Instructions;
        void Start()
        {
            if (FindFirstObjectByType<GuidedRealityApp>()) return;
            var rig = FindFirstObjectByType<OVRCameraRig>();
            if (!rig) { Debug.LogError("GuidedReality requires the scene's OVRCameraRig."); return; }
            var root = new GameObject("GuidedReality capture pipeline");
            root.SetActive(false);
            var camera = FindFirstObjectByType<PassthroughCameraAccess>();
            if (!camera) {
                camera = root.AddComponent<PassthroughCameraAccess>();
                camera.CameraPosition = PassthroughCameraAccess.CameraPositionType.Left;
                camera.RequestedResolution = new Vector2Int(1280, 960);
            }
            // OpenXR raycasting may not create the texture producer used by debug logging.
            if (!FindFirstObjectByType<EnvironmentDepthManager>()) root.AddComponent<EnvironmentDepthManager>();
            var depth = FindFirstObjectByType<EnvironmentRaycastManager>();
            if (!depth) depth = root.AddComponent<EnvironmentRaycastManager>();
            var capture = root.AddComponent<QuestFrameCapture>();
            capture.CameraAccess = camera; capture.Depth = depth;
            var app = root.AddComponent<GuidedRealityApp>();
            app.Instructions = Instructions;
            app.Capture = capture; app.CropMaterial = CropMaterial; app.VisualType = 2;
            root.SetActive(true);
        }
    }
}
