using System;
using System.IO;
using System.Linq;
using Meta.XR;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.Management;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;

namespace GuidedReality.Editor
{
    public static class ProjectSetup
    {
        [MenuItem("GuidedReality/Create or reset prototype scene")]
        public static void CreateScene()
        {
            Directory.CreateDirectory("Assets/GuidedReality/Scenes");
            Directory.CreateDirectory("Assets/GuidedReality/Materials");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab");
            if(!prefab) throw new Exception("Meta XR camera rig prefab is missing.");
            var rigGo=(GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var rig=rigGo.GetComponent<OVRCameraRig>();
            var manager=rigGo.GetComponent<OVRManager>();
            manager.isInsightPassthroughEnabled=true;
            manager.trackingOriginType=OVRManager.TrackingOrigin.FloorLevel;
            foreach(var camera in rigGo.GetComponentsInChildren<Camera>(true)) {
                camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.clear;
                camera.nearClipPlane=.05f;
            }
            var passthrough=new GameObject("Passthrough").AddComponent<OVRPassthroughLayer>();
            passthrough.overlayType=OVROverlay.OverlayType.Underlay;
            var captureGo=new GameObject("Capture and depth");
            var cameraAccess=captureGo.AddComponent<PassthroughCameraAccess>();
            cameraAccess.CameraPosition=PassthroughCameraAccess.CameraPositionType.Left;
            cameraAccess.RequestedResolution=new Vector2Int(1280,960);
            var depth=captureGo.AddComponent<EnvironmentRaycastManager>();
            var capture=captureGo.AddComponent<QuestFrameCapture>(); capture.CameraAccess=cameraAccess; capture.Depth=depth;
            var textGo=new GameObject("Status"); textGo.transform.SetParent(rig.centerEyeAnchor,false);
            textGo.transform.localPosition=new Vector3(-.42f,-.24f,1.15f); textGo.transform.localScale=Vector3.one*.0015f;
            var text=textGo.AddComponent<TextMesh>(); text.fontSize=48; text.characterSize=1;
            text.anchor=TextAnchor.MiddleLeft; text.color=Color.white; text.text="GuidedReality · right A to capture";
            const string materialPath="Assets/GuidedReality/Materials/Crop.mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(!mat) { mat=new Material(Shader.Find("GuidedReality/TransparentCrop")); AssetDatabase.CreateAsset(mat,materialPath); }
            var app=new GameObject("GuidedReality").AddComponent<GuidedRealityApp>();
            app.Instructions=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/3DPrinter.json");
            app.Capture=capture; app.LegacyStatusLabel=text; app.CropMaterial=mat; app.VisualType=2;
            EditorSceneManager.SaveScene(scene,"Assets/GuidedReality/Scenes/Keyboard.unity");
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/GuidedReality/Scenes/Keyboard.unity",true)};
            Configure(); VerifyGeometry(); AssetDatabase.SaveAssets();
            Debug.Log("GUIDEDREALITY_SETUP_OK");
        }
        [MenuItem("GuidedReality/Configure Quest build")]
        public static void Configure()
        {
            var playerAsset=AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0];
            var playerObject=new SerializedObject(playerAsset);
            playerObject.FindProperty("activeInputHandler").intValue=1;
            playerObject.ApplyModifiedPropertiesWithoutUndo();
            PlayerSettings.companyName="Ada"; PlayerSettings.productName="GuidedReality";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"com.ada.guidedreality");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android,ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion=AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.colorSpace=ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android,false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,new[]{GraphicsDeviceType.Vulkan});
            PlayerSettings.insecureHttpOption=InsecureHttpOption.AlwaysAllowed; // only private Tailscale/loopback endpoint; manifest scopes domains
            var xr=AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>("Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            if(!xr) throw new Exception("XR settings asset is missing.");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey,xr,true);
            var android=xr.SettingsForBuildTarget(BuildTargetGroup.Android);
            if(!XRPackageMetadataStore.AssignLoader(android.Manager,"UnityEngine.XR.OpenXR.OpenXRLoader",BuildTargetGroup.Android))
                throw new Exception("Could not configure OpenXR Android loader.");
            var packageSettings=AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/XR/Settings/OpenXRPackageSettings.asset");
            EditorBuildSettings.AddConfigObject("com.unity.xr.openxr.settings4",packageSettings,true);
            var openxr=OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if(!openxr) throw new Exception("OpenXR settings failed to initialize.");
            foreach(var feature in openxr.GetFeatures<UnityEngine.XR.OpenXR.Features.OpenXRFeature>()) {
                string name=feature.GetType().Name;
                feature.enabled=name=="MetaXRFeature" || name=="OculusTouchControllerProfile" || name=="MetaQuestTouchPlusControllerProfile";
                EditorUtility.SetDirty(feature);
            }
            var config=OVRProjectConfig.CachedProjectConfig;
            config.targetDeviceTypes=new System.Collections.Generic.List<OVRProjectConfig.DeviceType> { OVRProjectConfig.DeviceType.Quest3,OVRProjectConfig.DeviceType.Quest3S };
            config.anchorSupport=OVRProjectConfig.AnchorSupport.Enabled;
            config.sceneSupport=OVRProjectConfig.FeatureSupport.Required;
            config.insightPassthroughSupport=OVRProjectConfig.FeatureSupport.Required;
            config.isPassthroughCameraAccessEnabled=true;
            OVRProjectConfig.CommitProjectConfig(config);
            EditorUtility.SetDirty(xr); EditorUtility.SetDirty(openxr); AssetDatabase.SaveAssets();
        }
        [MenuItem("GuidedReality/Build Android APK")]
        public static void BuildAndroid()
        {
            Configure();
            Directory.CreateDirectory("Builds");
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes=new[]{"Assets/GuidedReality/Scenes/Keyboard.unity"}, locationPathName="Builds/GuidedReality.apk",
                target=BuildTarget.Android,options=BuildOptions.Development });
            if(result.summary.result!=BuildResult.Succeeded) throw new Exception("Android build failed; see Console.");
        }
        public static void VerifyPlayerBuild()
        {
            var player=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            player.FindProperty("microphoneUsageDescription").stringValue="GuidedReality does not request microphone input.";
            player.FindProperty("cameraUsageDescription").stringValue="GuidedReality captures a frame to locate and segment a keyboard.";
            player.ApplyModifiedPropertiesWithoutUndo();
            Directory.CreateDirectory("Builds");
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes=new[]{"Assets/GuidedReality/Scenes/Keyboard.unity"}, locationPathName="Builds/CompileCheck.app",
                target=BuildTarget.StandaloneOSX, options=BuildOptions.Development });
            if(result.summary.result!=BuildResult.Succeeded) throw new Exception("Player compile check failed");
            Debug.Log("GUIDEDREALITY_PLAYER_BUILD_OK");
        }
        [MenuItem("GuidedReality/Run geometry checks")]
        public static void VerifyGeometry()
        {
            var f=new FrameSnapshot { Width=100,Height=100,CameraPose=new Pose(new Vector3(3,2,1),Quaternion.Euler(0,30,0)),
                Ray00=new Vector3(-.5f,-.5f,1),Ray10=new Vector3(.5f,-.5f,1),Ray01=new Vector3(-.5f,.5f,1) };
            for(int y=2;y<8;y++) for(int x=2;x<8;x++) {
                var uv=new Vector2(x/10f,y/10f); var ray=f.RayAt(uv);
                var plane=new Plane(-(f.CameraPose.rotation*Vector3.forward),f.CameraPose.position+f.CameraPose.rotation*Vector3.forward);
                plane.Raycast(ray,out float d);
                f.Depth.Add(new DepthSample{Viewport=uv,Point=ray.GetPoint(d),Normal=plane.normal});
            }
            var response=new SegmentResponse { request_id=f.Id,image_width=100,image_height=100,crop_rect=new[]{20,20,60,60},png_base64="test" };
            var tex=new Texture2D(2,2); tex.SetPixels(new[]{Color.white,Color.white,Color.white,Color.white}); tex.Apply();
            var p=CropGeometry.Project(f,response,tex);
            if(Mathf.Abs(Vector3.Distance(p[0],p[1])-.6f)>.002f) throw new Exception("Crop geometry scale regression");
            if(CropGeometry.PixelToViewport(0,0,100,100)!=Vector2.up) throw new Exception("Top-left conversion regression");
            response.request_id="wrong"; bool rejected=false;
            try { response.Validate(f); } catch(Exception) { rejected=true; }
            if(!rejected) throw new Exception("Frame mismatch was not rejected");
            UnityEngine.Object.DestroyImmediate(tex);
            Debug.Log("GUIDEDREALITY_GEOMETRY_OK");
        }
    }
}
