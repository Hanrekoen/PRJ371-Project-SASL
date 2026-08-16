using System;
using System.Collections;
using UnityEngine;

namespace Convai.Runtime.Presentation.Services.Utilities
{
    /// <summary>
    ///     Utility class for fading CanvasGroup alpha with animations.
    ///     Provides fade in, fade out, and sequenced fade animations.
    /// </summary>
    /// <remarks>
    ///     Core utility component for transcript UI fade animations.
    /// </remarks>
    public class CanvasFader : MonoBehaviour
    {
        private float _currentAlpha;

        /// <summary>
        ///     Event called when the active fade animation is completed.
        /// </summary>
        public Action OnCurrentFadeCompleted;

        /// <summary>
        ///     Starts the fade in animation for the given CanvasGroup.
        /// </summary>
        /// <param name="canvasGroup">The CanvasGroup to fade in.</param>
        /// <param name="duration">The duration of the fade in animation.</param>
        public void StartFadeIn(CanvasGroup canvasGroup, float duration)
        {
            if (Mathf.Approximately(canvasGroup.alpha, 1)) return;

            StopAllCoroutines();
            StartCoroutine(FadeIn(canvasGroup, duration));
        }

        /// <summary>
        ///     Starts the fade out animation for the given CanvasGroup.
        /// </summary>
        /// <param name="canvasGroup">The CanvasGroup to fade out.</param>
        /// <param name="duration">The duration of the fade out animation.</param>
        public void StartFadeOut(CanvasGroup canvasGroup, float duration)
        {
            if (Mathf.Approximately(canvasGroup.alpha, 0)) return;

            StopAllCoroutines();
            StartCoroutine(FadeOut(canvasGroup, duration));
        }

        /// <summary>
        ///     Starts a sequence of fade in and fade out animations with a gap in between for the given CanvasGroup.
        /// </summary>
        /// <param name="canvasGroup">The CanvasGroup to animate.</param>
        /// <param name="fadeInDuration">The duration of the fade in animation.</param>
        /// <param name="fadeOutDuration">The duration of the fade out animation.</param>
        /// <param name="gapDuration">The duration of the gap between the fade in and fade out animations.</param>
        public void StartFadeInFadeOutWithGap(CanvasGroup canvasGroup, float fadeInDuration, float fadeOutDuration,
            float gapDuration)
        {
            StopAllCoroutines();
            StartCoroutine(FadeInFadeOutWithGap(canvasGroup, fadeInDuration, fadeOutDuration, gapDuration));
        }

        /// <summary>
        ///     Starts a sequence of fade out and fade in animations with a gap in between for the given CanvasGroup.
        /// </summary>
        /// <param name="canvasGroup">The CanvasGroup to animate.</param>
        /// <param name="fadeInDuration">The duration of the fade in animation.</param>
        /// <param name="fadeOutDuration">The duration of the fade out animation.</param>
        /// <param name="gapDuration">The duration of the gap between the fade out and fade in animations.</param>
        public void StartFadeOutFadeInWithGap(CanvasGroup canvasGroup, float fadeInDuration, float fadeOutDuration,
            float gapDuration)
        {
            StopAllCoroutines();
            StartCoroutine(FadeOutFadeInWithGap(canvasGroup, fadeInDuration, fadeOutDuration, gapDuration));
        }

        private void SetAlpha(CanvasGroup canvasGroup, float value)
        {
            _currentAlpha = value;
            canvasGroup.alpha = _currentAlpha;
        }

        /// <summary>
        ///     Coroutine for the fade in animation.
        /// </summary>
        /// <param name="canvasGroup">The CanvasGroup to fade in.</param>
        /// <param name="duration">The duration of the fade in animation.</param>
        private IEnumerator FadeIn(CanvasGroup canvasGroup, float duration)
        {
            float elapsedTime = 0.0f;
            while (_currentAlpha <= 1.0f)
            {
                SetAlpha(canvasGroup, elapsedTime / duration);
                elapsedTime += Time.deltaTime;
                yield return null;
            }

            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
            OnCurrentFadeCompleted?.Invoke();
        }

        /// <summary>
        ///     Coroutine for the fade out animation.
        /// </summary>
        /// <param name="canvasGroup">The CanvasGroup to fade out.</param>
        /// <param name="duration">The duration of the fade out animation.</param>
        private IEnumerator FadeOut(CanvasGroup canvasGroup, float duration)
        {
            float elapsedTime = 0.0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
            while (_currentAlpha >= 0.0f)
            {
                SetAlpha(canvasGroup, 1 - (elapsedTime / duration));
                elapsedTime += Time.deltaTime;
                yield return null;
            }

            OnCurrentFadeCompleted?.Invoke();
        }

        /// <summary>
        ///     Coroutine for a sequence of fade in and fade out animations with a gap in between.
        /// </summary>
        /// <param name="canvasGroup">The CanvasGroup to animate.</param>
        /// <param name="fadeInDuration">The duration of the fade in animation.</param>
        /// <param name="fadeOutDuration">The duration of the fade out animation.</param>
        /// <param name="gapDuration">The duration of the gap between the fade in and fade out animations.</param>
        private IEnumerator FadeInFadeOutWithGap(CanvasGroup canvasGroup, float fadeInDuration, float fadeOutDuration,
            float gapDuration)
        {
            float elapsedTime = 0.0f;

            while (_currentAlpha <= 1.0f)
            {
                SetAlpha(canvasGroup, elapsedTime / fadeInDuration);
                elapsedTime += Time.deltaTime;
                yield return null;
            }

            yield return new WaitForSeconds(gapDuration);

            elapsedTime = 0.0f;

            while (_currentAlpha >= 0.0f)
            {
                SetAlpha(canvasGroup, 1 - (elapsedTime / fadeOutDuration));
                elapsedTime += Time.deltaTime;
                yield return null;
            }

            OnCurrentFadeCompleted?.Invoke();
        }

        /// <summary>
        ///     Coroutine for a sequence of fade out and fade in animations with a gap in between.
        /// </summary>
        /// <param name="canvasGroup">The CanvasGroup to animate.</param>
        /// <param name="fadeInDuration">The duration of the fade in animation.</param>
        /// <param name="fadeOutDuration">The duration of the fade out animation.</param>
        /// <param name="gapDuration">The duration of the gap between the fade out and fade in animations.</param>
        private IEnumerator FadeOutFadeInWithGap(CanvasGroup canvasGroup, float fadeInDuration, float fadeOutDuration,
            float gapDuration)
        {
            float elapsedTime = 0.0f;

            while (_currentAlpha >= 0.0f)
            {
                SetAlpha(canvasGroup, 1 - (elapsedTime / fadeOutDuration));
                elapsedTime += Time.deltaTime;
                yield return null;
            }

            yield return new WaitForSeconds(gapDuration);

            elapsedTime = 0.0f;

            while (_currentAlpha <= 1.0f)
            {
                SetAlpha(canvasGroup, elapsedTime / fadeInDuration);
                elapsedTime += Time.deltaTime;
                yield return null;
            }

            OnCurrentFadeCompleted?.Invoke();
        }
    }
}
