using SphereDesk.Core.Geometry;

namespace SphereDesk.Core.Models;

public enum ItemKind
{
    File,
    Folder,
    Shortcut,
    InternetShortcut,
}

public enum ItemAvailability
{
    Available,
    Missing,
    SourceUnavailable,
}

public enum KnownFolderKind
{
    UserDesktop,
    PublicDesktop,
}

public enum ScanStatus
{
    NotScanned,
    Succeeded,
    Failed,
    Skipped,
}

public enum OperationStatus
{
    Success,
    Cancelled,
    PartialSuccess,
    Failure,
}

public static class SourceRootIds
{
    public const string UserDesktop = "user-desktop";
    public const string PublicDesktop = "public-desktop";
}

public sealed record DesktopItem(
    string Id,
    string SourceRootId,
    string OriginalPath,
    string DisplayName,
    ItemKind Kind,
    string IconKey,
    bool Exists,
    ItemAvailability Availability,
    long MetadataVersion);

public sealed record SourceRoot(
    string Id,
    KnownFolderKind KnownFolderKind,
    string? ResolvedPath,
    ScanStatus LastScanStatus,
    string? FailureMessage);

public sealed class Group
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public List<string> ItemIds { get; set; } = [];
}

public sealed record LayoutSlot(string ItemId, Vector3 UnitPosition, int StableOrder);

public sealed record HotkeyGesture(int Modifiers, int VirtualKey)
{
    public const int ModAlt = 0x0001;
    public const int ModControl = 0x0002;
    public const int ModShift = 0x0004;
    public const int ModWin = 0x0008;

    public static HotkeyGesture Default { get; } = new(ModControl | ModAlt, 0x44);

    public bool IsValid => Modifiers != 0 && VirtualKey is > 0 and <= 0xFE;
}

public sealed record WindowBounds(double X, double Y, double Width, double Height);

public sealed class UserSettings
{
    public int SchemaVersion { get; set; } = SettingsLimits.CurrentSchemaVersion;
    public WindowBounds? WindowBoundsDip { get; set; }
    public string? MonitorId { get; set; }
    public bool AutoRotateEnabled { get; set; } = true;
    public bool LaunchAtStartup { get; set; } = true;
    public double AngularSpeed { get; set; } = 0.15;
    public bool ReducedMotion { get; set; }
    public bool MotionPreferenceUserSet { get; set; }
    public int FallbackScanIntervalSeconds { get; set; } = 60;
    public List<Group> Groups { get; set; } = [];
    public List<LayoutSlot> LayoutSlots { get; set; } = [];
    public HotkeyGesture Hotkey { get; set; } = HotkeyGesture.Default;

    public UserSettings Clone()
    {
        return new UserSettings
        {
            SchemaVersion = SchemaVersion,
            WindowBoundsDip = WindowBoundsDip,
            MonitorId = MonitorId,
            AutoRotateEnabled = AutoRotateEnabled,
            LaunchAtStartup = LaunchAtStartup,
            AngularSpeed = AngularSpeed,
            ReducedMotion = ReducedMotion,
            MotionPreferenceUserSet = MotionPreferenceUserSet,
            FallbackScanIntervalSeconds = FallbackScanIntervalSeconds,
            Groups = Groups.Select(group => new Group
            {
                Id = group.Id,
                Name = group.Name,
                SortOrder = group.SortOrder,
                ItemIds = [.. group.ItemIds],
            }).ToList(),
            LayoutSlots = [.. LayoutSlots],
            Hotkey = Hotkey,
        };
    }
}

public sealed record ProjectedItem(
    string ItemId,
    DipPoint CenterDip,
    double Depth,
    double Scale,
    double Opacity,
    DipRect HitBoundsDip,
    int DrawOrder,
    bool IsBackFacing,
    bool ShowLabel);

public sealed record ItemOperationResult(string ItemId, OperationStatus Status, string? UserMessage, int? ErrorCode);

public sealed record OperationResult(
    OperationStatus Status,
    string? UserMessage,
    int? ErrorCode,
    IReadOnlyList<ItemOperationResult> PerItemResults)
{
    public static OperationResult Ok(string? message = null) =>
        new(OperationStatus.Success, message, null, []);

    public static OperationResult Fail(string message, int? errorCode = null) =>
        new(OperationStatus.Failure, message, errorCode, []);

    public static OperationResult Cancel(string message) =>
        new(OperationStatus.Cancelled, message, null, []);
}

public sealed record DesktopSnapshot(
    long Generation,
    IReadOnlyList<SourceRoot> Roots,
    IReadOnlyList<DesktopItem> Items,
    IReadOnlyDictionary<string, bool> CaseSensitiveByRoot,
    DateTimeOffset ScannedAt);

public static class SettingsLimits
{
    public const int CurrentSchemaVersion = 1;
    public const double MinAngularSpeed = 0;
    public const double MaxAngularSpeed = 2;
    public const int MinFallbackScanSeconds = 15;
    public const int MaxFallbackScanSeconds = 3600;
}
