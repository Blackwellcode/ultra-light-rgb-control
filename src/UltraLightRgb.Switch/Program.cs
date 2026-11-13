namespace UltraLightRgb.Switch;

/// <summary>
/// Picks the LCD theme and exits. It only writes theme.txt: the running service polls that file,
/// so nothing of this tool has to stay in memory.
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

        if (args.Length == 1)
        {
            if (!ThemeCatalog.Exists(args[0]))
            {
                MessageBox.Show($"Unknown theme: {args[0]}", Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }
            return Save(args[0]) ? 0 : 1;
        }

        Application.Run(new SwitchForm());
        return 0;
    }

    public static bool Save(string id)
    {
        try
        {
            ThemeCatalog.WriteState(id);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Could not save the theme:\n{e.Message}\n\nIs the service installed? The installer creates {Path.GetDirectoryName(ThemeCatalog.StatePath)}.",
                Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }
}

internal sealed class SwitchForm : Form
{
    private const int ThumbW = 240, ThumbH = 180, LabelH = 28, Gap = 12, Border = 3, Cols = 3;
    private static readonly Color Back = Color.FromArgb(14, 16, 22), Idle = Color.FromArgb(40, 44, 56), Active = Color.FromArgb(0, 200, 255);

    private readonly Dictionary<string, Panel> _frames = [];
    private string _active;

    public SwitchForm()
    {
        Text = Program.Title;
        // window and taskbar show the application icon instead of the generic WinForms one
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        BackColor = Back;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        _active = CurrentTheme();

        int rows = (ThemeCatalog.All.Length + Cols - 1) / Cols;
        int cellW = ThumbW + 2 * Border, cellH = ThumbH + LabelH + 2 * Border;
        ClientSize = new Size(Gap + Cols * (cellW + Gap), Gap + rows * (cellH + Gap));

        for (int i = 0; i < ThemeCatalog.All.Length; i++)
        {
            var (id, title, _) = ThemeCatalog.All[i];
            using var preview = Renderer.Preview(id);

            var frame = new Panel
            {
                Bounds = new Rectangle(Gap + i % Cols * (cellW + Gap), Gap + i / Cols * (cellH + Gap), cellW, cellH),
                Padding = new Padding(Border),
                BackColor = Idle,
                Cursor = Cursors.Hand,
            };
            var image = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = preview.Snapshot() };
            var label = new Label
            {
                Dock = DockStyle.Bottom,
                Height = LabelH,
                Text = title,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                BackColor = Back,
                Font = new Font("Bahnschrift", 11f),
            };
            frame.Controls.Add(image);
            frame.Controls.Add(label);
            foreach (Control c in new Control[] { frame, image, label })
            {
                c.Click += (_, _) => Apply(id);
                // a double click also means "that's the one": pick and leave
                c.DoubleClick += (_, _) => Close();
            }
            Controls.Add(frame);
            _frames[id] = frame;
        }
        Mark();
    }

    private static string CurrentTheme()
    {
        try
        {
            return ThemeCatalog.ReadState();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Current theme not readable:\n{e.Message}", Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return ThemeCatalog.DefaultId;
        }
    }

    private void Apply(string id)
    {
        if (id == _active || !Program.Save(id)) return;
        _active = id;
        Mark();
    }

    private void Mark()
    {
        foreach (var (id, frame) in _frames) frame.BackColor = id == _active ? Active : Idle;
    }
}
