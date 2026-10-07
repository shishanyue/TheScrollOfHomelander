namespace BetterTaiwuScroll.Frontend;

// Owns only preview freshness. Stopping a batch invalidates its callbacks without
// cancelling a craft that was already submitted to the game.
internal sealed class MakePreviewBarrier
{
    private int _generation;
    private bool _refreshQueued;

    internal bool TryQueueRefresh()
    {
        if (_refreshQueued) return false;
        _refreshQueued = true;
        return true;
    }

    internal Request Begin()
    {
        _refreshQueued = false;
        return new Request(this, ++_generation);
    }

    internal void Invalidate()
    {
        _generation++;
        _refreshQueued = false;
    }

    internal sealed class Request
    {
        private readonly MakePreviewBarrier _owner;
        private readonly int _generation;
        private int _pendingCallbacks;
        private bool _stale;

        internal Request(MakePreviewBarrier owner, int generation)
        {
            _owner = owner;
            _generation = generation;
        }

        internal bool IsCurrent => !_stale && _owner._generation == _generation;
        internal bool IsReady => IsCurrent && _pendingCallbacks == 0;
        internal void AddCallback() => _pendingCallbacks++;
        internal void MarkStale() => _stale = true;

        internal bool TryAcceptCallback()
        {
            if (!IsCurrent || _pendingCallbacks <= 0) return false;
            _pendingCallbacks--;
            return true;
        }
    }
}
