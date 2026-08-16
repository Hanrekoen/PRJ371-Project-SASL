using System;
using System.Collections;
using UnityEngine;

namespace Convai.Infrastructure.Networking.Native
{
    internal sealed class NativeMicrophoneSourceMonitor : MonoBehaviour
    {
        private Func<bool> _poll;
        private Coroutine _pollCoroutine;

        internal void StartPolling(Func<bool> poll, float intervalSeconds)
        {
            StopPolling();
            _poll = poll;
            _pollCoroutine = StartCoroutine(PollRoutine(Mathf.Max(0.1f, intervalSeconds)));
        }

        internal void StopPolling()
        {
            _poll = null;
            if (_pollCoroutine == null)
                return;

            StopCoroutine(_pollCoroutine);
            _pollCoroutine = null;
        }

        private IEnumerator PollRoutine(float intervalSeconds)
        {
            var wait = new WaitForSecondsRealtime(intervalSeconds);
            while (true)
            {
                if (_poll == null || !_poll.Invoke())
                {
                    _pollCoroutine = null;
                    yield break;
                }

                yield return wait;
            }
        }

        private void OnDisable() => StopPolling();
    }
}
