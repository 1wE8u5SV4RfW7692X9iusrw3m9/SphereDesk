using System.Text.Json;
using SphereDesk.Core.Geometry;
using SphereDesk.Core.Models;

namespace SphereDesk.Core.Configuration;

public sealed record SettingsLoadOutcome(
    UserSettings Settings,
    bool IsCorrupt,
    bool HasWarnings,
    string? UserMessage)
{
    public static SettingsLoadOutcome Success(UserSettings settings, IReadOnlyList<string> warnings)
    {
        var message = warnings.Count == 0 ? null : string.Join(Environment.NewLine, warnings);
        return new SettingsLoadOutcome(settings, false, warnings.Count > 0, message);
    }

    public static SettingsLoadOutcome Corrupt(string reason) =>
        new(new UserSettings(), true, false, reason);
}

public static class SettingsMigration
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static SettingsLoadOutcome Read(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return SettingsLoadOutcome.Corrupt("配置不是对象。");
            }

            if (document.RootElement.TryGetProperty("schemaVersion", out var versionElement))
            {
                if (!versionElement.TryGetInt32(out var version))
                {
                    return SettingsLoadOutcome.Corrupt("schemaVersion 无效。");
                }

                if (version > SettingsLimits.CurrentSchemaVersion)
                {
                    return SettingsLoadOutcome.Corrupt("配置版本过新，无法读取。");
                }
            }

            var raw = JsonSerializer.Deserialize<UserSettings>(json, Options);
            if (raw is null)
            {
                return SettingsLoadOutcome.Corrupt("配置为空。");
            }

            return SettingsLoadOutcome.Success(raw, Normalize(raw));
        }
        catch (JsonException)
        {
            return SettingsLoadOutcome.Corrupt("配置无法解析。");
        }
    }

    public static string Write(UserSettings settings)
    {
        settings.SchemaVersion = SettingsLimits.CurrentSchemaVersion;
        return JsonSerializer.Serialize(settings, Options);
    }

    public static List<string> Normalize(UserSettings settings)
    {
        var warnings = new List<string>();
        settings.SchemaVersion = SettingsLimits.CurrentSchemaVersion;
        if (!double.IsFinite(settings.AngularSpeed) || settings.AngularSpeed < SettingsLimits.MinAngularSpeed || settings.AngularSpeed > SettingsLimits.MaxAngularSpeed)
        {
            settings.AngularSpeed = Math.Clamp(double.IsFinite(settings.AngularSpeed) ? settings.AngularSpeed : 0.15, SettingsLimits.MinAngularSpeed, SettingsLimits.MaxAngularSpeed);
            warnings.Add("旋转速度超出范围，已改回有效值。");
        }

        if (settings.FallbackScanIntervalSeconds is < SettingsLimits.MinFallbackScanSeconds or > SettingsLimits.MaxFallbackScanSeconds)
        {
            settings.FallbackScanIntervalSeconds = Math.Clamp(
                settings.FallbackScanIntervalSeconds,
                SettingsLimits.MinFallbackScanSeconds,
                SettingsLimits.MaxFallbackScanSeconds);
            warnings.Add("补扫间隔超出范围，已改回有效值。");
        }

        if (settings.Hotkey is null || !settings.Hotkey.IsValid)
        {
            settings.Hotkey = HotkeyGesture.Default;
            warnings.Add("热键无效，已恢复为 Ctrl+Alt+D。");
        }

        if (settings.WindowBoundsDip is { } bounds &&
            (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) || bounds.Width < 320 || bounds.Height < 240))
        {
            settings.WindowBoundsDip = null;
            warnings.Add("窗口位置无效，已改为默认位置。");
        }

        settings.Groups ??= [];
        settings.LayoutSlots ??= [];
        settings.Groups = settings.Groups
            .Where(group => !string.IsNullOrWhiteSpace(group.Id) && !string.IsNullOrWhiteSpace(group.Name))
            .Select(group =>
            {
                group.Name = group.Name.Trim();
                group.ItemIds = group.ItemIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList() ?? [];
                return group;
            })
            .GroupBy(group => group.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(group => group.SortOrder)
            .ToList();

        settings.LayoutSlots = settings.LayoutSlots
            .Where(slot => !string.IsNullOrWhiteSpace(slot.ItemId) && slot.UnitPosition.IsFinite && slot.UnitPosition.LengthSquared > 1e-8)
            .GroupBy(slot => slot.ItemId, StringComparer.Ordinal)
            .Select(group =>
            {
                var slot = group.First();
                return slot with { UnitPosition = slot.UnitPosition.Normalized() };
            })
            .ToList();

        return warnings;
    }
}
