using System;
using System.Collections.Generic;
using UnityEngine;

namespace GuidedReality
{
    // Owns each step's generated objects/materials; all coordinates derive from the frozen capture.
    public sealed class GuidanceVisual : MonoBehaviour
    {
        readonly List<Material> materials = new List<Material>();
        Transform moving;
        Vector3 start, end, left, right, top, bottom;
        string motion;
        float began, countdown;
        TextMesh timer;
        Quaternion initialRotation;
        Material Template;
        Material MakeMaterial(Color color)
        {
            var m = new Material(Template); m.mainTexture = Texture2D.whiteTexture; m.color = color;
            materials.Add(m); return m;
        }
        public static Vector3 CapturedPoint(FrameSnapshot frame, Vector2 uv, out Vector3 normal)
        {
            float best = float.PositiveInfinity; DepthSample nearest = default; bool found = false;
            foreach (var s in frame.Depth) {
                float distance = (s.Viewport - uv).sqrMagnitude;
                if (distance < best && Vector3.Distance(s.Point, frame.CameraPose.position) < 5f) { best = distance; nearest = s; found = true; }
            }
            if (!found || best > .0125f) throw new Exception("No captured depth near this component. Move closer and press A to retry.");
            var forward = frame.CameraPose.rotation * Vector3.forward;
            float z = Vector3.Dot(nearest.Point - frame.CameraPose.position, forward);
            if (float.IsNaN(z) || float.IsInfinity(z) || z < .1f) throw new Exception("Invalid captured depth.");
            var ray = frame.Ray00 + uv.x * (frame.Ray10-frame.Ray00) + uv.y * (frame.Ray01-frame.Ray00);
            normal = nearest.Normal.sqrMagnitude > .1f ? nearest.Normal.normalized : -forward;
            if (Vector3.Dot(normal, forward) > 0) normal = -normal;
            return frame.CameraPose.position + frame.CameraPose.rotation * (ray*z);
        }
        Vector3 Local(FrameSnapshot f, Vector2 uv) => transform.InverseTransformPoint(CapturedPoint(f, uv, out _));
        public void Build(InstructionStep step, FrameSnapshot frame, Detection detection, Material template)
        {
            Template = template;
            float x0=detection.box_2d[1]/1000f, x1=detection.box_2d[3]/1000f;
            float y0=1-detection.box_2d[2]/1000f, y1=1-detection.box_2d[0]/1000f;
            var uv = new Vector2((x0+x1)*.5f,(y0+y1)*.5f);
            Vector3 world = CapturedPoint(frame, uv, out Vector3 normal);
            Vector3 center = transform.InverseTransformPoint(world);
            Quaternion face = Quaternion.Inverse(transform.rotation) * frame.CameraPose.rotation;
            began = Time.time;
            if (step.visual_type == 1) {
                // iOS uses particles for small targets and an outline for larger ones.
                if (x1-x0 < .28f || y1-y0 < .28f) {
                    var go = new GameObject("Target sparkles"); go.transform.SetParent(transform,false); go.transform.localPosition=center;
                    var ps=go.AddComponent<ParticleSystem>(); ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main=ps.main; main.startLifetime=2.5f; main.startSpeed=new ParticleSystem.MinMaxCurve(-.025f,.025f); main.startSize=.0032f;
                    main.startColor=Color.white; main.maxParticles=300; main.simulationSpace=ParticleSystemSimulationSpace.Local;
                    var emission=ps.emission; emission.rateOverTime=100;
                    var shape=ps.shape; shape.shapeType=ParticleSystemShapeType.Sphere; shape.radius=.005f;
                    var color=ps.colorOverLifetime; color.enabled=true;
                    var gradient=new Gradient(); gradient.SetKeys(new[]{new GradientColorKey(new Color(1,.976f,0),0),new GradientColorKey(new Color(.822f,.452f,.839f),.78f),new GradientColorKey(new Color(.338f,.45f,.872f),1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(.125f,1)}); color.color=gradient;
                    var size=ps.sizeOverLifetime; size.enabled=true; size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,1),new Keyframe(1,2.25f)));
                    var particleMaterial=MakeMaterial(Color.white); particleMaterial.mainTexture=Resources.Load<Texture2D>("GuidanceModels/spark"); particleMaterial.SetFloat("_UseVertexColor",1);
                    ps.GetComponent<ParticleSystemRenderer>().sharedMaterial=particleMaterial; ps.Play();
                } else {
                    var line=new GameObject("Target outline").AddComponent<LineRenderer>(); line.transform.SetParent(transform,false);
                    line.useWorldSpace=false; line.loop=true; line.positionCount=4; line.widthMultiplier=.004f;
                    line.sharedMaterial=MakeMaterial(Color.cyan);
                    line.SetPositions(new[]{Local(frame,new Vector2(x0,y0)),Local(frame,new Vector2(x1,y0)),Local(frame,new Vector2(x1,y1)),Local(frame,new Vector2(x0,y1))});
                }
            } else if (step.visual_type == 5) {
                timer=MakeText(transform,"Countdown", "", .018f);
                timer.transform.localPosition=transform.InverseTransformPoint(world+Vector3.up*.1f);
                timer.transform.localRotation=face; countdown=InstructionStep.TimerSeconds(step.Action); UpdateTimer();
            } else {
                moving=Model(step.Model, step.visual_type==3 ? .20f : .18f);
                moving.localPosition=center; moving.localRotation=face;
                if (step.visual_type==4) {
                    // Tool's up axis follows the captured surface normal, as in the Swift renderer.
                    Vector3 forward=Vector3.ProjectOnPlane(frame.CameraPose.rotation*Vector3.up,normal);
                    if (forward.sqrMagnitude>.001f) moving.rotation=Quaternion.LookRotation(forward.normalized,normal);
                    motion=step.Action;
                    top=Local(frame,new Vector2(uv.x,y1)); bottom=Local(frame,new Vector2(uv.x,y0));
                    left=Local(frame,new Vector2(x0,uv.y)); right=Local(frame,new Vector2(x1,uv.y));
                    start=motion=="left and right" ? left : top; end=motion=="left and right" ? right : bottom;
                }
                initialRotation=moving.localRotation;
            }
        }
        Transform Model(string name, float size)
        {
            var prefab=Resources.Load<GameObject>("GuidanceModels/"+name);
            if (!prefab) throw new Exception("Missing converted model: "+name);
            var holder=new GameObject(name+" placement"); holder.transform.SetParent(transform,false);
            var model=Instantiate(prefab,holder.transform,false); model.transform.localPosition=Vector3.zero;
            // Neutralize imported cameras/lights; retain mesh, skin and animation data.
            foreach (var c in model.GetComponentsInChildren<Camera>(true)) c.enabled=false;
            foreach (var l in model.GetComponentsInChildren<Light>(true)) l.enabled=false;
            var lightObject=new GameObject("Guidance model fill light"); lightObject.transform.SetParent(holder.transform,false);
            lightObject.transform.localPosition=new Vector3(0,.25f,-.3f);
            var fill=lightObject.AddComponent<Light>(); fill.type=LightType.Point; fill.range=1.25f; fill.intensity=1.5f; fill.shadows=LightShadows.None;
            var renderers=model.GetComponentsInChildren<Renderer>();
            if(renderers.Length==0) throw new Exception("Model contains no renderers: "+name);
            Bounds b=renderers[0].bounds; foreach(var r in renderers) b.Encapsulate(r.bounds);
            Vector3 offset=holder.transform.InverseTransformPoint(b.center);
            float scale=size/Mathf.Max(.0001f,Mathf.Max(b.size.x,Mathf.Max(b.size.y,b.size.z)));
            model.transform.localPosition-=offset;
            model.transform.localScale*=scale; model.transform.localPosition*=scale;
            return holder.transform;
        }
        public void AddCropArrow(InstructionStep step, Vector3 centerWorld, Quaternion cameraRotation, float width, Material template)
        {
            if (!step.Action.StartsWith("flip")) return;
            Template=template;
            var arrow=Model("flipUp",Mathf.Clamp(width*.25f,.06f,.25f));
            float imagePlaneAngle = step.Action switch {
                "flip up" => 0f,
                "flip down" => 180f,
                "flip left" => 90f,
                "flip right" => -90f,
                _ => throw new Exception("Unknown flip direction: " + step.Action)
            };
            arrow.position=centerWorld;
            // Preserve the verified Up facing, then turn in the captured camera's image plane.
            arrow.rotation=cameraRotation * Quaternion.AngleAxis(imagePlaneAngle, Vector3.forward)
                * Quaternion.Euler(0,180,0);

            // Keep the entire model on the camera-facing side of the captured crop plane.
            // A pivot-only offset is insufficient for a thick/curved arrow.
            Vector3 forward=cameraRotation * Vector3.forward;
            var renderers=arrow.GetComponentsInChildren<Renderer>();
            float farthest=float.NegativeInfinity;
            foreach(var renderer in renderers) {
                Bounds bounds=renderer.bounds;
                float radius=Mathf.Abs(forward.x)*bounds.extents.x
                    + Mathf.Abs(forward.y)*bounds.extents.y + Mathf.Abs(forward.z)*bounds.extents.z;
                farthest=Mathf.Max(farthest,Vector3.Dot(bounds.center-centerWorld,forward)+radius);
                // Transparent meshes must consistently composite after the crop. Keep depth testing.
                var shared=renderer.sharedMaterials;
                bool changed=false;
                for(int i=0;i<shared.Length;i++) {
                    if(!shared[i] || shared[i].renderQueue < (int)UnityEngine.Rendering.RenderQueue.Transparent) continue;
                    var instance=new Material(shared[i]);
                    instance.renderQueue=Mathf.Max(template.renderQueue+10,(int)UnityEngine.Rendering.RenderQueue.Transparent+10);
                    materials.Add(instance); shared[i]=instance; changed=true;
                }
                if(changed) renderer.sharedMaterials=shared;
            }
            float clearance=Mathf.Max(0,farthest+.01f);
            arrow.position-=forward*clearance;
            Debug.Log($"GuidedReality: Arrow separated from crop by 1 cm clearance (offset {clearance*100:F1} cm).");
        }
        public static TextMesh MakeText(Transform parent,string name,string text,float size)
        {
            var go=new GameObject(name); go.transform.SetParent(parent,false);
            var label=go.AddComponent<TextMesh>(); label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); go.GetComponent<MeshRenderer>().sharedMaterial=label.font.material; label.text=text; label.fontSize=96; label.characterSize=size*.2f;
            label.anchor=TextAnchor.MiddleCenter; label.alignment=TextAlignment.Center; label.color=Color.white;
            return label;
        }
        void UpdateTimer()
        {
            int remaining=Mathf.CeilToInt(Mathf.Max(0,countdown-(Time.time-began)));
            timer.text=$"{remaining/60:00}:{remaining%60:00}";
            if(remaining==0) { Debug.Log("GuidedReality: Countdown complete."); timer.gameObject.SetActive(false); timer=null; }
        }
        void Update()
        {
            if(timer) UpdateTimer();
            if(!moving || string.IsNullOrEmpty(motion)) return;
            float elapsed=Time.time-began;
            if(motion=="up and down" || motion=="left and right") moving.localPosition=Vector3.Lerp(start,end,Mathf.PingPong(elapsed/2,1));
            else if(motion=="rotate") {
                var points=new[]{left,bottom,right,top}; int i=Mathf.FloorToInt(elapsed)%4;
                moving.localPosition=Vector3.Lerp(points[i],points[(i+1)%4],elapsed-Mathf.Floor(elapsed));
            } else if(motion=="clockwise" || motion=="counterclockwise")
                moving.localRotation=initialRotation*Quaternion.Euler(0,(motion=="clockwise"?1:-1)*elapsed*90,0);
        }
        void OnDestroy() { foreach(var m in materials) if(m) Destroy(m); }
    }
}
