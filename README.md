# Cam Link Pro Mobile

Turns an Android phone into a virtual camera for Blender: point it at real
space, walk around, and a camera inside a Blender scene mirrors the phone's
position and rotation in real time. A second channel streams Blender's own
render of that camera back to the phone, composited over the AR passthrough
like a monitor. The Blender side (the `cam_link_pro` add-on) is a separate,
already-tested project and is untouched here.

Unity 6000.0.84f1, AR Foundation 6.6.2 + ARCore XR Plugin, Android only.

## Version 2 — UI Toolkit rewrite

This is a ground-up rewrite of the original uGUI app on **UI Toolkit**
(UXML/USS), with a proper screen-navigation shell instead of one big scene.
Highlights over v1:

- `AppShell` drives navigation between screens (Landing, Scan QR, Manual
  Entry, AR Preflight, Recording HUD, Settings) via a single `IScreenController`
  interface per screen; Settings remembers which screen it was opened from
  and returns there.
- The pose pipeline (axis conversion, One Euro filtering, horizon levelling,
  jump/teleport absorption, origin/heading anchoring, dolly offset,
  per-axis freezing) is carried over from the proven v1 implementation.
- Fixes a v1 bug where AR pose never reached Blender: the AR camera had no
  `TrackedPoseDriver`, so ARCore initialized the session but never wrote a
  tracked pose onto the camera transform.
- A live on-HUD diagnostics overlay (translucent box, small green terminal
  text) shows tracking state, raw/Blender-space position and rotation,
  packet counts, and record state — each field toggleable from
  Settings → Diagnostics, for troubleshooting a pairing on-device.
- Custom pointer-driven controls (e.g. the HUD's vertical zoom rail) replace
  UI Toolkit's stock controls where they don't render well at phone density.

## Project layout

```
Assets/CamLinkPro/
  Runtime/
    Pipeline/     Pure C# pose math -- axis conversion, jump absorption,
                  origin/heading anchor, horizon levelling, angle
                  unwrapping, One Euro filtering, dolly, axis freezing.
                  No MonoBehaviour/AR Foundation dependency.
    Networking/   UDP pose sender + TCP video/command channel (the two
                  FIXED CONTRACT wire protocols), pairing string parser.
    Ar/           Thin glue reading the live AR camera pose/FOV each frame.
    Qr/           QR payload decoding (ZXing.Net) for the pairing string.
    Calibration/  Per-axis flip/remap gain, rig presets, zoom state.
    Preferences/  PlayerPrefs-backed settings (AppPrefs).
    UI/           AppShell navigation, ThemeService (font scaling), the
                  per-screen controllers, and custom controls (e.g.
                  VerticalDragControl).
  Resources/UI/Screens/   UXML for each screen; Theme.uss for shared styling.
  Editor/         Headless project/scene bootstrap and CLI build tools
                  (SceneBootstrap, ProjectBootstrap, XrConfig, CliBuildTools)
                  -- the project is built and configured entirely from
                  editor scripts, no manual scene editing required.
  Tests/          NUnit test assembly scaffold (no tests written yet).
```

## Building

Android Build Support (with SDK/NDK/OpenJDK) must be installed via Unity
Hub. Player Settings are configured via `Editor/XrConfig.cs` and
`Editor/ProjectBootstrap.cs`: package id `com.camlinkpro.mobilev2`, min SDK
29 (required for ARCore), IL2CPP, ARM64, OpenGLES3 (UI Toolkit runtime
panels have known attach/render issues under Vulkan on Android). Build via
**File > Build Settings > Build**, or headless:

```
Unity -batchmode -nographics -quit -projectPath <path> \
  -executeMethod CamLinkPro.Editor.CliBuildTools.BuildAndroid
```

## Third-party code

- **ZXing.Net** 0.16.9 (`Assets/CamLinkPro/Plugins/ZXing/zxing.dll`),
  Apache-2.0, from the official NuGet package (nuget.org/packages/ZXing.Net).
  Used only to decode the pairing QR from a camera frame; see
  `Assets/CamLinkPro/Runtime/Qr/QrDecoder.cs`.

## What's deliberately not done yet

- No automated test coverage over the pose pipeline yet (v1 had NUnit
  coverage here; carrying that forward is a good next step).
- Sensor height, lens distortion, stream quality, and frame rate are
  reserved placeholders in Settings — not wired up.
- Focal length/sensor width shown in Settings are still fixed values, not
  read from the device's actual camera intrinsics.
