namespace GameSidebar.Application.Tests;

internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<Timer> _timers = [];
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
    private TaskCompletionSource _timerCreated = NewSignal();
    public override DateTimeOffset GetUtcNow() { lock (_gate) return _now; }
    public Task TimerCreated { get { lock (_gate) return _timerCreated.Task; } }
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (_gate)
        {
            var timer = new Timer(this, callback, state, dueTime, period);
            _timers.Add(timer);
            _timerCreated.TrySetResult();
            return timer;
        }
    }
    public void Advance(TimeSpan duration)
    {
        List<Timer> due;
        lock (_gate)
        {
            _now += duration;
            due = _timers.Where(t => !t.Disposed && t.DueAt <= _now).ToList();
            foreach (var timer in due) timer.Reschedule();
            _timerCreated = NewSignal();
        }
        foreach (var timer in due) timer.Fire();
    }
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class Timer : ITimer
    {
        private readonly ManualTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private TimeSpan _period;
        public DateTimeOffset DueAt { get; private set; }
        public bool Disposed { get; private set; }
        public Timer(ManualTimeProvider owner, TimerCallback callback, object? state, TimeSpan due, TimeSpan period)
        { _owner = owner; _callback = callback; _state = state; Change(due, period); }
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_owner._gate)
            {
                if (Disposed) return false;
                _period = period;
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : _owner._now + dueTime;
                return true;
            }
        }
        public void Reschedule() => DueAt = _period == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : _owner._now + _period;
        public void Fire() { if (!Disposed) _callback(_state); }
        public void Dispose() { lock (_owner._gate) Disposed = true; }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
