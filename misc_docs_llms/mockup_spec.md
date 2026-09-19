# Cam Link Pro — Mockup Build Spec
Source: Claude artifact "Cam Link Pro Mockups" (https://claude.ai/artifact/2sDLBzMfR4Hdf16u5V9tmW)
Captured 2026-09-19. Single-page canvas prototype ("Page 1", no other pages), 28% zoom grid of live clickable frames + yellow sticky-note design-intent annotations.

General note on prototype: "Click through — every screen here is a live, clickable prototype (arrows in the flow are the actual links)."

---

## Design system notes (apply globally)
- **Read-only telemetry / live values** (pose data, focal length, sensor width, countdown timers, free-space readouts) use a **green monospace "terminal" treatment** (green text on near-black box) — visually distinct from editable controls (toggles, dropdowns, sliders). When the value represents an error/low condition (e.g. low free space), the same terminal-box treatment turns **red**.
- Camera/AR session lifecycle: the AR camera session must **only initialize when entering the Recording HUD screen** — never at app launch, never while just Landing or Settings are open. This fixes battery drain / device heat from an always-on camera. A quick battery / storage / AR-tracking preflight check runs before handing off from "Starting camera…" to the HUD.
- Buttons: primary actions = solid blue rounded-pill; destructive/stop actions = solid red rounded-pill; secondary/cancel = dark outline or plain text link; disabled = grayed pill.
- Status dots (paired/live indicators) should carry a **symbol in addition to color** (✓ good · ! degraded · ✗ lost) — not hue alone — for outdoor visibility and color-vision-deficiency accessibility.
- Reset Origin Position (ROP) and Reset Origin Rotation (ROR) are two independent calibration captures, shown as **two separate cards**, not one merged block.

---

## Pairing flow — camera stays off the whole time

### 1. Landing — unpaired
- Title "Cam Link Pro", subtitle "Made with ❤️ by Teja"
- Body text: "Pair with the Blender add-on"
- Primary blue pill button: "Scan QR"
- Link below: "Enter details manually"
- Two dark pill chips showing recent/known IPs: "192.168.0.9", "192.168.0.4"
- Bottom bar: "Settings" and "Let's Record" (disabled/grayed until paired)

### 2. Scan QR — camera on here
- Header "Scan QR", top-right status "● CAMERA ACTIVE" (gold/yellow)
- Rounded dark square viewfinder with blue L-shaped corner brackets, placeholder text "live camera preview" centered
- Below viewfinder: instructional text (partially obscured — general "point camera at QR on Blender panel" style copy)
- Bottom controls: "(mock) simulate timeout →" (dev-only affordance, links to error screen 12) and "Cancel" pill button

### 3. Landing — paired (camera off)
- Title "Cam Link Pro", subtitle "Made with ❤️ by Teja"
- Status card: green dot + "Live — driving camera", sub-line "192.168.0.9 · pose:5005 · video/cmd:5006"
- Link: "Re-pair"
- Bottom bar: "Settings" (dark) and "Let's Record" (blue, primary/active now that paired)

### 4. Starting camera… (new)
- Centered blue circular spinner
- "Starting camera…" title, "Initializing AR tracking" subtitle
- Checklist (green checkmarks): "✓ Battery 82%", "✓ Storage 12.4 GB free", "✓ AR tracking ready"
- Bottom: "(mock) continue →"
- **Sticky notes attached:**
  - "NEW: the AR camera/session now initializes only when entering this screen — never at app launch, never while just Landing or Settings sit open. Fixes the battery drain / heat from an always-on camera."
  - "Also now runs a quick battery / storage / AR-tracking preflight check before handing off to the HUD."

---

## Recording flow — camera only ever runs here

### 5. Recording HUD — Idle
- Top mode-chip row: "Handheld", "Tripod", "Dolly", "Crane" (pill selector)
- Small gray label under chips: "Pan·Y Tilt·X Roll·Z"
- Floating right-side panel: title "Idle", "Lock Start" toggle switch, blue "Record" button (primary), grayed "Stop" button (disabled)
- Bottom control bar: "Steady" slider (value 0.4), "Opacity" slider (value 0.6), "Zoom · auto" with − / + steppers, "Dolly · 0.0m" with "Reset Origin" button, "Zero Start" section with "RDP" and "ROR" buttons
- **Sticky note:** "RDP (Reset Origin Position) and ROR (Reset Origin Rotation) are two separate calibration captures — now shown as two separate cards instead of one merged block." (applies to Settings → Calibration, screen 8)

### 6. Recording HUD — Countdown
- Full-bleed centered giant white "2" with soft glow
- Caption below: "RECORDING STARTS IN…"
- Bottom-right: "(mock) skip →"
- **Sticky note:** "NEW: replaces the old small corner 'Starting in Xs…' text with a big center-screen flash, counting down from whatever Record Delay is set to in Settings (0–10s, default 2s)."

### 7. Recording HUD — Live
- Whole screen gets a subtle red tint + thin red border to signal active recording
- Floating right-side panel: red bold title "Recording", grayed "Lock Start" toggle, grayed "Record" (disabled), solid red "Stop" button (primary/active)
- Bottom-left: "(mock) simulate disconnect →" (dev affordance → links to screen "Blender disconnected")

### 8. Settings — Calibration tab
- Top tab bar: "Calibration" (active/blue), "App", "Connection", "Camera"
- "Output Calibration" section:
  - "Position gain": three green terminal-style value boxes — 1.000 / 1.000 / 1.000 (X/Y/Z)
  - "Rotation axes": chip "Remap: XZY", chip "Flip: X · Z", link "edit in dropdown"
  - "Horizon leveling": toggle switch, "Off (default)"
- Two side-by-side cards:
  - "Reset Origin — Position (ROP)" — "Captured camera position, used as the (0,0,0) origin for this take." link "Re-zero", three green boxes X/Y/Z: 0.000 / 0.000 / 0.000
  - "Reset Origin — Rotation (ROR)" — "Captured camera rotation, used as the level/forward reference for this take." link "Re-zero", three green boxes Roll/Pitch/Yaw: 3.5° / 0.0° / −209.9°

### 9. Settings — Camera tab
- Top tab bar: "Calibration", "App", "Connection", "Camera" (active/blue)
- Status line: "● Live from Blender — received every pose packet" (green dot)
- Two green terminal-style value boxes: "FOCAL LENGTH (MM)" 35.0, "SENSOR WIDTH (MM)" 36.0
- Section "Blender camera settings", subtitle "Placeholder controls for when the add-on exposes these. Values shown are dummy defaults.", badge "disabled — not yet wired" (amber)
- Rows (disabled/grayed): "Sensor height (mm)" 24.0, "Lens distortion profile" None, "Stream quality target" Auto, "Preferred frame rate" 30 fps
- **Design note (applies here):** focal length / sensor width values already come live off the wire (pose packet) and are shown live in the green terminal style; everything else on this tab is a dummy placeholder until the Blender add-on exposes it.

---

## Round 2 — accessibility, unhappy paths, HUD-visibility settings
(New/updated screens layered onto the same flows above.)

### 10. Settings — App tab (HUD visibility) — NEW
- Header: "‹ Back  Settings", tabs "Calibration", "App" (active/blue), "Connection", "Camera"
- "Record delay" row — "Seconds of countdown after pressing Record" — value "2.0 s"
- "HUD visibility" section — "Choose what shows on screen while recording. Everything stays functional either way — this only hides the on-screen element."
  - Per-row blue pill toggles:
    - "Status strip (link · blender · pose)" — ON
    - "Rig preset row (Handheld / Tripod / Dolly / Crane)" — ON
    - "Freeze-axis labels (Pan / Tilt / Roll)" — ON
    - "Zoom rocker slider" — OFF (default off — matches real app default)
    - "Bottom control bar (Steady / Opacity / Zoom / Dolly / Zero Start)" — ON
    - "Record-readiness warning banner" — ON (row cut off in capture, inferred default-on per note)
- **Sticky note:** "NEW: App tab. Per-element HUD visibility toggles — hide any HUD piece without losing its function. Zoom rocker defaults OFF to match the real app; everything else defaults ON."
- **Also applies here (accessibility note captured on this frame group):** "Status dots now carry a symbol too (✓ good · ! degraded · ✗ lost), not just hue — reads correctly outdoors and for color-vision deficiency. See the link/blender/pose… row in Settings for [reference to] Recording HUD [status strip]."

### 11. Recording — Saved confirmation — NEW
- Top bar: "Home" link (left), "Link" status (right, blue)
- Center green terminal-style toast: "✓ Saved · 0:42", sub-line "Movies/CamLinkPro/take_2026-09-18_001.mp4"
- Bottom-right: "(mock) continue →"
- **Sticky note:** "NEW: brief 'Saved · duration + filename' confirmation toast after Stop, so it's never ambiguous whether the take actually wrote to disk."

### 12. Error — QR scan timeout — NEW (unhappy path)
- Centered amber circular icon with "!" 
- Title: "Couldn't find a pairing QR code"
- Body: "The camera timed out waiting for a code. Make sure Blender's Cam Link Pro panel is showing its QR and this phone is on the same Wi-Fi network as your computer."
- Buttons: "Try again" (blue primary), "Enter manually" (dark outline), "Cancel" (text link)
- Reached via "(mock) simulate timeout →" on screen 2.

### 13. Blender disconnected — NEW (unhappy path, mid-take)
- Top banner bar, dark red background, amber/red text: "⚠ Blender disconnected"
- Body: "Recording is paused on-device. Reconnect within 30s to resume driving the Blender camera, or stop now and keep what's already captured."
- Green terminal-style countdown: "reconnecting… 00:14"
- Buttons: "Reconnect" (blue primary), "Stop & Save" (red)
- Reached via "(mock) simulate disconnect →" on screen 7.
- **Design note:** this countdown/readout uses the same green-on-black terminal treatment as other live values, to visually distinguish it from editable controls.

### 14. Storage full — NEW (unhappy path, pre-record)
- Centered red triangle warning icon (circle background)
- Title: "Not enough storage to record"
- Body: "Free up at least 500 MB on this device, then try again."
- Red-bordered terminal-style box: label "FREE SPACE", value "210 MB" in red monospace (error variant of the green terminal treatment)
- Button: "Dismiss" (dark)

### Unhappy-path group note (covers screens 12–14)
"NEW: three unhappy-path screens — QR scan timeout, Blender disconnecting mid-take, and storage full before you can record. Each links back from a '(mock) simulate …' affordance on its parent screen."

---

## Open items / things not fully legible from the capture
- Screen 10's exact title text was partially obscured by the "Round 2" section header overlapping it in the canvas rendering; content was reverse-engineered from the visible frame body (confirmed: Settings → App tab, HUD visibility).
- The last "Record-readiness warning banner" toggle row on screen 10 was cut off at the bottom of the frame; state (ON) is inferred from the note that "everything else defaults ON" except the zoom rocker.
