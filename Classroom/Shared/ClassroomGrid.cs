namespace Classroom;
sealed class ClassroomGrid : UserControl
{
    sealed class Tile : Panel
    {
        public readonly PictureBox Preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(18, 26, 40) };
        public readonly Label Updated = new() { Dock = DockStyle.Bottom, Height = 23, ForeColor = Design.Muted, Text = "Ожидание первого кадра", Padding = new Padding(8, 2, 0, 0) };
        public Tile(string name, Action open)
        {
            BackColor = Color.White; Margin = new Padding(0, 0, 12, 12); Padding = new Padding(1); Cursor = Cursors.Hand;
            var title = new Label { Text = name, Dock = DockStyle.Top, Height = 32, ForeColor = Design.Ink, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Padding = new Padding(8, 7, 0, 0), AutoEllipsis = true };
            Controls.Add(Preview); Controls.Add(Updated); Controls.Add(title);
            foreach (Control child in Controls) child.Click += (_, _) => open(); Click += (_, _) => open();
        }
        protected override void Dispose(bool disposing) { if (disposing) Preview.Image?.Dispose(); base.Dispose(disposing); }
    }
    readonly FlowLayoutPanel flow = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, Padding = new Padding(12), BackColor = Design.Surface };
    readonly Dictionary<object, Tile> tiles = [];
    readonly Label empty = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Design.Muted, Font = new("Segoe UI", 14), Text = "Нет подключённых учеников\n\nЗапустите ScreenLink USER на компьютерах класса" };
    bool arranging;
    public event Action<object>? OpenRequested;
    public int Count => tiles.Count;
    public ClassroomGrid() { Dock = DockStyle.Fill; Controls.Add(flow); Controls.Add(empty); empty.BringToFront(); flow.SizeChanged += (_, _) => Arrange(); }
    public void Add(object key, string name)
    {
        if (tiles.ContainsKey(key)) return;
        var tile = new Tile(name, () => OpenRequested?.Invoke(key)); tiles.Add(key, tile); flow.Controls.Add(tile); empty.Visible = false; Arrange();
    }
    public void UpdateFrame(object key, Bitmap thumbnail)
    {
        if (!tiles.TryGetValue(key, out var tile)) { thumbnail.Dispose(); return; }
        var old = tile.Preview.Image; tile.Preview.Image = thumbnail; old?.Dispose(); tile.Updated.Text = "Обновлено · " + DateTime.Now.ToString("HH:mm:ss");
    }
    public Bitmap? Snapshot(object key) => tiles.TryGetValue(key, out var tile) && tile.Preview.Image != null ? new Bitmap(tile.Preview.Image) : null;
    public void Remove(object key) { if (tiles.Remove(key, out var tile)) { flow.Controls.Remove(tile); tile.Dispose(); } empty.Visible = tiles.Count == 0; Arrange(); }
    public void Clear() { foreach (var key in tiles.Keys.ToArray()) Remove(key); }
    void Arrange()
    {
        if (arranging) return; arranging = true;
        try {
            var available = Math.Max(1, flow.ClientSize.Width - flow.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
            var columns = GridSizing.Columns(available); var width = GridSizing.TileWidth(available, columns);
            flow.SuspendLayout();
            var index = 0;
            foreach (var tile in tiles.Values) { tile.Width = width; tile.Height = Math.Max(80, width * 9 / 16 + 55); tile.Margin = new Padding(0, 0, ++index % columns == 0 ? 0 : 12, 12); }
            flow.ResumeLayout(true);
        } finally { arranging = false; }
    }
}
