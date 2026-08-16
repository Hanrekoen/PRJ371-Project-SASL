using System;
using UnityEngine;

namespace Convai.Runtime.Embodiment
{
    /// <summary>
    ///     Encapsulates the "fall back to a runtime default when no asset is wired up"
    ///     pattern shared by every <see cref="IEmbodimentProfileReceiver"/>: a single
    ///     authored override slot, an internally-created default ScriptableObject when
    ///     the slot is empty, and disciplined cleanup that destroys only the helper-owned
    ///     default (never an asset reference supplied by the caller).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The helper centralizes the lifecycle so every profile-owning receiver shares one
    ///         implementation of the <c>_effectiveProfile</c> / <c>_createdDefaultProfile</c> pair,
    ///         instead of each maintaining its own and letting default-profile ownership diverge
    ///         between receivers.
    ///     </para>
    ///     <para>
    ///         <see cref="Resolve"/> is allocation-free after the first default-creation
    ///         and is safe to call from per-frame paths.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TProfile">
    ///     ScriptableObject profile type owned by the receiver.
    /// </typeparam>
    internal sealed class OwnedProfile<TProfile> where TProfile : ScriptableObject
    {
        private readonly Func<TProfile> _defaultFactory;
        private TProfile _effective;
        private bool _ownsCreatedInstance;

        /// <summary>
        ///     Creates a new owner. <paramref name="defaultFactory"/> must return a freshly
        ///     created (and ideally <see cref="HideFlags.HideAndDontSave"/>-flagged) instance.
        /// </summary>
        public OwnedProfile(Func<TProfile> defaultFactory)
        {
            _defaultFactory = defaultFactory ?? throw new ArgumentNullException(nameof(defaultFactory));
        }

        /// <summary>
        ///     The profile currently in use, or <c>null</c> until <see cref="Resolve"/> runs.
        /// </summary>
        public TProfile Effective => _effective;

        /// <summary>
        ///     Returns the authored profile when supplied, otherwise lazily creates and
        ///     returns the default instance. Releases the previous owned default when the
        ///     authored slot is non-null and differs from the cached effective profile.
        /// </summary>
        public TProfile Resolve(TProfile authored)
        {
            if (authored != null)
            {
                if (!ReferenceEquals(_effective, authored))
                {
                    ReleaseOwned();
                    _effective = authored;
                }
                return authored;
            }

            if (_effective == null)
            {
                _effective = _defaultFactory();
                _ownsCreatedInstance = _effective != null;
            }
            return _effective;
        }

        /// <summary>
        ///     Applies a new authored profile (or clears the override when <paramref name="authored"/> is null).
        ///     The next call to <see cref="Resolve"/> picks up the change.
        /// </summary>
        public void Apply(TProfile authored)
        {
            ReleaseOwned();
            _effective = authored;
        }

        /// <summary>
        ///     Destroys the helper-owned default if any, leaves authored references untouched.
        ///     Call from <c>OnDestroy</c> (and at the start of <c>Apply</c>).
        /// </summary>
        public void Release()
        {
            ReleaseOwned();
        }

        private void ReleaseOwned()
        {
            if (!_ownsCreatedInstance || _effective == null)
            {
                _effective = null;
                _ownsCreatedInstance = false;
                return;
            }

            // Destroy() is forbidden in EditMode (e.g. EditMode tests with [ExecuteAlways]).
            // DestroyImmediate must be used outside Play Mode.
            if (UnityEngine.Application.isPlaying)
                UnityEngine.Object.Destroy(_effective);
            else
                UnityEngine.Object.DestroyImmediate(_effective);

            _effective = null;
            _ownsCreatedInstance = false;
        }
    }
}
