using System.IO;
using System.Text.Json;
using UsageBeacon.Models;

namespace UsageBeacon.Services;

public interface IAppSettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}

public sealed class AppSettingsStore : IAppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public AppSettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppDataPaths.DirectoryPath, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (JsonException)
        {
            PreserveCorruptFile();
            return new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var temporaryPath = _path + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private void PreserveCorruptFile()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var backupPath = _path + $".corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}";
            File.Move(_path, backupPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
