using Convai.Runtime.Animation;
using Convai.Runtime.Embodiment;
using UnityEngine;

namespace Convai.Modules.LipSync
{
    /// <summary>
    ///     Speech energy provider adapter that bridges <see cref="ConvaiLipSyncComponent" />
    ///     to the <see cref="ISpeechEnergyProvider" /> contract used by embodiment modules.
    ///     Automatically registers itself with the parent <see cref="EmbodimentContext" />
    ///     during <see cref="OnEnable" /> to eliminate the need for manual component scanning,
    ///     and drives its own <see cref="Sample" /> every Cognition tick (see
    ///     <see cref="IEmbodimentTickable" />) so <see cref="Current" /> stays up to date.
    ///     When the adapter is auto-provisioned by <c>EmbodimentLipSyncBridge</c> from a
    ///     consumer's own Cognition tick, the scheduler appends the new tickable to the
    ///     Cognition bucket after the consumer that triggered the provisioning, so that
    ///     consumer reads a value that is one Cognition tick behind for that first tick —
    ///     consistent with other lazily-provisioned sources (e.g. <c>EmotionStateSource</c>,
    ///     <c>ConversationFlowSource</c>) and negligible against the ~80&#160;ms RMS window
    ///     this value is smoothed over.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class ConvaiLipSyncSpeechEnergyAdapter : MonoBehaviour, IConfigurableSpeechEnergyProvider, IEmbodimentTickable
    {
        private ConvaiLipSyncComponent _lipSync;
        private LipSyncSpeechEnergyProvider _provider;
        private EmbodimentContext _context;
        private CharacterServiceRegistry.ServiceToken _token;
        private bool _tickRegistered;

        public float Current => _provider?.Current ?? 0f;

        EmbodimentTickPhase IEmbodimentTickable.Phase => EmbodimentTickPhase.Cognition;

        private void Awake()
        {
            // Deliberately visible. This component is auto-provisioned onto the user's character, and
            // a component they cannot see is a component they cannot debug — the same reason
            // EmbodimentContext stopped hiding itself.
            EnsureProvider();
        }

        private void OnEnable()
        {
            RegisterWithContext();
        }

        private void OnDisable()
        {
            UnregisterFromContext();
        }

        void IEmbodimentTickable.EmbodimentTick(float deltaTime)
        {
            Sample(deltaTime);
        }

        public void Configure(float windowSeconds)
        {
            EnsureProvider();
            _provider?.Configure(windowSeconds);
        }

        public void Sample(float deltaTime)
        {
            EnsureProvider();
            _provider?.Sample(deltaTime);
        }

        private bool EnsureProvider()
        {
            if (_provider != null) return true;

            _lipSync = GetComponentInParent<ConvaiLipSyncComponent>(true);
            if (_lipSync == null)
                _lipSync = GetComponentInChildren<ConvaiLipSyncComponent>(true);
            if (_lipSync == null) return false;

            _provider = new LipSyncSpeechEnergyProvider(_lipSync);
            return true;
        }

        private void RegisterWithContext()
        {
            if (_context != null) return;
            if (!EnsureProvider()) return;
            if (!EmbodimentContext.TryResolve(this, out _context)) return;

            _token = _context.Provide<ISpeechEnergyProvider>(this);

            // Tick registration is idempotent from the scheduler's side (HashSet-backed), but
            // guard locally too so a re-entrant OnEnable (e.g. hideFlags churn in edit mode)
            // never queues a duplicate pending-change entry.
            if (!_tickRegistered)
            {
                _context.EnsureTickScheduler()?.Register(this);
                _tickRegistered = true;
            }
        }

        private void UnregisterFromContext()
        {
            if (_tickRegistered)
            {
                _context?.TickScheduler?.Unregister(this);
                _tickRegistered = false;
            }

            if (_context == null) return;

            _token.Release();
            _token = default;
            _context = null;
        }
    }
}
