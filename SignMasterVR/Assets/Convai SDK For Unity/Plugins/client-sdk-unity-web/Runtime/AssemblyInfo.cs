using System.Runtime.CompilerServices;

#if UNITY_WEBGL && !UNITY_EDITOR
using UnityEngine.Scripting;
#endif

[assembly: InternalsVisibleTo("Convai.Tests.EditMode")]

// Force IL2CPP to include the LiveKitBridge assembly in WebGL builds.
// This assembly provides the C#-to-JavaScript interop layer for LiveKit WebRTC.
// It is only referenced by Convai.Infrastructure.Networking.WebGL, which itself
// has no inbound compile-time references. Without this attribute, both assemblies
// are stripped from the player build by the managed code linker.
#if UNITY_WEBGL && !UNITY_EDITOR
[assembly: AlwaysLinkAssembly]
[assembly: Preserve]
#endif
