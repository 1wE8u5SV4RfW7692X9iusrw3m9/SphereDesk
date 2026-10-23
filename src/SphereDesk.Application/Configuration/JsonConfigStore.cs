using SphereDesk.Application.Abstractions;
using SphereDesk.Core.Configuration;
using SphereDesk.Core.Models;

namespace SphereDesk.Application.Configuration;

public sealed class JsonConfigStore : IConfigStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _settingsPath;
    private readonly string _backupPath;

    public JsonConfigStore(string? directory = null)
    {
        var root = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SphereDesk");
        Directory.CreateDirectory(root);
        _settingsPath = Path.Combine(root, "settings.json");
        _backupPath = Path.Combine(root, "settings.bak.json");
    }

    public async Task<ConfigLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new ConfigLoadResult(new UserSettings(), false, false, null);
            }

            var primary = ReadFile(_settingsPath);
            if (!primary.IsCorrupt)
            {
                return new ConfigLoadResult(primary.Settings, false, false, primary.UserMessage);
            }

            Quarantine(_settingsPath);
            if (File.Exists(_backupPath))
            {
                var backup = ReadFile(_backupPath);
                if (!backup.IsCorrupt)
                {
                    var message = "配置文件损坏，已从上一份备份恢复。";
                    if (!string.IsNullOrWhiteSpace(backup.UserMessage))
                    {
                        message += Environment.NewLine + backup.UserMessage;
                    }

                    return new ConfigLoadResult(backup.Settings, true, true, message);
                }

                Quarantine(_backupPath);
            }

            return new ConfigLoadResult(
                new UserSettings(),
                false,
                true,
                "配置文件损坏，已隔离并恢复为默认设置。已有分组需要重新创建。");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(UserSettings settings, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var tempPath = _settingsPath + ".tmp";
        try
        {
            var json = SettingsMigration.Write(settings);
            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
            using (var stream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                stream.Flush(true);
            }

            if (File.Exists(_settingsPath))
            {
                File.Replace(tempPath, _settingsPath, _backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, _settingsPath);
            }
        }
        finally
        {
            _gate.Release();
            if (File.Exists(tempPath))
            {
                TryDelete(tempPath);
            }
        }
    }

    private static SettingsLoadOutcome ReadFile(string path)
    {
        try
        {
            return SettingsMigration.Read(File.ReadAllText(path));
        }
        catch (IOException)
        {
            return SettingsLoadOutcome.Corrupt("配置无法读取。");
        }
        catch (UnauthorizedAccessException)
        {
            return SettingsLoadOutcome.Corrupt("配置无法读取。");
        }
    }

    private static void Quarantine(string path)
    {
        var isolated = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".json";
        try
        {
            File.Move(path, isolated, overwrite: false);
        }
        catch (IOException)
        {
            TryDelete(path);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
