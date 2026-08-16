using System.Runtime.CompilerServices;
#if UNITY_WEBGL && !UNITY_EDITOR
using UnityEngine.Scripting;
#endif

[assembly: InternalsVisibleTo("Convai.Tests.EditMode")]

#if UNITY_WEBGL && !UNITY_EDITOR
// Keep this assembly linked in WebGL player builds.
[assembly: AlwaysLinkAssembly]
[assembly: Preserve]
#endif
