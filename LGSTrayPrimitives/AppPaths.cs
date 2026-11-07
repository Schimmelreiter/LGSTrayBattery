namespace LGSTrayPrimitives;

/// <summary>
/// Where LGSTray keeps its files: %LOCALAPPDATA%\LGSTray (settings, logs, crash logs).
/// The program folder only holds the template appsettings.default.toml. On first start, an existing
/// appsettings.toml in the program folder (from 3.0.5 or older) is taken over, otherwise the template.
/// </summary>
public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LGSTray");

    public static string SettingsFile => Path.Combine(DataDirectory, "appsettings.toml");

    public static string TemplateSettingsFile => Path.Combine(AppContext.BaseDirectory, "appsettings.default.toml");

    /// <summary>appsettings.toml in the program folder (versions up to 3.0.5).</summary>
    public static string OldSettingsFile => Path.Combine(AppContext.BaseDirectory, "appsettings.toml");

    /// <summary>Creates the data folder and, if missing, the settings file from the program folder.</summary>
    public static string EnsureSettingsFile()
    {
        Directory.CreateDirectory(DataDirectory);
        string source = File.Exists(OldSettingsFile) ? OldSettingsFile : TemplateSettingsFile;
        if (!File.Exists(SettingsFile) && File.Exists(source))
        {
            try
            {
                File.Copy(source, SettingsFile, false);
                AppLog.Write($"Settings copied from {source} to {SettingsFile}");
            }
            catch (IOException)
            {
                // copied at the same time by the other process
            }
        }
        return SettingsFile;
    }
}
