using System.Runtime.Versioning;
using GameSidebar.Application.Abstractions;
using GameSidebar.Core.Sessions;
using GameSidebar.Platform.Windows.Interop;

namespace GameSidebar.Platform.Windows.Discovery;

[SupportedOSPlatform("windows")]
public sealed class WindowsWindowCatalog : IWindowCatalog
{
    public Task<WindowCatalogResult> EnumerateAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        var candidates = new List<WindowCandidate>();
        WindowOperationError? error = null;
        var success = NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (cancellationToken.IsCancellationRequested) return false;
            try { candidates.Add(WindowNative.Candidate(hwnd)); }
            catch (Exception e) { candidates.Add(new(WindowNative.Id(hwnd), 0, null, "", "", null,
                false, false, false, false, false, false, new(WindowErrorCode.ApiFailure, e.Message))); }
            return true;
        }, 0);
        cancellationToken.ThrowIfCancellationRequested();
        if (!success) error = new(WindowErrorCode.ApiFailure, "EnumWindows 调用失败", WindowNative.LastError());
        return new WindowCatalogResult(candidates, error);
    }, cancellationToken);
}
