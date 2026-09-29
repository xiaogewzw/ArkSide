using Microsoft.Extensions.Logging;

namespace GameSidebar.App.Storage;

public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly object _gate = new();
    public RollingFileLoggerProvider(string directory) { _directory = directory; Directory.CreateDirectory(directory); }
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    public void Dispose() { }
    private void Write(string line)
    {
        lock (_gate)
        {
            var path = Path.Combine(_directory, "game-sidebar.log");
            if (File.Exists(path) && new FileInfo(path).Length > 2_000_000)
            {
                for (var i = 2; i >= 1; i--)
                {
                    var source = i == 1 ? path : path + "." + (i - 1);
                    var target = path + "." + i;
                    if (File.Exists(source)) File.Move(source, target, true);
                }
            }
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }
    private sealed class FileLogger(RollingFileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            owner.Write($"{DateTimeOffset.UtcNow:O} {logLevel} {category}: {formatter(state, exception)} {exception}");
        }
    }
}
