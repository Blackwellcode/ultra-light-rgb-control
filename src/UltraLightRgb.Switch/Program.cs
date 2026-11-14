namespace UltraLightRgb.Switch;

/// <summary>
/// Theme and settings window of Ultra Light RGB Control. It only writes small files the running
/// service polls, so nothing of it stays in memory once it is closed.
/// </summary>
internal static class Program
{
    public const string Title = "Ultra Light RGB Control";

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        // UltraLightRgb.Switch.exe <theme>: pick a theme without opening the window
        if (args.Length == 1)
        {
            if (!ThemeCatalog.Exists(args[0]))
            {
                MessageBox.Show($"Unknown theme: {args[0]}", Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }
            try
            {
                ThemeCatalog.WriteState(args[0]);
                return 0;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show($"Could not save the theme:\n{e.Message}\n\nIs the service installed? The installer creates {Path.GetDirectoryName(ThemeCatalog.StatePath)}.",
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        Application.Run(new MainForm());
        return 0;
    }
}
