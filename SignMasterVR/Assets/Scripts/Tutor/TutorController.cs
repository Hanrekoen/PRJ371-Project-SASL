using UnityEngine;

namespace SignMasterVR.Tutor
{
    /// <summary>
    /// PHASE 8/9 — Drives the tutor avatar. LessonManager is the only thing that
    /// should call into this; it never decides lesson flow itself.
    ///
    /// THIS WEEK'S PLACEHOLDER DESIGN: since there's no finished avatar or real
    /// per-sign animation set yet, every state plays one shared "Active" Animator
    /// trigger (a short generic motion) and, more importantly, changes the color
    /// of a small `stateLight` object so it's obvious at a glance what the tutor
    /// is doing (grey=idle, blue=welcome, yellow=demonstrating, green=correct,
    /// red=try again, gold=complete). This is built automatically by
    /// Tools > SignMasterVR > 2 Build Placeholder Tutor Prefab.
    ///
    /// UPGRADE PATH: once a real rigged avatar + real per-sign clips exist, give
    /// each GestureData asset its own Animator trigger name in
    /// `tutorAnimationTrigger` (e.g. "Sign_A") and add a matching state to
    /// TutorAnimator.controller — Demonstrate() already checks for a matching
    /// parameter and will use it automatically, no other script needs to change.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class TutorController : MonoBehaviour
    {
        [Header("Refs")]
        public Animator animator;
        public AudioSource voiceSource;

        [Tooltip("Placeholder-only: a small object whose color signals tutor state. Safe to leave null once a real avatar/animations replace this.")]
        public Renderer stateLight;

        [Header("Placeholder state colors")]
        public Color idleColor = Color.white;
        public Color welcomeColor = new Color(0.2f, 0.5f, 0.9f);
        public Color explainColor = new Color(0.3f, 0.8f, 0.8f);
        public Color demonstrateColor = Color.yellow;
        public Color correctColor = Color.green;
        public Color tryAgainColor = Color.red;
        public Color completeColor = new Color(1f, 0.65f, 0f);

        private void Reset() => animator = GetComponent<Animator>();

        public void PlayIdle() { Trigger(); SetLight(idleColor); }
        public void PlayWelcome() { Trigger(); SetLight(welcomeColor); }
        public void PlayExplain() { Trigger(); SetLight(explainColor); }
        public void PlayCorrect() { Trigger(); SetLight(correctColor); }
        public void PlayTryAgain() { Trigger(); SetLight(tryAgainColor); }
        public void PlayComplete() { Trigger(); SetLight(completeColor); }

        /// <summary>Demonstrates a specific sign. animationTrigger comes from GestureData.tutorAnimationTrigger.</summary>
        public void Demonstrate(string animationTrigger)
        {
            Debug.Log($"[TutorController] Demonstrating: {animationTrigger}");
            if (!string.IsNullOrEmpty(animationTrigger) && HasParameter(animationTrigger))
                animator.SetTrigger(animationTrigger);
            else
                Trigger();
            SetLight(demonstrateColor);
        }

        public void Speak(AudioClip clip)
        {
            if (clip == null || voiceSource == null) return;
            voiceSource.Stop();
            voiceSource.clip = clip;
            voiceSource.Play();
        }

        private void Trigger()
        {
            if (animator != null) animator.SetTrigger("Active");
        }

        private void SetLight(Color c)
        {
            if (stateLight == null) return;
            var mat = stateLight.material;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            else if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        }

        private bool HasParameter(string name)
        {
            if (animator == null) return false;
            foreach (var p in animator.parameters)
                if (p.name == name) return true;
            return false;
        }
    }
}
