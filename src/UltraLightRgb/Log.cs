namespace UltraLightRgb;

/// <summary>
/// Console plus a small rotating file. The service runs as SYSTEM without a console, so without
/// the file a failing device would go unnoticed.
/// </summary>
internal static class Log
{
    private const long MaxBytes = 1_000_000;

    private static readonly object Gate = new();
    private static string? _path;
    private static bool _fileBroken;

    /// <summary>Enables the file; without this call messages only go to stderr.</summary>
    public static void ToFile(string path)
    {
        lock (Gate)
        {
            _path = path;
            _fileBroken = false;
        }
    }

    public static void Write(string msg)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}";
        Console.Error.WriteLine(line);
        lock (Gate)
        {
            if (_path is null || _fileBroken) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > MaxBytes) File.Move(_path, _path + ".1", overwrite: true);
                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // reported once on stderr, then the file stays off instead of failing on every line
                _fileBroken = true;
                Console.Error.WriteLine($"Log file disabled ({_path}): {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
