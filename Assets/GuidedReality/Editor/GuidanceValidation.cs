using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GuidedReality.Editor
{
    public static class GuidanceValidation
    {
        static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
        [MenuItem("GuidedReality/Verify instruction pipeline")]
        public static void Run()
        {
            var doc=InstructionDocument.Parse(File.ReadAllText("Assets/3DPrinter.json"));
            Check(doc.instructions.Length==7,"3DPrinter document should contain seven steps.");
            Check(InstructionStep.TimerSeconds("0:10")==10,"Timer parsing failed.");
            bool invalid=false; try {InstructionStep.TimerSeconds("0:60");} catch {invalid=true;}
            Check(invalid,"Invalid timer seconds accepted.");
            foreach (var step in doc.instructions) {
                var clip=Resources.Load<AudioClip>(InstructionSpeech.ResourcePath(step.instruction));
                Check(clip && clip.length > 0f,"Missing bundled narration: " + step.instruction);
            }
            var frame=new FrameSnapshot {Width=1000,Height=1000,CameraPose=new Pose(new Vector3(2,1,3),Quaternion.Euler(0,45,0)),Ray00=new Vector3(-1,-1,1),Ray10=new Vector3(1,-1,1),Ray01=new Vector3(-1,1,1)};
            frame.Depth.Add(new DepthSample {Viewport=new Vector2(.5f,.5f),Point=frame.CameraPose.position+frame.CameraPose.rotation*new Vector3(0,0,2),Normal=frame.CameraPose.rotation*Vector3.back});
            var result=GuidanceVisual.CapturedPoint(frame,new Vector2(.5f,.5f),out var normal);
            Check(Vector3.Distance(result,frame.Depth[0].Point)<.0001f,"Captured pose/depth projection changed coordinate frames.");
            Check(Vector3.Dot(normal,frame.CameraPose.rotation*Vector3.forward)<0,"Surface normal should face captured camera.");
            var offCenter=GuidanceVisual.CapturedPoint(frame,new Vector2(.55f,.5f),out _);
            Check(Vector3.Distance(offCenter,frame.CameraPose.position+frame.CameraPose.rotation*new Vector3(.2f,0,2))<.0001f,"Frozen off-center ray projection failed.");
            invalid=false; try {GuidanceVisual.CapturedPoint(frame,Vector2.zero,out _);} catch {invalid=true;}
            Check(invalid,"Missing depth must not produce an invented placement.");
            foreach(var step in doc.instructions) if(step.visual_type==3 || step.visual_type==4)
                Check(Resources.Load<GameObject>("GuidanceModels/"+step.Model),"Missing model: "+step.Model);
            int count=0;
            foreach(var file in Directory.GetFiles("Assets/Resources/GuidanceModels","*.fbx")) {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(file);
                Check(prefab,"FBX could not import: "+file);
                var renderers=prefab.GetComponentsInChildren<Renderer>(true);
                Check(renderers.Length>0,"No imported geometry: "+file);
                foreach(var renderer in renderers) foreach(var material in renderer.sharedMaterials)
                    Check(material && material.shader && material.shader.isSupported,"Missing/unsupported material: "+file);
                count++;
            }
            Check(Resources.Load<Texture2D>("GuidanceModels/spark"),"Particle sprite missing.");
            foreach(var name in new[]{"OculusHand_R_modifymodel","poke","Squeez","hold","Palm","palmverticle","hook","hook1","PressWithPlam"}) {
                var hand=Resources.Load<GameObject>("GuidanceModels/"+name);
                Check(hand,"Missing hand gesture: "+name);
                Check(hand.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length==0,
                    "Baked hand must not be skinned again: "+name);
                Check(hand.GetComponentsInChildren<MeshFilter>(true).Any(m => m.sharedMesh && m.sharedMesh.vertexCount>0),
                    "Hand has no static posed mesh: "+name);
            }
            var input=new ControllerStepInput();
            Check(input.Read(true,false,Vector2.zero)==StepCommand.Load,"B must load.");
            Check(input.Read(true,false,Vector2.zero)==StepCommand.None,"Held B must not reload.");
            Check(input.Read(false,false,Vector2.right)==StepCommand.Next,"Right stick must advance.");
            Check(input.Read(false,false,Vector2.right)==StepCommand.None,"Held stick must not skip steps.");
            Check(input.Read(false,false,Vector2.left)==StepCommand.None,"Stick must return to neutral before another step.");
            input.Read(false,false,Vector2.zero);
            Check(input.Read(false,false,Vector2.left)==StepCommand.Previous,"Left stick must go back after release.");
            input.Read(false,false,Vector2.zero);
            Check(input.Read(false,false,Vector2.up)==StepCommand.None,"Vertical movement must not navigate.");
            Check(input.Read(false,true,Vector2.zero)==StepCommand.Retry,"A must retry.");
            ProjectSetup.VerifyGeometry();
            Debug.Log($"GUIDANCE_VALIDATION_OK: {doc.instructions.Length} steps, {count} converted models, frozen-depth projection, timer validation, crop geometry.");
        }
    }
}
