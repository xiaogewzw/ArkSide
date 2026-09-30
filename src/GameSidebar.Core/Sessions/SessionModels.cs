using GameSidebar.Core.Geometry;
using GameSidebar.Core.Profiles;

namespace GameSidebar.Core.Sessions;

public readonly record struct WindowId(string Value)
{
    public override string ToString() => Value;
}
public enum IdentityVerification { Verified, Insufficient, Mismatch, Destroyed }
public sealed record WindowIdentity(WindowId Id, int ProcessId, DateTimeOffset? ProcessStartedAt, IdentityVerification Verification);
public enum WindowErrorCode { ApiFailure, NativeLoadFailure, CaptureFailed, IdentityInsufficient, WindowDestroyed, PermissionDenied, InvalidConfiguration, NoConfiguration, NoCandidate, RegexTimeout, Cancelled, Unknown }
public sealed record WindowOperationError(WindowErrorCode Code, string Message, int? NativeErrorCode = null);
public sealed record WindowCandidate(
    WindowId Id, int ProcessId, string? Executable, string Title, string ClassName,
    DateTimeOffset? ProcessStartedAt, bool IsVisible, bool IsMinimized,
    bool IsToolWindow, bool IsCloaked, bool IsOwned, bool IsSelf,
    WindowOperationError? Error,
    string? ExecutablePath = null, string? BundleId = null, string? AppBundlePath = null,
    string Platform = "windows")
{
    public bool IsSelectable => ProcessId > 0 && IsVisible && !IsToolWindow && !IsCloaked && !IsSelf &&
        Error?.Code is not (WindowErrorCode.WindowDestroyed or WindowErrorCode.ApiFailure);
    public bool IsAutoEligible => IsSelectable && !IsOwned && Error is null;
    public string? ExclusionReason => !IsVisible ? "隐藏窗口" : IsToolWindow ? "工具窗口" :
        IsCloaked ? "DWM 隐藏窗口" : IsSelf ? "本程序窗口" : IsOwned ? "Owned window（仅可手动选择）" : Error?.Message;
}
public enum GameSessionState { Starting, NotConfigured, Searching, NeedsSelection, Binding, Validating, Ready, UnsupportedResolution, PreviewOnly, BoundUnavailable, GameLost, Unbound, Faulted }
public enum SelectionKind { None, Window, Profile }
public enum BindingMode { None, Auto, Manual }
public enum TargetCompatibility { Unknown, Verified, Unsupported }
public sealed record GameSessionSnapshot(
    Guid? SessionId, long BindingGeneration, long GeometryVersion,
    WindowIdentity? Identity, WindowGeometrySnapshot? Geometry,
    ProfileValidationResult? ProfileValidation, GameSessionState State,
    SelectionKind SelectionKind, BindingMode BindingMode,
    bool AutoDiscoveryEnabled, TargetCompatibility TargetCompatibility,
    bool IsForeground, bool IsMinimized, DateTimeOffset ObservedAt,
    WindowOperationError? Error)
{
    public bool CanPreview => SessionId is not null && Identity?.Verification == IdentityVerification.Verified &&
        !IsMinimized && Geometry?.Validity == GeometryValidity.Valid &&
        (Geometry.IsUsable || Geometry.Placement?.Frame.IsValid == true);
    public static GameSessionSnapshot Initial(DateTimeOffset now) => new(null, 0, 0, null, null, null,
        GameSessionState.Starting, SelectionKind.None, BindingMode.None, false,
        TargetCompatibility.Unknown, false, false, now, null);
}
