using System;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Adapter that bridges a delegate-based dispatcher to the <see cref="IMainThreadDispatcher" /> interface.
    ///     Enables dependency injection of main thread dispatch capability.
    /// </summary>
    public class MainThreadDispatcherAdapter : IMainThreadDispatcher
    {
        private readonly Func<Action, bool> _postFunc;

        /// <summary>
        ///     Creates a new adapter wrapping the specified dispatch function.
        /// </summary>
        /// <param name="postFunc">Function that dispatches an action and returns success status.</param>
        /// <exception cref="ArgumentNullException">Thrown if postFunc is null.</exception>
        public MainThreadDispatcherAdapter(Func<Action, bool> postFunc)
        {
            _postFunc = postFunc ?? throw new ArgumentNullException(nameof(postFunc));
        }

        /// <inheritdoc />
        public bool TryDispatch(Action action) => _postFunc.Invoke(action);
    }
}
