using System.Collections;
using UnityEngine;

namespace Convai.Infrastructure.Networking.Native
{
    internal sealed class NativeCoroutineRunner : MonoBehaviour
    {
        private static NativeCoroutineRunner _instance;

        internal static NativeCoroutineRunner Instance
        {
            get
            {
                if (_instance != null) return _instance;

                _instance = FindAnyObjectByType<NativeCoroutineRunner>();
                if (_instance != null) return _instance;

                var runnerObject = new GameObject("[Convai] Native Coroutine Runner");
                runnerObject.hideFlags = HideFlags.HideAndDontSave;
                DontDestroyOnLoad(runnerObject);
                _instance = runnerObject.AddComponent<NativeCoroutineRunner>();
                return _instance;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        internal static Coroutine Run(IEnumerator routine) => routine == null ? null : Instance.StartCoroutine(routine);

        internal static void Stop(Coroutine coroutine)
        {
            if (coroutine == null || _instance == null) return;

            _instance.StopCoroutine(coroutine);
        }
    }
}
