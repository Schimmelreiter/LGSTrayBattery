namespace LGSTrayPrimitives;

/// <summary>
/// Minimal file log in %LOCALAPPDATA%\LGSTray\lgstray.log. Used for errors that would otherwise be
/// swallowed (startup failures, HTTP server, MQTT). Never logs passwords.
/// </summary>
public static class AppLog
{
    private const long MaxSize = 1024 * 1024;
    private static readonly object _lock = new();

    public static string FilePath => Path.Combine(AppPaths.DataDirectory, "lgstray.log");

    public static void Write(string message, Exception? ex = null)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                var path = FilePath;
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxSize)
                {
                    File.Move(path, path + ".1", true);
                }

                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}";
                if (ex != null)
                {
                    line += Environment.NewLine + ex;
                }
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never crash the application.
        }
    }
}
