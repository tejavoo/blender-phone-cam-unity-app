# Cam Link Pro Mobile — build spec for Claude Code (Unity MCP)

Hand this whole file to Claude Code. It describes what the app must do and
the exact wire contracts it must speak — not how to structure the Unity
project internally. Let it make its own implementation decisions; only the
sections marked **FIXED CONTRACT** are non-negotiable, because the other
half of this system (a Blender add-on) already exists, is tested, and
must not be touched.

## Start genuinely fresh

The previous Unity project is corrupted/unstable enough to abandon rather
than repair. Create a brand new Unity project rather than opening the old
one. Nothing from the old project needs to be reused or migrated — this
spec is the complete replacement for it.

**Build incrementally, verify at each stage**, rather than writing
everything and testing once at the end:
1. Empty AR Foundation project that just shows camera passthrough on a
   phone — confirm this runs before adding anything else.
2. Add the network layer, verify with a fake/mock server before touching
   the real Blender add-on.
3. Add the pose pipeline, verify the numbers look sane in an on-screen
   debug readout before wiring it to the network layer.
4. Add the HUD controls one group at a time.
5. Add QR scanning and video compositing last — they're the least
   critical to core function and the most likely to have
   platform-specific friction.

Get a working, ugly version at each stage before making it good. A big-bang
rebuild is how the previous attempt likely ended up unstable.

---

## What this app is

A handheld Android phone app that turns the phone into a virtual camera
for Blender. You point the phone at real space, walk around with it, and
a camera inside a Blender scene mirrors your exact position and rotation
in real time — for blocking shots, previz, and eventually recording actual
camera moves as keyframes. A second channel streams Blender's own render
of that camera back to the phone, composited translucently over the AR
camera passthrough, so you can see the virtual shot and the real room at
once, like a monitor.

The receiving side (a Blender add-on called Cam Link Pro) is done and
already tested. This app is the only thing being built.

---

## FIXED CONTRACT 1 — Pose packets (UDP, phone → Blender)

The phone sends one UDP packet per pose update to the IP and port
obtained during pairing (see Contract 3). Fire-and-forget — no
acknowledgement, no retry, no delivery guarantee. A dropped packet is
simply the next one arriving slightly later; don't build any reliability
layer on top of this.

**Format:** a single line of ASCII text, comma-separated, no spaces, nine
fields in this exact order:

```
sequence_id,pos_x,pos_y,pos_z,rot_x,rot_y,rot_z,focal_length_mm,sensor_width_mm
```

- `sequence_id` — a non-negative integer. Must be monotonically
  increasing across the life of the connection (the receiver discards
  any packet whose id is not greater than the last one it applied). Do
  not restart it at 0 after a reconnect — derive it from a
  monotonically increasing source such as elapsed milliseconds, so a
  dropped-and-resumed connection never sends a lower id than one
  already applied.
- `pos_x/y/z` — position in **Blender's coordinate space** (right-handed,
  Z-up), in metres, five decimal places.
- `rot_x/y/z` — rotation in **Blender's XYZ Euler order** (composes as
  R = Rz · Ry · Rx), in **degrees**, five decimal places.
- `focal_length_mm`, `sensor_width_mm` — floats, five decimal places.

Send at whatever rate the pose pipeline produces frames (see the pose
pipeline section) — nominally 24–60Hz, user-configurable. Faster than
60Hz gains nothing: the receiver only processes one packet per its own
60Hz tick and keeps the newest.

## FIXED CONTRACT 2 — Video + command channel (TCP, both directions)

A single persistent TCP connection to the IP and (separate) port obtained
during pairing. Two kinds of traffic share this one connection:

**On connect**, the first thing the app sends is:
```
AUTH <token>\n
```
using the token from the pairing QR (see Contract 3).

**Commands the app sends** (newline-terminated ASCII, any time after
connecting):
- `START\n` — begin recording on the Blender side.
- `STOP\n` — stop recording.
- `LOCK_START\n` — arm: the current pose becomes the frame-one reference.
- `PING\n` — liveness check.

**Status lines the app receives** (newline-terminated ASCII, sent by
Blender in response to the above or on its own initiative):
- `REC_ON`, `REC_OFF`, `ARMED`, `PONG`.

**Video frames the app receives**, interleaved on the same connection:
a 4-byte big-endian unsigned integer giving the byte length of a JPEG
image, immediately followed by exactly that many bytes of JPEG data. No
other framing or delimiter.

**Telling a status line apart from a frame:** peek the first byte. A
length prefix's first byte is always `0` in practice (frames are well
under 16MB), while every status word is ASCII text starting with a
printable character. Read a status line by accumulating bytes to the next
`\n`; read a frame by treating the first 4 bytes as the length prefix.

**Expect roughly 10–25fps** on this video channel, not 60 — the
bottleneck is Blender's own render+encode time, not the network. Frames
may arrive at an irregular interval; always display the newest one
received rather than queuing.

**If the connection drops**, reconnect with a short fixed backoff
(around 1–2 seconds between attempts) and re-send the `AUTH` line on
each new connection. Pose packets (Contract 1) are independent UDP and
don't need this connection to be up — keep sending pose even while the
video/command channel is reconnecting, so tracking never visibly pauses
just because the monitor did.

## FIXED CONTRACT 3 — Pairing via QR

Blender's add-on panel has a "Show Pairing QR" button that displays a QR
code encoding a string in this exact format:

```
camlink://<ip>:<pose_port>:<video_port>?t=<token>
```

Example: `camlink://192.168.1.42:5005:5006?t=9f3a7c21`

The app must be able to scan this QR (device camera, any QR-reading
approach) and parse it into the four values needed to open both
connections above. A string that doesn't match this exact shape should be
silently ignored (rather than erroring), since a stray QR code in view of
the phone that isn't a Cam Link Pro pairing code should not crash or
confuse the pairing flow. Also provide a manual-entry fallback (typing IP
and both ports) in case scanning isn't practical in a given room.

---

## The pose pipeline (this is the core of the app — get this right)

Raw AR tracking data is not sendable to Blender as-is. It needs to pass
through several transformations, in this order, every frame:

### 1. Axis conversion (AR space → Blender space)
AR tracking (ARCore) reports pose in a right-handed, **Y-up** world.
Blender is right-handed, **Z-up**. Converting one to the other is a
fixed 90° rotation about the X axis, applied to both the position and
the orientation.

### 2. Jump absorption
AR tracking occasionally "teleports" — loses its fix on the room and
re-anchors somewhere slightly different. A single frame moving more than
roughly 0.35m is not something a human hand can do; treat any such jump
as a tracking artifact, not real motion. When detected, shift the
session's origin by exactly the size of the jump, so the Blender camera
does not visibly move at all when this happens — the jump becomes
invisible to the shot, not corrected-after-the-fact.

### 3. Origin and heading anchor
The first good frame after a reset defines both where Blender's (0,0,0)
is, and which real-world compass direction counts as Blender's +Y
("forward"). Every subsequent frame's position and rotation are
expressed relative to that anchor. "Reset origin" (a user action, see
HUD section) re-anchors both.

### 4. Horizon levelling (toggleable, default on)
Blender's XYZ Euler rotation order has an unstable point at 90° of roll
— which is exactly the orientation a phone is in during a normal
landscape grip. Small hand tremor near that orientation produces large,
wrong swings in the computed rotation once it reaches Blender. The fix:
reconstruct the camera's orientation with its "up" vector forced to
match world-up, discarding roll, rather than passing roll through
unmodified. This keeps normal handheld shooting far away from that
unstable point entirely. When disabled, roll passes through unmodified
(for intentional dutch angles), accepting the instability near 90°.

### 5. Angle continuity (unwrapping)
Rotation angles naturally wrap at ±180°. A camera panning past that
boundary must read as a continuously increasing/decreasing number (e.g.
179° → 181° → 183°), not jump to -179°. Track the previous frame's
angles and add/subtract 360° as needed to keep each axis continuous
frame to frame.

### 6. Adaptive steadying (One Euro filter)
A plain fixed-blend smoothing filter (old value × k + new value ×
(1-k)) forces a choice between shaky-but-responsive and
smooth-but-laggy — it can't be both, because it treats a slow drift and
a fast intentional move identically. Use a **One Euro filter**: it
filters heavily when the signal is barely changing (killing hand
tremor) and opens up automatically the instant real motion starts
(keeping lag low during an actual move). Apply it independently to each
of the 6 channels (3 position, 3 rotation) after step 5. Expose a single
"Steadiness" 0–1 slider to the user; internally this should map to the
filter's minimum cutoff frequency (roughly 6Hz at Steadiness=0 down to
~0.3Hz at Steadiness=1) with a fixed beta (speed-adaptivity) constant of
around 4.0, which is far more useful than exposing raw filter
parameters directly.

### 7. Dolly offset
A separate, user-controlled push/pull distance (metres) applied along
the camera's *own current forward direction* — not world space. This is
physically a dolly move: walking forward vs. the dolly control should
feel like the same kind of motion, just one driven by your feet and one
by a UI control, and they should combine (you can walk while also
holding a dolly offset). This is distinct from optical zoom (below),
which changes focal length instead of position — the two must be
separate controls, not conflated.

### 8. Axis freezing (per-channel locks + rig presets)
Any of the 6 channels (X/Y/Z position, pan/tilt/roll rotation) can be
individually frozen by the user. A frozen channel holds exactly the
value it had at the moment it was frozen (not zero, not some default) —
so a user can walk to a height they like, freeze it, and keep shooting
at that fixed height. Re-freezing (or changing rig preset) re-captures
the hold value at that new moment.

Provide four one-tap rig presets built from these locks:
- **Handheld** — nothing frozen.
- **Tripod** — all three position channels frozen (pan/tilt free).
- **Dolly** — only height (Z) frozen.
- **Crane** — X and Y frozen, height free.

Freezing must happen in the app, before a packet is ever sent — do not
rely on anything in Blender to enforce a freeze, since Blender's own
transform locks only block manual mouse editing and do nothing against
values arriving over the network.

### Ordering and warm-up
Apply these steps in the order listed above (axis conversion → jump
absorption → origin/heading anchor → horizon levelling → unwrapping →
filtering → dolly offset → freezing) every frame. Don't send any pose
packets for the first ~20 frames after a reset (origin reset, rig
change, or reconnect) — this is "finding the room" warm-up time, and
packets sent before tracking has stabilized are worse than a brief
pause. Also suppress sending while the AR system itself reports the
pose as unreliable/emulated (i.e. genuinely lost, not just noisy) —
resume once real tracking data returns, restarting the warm-up count.

---

## HUD (on-screen during a live AR session)

- **Rig preset buttons** (4): Handheld / Tripod / Dolly / Crane, as
  defined above.
- **Per-axis freeze toggles** (6): independent of rig presets, so a user
  can start from a preset and then hand-tune it.
- **Steadiness slider** (0–1), as defined above.
- **Optical zoom**: a +/- control (press-and-hold to repeat) adjusting
  focal length between roughly 10mm and 300mm. Should read live from the
  AR system's actual field of view by default (so the Blender lens
  matches the phone's real field of view), with a manual override mode
  that locks to a fixed focal length instead.
- **Dolly push**: a separate +/- control for the dolly offset defined
  above, clearly distinguished in the UI from optical zoom (these are
  easy to confuse and are NOT the same control).
- **Reset origin** button: re-anchors position and heading to the
  current pose, re-triggers warm-up.
- **Opacity slider**: for the video monitor overlay (see Video
  Compositing section).
- **Record flow**, with three distinct states:
  1. **Live/preview** — tracking is active, nothing is being recorded.
  2. **Armed** — user tapped "Lock Start"; this sends `LOCK_START` and
     the app shows a clear "armed" indicator. Nothing records yet.
  3. **Recording** — after tapping Record, run a ~2 second on-screen
     countdown *before* sending `START`, so the first recorded frame
     isn't the wobble of the user's thumb hitting the button. A visible
     "Stop" control sends `STOP` and returns to live/preview.
  The app must also react correctly if these state changes are
  triggered from the *Blender side* instead of the phone (i.e. someone
  starts recording from the Blender panel directly) — the received
  `REC_ON`/`REC_OFF`/`ARMED` status lines are the source of truth for
  what state to display, not just the app's own button presses.

## Video compositing (the "virtual camera monitor")

Received JPEG frames (Contract 2) are displayed as a layer on top of the
AR camera's own real-time passthrough, at an opacity the user controls
(0 = only the real world visible, 1 = only Blender's render visible,
between = both blended). If no new frame has arrived in roughly 1.5
seconds, fade the overlay back toward fully transparent automatically,
so a stalled connection doesn't leave a frozen, misleading frame stuck
on screen.

## QR pairing scan

A scan mode that reads the pairing QR (Contract 3) using the device
camera, parses it, and on success immediately proceeds to open both
connections (Contracts 1 and 2) and begin tracking. Provide a manual
text-entry fallback for IP/ports if scanning fails or isn't practical.

---

## Non-functional requirements

- **Android only**, using AR Foundation + ARCore (this project has no
  iOS ambitions — Apple doesn't expose position tracking to WebXR/most
  third-party AR frameworks the way ARCore does, so iOS was deliberately
  descoped earlier).
- **Never block the pose-sending path** on video/command channel state
  — a slow or disconnected TCP connection must not stall or delay UDP
  pose packets.
- **Never let a malformed or oversized video frame, or a garbage
  command line, crash the app** — validate/bound-check before trusting
  lengths or content from the network.
- Everything under "the pose pipeline" section should be structured so
  it could plausibly be unit-tested in isolation from AR Foundation
  itself (i.e. as pure functions/methods operating on plain position +
  rotation values, not tangled up with MonoBehaviour lifecycle) — this
  was a deliberate design goal last time and is worth preserving, since
  it's the only part of the app that's practical to verify without a
  physical device.

---

## What NOT to do

- Do not modify the Blender add-on. It already works and is out of
  scope for this rebuild.
- Do not change any of the three FIXED CONTRACT formats above — the
  add-on parses exactly these shapes and nothing else.
- Do not attempt iOS support.
- Do not add a reliability/retry layer on top of the UDP pose channel —
  fire-and-forget is intentional.
