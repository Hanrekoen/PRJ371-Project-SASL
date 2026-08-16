# Infrastructure - Networking (LiveKit WebRTC)

## What this is

This folder contains the SDK's real-time transport implementation. It connects to Convai's backend, joins a LiveKit room, publishes local audio and optional video, and routes incoming data into the runtime.

If you're integrating the SDK into a scene, start with:

- `Documentation~/SETUP.md`

## Who should read this

- contributors changing connection, reconnect, mic, remote-audio, or video-track behavior
- integrators debugging transport issues beyond normal scene setup

## Why it exists

Networking depends on third-party SDKs and platform constraints. Keeping it here:

- isolates Native and WebGL transport differences
- keeps `ConvaiManager` and `ConvaiRoomManager` focused on Unity-facing orchestration
- keeps transport selection behind `ITransportProvider`

## End-to-end flow

1. `ConvaiManager` delegates room/session work to `ConvaiRoomManager`.
2. `ConvaiRoomManager` resolves credentials, session intent, and connection type.
3. The active `ITransportProvider` creates the platform-specific transport path.
4. Native or WebGL room controllers join LiveKit, publish mic audio, and subscribe to remote media.
5. RTVI and related data messages are translated into runtime/domain events.
6. If the room is in video mode, the Vision module publishes a video track.

## Platform split

- `Native/` contains the native LiveKit transport implementation
- `WebGL/` contains the browser/WebGL transport implementation

Current WebGL specifics:

- room connect and room-details lookup use coroutine-backed `UnityWebRequest` flows
- browser audio still requires a user gesture
- the Vision module publishes the visible Unity canvas rather than a Unity `RenderTexture`

## Debugging checklist

When the room connects but media or protocol behavior looks wrong:

- confirm API key and character configuration in `Documentation~/TROUBLESHOOTING.md`
- check mic permissions and device availability
- for WebGL, verify HTTPS and user-gesture requirements in `Documentation~/PLATFORMS.md`
- turn logging up to `Info` in `Edit > Project Settings > Convai SDK`

## Go deeper (paths from `SDK/Infrastructure/Networking/`)

- Runtime entrypoint: `../../Runtime/Components/ConvaiManager.cs`
- Runtime composition root: `../../Runtime/Core/Composition/ConvaiRuntimeHost.cs`
- Room/session adapter: `../../Runtime/Adapters/Networking/ConvaiRoomManager.cs`
- Native room controller: `Native/NativeRoomController.cs`
- WebGL room controller: `WebGL/WebGLRoomController.cs`
- Package-wide index: `../../../Documentation~/SOURCE-REFERENCE.md`
