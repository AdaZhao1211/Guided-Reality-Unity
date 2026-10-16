using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Meta.XR;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR;

namespace GuidedReality
{
    public sealed class GuidedRealityApp : MonoBehaviour
    {
        public QuestFrameCapture Capture;
        [UnityEngine.Serialization.FormerlySerializedAs("StatusLabel")]
        public TextMesh LegacyStatusLabel;
        public Material CropMaterial;
        public int VisualType = 2;
        public TextAsset Instructions;
        InstructionDocument document;
        int stepIndex = -1;
        public string StatusText => status;
        public string StepHeading => document == null ? "Printer guidance" : $"Step {stepIndex+1} / {document.instructions.Length}";
        public string InstructionText => document == null ? "Look at the printer and press right B to load instructions." : document.instructions[stepIndex].instruction;
        public bool CanNavigate(int action) => !busy && ready && (action == 0 || document != null && (action == 1 ? stepIndex > 0 : stepIndex < document.instructions.Length-1));
        bool ready;
        public void Navigate(int action)
        {
            if (!CanNavigate(action)) {
                SetStatus(busy ? "Still processing; release the stick and try again when ready." : !ready ? "App is still starting." : document == null ? "Press right B to load 3DPrinter.json first." : action == 1 ? "Already at the first step." : "Already at the last step.");
                return;
            }
            try {
                if (action == 0) { if (!Instructions) throw new Exception("Assign 3DPrinter.json to the pipeline Instructions field."); document=InstructionDocument.Parse(Instructions.text); stepIndex=0; }
                else stepIndex += action == 1 ? -1 : 1;
                ClearCrop();
                SetStatus($"Step {stepIndex+1}/{document.instructions.Length}: {document.instructions[stepIndex].instruction}");
                _ = Run();
            } catch (Exception e) { SetStatus(e.Message); }
        }
        RuntimeSettings settings;
        CancellationTokenSource lifetime;
        bool busy;
        readonly ControllerStepInput controllerInput = new ControllerStepInput();
        InstructionSpeech speech;
        GameObject cropRoot;
        Texture2D cropTexture;
        Mesh cropMesh;
        Material cropMaterialInstance;
        string status = "Starting…";
        void SetStatus(string text) { status=text; Debug.Log("GuidedReality: " + text); }
        void Awake()
        {
            if (LegacyStatusLabel) LegacyStatusLabel.gameObject.SetActive(false);
        }
        async void Start()
        {
            lifetime = new CancellationTokenSource();
            speech=gameObject.AddComponent<InstructionSpeech>();
            try {
                settings = new RuntimeSettings();
                string persistent = Path.Combine(Application.persistentDataPath, "guidedreality.local.json");
                if (File.Exists(persistent)) settings = JsonConvert.DeserializeObject<RuntimeSettings>(File.ReadAllText(persistent));
                else {
                    string path = Path.Combine(Application.streamingAssetsPath, "guidedreality.local.json");
                    if (!path.Contains("://")) path = new Uri(path).AbsoluteUri;
                    using var req = UnityWebRequest.Get(path);
                    try { settings=JsonConvert.DeserializeObject<RuntimeSettings>(await RemoteClients.Send(req, lifetime.Token)); }
                    catch (Exception) { SetStatus("Add guidedreality.local.json credentials. See README."); }
                }
                settings ??= new RuntimeSettings();
#if UNITY_ANDROID && !UNITY_EDITOR
                UnityEngine.Android.Permission.RequestUserPermissions(new[] {"horizonos.permission.HEADSET_CAMERA", "com.oculus.permission.USE_SCENE"});
#endif
                if (string.IsNullOrWhiteSpace(settings.geminiApiKey)) SetStatus("Set Gemini API key in local config.\nThen look at keyboard and press right A.");
                else SetStatus("Right B loads 3DPrinter.json. Right stick left/right changes steps. A retries.");
                ready=true;
            } catch(Exception e) { SetStatus(e.Message); }
        }
        void Update()
        {
            InputDevice right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            right.TryGetFeatureValue(CommonUsages.primaryButton, out bool a);
            right.TryGetFeatureValue(CommonUsages.secondaryButton, out bool b);
            right.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick);
            switch(controllerInput.Read(b, a, stick)) {
                case StepCommand.Load: SetStatus("B pressed - load 3DPrinter.json"); Navigate(0); break;
                case StepCommand.Previous: Navigate(1); break;
                case StepCommand.Next: Navigate(2); break;
                case StepCommand.Retry:
                    if (!busy && ready) { SetStatus("A pressed - capture started"); _ = Run(); }
                    else SetStatus(busy ? "A pressed - already processing" : "A pressed - app still starting");
                    break;
            }
        }
        async Task Run()
        {
            busy=true;
            GameObject pending=null;
            Texture2D texture=null;
            try {
                var cancel = lifetime.Token;
                var step=document == null ? new InstructionStep { instruction="Locate keyboard", visual_type=2, key_components=new[]{"keyboard",""} } : document.instructions[stepIndex];
                VisualType=step.visual_type;
                speech?.Speak(step.instruction);
                if (string.IsNullOrWhiteSpace(settings.geminiApiKey) || (VisualType == 2 && string.IsNullOrWhiteSpace(settings.samToken)))
                    throw new Exception("Add Gemini API key and SAM token in local config first.");
                SetStatus("Capturing camera + depth…");
                FrameSnapshot frame = await Capture.Capture(settings, cancel);
                SetStatus($"Saved camera pose + {frame.Width}x{frame.Height} image + {frame.DepthGridColumns}x{frame.DepthGridRows} depth grid ({frame.Depth.Count} valid)");
                pending = new GameObject("Guidance capture anchor");
                pending.transform.SetPositionAndRotation(frame.CameraPose.position, frame.CameraPose.rotation);
                var anchor = pending.AddComponent<OVRSpatialAnchor>();
                float limit=Time.realtimeSinceStartup+8;
                while (!anchor || !anchor.Localized) {
                    cancel.ThrowIfCancellationRequested();
                    if (!anchor || Time.realtimeSinceStartup > limit) throw new Exception("Spatial anchor could not localize. Check tracking and try again.");
                    await Task.Yield();
                }
                SetStatus($"Gemini: locating {step.Target}…");
                Detection detection = await RemoteClients.FindTarget(frame, step.Target, settings, cancel);
                if (!detection.found) { SetStatus($"No {step.Target} found. Reframe and press A."); return; }
                SetStatus($"Target box: [{string.Join(", ", detection.box_2d)}]");
                cancel.ThrowIfCancellationRequested();
                if (Time.realtimeSinceStartup-frame.CapturedAt > settings.maxCaptureAgeSeconds) throw new Exception("Capture expired. Press A again.");
                if (VisualType != 2) {
                    pending.AddComponent<GuidanceVisual>().Build(step, frame, detection, CropMaterial);
                    ClearCrop(); cropRoot=pending; pending=null;
                    SetStatus($"Type {VisualType} anchored for {step.Target}. A retries; right stick advances.");
                    return;
                }
                SegmentResponse result = await RemoteClients.Segment(frame, detection, settings, cancel, SetStatus);
                cancel.ThrowIfCancellationRequested();
                if (Time.realtimeSinceStartup-frame.CapturedAt > settings.maxCaptureAgeSeconds) throw new Exception("Capture expired. Press A again.");
                byte[] cropPng = Convert.FromBase64String(result.png_base64);
                texture = new Texture2D(2,2,TextureFormat.RGBA32,false);
                if (!texture.LoadImage(cropPng) || texture.width != result.crop_rect[2] || texture.height != result.crop_rect[3])
                    throw new Exception("Invalid crop PNG.");
                SetStatus($"Crop received and decoded: {texture.width} x {texture.height}");
                CropGeometry.ApplyBlueTint(texture);
                await CaptureDebugLog.SaveCrop(frame, texture.EncodeToPNG());
                Vector3[] vertices = CropGeometry.Project(frame, result, texture);
                float cropWidth=Vector3.Distance(vertices[0],vertices[1]);
                float cropHeight=Vector3.Distance(vertices[0],vertices[3]);
                Vector3 cropCenter=(vertices[0]+vertices[1]+vertices[2]+vertices[3])*.25f;
                float cropDistance=Vector3.Distance(frame.CameraPose.position,cropCenter);
                // Anchors may refine their pose while network requests are in flight.
                for(int i=0;i<vertices.Length;i++) vertices[i] = pending.transform.InverseTransformPoint(vertices[i]);
                if (!CropMaterial || !CropMaterial.shader || !CropMaterial.shader.isSupported)
                    throw new Exception("Crop material/shader is missing or unsupported in this build.");
                var mesh = new Mesh { name="Captured keyboard crop" };
                mesh.vertices=vertices; mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};
                mesh.triangles=new[]{0,2,1,0,3,2}; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                var crop = new GameObject("Keyboard transparent crop", typeof(MeshFilter), typeof(MeshRenderer));
                crop.transform.SetParent(pending.transform,false);
                crop.GetComponent<MeshFilter>().sharedMesh=mesh;
                var material = new Material(CropMaterial); material.mainTexture=texture;
                crop.GetComponent<MeshRenderer>().sharedMaterial=material;
                pending.AddComponent<GuidanceVisual>().AddCropArrow(step,cropCenter,frame.CameraPose.rotation,cropWidth,CropMaterial);
                ClearCrop(); cropRoot=pending; pending=null; cropTexture=texture; texture=null;
                cropMesh=mesh; cropMaterialInstance=material;
                SetStatus($"Crop anchored: {cropWidth:F2} x {cropHeight:F2} m, {cropDistance:F2} m from capture. Right stick changes steps.");
            } catch(OperationCanceledException) { }
            catch(Exception e) { SetStatus(e.Message); }
            finally { if(pending) Destroy(pending); if(texture) Destroy(texture); busy=false; }
        }
        void ClearCrop()
        {
            if(cropRoot) Destroy(cropRoot); if(cropTexture) Destroy(cropTexture);
            if(cropMesh) Destroy(cropMesh); if(cropMaterialInstance) Destroy(cropMaterialInstance);
            cropRoot=null; cropTexture=null; cropMesh=null; cropMaterialInstance=null;
        }
        void OnDestroy() { lifetime?.Cancel(); lifetime?.Dispose(); ClearCrop(); }
        void OnGUI() {
#if UNITY_EDITOR
            GUI.Label(new Rect(20,20,900,90), "GuidedReality · Quest 3/3S\n"+status+"\nCamera/depth require a headset. This Mac editor is not a camera simulator.");
#endif
        }
    }
}
