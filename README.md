# Guided Reality Unity

An experimental Meta Quest 3 / 3S guidance app. The headset captures a passthrough
camera frame and depth samples, asks Gemini to locate an instruction's target,
and places guidance in the physical scene. Segmentation requests go to a separate
SAM-2 GPU service. Gemini requests go directly from the headset.

## Requirements

- Unity **6000.3.19f1**, with Android Build Support, SDK, NDK, and OpenJDK.
- Quest 3 or 3S with developer mode, camera/spatial permissions, and compatible
  Horizon OS for Meta XR SDK 85.
- Your own Gemini API key and reachable SAM-2 endpoint/bearer token.

Unity restores the pinned Meta XR, MRUK, and OpenXR packages from `Packages/`.
The repository includes source, project settings, converted models, and bundled
audio. Unity caches, builds, credentials, and model checkpoints are excluded.

## Open and run

1. Clone this repository and open its root in Unity Hub.
2. Open `Assets/GuidedReality/Scenes/Keyboard.unity`.
3. Open **GuidedReality → Connection Settings**. Set your Gemini model/key and
   SAM endpoint/token, then save. An empty credential example is provided in
   `Assets/StreamingAssets/guidedreality.example.json`.
4. Configure Android using **GuidedReality → Configure Quest build** and review
   XR Project Validation. Build and install on your headset.
5. Allow camera and spatial data access. Look around to initialize depth before
   processing an instruction.

The local settings file is ignored by Git but included in development builds.
Do not distribute an APK containing your credentials. This prototype is not a
production credential-distribution solution.

## Controls

- Right **B** loads `Assets/3DPrinter.json` and starts step 1.
- Right joystick **left/right** selects the previous/next step. Return the stick
  to center between pushes. Navigation waits until current processing finishes.
- Right **A** retries the current step; before loading instructions, it runs the
  keyboard capture demo.

Each instruction plays bundled English narration. Playback requires no on-device
TTS service or speech API. Regenerate after changing instruction text, on macOS:

```bash
python3 Tools/generate_narration.py
```

Rebuild the app after changing bundled settings, instructions, or narration.

## Guidance types

| `visual_type` | Guidance |
| --- | --- |
| 1 | Target particles or outline |
| 2 | Blue-tinted SAM crop, with an optional flip arrow |
| 3 | Hand gesture |
| 4 | Tool model and optional movement |
| 5 | Countdown |

Placement uses the image's saved camera pose, projection rays, and sampled depth.
The default debug depth grid is 81 × 61 samples; it is not the sensor's native
resolution. Captures are saved under the app's persistent `logs` directory.

```bash
adb pull /sdcard/Android/data/com.ada.guidedreality/files/logs ./quest-logs
```

## Remote segmentation

This Unity client requires a separately deployed SAM-2 service. Machine-specific
deployment scripts, checkpoints, SSH configuration, and private addresses are
not included in this repository.

The service exposes authenticated `GET /health` and `POST /segment`. Requests
contain a JPEG and normalized top-left YXYX bounding box (0–1000). Responses
contain a tight transparent PNG plus pixel XYWH crop coordinates in the original
image. Only visual type 2 needs the SAM service.

For direct headset access, connect the headset to the same private network/VPN
as the service. Use its reachable address as the SAM endpoint. For USB development,
create an SSH tunnel from Mac port 8765 to your service and run
`adb reverse tcp:8765 tcp:8765`. The headset then uses
`http://127.0.0.1:8765` while both connections remain open.

ssh alienware-4090 'gpu-status'

ssh alienware-4090 'tmux new-session -d -s ada-guidedreality-sam "bash /home/adazhao/guidedreality-sam/start_persistent.sh >> /home/adazhao/guidedreality-sam/resident.log 2>&1"'

ssh alienware-4090 'tmux kill-session -t ada-guidedreality-sam'


## Models and checks

Models are loaded from `Assets/Resources/GuidanceModels`. Hand gestures are static
posed meshes: skinning is baked once and removed before FBX export to prevent
double deformation. To regenerate from your original USDZ assets:

```bash
blender --background --python Tools/convert_guidance_models.py -- \
  /path/to/source-usdz Assets/Resources/GuidanceModels --hands-only
```

Keep the existing `.meta` files. The original USDZ sources are not included.

Run **GuidedReality → Verify instruction pipeline** in Unity to check guidance
assets, narration, frozen-depth placement, controller navigation, and crop geometry.

Physical camera access, depth quality, anchoring, audio, and final appearance need
testing on a Quest headset. Guidance is based on a captured scene and does not
track objects that move after capture.
