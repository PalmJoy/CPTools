using CPSystemMonitor.Core.Model;
namespace CPSystemMonitor.Core.Storage;
public sealed class AppDataStore
{
    public DataPaths Paths { get; }
    public VersionedJsonStore<AppSettings> SettingsStore { get; }
    public bool SavingSuspended { get; private set; }
    private AppSettings? _settings;
    public AppDataStore(DataPaths paths)
    {
        Paths = paths;
        SettingsStore = new(paths.SettingsFile, AppSettings.CurrentSchemaVersion, () => new(),
            backupDirectory: paths.BackupDirectory, backupGenerations: () => _settings?.Data.BackupGenerations ?? 10,
            normalize: s => { s.Normalize(); _settings = s; }, backupMinInterval: TimeSpan.FromMinutes(10));
    }
    public void SaveSettings(AppSettings settings) { if (!SavingSuspended) { _settings = settings; SettingsStore.Save(settings); } }
    public void Import(ArchiveContents contents, DateTime now)
    {
        if (SettingsStore.IsReadOnly) throw new InvalidOperationException("Settings are read-only.");
        Paths.EnsureCreated();
        if (contents.Settings is { } text)
        {
            if (File.Exists(Paths.SettingsFile)) File.Copy(Paths.SettingsFile, Path.Combine(Paths.BackupDirectory, $"import-before-settings-{now:yyyyMMdd-HHmmssfff}.json"));
            File.WriteAllText(Paths.SettingsFile + ".import", text); File.Move(Paths.SettingsFile + ".import", Paths.SettingsFile, true);
        }
        if (contents.Files is { Count: > 0 })
        {
            Directory.CreateDirectory(Paths.BackgroundDirectory);
            foreach (var (name, bytes) in contents.Files) File.WriteAllBytes(Path.Combine(Paths.BackgroundDirectory, Path.GetFileName(name)), bytes);
        }
        SavingSuspended = true;
    }
}
