namespace GameSidebar.Application.Sessions;

/// <summary>Runs exactly one window sample or discovery operation at a time.</summary>
public sealed class WindowTracker
{
    private readonly Func<CancellationToken, Task<TimeSpan>> _tick;
    private readonly TimeProvider _time;
    private Task? _run;
    public WindowTracker(Func<CancellationToken, Task<TimeSpan>> tick, TimeProvider time)
    { _tick = tick; _time = time; }
    public void Start(CancellationToken token) => _run ??= Task.Run(() => RunAsync(token));
    public Task Completion => _run ?? Task.CompletedTask;
    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var delay = await _tick(token);
                await Task.Delay(delay, _time, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
        }
    }
}
