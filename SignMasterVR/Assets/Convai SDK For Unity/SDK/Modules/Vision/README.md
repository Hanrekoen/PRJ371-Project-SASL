# Vision Module (Video Streaming)

This module publishes a video track to the active Convai room so characters can receive visual context from your Unity scene.

## Use this when

- `ConvaiRoomManager` is in `Video` connection mode
- you want the AI to see a Unity camera feed, another custom frame source, or the visible WebGL canvas
- you need editor-side preview/debug tools for the active vision source

## Runtime model

- On native platforms, `ConvaiVisionPublisher` streams an `IVisionFrameSource`
- On WebGL, `ConvaiVisionPublisher` publishes the visible Unity browser canvas via `canvas.captureStream()`
- `CameraVisionFrameSource` uses built-in render hooks on the built-in pipeline and explicit camera rendering on SRP/URP
- `CameraVisionFrameSource` now owns capture presets and custom capture dimensions directly in the component inspector
- camera backend internals are treated as SDK-debug settings; normal setup should leave capture strategy on `Auto`
- Unity treats backend vision capabilities as unknown, so the publisher exposes client transport policy rather than backend-model mode
- publishing starts after the room connects and stops when the room or module stops unless the publisher is set to `Manual`

## Publish policy

`ConvaiVisionPublisher` owns the public vision transport surface.

- `AutoCompatible`: default continuous publish profile for unknown backend capability
- `HighResponsiveness`: higher transport budget for latency-sensitive live multimodal sessions
- `LowOverhead`: lower transport budget for cost-sensitive or snapshot-heavy sessions
- `Manual`: do not auto-publish on connect; call `EnablePublishing(true)` explicitly

Policy defaults:

- `AutoCompatible`: `10 fps`, `750_000 bps`
- `HighResponsiveness`: `15 fps`, `1_000_000 bps`
- `LowOverhead`: `5 fps`, `350_000 bps`
- `Manual`: uses `AutoCompatible` defaults when manually enabled

Optional overrides:

- `publishFrameRateOverride = 0` means "use the policy default"
- `publishBitrateOverride = 0` means "use the policy default"

## Recommended setup

1. Ensure the Vision module is present in the project.
2. Set `ConvaiRoomManager.Connection Type` to `Video`.
3. Add or accept the suggested vision components:
   - `ConvaiVisionPublisher`
   - `CameraVisionFrameSource` or another `IVisionFrameSource` on native platforms
4. Connect the room.

## Common pitfalls

- Camera capture defaults live on `CameraVisionFrameSource`; there is no separate project-wide Vision capture defaults surface
- if multiple frame sources exist in one hierarchy, assign the publisher source explicitly instead of relying on auto-find
- `VisionPublishPolicy` describes only client-side publish behavior; it does not reveal which model/provider the backend resolved
- WebGL still needs a user gesture for audio playback even when video is publishing
- `VisionDebugPreview` previews `IVisionFrameSource.CurrentRenderTexture`; it does not preview the WebGL canvas-capture path
- Built-in and SRP camera capture use different internals by design; SRP/URP does not rely on render-hook command buffers

## Go deeper (paths from package root)

- Publisher (MonoBehaviour entry): `SDK/Modules/Vision/ConvaiVisionPublisher.cs`
- Frame source contract: `SDK/Runtime/Vision/Sources/IVisionFrameSource.cs`
- Camera frame source: `SDK/Runtime/Vision/Sources/CameraVisionFrameSource.cs`
- Webcam source: `SDK/Runtime/Vision/Sources/WebcamVisionFrameSource.cs`
- Publish controller: `SDK/Runtime/Vision/Publishing/VisionPublishCoordinator.cs`
- Publish profile resolver: `SDK/Runtime/Vision/Publishing/VisionPublishProfileResolver.cs`
- Publish policy enum: `SDK/Runtime/Vision/Publishing/VisionPublishPolicy.cs`
- Debug preview: `SDK/Runtime/Vision/Debug/VisionDebugPreview.cs`
- In-repo overview: `Documentation~/SOURCE-REFERENCE.md`
