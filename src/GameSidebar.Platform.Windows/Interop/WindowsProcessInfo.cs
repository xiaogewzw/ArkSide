using System.Text;
using GameSidebar.Core.Sessions;

namespace GameSidebar.Platform.Windows.Interop;

internal sealed record WindowsProcessInfo(string? Executable, string? ExecutablePath, DateTimeOffset? StartedAt, WindowOperationError? Error)
{
    public static WindowsProcessInfo Read(uint pid)
    {
        if (pid == 0) return new(null, null, null, new(WindowErrorCode.WindowDestroyed, "进程 ID 无效"));
        var handle = NativeMethods.OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (handle == 0)
        {
            var code = WindowNative.LastError();
            return new(null, null, null, new(code == 5 ? WindowErrorCode.PermissionDenied : WindowErrorCode.ApiFailure,
                "OpenProcess 有限查询权限失败", code));
        }
        try
        {
            WindowOperationError? error = null;
            DateTimeOffset? started = null;
            string? executable = null;
            string? executablePath = null;
            if (NativeMethods.GetProcessTimes(handle, out var created, out _, out _, out _))
            {
                var ticks = ((long)created.High << 32) | created.Low;
                started = DateTimeOffset.FromFileTime(ticks).ToUniversalTime();
            }
            else error = new(WindowErrorCode.IdentityInsufficient, "GetProcessTimes 无法读取创建时间", WindowNative.LastError());
            var name = new StringBuilder(32768);
            uint length = (uint)name.Capacity;
            if (NativeMethods.QueryFullProcessImageName(handle, 0, name, ref length))
            {
                executablePath = name.ToString();
                executable = Path.GetFileName(executablePath);
            }
            else error ??= new(WindowErrorCode.PermissionDenied, "QueryFullProcessImageName 无法读取进程路径", WindowNative.LastError());
            return new(executable, executablePath, started, error);
        }
        finally { NativeMethods.CloseHandle(handle); }
    }
}
