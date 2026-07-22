# Scripictural — Client Knowledge Transfer (KT) Guide

**Product:** Scripictural  
**Company:** Scripictural  
**App version (Player Settings):** `0.1`  
**Document type:** Full technical + operational handoff for client / technical stakeholders  
**Last updated:** July 2026  

---

## Table of contents

1. [What this app does](#1-what-this-app-does)
2. [System requirements](#2-system-requirements)
3. [Install Unity Hub and the correct Unity version](#3-install-unity-hub-and-the-correct-unity-version)
4. [Install Android and iOS build modules](#4-install-android-and-ios-build-modules)
5. [Open the project](#5-open-the-project)
6. [Project overview and architecture](#6-project-overview-and-architecture)
7. [Main scene and key GameObjects](#7-main-scene-and-key-gameobjects)
8. [End-to-end user flow](#8-end-to-end-user-flow)
9. [Backend / API integration](#9-backend--api-integration)
10. [Inspector field guide (how to tweak settings)](#10-inspector-field-guide-how-to-tweak-settings)
11. [Permissions and device requirements](#11-permissions-and-device-requirements)
12. [Normal (development) build](#12-normal-development-build)
13. [Production-ready build checklist](#13-production-ready-build-checklist)
14. [Android production build](#14-android-production-build)
15. [iOS production build](#15-ios-production-build)
16. [Testing checklist](#16-testing-checklist)
17. [Common issues and fixes](#17-common-issues-and-fixes)
18. [Handover notes / ownership map](#18-handover-notes--ownership-map)

---

## 1. What this app does

Scripictural is a **mobile AR (Augmented Reality) experience** for artworks.

At a high level the app:

1. Opens the device camera.
2. Continuously captures frames and sends them to a **recognition server**.
3. When an artwork is recognized, it:
   - Creates a **local image target** for that artwork.
   - Downloads / caches the artwork marker image and video.
   - Overlays and plays the artwork **video** on top of the physical artwork when tracking is stable.
4. Shows **artwork title + description** in a description panel.
5. Provides an **AI chatbot** so users can ask questions about the currently focused artwork.

The experience is designed for **portrait mobile use** (Android phones / iPhones).

---

## 2. System requirements

### For developers / builders

| Item | Requirement |
|------|-------------|
| OS (Android builds) | Windows 10/11 (64-bit) or macOS |
| OS (iOS builds) | **macOS required** (Xcode only runs on Mac) |
| Disk space | ~20–40 GB free (Unity Editor + modules + caches) |
| RAM | 16 GB recommended (8 GB minimum) |
| Internet | Required for Hub login, package restore, API testing |
| Unity version | **Unity 6000.3.9f1** (Unity 6) |
| Android tooling | Android Build Support + SDK + NDK + OpenJDK (via Unity Hub) |
| iOS tooling | iOS Build Support (via Unity Hub) + **Xcode** (Mac App Store) |
| Device testing | Physical Android / iPhone with camera (AR does not work well in Editor Play Mode alone) |

### For end users (runtime)

| Platform | Notes |
|----------|-------|
| Android | Min SDK **29** (Android 10+), ARM64 devices |
| iOS | Target OS **15.0+** |
| Camera | Required |
| Network | Required for recognition + chat + media download |

---

## 3. Install Unity Hub and the correct Unity version

### 3.1 Download Unity Hub

1. Open: [https://unity.com/download](https://unity.com/download)
2. Download **Unity Hub** for your OS (Windows / macOS).
3. Install and open Unity Hub.
4. Sign in with a Unity account (free Personal license is fine for most client evaluation; use Unity Pro if your organization already has seats).

### 3.2 Install the exact Editor version used by this project

This project was built with:

> **Unity Editor `6000.3.9f1`**  
> (Unity 6 family — revision recorded in `ProjectSettings/ProjectVersion.txt`)

**Important:** Always use this exact version (or the same patch line if Unity confirms compatibility). Opening with a different major/minor version can change serializers, packages, and break the scene.

#### Install from Unity Hub

1. Open **Unity Hub → Installs → Install Editor**.
2. Find / search **`6000.3.9f1`**.
   - If it is not in the default list, open: [Unity Download Archive](https://unity.com/releases/editor/archive)
   - Locate **Unity 6 / 6000.3.9f1** → **Unity Hub** install button.
3. Before finishing install, open **Add modules** and select the modules in the next section.
4. Complete installation.

---

## 4. Install Android and iOS build modules

When installing (or later via **Hub → Installs → gear icon on 6000.3.9f1 → Add modules**), enable:

### Required for Android builds

- **Android Build Support**
  - **Android SDK & NDK Tools**
  - **OpenJDK**

### Required for iOS builds

- **iOS Build Support**

### Strongly recommended

- **Documentation** (optional)
- A code editor module if desired:
  - **Visual Studio** (Windows) or
  - **Visual Studio Code** / Rider support packages (already referenced in project packages)

### Extra Mac requirement for iOS

On the Mac used for iOS builds, also install:

1. **Xcode** (latest stable compatible with your macOS).
2. Open Xcode once and accept license / install additional components.
3. Sign in with an **Apple Developer** account in Xcode → Settings → Accounts.
4. Ensure a valid **Team / Signing certificate** exists for device install and App Store / TestFlight.

> Without Android/iOS modules, Unity can open the project but **cannot export mobile builds**.

---

## 5. Open the project

1. Open **Unity Hub → Projects → Open**.
2. Select the project root folder:  
   `Scripictural-New`  
   (the folder that contains `Assets`, `Packages`, `ProjectSettings`).
3. Choose Editor **6000.3.9f1** if prompted.
4. Wait for first-time import (can take several minutes).
5. Open the main scene:  
   `Assets/Scenes/0-Main.unity`

### Build Settings scene note

Confirm **File → Build Settings** (or **File → Build Profiles** in Unity 6) includes:

- `Assets/Scenes/0-Main.unity` as an enabled scene (index 0).

If another / missing path is listed, remove it and add `Assets/Scenes/0-Main.unity`.

---

## 6. Project overview and architecture

### 6.1 High-level architecture

```text
[Device Camera]
      |
      v
[ImageScanner]  ---- captures frames ---->  [ServerManager]
      |                                           |
      |                                           v
      |                                  Recognition API
      |                                   /api/recognition/match
      |
      v (on match)
[DynamicTracker] ---- creates local marker + video overlay
      |
      +---- updates ----> [DescriptionManager] (title/description UI)
      +---- updates ----> [ChatManager] (artwork context for AI chat)
                              |
                              v
                     Ask-AI API
                     /api/artworks/ask-ai
```

### 6.2 Core scripts (custom app logic)

| Script | Role |
|--------|------|
| `ImageScanner` | Captures camera frames on an interval, filters weak/duplicate frames, sends good frames for recognition |
| `ServerManager` | Uploads JPEG frames to recognition API and returns match results |
| `DynamicTracker` | On match: downloads marker, creates runtime image target, binds video, manages tracking lifecycle + loading UI |
| `ArtworkVideoSurface` | Builds video quad, prepares/plays/pauses video when tracking changes |
| `ArtworkSessionCache` | Local disk cache for marker images / videos / metadata for session reuse |
| `DescriptionManager` | Description panel open/close, title/body text, frozen camera backdrop |
| `ChatManager` | Chat UI, keyboard safe shifting, AI ask endpoint, unpublished artwork messaging |
| `MessageItemUi` | Chat bubble layout + typewriter animation |
| `SafeAreaFitter` | Fits UI into device safe area (notch / home indicator) |
| `RuntimeVideoTarget` | Optional legacy/demo helper for a single hardcoded image+video pair (usually disabled in main scene) |

### 6.3 Supporting folders

| Path | Purpose |
|------|---------|
| `Assets/Scripts/` | Main gameplay / AR / UI logic |
| `Assets/Scenes/` | Main scene (`0-Main`) |
| `Assets/Plugins/` | Third-party plugins (e.g. in-game debug console) |
| `Assets/MOST_HapticFeedback/` | Haptic feedback utility used on track gain |
| `Assets/Resources/` | Runtime configuration / shared resources |
| `Packages/` | Unity package dependencies |

---

## 7. Main scene and key GameObjects

Open `Assets/Scenes/0-Main.unity`. Important objects:

| GameObject | What to know |
|------------|--------------|
| `ARCamera` | Main AR camera (must remain active for camera feed + tracking) |
| `ImageScanner` | Frame capture + scan loop |
| `DynamicTracker` | Runtime artwork targets + video overlays parent |
| `ServerManager` | Recognition API client |
| `Canvas` / UI hierarchy | Chat UI, description panel, buttons, splash |
| `Chat Ui` | Chatbot panel and input |
| `Description Panel` | Artwork title/description |
| `Frozen camera Frame` | Backdrop image when panels open |
| `Runtime` | Demo/runtime single-target helper (often inactive) |

Select any object and use the **Inspector** on the right to tweak serialized fields (see Section 10).

---

## 8. End-to-end user flow

1. App launches → splash briefly shown → scanner starts after AR engine is ready.
2. Camera permission is requested (first run).
3. `ImageScanner` captures frames every `scanInterval` seconds.
4. Weak frames (low edges / low color variance) are skipped.
5. Near-duplicate frames are skipped (fingerprint similarity).
6. Good frames are JPEG-encoded by `ServerManager` and POSTed to recognition API.
7. On match:
   - `DynamicTracker` resolves image/video URLs.
   - Marker image is downloaded / cached.
   - Local image target is created at `physicalWidthMeters`.
   - Video surface is prepared; loading canvas shows until video is ready.
8. When physical artwork is tracked:
   - Haptic tick (light impact).
   - Video shows and plays on the artwork plane.
   - Chat/description receive artwork metadata.
9. User can open **Description** or **Chat**.
10. If artwork is not published, chat blocks AI with a clear message.
11. When tracking is lost, video hides; scanner can resume after unlock delay.

---

## 9. Backend / API integration

### 9.1 Recognition API

Configured on `ServerManager`:

- Base URL field: **Server Url**
- Endpoint used in code: `{serverUrl}/api/recognition/match`
- Method: `POST` multipart form
- File field name: **Upload Field Name** (default `file`)
- Uploaded payload: JPEG camera frame (`frame.jpg`)

Default placeholder in script: `https://your-app.onrender.com`  
**For production, set this in the Inspector to the live recognition base URL.**

### 9.2 Media / asset base URL

Configured on `DynamicTracker`:

- **Asset Base Url** default: `https://api.scripictural.tecshield.net/`
- Used when API returns relative image/video paths.

### 9.3 Chat / Ask-AI API

Configured in `ChatManager` code constant:

- `https://api.scripictural.tecshield.net/api/artworks/ask-ai`

This sends the user question with the current artwork ID.

> If the AI endpoint changes, update `ChatManager` (currently a const string) and rebuild.

### 9.4 Local cache

`ArtworkSessionCache` stores under:

`Application.persistentDataPath/artwork_cache/`

- `images/` — marker JPEGs  
- `videos/` — downloaded MP4s  
- `artworks.json` — index of artwork IDs and URLs  

This improves repeat recognition playback speed and reduces re-downloads.

---

## 10. Inspector field guide (how to tweak settings)

This section is the practical “knobs” list for non-code changes.

### How to edit Inspector values safely

1. Open `0-Main` scene.
2. Select the GameObject (e.g. `ImageScanner`).
3. In **Inspector**, change values.
4. Press **Ctrl/Cmd + S** to save the scene.
5. Test on a **real device** whenever changing scan/tracking values.

> Tip: Prefer small incremental changes. Extreme thresholds can stop recognition or spam the server.

---

### 10.1 `ImageScanner` Inspector

**References**

| Field | What it does | When to tweak |
|------|---------------|---------------|
| Debug Text | Optional on-screen log (TMP) | Enable for QA builds; leave empty/hidden for production |
| Dynamic Tracker | Link to tracker component | Must stay assigned |
| Splash Canvas | Splash UI disabled after engine ready | Keep assigned |

**Scan Settings**

| Field | Typical effect | Guidance |
|------|----------------|----------|
| Scan Interval | Seconds between scan attempts | Lower = more responsive, more CPU/network. Production often `0.1`–`1.0` |
| Use Original Camera Resolution | If on, keeps native capture size for processing | On = better quality, heavier; Off = uses Processing Width/Height |
| Processing Width / Height | Downscale size when not using original resolution | `320x240` is light; increase for harder artworks |
| Send Cooldown | Minimum seconds between successful uploads | Raise to reduce API cost / battery |
| Quality Sample Stride | Sampling step for quality metrics | Higher = cheaper quality checks, less accurate |
| Server Frame Zoom | Center-crop zoom before upload | `>1` focuses center; useful if artwork is framed centrally |

**Filter Thresholds**

| Field | What it does | Guidance |
|------|--------------|----------|
| Edge Threshold | Minimum edge density to accept frame | Too high → rejects real artworks; too low → sends blurry walls |
| Color Var Threshold | Minimum color variance | Same tradeoff as edges |
| Frame Similarity Threshold | Duplicate rejection sensitivity (`0.7`–`0.99`) | Higher = stricter “same frame” skip |
| Fingerprint Grid Size | Resolution of frame fingerprint | Higher = finer duplicate detection, more CPU |

**Orientation**

| Field | Notes |
|------|------|
| Normalize Portrait Orientation | Rotates landscape camera buffers to portrait when needed. Keep enabled for phones. |

**Debug**

| Field | Production recommendation |
|------|---------------------------|
| Verbose Logs | **Off** for production |
| Save Debug Frames | **Off** for production (writes JPEGs to persistent data) |

---

### 10.2 `ServerManager` Inspector

| Field | Purpose | Guidance |
|------|---------|----------|
| Server Url | Recognition API base URL | Set to live backend for production |
| Upload Field Name | Multipart form key | Must match backend (`file` by default) |
| Jpeg Quality | Starting JPEG quality (`1–100`) | Higher = larger uploads |
| Adaptive Jpeg Size | Iteratively reduces quality to hit size target | Keep **On** for mobile networks |
| Target Upload Size Kb | Desired max upload size | Default `24` KB is aggressive/light; raise if matches fail due to compression |
| Min Jpeg Quality | Floor quality while adapting | Don’t go too low or recognition suffers |
| Quality Step | Quality decrement step | Smaller step = finer sizing, slightly slower encode |
| Image Scanner | Reference for logging bridge | Keep assigned |

---

### 10.3 `DynamicTracker` Inspector

**Target**

| Field | Purpose | Guidance |
|------|---------|----------|
| Physical Width Meters | Assumed real-world width of marker | Must roughly match physical print size for correct video scale. Default `0.2` (20 cm) |
| Cache Marker Images | Saves markers locally | Keep **On** |
| Video Material | Material used for video surface | Must be assigned or video won’t render |

**Loading UI**

| Field | Purpose |
|------|---------|
| Loading Canvas Prefab | Shown above artwork while video prepares |

**URLs**

| Field | Purpose |
|------|---------|
| Asset Base Url | Prefix for relative media paths from API |

**Scanner Control**

| Field | Purpose | Guidance |
|------|---------|----------|
| Scan Unlock Delay | Delay after track loss before scanner is considered unlocked | Increase if scanner fights tracking |
| Tracking Heartbeat Timeout | Reserved tracking timeout related setting | Leave unless advised by engineering |

---

### 10.4 `ArtworkVideoSurface` (runtime component)

Usually created at runtime; defaults matter if you edit the script defaults or prefab setup.

| Field | Purpose |
|------|---------|
| Video Material | Shader/material for video texture |
| Video Texture Property | Usually `_MainTex` |
| Surface Y Offset | Tiny lift above marker to avoid z-fighting |
| Loop | Loop playback |

---

### 10.5 `DescriptionManager` Inspector

| Field | Purpose |
|------|---------|
| Description Open/Close Buttons | UI wiring |
| Chat Open/Close Buttons | Visibility coordination with chat |
| Description Panel | Panel root |
| Artwork Title / Description | TMP text targets |
| Scroll Rect / Content | Scrollable body |
| Frozen Camera Image / Background Image | Visual backdrop when panel opens |
| Title Font Size / Body Font Size | Typography |
| Body Line Spacing / Paragraph Spacing | Text rhythm |
| Description Horizontal Padding | Side padding |

---

### 10.6 `ChatManager` Inspector

**UI**

Wire chat parent, input, submit, scroll content, open/close buttons, artwork title text, description buttons.

**Message Prefabs**

| Field | Purpose |
|------|---------|
| My Message Item | User bubble prefab (`MessageItemUi`) |
| Response Message Item | Bot bubble prefab |

**Typing**

| Field | Effect |
|------|--------|
| Typewriter Delay | Character reveal speed |
| Dots Delay | “…” waiting animation speed |

**Keyboard Mobile**

| Field | Effect |
|------|--------|
| Root Canvas | Used for keyboard offset math |
| Scroll Area Rect | Area compressed when keyboard opens |
| Keyboard Padding | Extra gap above keyboard |
| Keyboard Fallback Screen Percent | Fallback keyboard height estimate |
| Keyboard Move Speed | Smooth follow speed |
| Keyboard Hide Grace Seconds | Prevents jitter when keyboard closes |

---

### 10.7 `SafeAreaFitter`

No Inspector knobs. Attach to a full-stretch RectTransform under Canvas; keep interactive UI as children so notches don’t cover buttons.

---

### 10.8 `RuntimeVideoTarget` (optional demo)

| Field | Purpose |
|------|---------|
| Image Url / Video Url | Hardcoded demo pair |
| Target Name | Runtime target object name |
| Target Width Meters | Physical width assumption |
| Video Material | Required |
| Download Video Before Play | Prefetch behavior |

In main scene this object is typically **inactive**. Keep it disabled for production unless needed for demos.

---

## 11. Permissions and device requirements

### Android

- Camera permission is required at runtime.
- Internet access is required for recognition, media, and chat.
- Min SDK: **29**
- Architecture: **ARM64** (project is configured for modern 64-bit Android)

### iOS

- Camera usage description must be present in Player Settings / Info.plist style fields.
- Target iOS: **15.0+**
- Requires signing with a valid Apple Team.

### Editor Play Mode limitation

AR camera tracking and real recognition quality must be validated on **physical devices**. Editor Play Mode is useful for UI wiring checks only.

---

## 12. Normal (development) build

Use this for internal QA / day-to-day testing.

### 12.1 Shared prep

1. Open project in **Unity 6000.3.9f1**.
2. Open `Assets/Scenes/0-Main.unity`.
3. Confirm Build Settings contains that scene.
4. Set `ServerManager → Server Url` to the environment you want (dev/staging).
5. Optionally enable `ImageScanner → Verbose Logs` for debugging.

### 12.2 Android development build (APK)

1. **File → Build Settings / Build Profiles**.
2. Switch platform to **Android**.
3. Player Settings quick check:
   - Company Name: `Scripictural`
   - Product Name: `Scripictural`
   - Package Name (Application ID): currently `com.scripictural.arplatform`
   - Minimum API Level: 29+
4. **Build** (or **Build And Run** with USB device + USB debugging).
5. Output: `.apk`

Development tips:

- Keep **Development Build** checked if you need deeper debugging.
- You may keep script debugging / console plugins enabled for QA only.

### 12.3 iOS development build

Must be done on a Mac:

1. Switch platform to **iOS**.
2. **Build** → choose an empty output folder (Unity exports an Xcode project).
3. Open the generated `.xcodeproj` / `.xcworkspace` in **Xcode**.
4. Select your Team for signing.
5. Set a unique Bundle Identifier for your team (see production section).
6. Run on a connected iPhone.

---

## 13. Production-ready build checklist

Complete this before store / client UAT release builds.

### Product / branding

- [ ] Final app icon set (Android adaptive + iOS App Icons)
- [ ] Final splash / launch screens
- [ ] Product name final (`Scripictural` or client brand)
- [ ] Version string bumped (`bundleVersion`, e.g. `1.0.0`)
- [ ] Android `Bundle Version Code` incremented for each Play upload
- [ ] iOS build number incremented for each TestFlight/App Store upload

### Identifiers

- [ ] Android Application ID finalized (current: `com.scripictural.arplatform`)
- [ ] iOS Bundle ID finalized to client-owned ID (do **not** ship with placeholder/engine defaults)
- [ ] Signing keystore (Android) secured and backed up
- [ ] Apple certificates / provisioning profiles valid

### Backend

- [ ] `ServerManager.Server Url` points to **production** recognition API
- [ ] `DynamicTracker.Asset Base Url` points to production CDN/API media host
- [ ] Chat Ask-AI endpoint verified against production
- [ ] SSL certificates valid on all endpoints
- [ ] CORS / auth (if any) confirmed for mobile clients

### Quality / performance

- [ ] `Verbose Logs` = Off
- [ ] `Save Debug Frames` = Off
- [ ] In-game debug console disabled/removed from production scene if present
- [ ] Development Build = Off
- [ ] Script Debugging = Off
- [ ] Autoconnect Profiler = Off
- [ ] Scan interval / cooldown tuned to balance UX vs API cost
- [ ] Test on mid-range Android + recent iPhone

### Platform build type

- [ ] Android: prefer **AAB (App Bundle)** for Play Store
- [ ] Android: IL2CPP + ARM64
- [ ] iOS: Release configuration in Xcode, bitcode/settings per current Apple requirements
- [ ] Strip engine code / managed stripping reviewed if size is critical

### Legal / store

- [ ] Camera permission purpose strings finalized
- [ ] Privacy policy URL ready
- [ ] Data usage disclosure (camera frames uploaded for recognition; chat questions sent to AI API)
- [ ] Age rating / content questionnaire prepared

---

## 14. Android production build

### 14.1 Player Settings (Android)

Path: **Edit → Project Settings → Player → Android**

Recommended production values for this project:

| Setting | Guidance |
|---------|----------|
| Package Name | Client final ID (currently `com.scripictural.arplatform`) |
| Version | e.g. `1.0.0` |
| Bundle Version Code | Integer, increase every store upload |
| Minimum API Level | 29+ |
| Target API Level | Latest required by Google Play |
| Scripting Backend | **IL2CPP** (project already uses IL2CPP on Android) |
| Target Architectures | **ARM64** |
| Internet Access | Required / automatic |
| Write Permission | As needed for cache |

### 14.2 Keystore (signing)

1. Create a release keystore (or use client-provided keystore).
2. In Publishing Settings, assign:
   - Keystore
   - Key alias
   - Passwords
3. Store keystore + passwords in a secure password manager. **Losing the keystore prevents Play Store updates.**

### 14.3 Build App Bundle (Play Store)

1. Build Settings → Android.
2. Enable **Build App Bundle (Google Play)** / AAB.
3. Disable Development Build.
4. Click **Build**.
5. Upload `.aab` to Google Play Console (internal testing track first).

### 14.4 Internal APK (optional)

For sideload QA, build an APK signed with the release/debug key as needed. Prefer AAB for store.

---

## 15. iOS production build

### 15.1 Unity side

1. Switch platform to **iOS**.
2. Player Settings → iOS:
   - Bundle Identifier = client-owned ID
   - Version / Build numbers set
   - Target minimum iOS **15.0**
   - Camera Usage Description filled with clear user-facing text
3. Build → export Xcode project.

### 15.2 Xcode side

1. Open exported project in Xcode.
2. Signing & Capabilities:
   - Team selected
   - Automatically manage signing (or use client profiles)
3. Set Release scheme.
4. Archive (**Product → Archive**).
5. Distribute to **TestFlight** first, then App Store.

### 15.3 Mac-only reminder

iOS compilation and App Store upload require macOS + Xcode. A Windows machine can prepare the Unity project, but final iOS build must happen on Mac (or a Mac CI runner).

---

## 16. Testing checklist

### Functional

- [ ] App launches and splash dismisses
- [ ] Camera permission prompt appears and works after allow/deny
- [ ] Scanning recognizes known artworks
- [ ] Loading indicator appears while video prepares
- [ ] Video aligns/plays on tracked artwork
- [ ] Tracking loss hides video cleanly
- [ ] Description shows correct title/body
- [ ] Chat opens/closes
- [ ] Keyboard does not cover input on Android/iOS
- [ ] Unpublished artwork shows proper chat lock message
- [ ] Switching to a new artwork updates chat/description context
- [ ] Safe area: buttons not under notch/home indicator

### Network / resilience

- [ ] Airplane mode shows graceful failure (no crash)
- [ ] Slow 3G/4G still eventually matches / loads video
- [ ] Cached artwork replays without full re-download when possible

### Device matrix (minimum)

- [ ] Mid-range Android (ARM64)
- [ ] Flagship Android
- [ ] Recent iPhone
- [ ] One notched Android / Dynamic Island iPhone for safe-area

---

## 17. Common issues and fixes

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| No recognition ever | Wrong `Server Url`, network blocked, thresholds too strict | Verify API URL; lower edge/color thresholds slightly; check device logs |
| Recognition works, no video | Missing video URL, material missing, download fail | Check API media fields; assign Video Material; verify Asset Base Url |
| Video scale wrong | `Physical Width Meters` mismatch | Measure print width and set meters accurately |
| Scanner never starts | Camera permission denied / engine not ready | Grant camera permission; relaunch |
| Chat always blocked | Artwork marked unpublished / no artwork focused | Confirm API `isPublished`; scan a published artwork first |
| Build fails Android SDK/NDK | Hub modules incomplete | Add Android modules from Unity Hub |
| iOS build can’t sign | No Apple Team / wrong bundle ID | Fix Xcode signing + unique bundle ID |
| Scene missing on build | Wrong/missing scene in Build Settings | Add `Assets/Scenes/0-Main.unity` |

---

## 18. Handover notes / ownership map

### Client-owned decisions

- Final package/bundle IDs
- Store listing assets and privacy policy
- Production API base URLs
- Signing identities (Android keystore, Apple Team)

### Engineering-owned areas

- Scan quality tuning (`ImageScanner` thresholds)
- Upload size vs recognition accuracy (`ServerManager` JPEG settings)
- Tracking/video lifecycle (`DynamicTracker`, `ArtworkVideoSurface`)
- Chat UX / keyboard handling (`ChatManager`)

### Suggested environments

| Environment | Recognition Server Url | Notes |
|-------------|------------------------|-------|
| Development | Dev/staging API | Verbose logs OK |
| UAT | Staging/pre-prod API | Production-like build flags |
| Production | Live API | Logs off, AAB/TestFlight, store IDs |

---

## Appendix A — Quick start (one page)

1. Install **Unity Hub** from [unity.com/download](https://unity.com/download).  
2. Install Editor **`6000.3.9f1`**.  
3. Add modules: **Android Build Support (SDK/NDK/OpenJDK)** + **iOS Build Support**.  
4. On Mac, install **Xcode** for iOS.  
5. Open `Scripictural-New` in that Editor.  
6. Open `Assets/Scenes/0-Main.unity`.  
7. Set `ServerManager → Server Url`.  
8. Build Android APK/AAB or export iOS Xcode project.  
9. Test recognition + video + chat on real devices.  
10. For release: follow Section 13 checklist.

---

## Appendix B — Key project facts

| Item | Value |
|------|-------|
| Product name | Scripictural |
| Company name | Scripictural |
| Unity version | 6000.3.9f1 |
| Main scene | `Assets/Scenes/0-Main.unity` |
| Android application ID (current) | `com.scripictural.arplatform` |
| Android min SDK | 29 |
| iOS minimum | 15.0 |
| App version (current project setting) | 0.1 |
| Recognition endpoint pattern | `{serverUrl}/api/recognition/match` |
| Asset base URL (default) | `https://api.scripictural.tecshield.net/` |
| Ask-AI endpoint (current) | `https://api.scripictural.tecshield.net/api/artworks/ask-ai` |

---

## Appendix C — Script map (for developers)

```text
Assets/Scripts/
  ImageScanner.cs            # camera frame sampling + filters
  ServerManager.cs           # recognition upload client
  DynamicTracker.cs          # runtime targets + tracking orchestration
  ArtworkVideoSurface.cs     # video mesh/playback
  ArtworkSessionCache.cs     # local media cache
  DescriptionManager.cs      # description UI
  ChatManager.cs             # chatbot UI + AI request
  MessageItemUi.cs           # bubble UI helper
  SafeAreaFitter.cs          # notch/safe-area fitting
  RuntimeVideoTarget.cs      # optional single-target demo
```

---

**End of KT document.**  
For questions during handover, use this document as the source of truth for setup, Inspector tuning, and release build process.
