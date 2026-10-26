namespace HexaWall;
internal sealed class LiteSettings : Form
{
    private readonly Settings settings;
    private readonly ConfigurationController controller;
    private readonly HotKeyWindow hotkey;
    private readonly TextBox folder = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox shortcut = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly ComboBox layout = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat };
    private readonly CheckBox all = new() { Text = "Aplicar en todos los monitores", AutoSize = true };
    private readonly CheckBox gaming = new() { Text = "Pausar en juegos a pantalla completa", AutoSize = true };
    private readonly CheckBox startup = new() { Text = "Iniciar automáticamente con Windows", AutoSize = true };
    private readonly Label status = new() { AutoSize = true, ForeColor = IconFactory.Accent };
    private bool updating, capturing;
    public LiteSettings(Settings settings, ConfigurationController controller, HotKeyWindow hotkey)
    {
        this.settings = settings; this.controller = controller; this.hotkey = hotkey;
        Text = "HEXAWALL · Configuración"; Icon = IconFactory.Create(); BackColor = Color.FromArgb(24, 26, 32); ForeColor = Color.White;
        Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(620, 660);
        FormBorderStyle = FormBorderStyle.None; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 2, RowCount = 13, AutoScroll = true };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115)); Controls.Add(grid);
        for (int row = 0; row < 13; row++) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Label TextLabel(string value) => new() { Text = value, AutoSize = true, Padding = new Padding(0, 10, 0, 8) };
        void Wide(Control control, int row) { grid.Controls.Add(control, 0, row); grid.SetColumnSpan(control, 2); }
        Wide(new Label { Text = "Configuración", Font = new Font("Segoe UI", 22), AutoSize = true }, 0);
        Wide(TextLabel("Carpeta de wallpapers"), 1); grid.Controls.Add(folder, 0, 2);
        var browse = Button("Examinar…"); grid.Controls.Add(browse, 1, 2);
        browse.Click += (_, _) => { using var dialog = new FolderBrowserDialog { SelectedPath = settings.Folder }; if (dialog.ShowDialog(this) == DialogResult.OK) { folder.Text = dialog.SelectedPath; Save(); } };
        Wide(TextLabel("Atajo global · haz clic y pulsa una combinación"), 3); Wide(shortcut, 4);
        shortcut.Enter += (_, _) => { capturing = true; hotkey.Suspend(); shortcut.Text = "Pulsa Ctrl / Alt / Shift / Win + tecla…"; };
        shortcut.Leave += (_, _) => FinishCapture();
        shortcut.KeyDown += (_, e) =>
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Escape) { FinishCapture(); ActiveControl = layout; return; }
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;
            uint modifiers = (e.Alt ? 1u : 0) | (e.Control ? 2u : 0) | (e.Shift ? 4u : 0);
            if (GetKeyState(0x5B) < 0 || GetKeyState(0x5C) < 0) modifiers |= 8;
            var change = settings.Copy(); change.HotKeyModifiers = modifiers; change.HotKeyVirtualKey = (uint)e.KeyCode;
            if (controller.Apply(change, out string error)) { capturing = false; LoadValues(); ActiveControl = layout; }
            else status.Text = error;
        };
        Wide(TextLabel("Diseño predeterminado"), 5); layout.Items.AddRange(Enum.GetNames<LayoutMode>()); Wide(layout, 6);
        layout.DrawMode = DrawMode.OwnerDrawFixed; layout.ItemHeight = 26;
        layout.DrawItem += (_, e) =>
        {
            using var background = new SolidBrush(Color.FromArgb(37, 42, 52)); e.Graphics.FillRectangle(background, e.Bounds);
            string text = e.Index >= 0 ? layout.Items[e.Index]?.ToString() ?? "" : settings.Layout.ToString();
            TextRenderer.DrawText(e.Graphics, text, Font, e.Bounds, Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        };
        layout.SelectedIndexChanged += (_, _) => Save();
        Wide(TextLabel("Modo ligero: cambio directo del fondo, sin superficies GPU de transición."), 7);
        Wide(all, 8); Wide(gaming, 9); Wide(startup, 10);
        foreach (var toggle in new[] { all, gaming, startup }) { toggle.FlatStyle = FlatStyle.Flat; toggle.Margin = new Padding(0, 9, 0, 9); toggle.CheckedChanged += (_, _) => Save(); }
        Wide(status, 11); var done = Button("Listo"); grid.Controls.Add(done, 1, 12); done.Click += (_, _) => Close(); CancelButton = done;
        foreach (var input in new Control[] { folder, shortcut, layout }) { input.BackColor = Color.FromArgb(37, 42, 52); input.ForeColor = Color.White; input.Margin = new Padding(0, 0, 8, 5); }
        LoadValues();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern short GetKeyState(int key);
    private static Button Button(string text) => new() { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(37, 42, 52), ForeColor = Color.White, Padding = new Padding(6), Dock = DockStyle.Fill };
    private void LoadValues()
    {
        updating = true; folder.Text = settings.Folder; shortcut.Text = HotKeyWindow.Format(settings.HotKeyModifiers, settings.HotKeyVirtualKey);
        layout.SelectedIndex = (int)settings.Layout; all.Checked = settings.AllMonitors; gaming.Checked = settings.PauseInFullscreen; startup.Checked = settings.StartWithWindows;
        updating = false;
    }
    private void Save()
    {
        if (updating) return;
        var change = settings.Copy(); change.Folder = folder.Text; change.Layout = (LayoutMode)Math.Max(0, layout.SelectedIndex);
        change.AllMonitors = all.Checked; change.PauseInFullscreen = gaming.Checked; change.StartWithWindows = startup.Checked;
        if (controller.Apply(change, out string error)) status.Text = "Guardado";
        else { status.Text = error; LoadValues(); }
    }
    private void FinishCapture() { if (capturing) { capturing = false; if (!hotkey.Resume(out string error)) status.Text = error; shortcut.Text = HotKeyWindow.Format(settings.HotKeyModifiers, settings.HotKeyVirtualKey); } }
    protected override void OnFormClosed(FormClosedEventArgs e) { FinishCapture(); base.OnFormClosed(e); }
    protected override void Dispose(bool disposing) { if (disposing) { Icon?.Dispose(); Font.Dispose(); } base.Dispose(disposing); }
}
