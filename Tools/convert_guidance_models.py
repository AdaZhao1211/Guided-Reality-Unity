import bpy, pathlib, tempfile, zipfile
from pxr import Usd, UsdSkel, UsdShade, UsdGeom
import sys
args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
if len(args) not in (2,3): raise SystemExit('Usage: blender --background --python convert_guidance_models.py -- IOS_ASSET_DIRECTORY UNITY_MODEL_DIRECTORY [--hands-only]')
hands={'OculusHand_R_modifymodel','poke','Squeez','hold','Palm','palmverticle','hook','hook1','PressWithPlam'}
src=pathlib.Path(args[0])
out=pathlib.Path(args[1])
out.mkdir(parents=True,exist_ok=True)
for path in src.glob('*.usdz'):
    if len(args)==3 and args[2]=='--hands-only' and path.stem not in hands: continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    try:
        # Legacy SceneKit USDs omit applied API metadata. Repair only the extracted copy.
        with tempfile.TemporaryDirectory() as temporary:
            with zipfile.ZipFile(path) as archive:
                archive.extractall(temporary)
                root=pathlib.Path(temporary)/archive.namelist()[0]
            stage=Usd.Stage.Open(str(root))
            for prim in stage.Traverse():
                if prim.HasAttribute('primvars:skel:jointIndices'): UsdSkel.BindingAPI.Apply(prim)
                if prim.HasRelationship('material:binding'): UsdShade.MaterialBindingAPI.Apply(prim)
            if not UsdSkel.BakeSkinning(stage.Traverse()): raise RuntimeError('USD skinning bake failed')
            if path.stem in hands:
                # BakeSkinning changes points but retains binding metadata. Importing
                # those bindings makes Blender deform the already-posed hand again.
                # These gesture assets are still poses, so retain only baked geometry.
                for prim in stage.Traverse():
                    for prop in list(prim.GetProperties()):
                        if str(prop.GetName()).startswith(('skel:', 'primvars:skel:')):
                            prim.RemoveProperty(prop.GetName())
                    if prim.HasAPI(UsdSkel.BindingAPI): prim.RemoveAPI(UsdSkel.BindingAPI)
                skeletons=[p.GetPath() for p in stage.Traverse() if p.IsA(UsdSkel.Skeleton) or p.IsA(UsdSkel.Animation)]
                for skeleton in skeletons: stage.RemovePrim(skeleton)
            stage.GetRootLayer().Save()
            bpy.ops.wm.usd_import(filepath=str(root))
            # Pack texture data before the extracted archive is removed.
            bpy.ops.file.pack_all()
            meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
            if not meshes: raise Exception('No mesh imported')
            if path.stem in hands:
                if any(o.type=='ARMATURE' for o in bpy.context.scene.objects) or any(m.type=='ARMATURE' for o in meshes for m in o.modifiers):
                    raise RuntimeError('Baked gesture still has a skeleton')
            bpy.ops.export_scene.fbx(filepath=str(out/(path.stem+'.fbx')),use_selection=False,object_types={'MESH','ARMATURE','EMPTY'},bake_anim=True,path_mode='COPY',embed_textures=True,axis_forward='-Z',axis_up='Y')
            print('CONVERTED',path.name,len(meshes),flush=True)
    except Exception as e:
        print('FAILED',path.name,str(e),flush=True)
        raise
