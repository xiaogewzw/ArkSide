using GameSidebar.Core.Geometry;
using GameSidebar.Core.Profiles;
using GameSidebar.Core.Sessions;

namespace GameSidebar.Application.Abstractions;

public sealed record WindowCatalogResult(IReadOnlyList<WindowCandidate> Candidates, WindowOperationError? Error = null);
public sealed record WindowReadResult(WindowIdentity Identity, WindowGeometrySnapshot? Geometry,
    bool IsForeground, bool IsMinimized, WindowOperationError? Error = null);
public sealed record ProfileLoadResult(IReadOnlyList<GameWindowProfile> Profiles, WindowOperationError? Error = null);
public interface IWindowCatalog
{
    Task<WindowCatalogResult> EnumerateAsync(CancellationToken cancellationToken);
}
public interface IWindowGeometryProvider
{
    Task<WindowReadResult> ReadAsync(WindowIdentity identity, CancellationToken cancellationToken);
}
public interface IProfileRepository
{
    Task<ProfileLoadResult> LoadAsync(CancellationToken cancellationToken);
}
