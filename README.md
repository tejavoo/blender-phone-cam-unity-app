# Cam Link Pro Mobile

Turns an Android phone into a virtual camera for Blender: point it at real
space, walk around, and a camera inside a Blender scene mirrors the phone's
position and rotation in real time. A second channel streams Blender's own
render of that camera back to the phone, composited over the AR passthrough
like a monitor. Built fresh per `CAMLINK_APP_SPEC.md`; the Blender side (the
`cam_link_pro` add-on) is a separate, already-tested project and is
untouched here.

Unity 6000.6.0f1, AR Foundation 6.6.2 + ARCore XR Plugin, Android only.

## Project layout

```
Assets/CamLinkPro/
  Scripts/
    Pipeline/     Pure C# pose math -- axis conversion, jump absorption,
                  origin/heading anchor, horizon levelling, angle
                  unwrapping, One Euro filtering, dolly, axis freezing.
                  No MonoBehaviour/AR Foundation dependency; unit tested.
    Networking/   UDP pose sender + TCP video/command channel (the two
                  FIXED CONTRACT wire protocols), pairing string parser.
    AR/           Thin glue reading the live AR camera pose/FOV each frame.
    Pairing/      QR scanner (ZXing.Net) + manual entry fallback.
    UI/           Runtime-built HUD (rig presets, freeze toggles,
                  steadiness, zoom, dolly, record flow) and the video
                  compositor (monitor overlay).
    App/          CamLinkSessionController -- the top-level orchestrator
                  tying pipeline + both network channels + record state
                  machine together.
  Scenes/Main.unity   The one scene: AR Session, XR Origin (AR Camera),
                      VideoOverlay canvas, SessionController, HUD, EventSystem.
  Tests/EditMode/     33 NUnit tests over the pose pipeline, packet
                      formatter, and pairing parser -- run from
                      Window > General > Test Runner > EditMode, or
                      `unity test <project> --mode EditMode`.
```

## Building

Android Build Support (with SDK/NDK/OpenJDK) must be installed via Unity
Hub. Player Settings are already configured: package id
`com.camlinkpro.mobile`, min SDK 29 (required for ARCore + Vulkan), IL2CPP,
ARM64. Build via **File > Build Settings > Build**, or:

```
unity build /Users/ajithgonamanda/CamLinkPro --target Android
```

## Third-party code

- **ZXing.Net** 0.16.9 (`Assets/Plugins/ZXing/zxing.dll`), Apache-2.0,
  fetched directly from the official NuGet package
  (nuget.org/packages/ZXing.Net) -- not the `zxing.unity_x86.zip` that was
  sitting in Downloads, which came from a third-party DLL-hosting site and
  wasn't trusted. Used only to decode the pairing QR from a WebCamTexture
  frame; see `QRPairingScanner.cs`.

## What's deliberately not done yet

Per the spec's staged build order, this pass gets the whole app wired
end-to-end and building for Android, but a few things only a physical
device/session can really prove out: on-device ARCore tracking quality,
real QR scanning under room lighting, and a live pairing run against the
Blender add-on. Test each of those on your own machine/phone next, per the
spec's own "verify at each stage" guidance.
