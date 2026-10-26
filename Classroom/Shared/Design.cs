using System.Drawing.Drawing2D;
namespace Classroom;
static class Design
{
    public static readonly Color Ink = Color.FromArgb(25, 39, 59), Muted = Color.FromArgb(99, 116, 137), Accent = Color.FromArgb(37, 99, 235), Surface = Color.FromArgb(243, 246, 251);
    public static void Button(Button button, bool primary = false, bool danger = false)
    {
        button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0;
        button.BackColor = primary ? Accent : danger ? Color.FromArgb(254, 226, 226) : Color.FromArgb(233, 239, 248);
        button.ForeColor = primary ? Color.White : danger ? Color.FromArgb(170, 42, 51) : Ink;
        button.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular); button.Cursor = Cursors.Hand;
        button.Height = 34; button.Padding = new Padding(8, 3, 8, 3); button.Margin = new Padding(0, 0, 7, 7);
    }
    public static void Dialog(Form form)
    {
        form.BackColor = Surface; form.ForeColor = Ink; form.Font = new Font("Segoe UI", 10); form.Padding = new Padding(24);
        form.MinimumSize = new Size(580, 320);
    }
    public static void ApplyMain(Form form, bool teacher, FlowLayoutPanel tools, ListBox students, Panel viewer, TextBox status, Label videoInfo, CheckBox broadcast, ComboBox quality)
    {
        form.SuspendLayout(); form.BackColor = Surface; form.ForeColor = Ink;
        form.Text = teacher ? "Класс · Панель учителя" : "Класс · Рабочее место ученика";
        form.Width = 1360; form.Height = 860; form.MinimumSize = new Size(1120, 700); form.StartPosition = FormStartPosition.CenterScreen;
        var original = tools.Controls.Cast<Control>().ToArray(); tools.Controls.Clear();
        var tips = new ToolTip { AutoPopDelay = 6000 }; foreach (var child in original) tips.SetToolTip(child, child.Text); form.Disposed += (_, _) => tips.Dispose();
        tools.Padding = new Padding(18, 10, 12, 8); tools.Height = 158; tools.BackColor = Surface;
        tools.WrapContents = true;
        foreach (var b in original.OfType<Button>()) Button(b, b.Text is "Сообщение всем" or "Управлять выбранным", b.Text.Contains("Заблокировать") || b.Text.Contains("Стоп"));
        var assigned = new HashSet<Control>();
        Control? Find(string text) => original.FirstOrDefault(c => c.Text == text);
        FlowLayoutPanel Card(string title, int width, params Control?[] children)
        {
            var card = new FlowLayoutPanel { Width = width, Height = 128, BackColor = Color.White, Padding = new Padding(12, 10, 8, 8), Margin = new Padding(0, 0, 10, 0) };
            card.Controls.Add(new Label { Text = title.ToUpperInvariant(), Width = width - 28, Height = 24, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = Muted });
            foreach (var child in children.Where(c => c != null).Cast<Control>()) { assigned.Add(child); card.Controls.Add(child); }
            tools.Controls.Add(card); return card;
        }
        void Shorten(string old, string text, int width) { if (Find(old) is Button b) { b.Text = text; b.AutoSize = false; b.Width = width; } }
        // Keep the original controls and their event handlers when moving actions into groups.
        var connect = Find("Возобновить поиск"); var stop = Find("Отключиться / Стоп"); var details = Find("Сведения о компьютере"); var forget = Find("Сбросить доверие");
        Shorten("Возобновить поиск", "Найти снова", 110); Shorten("Отключиться / Стоп", "Остановить", 110); Shorten("Сведения о компьютере", "Сведения", 110); Shorten("Сбросить доверие", "Сбросить", 110);
        Control? sidebar = null;
        if (teacher) {
            broadcast.Appearance = Appearance.Button; broadcast.FlatStyle = FlatStyle.Flat; broadcast.FlatAppearance.BorderSize = 0; broadcast.AutoSize = false; broadcast.Width = 270; broadcast.Height = 34; broadcast.TextAlign = ContentAlignment.MiddleCenter; broadcast.Cursor = Cursors.Hand;
            void BroadcastColor() { broadcast.BackColor = broadcast.Checked ? Accent : Color.FromArgb(229, 238, 255); broadcast.ForeColor = broadcast.Checked ? Color.White : Accent; }
            broadcast.CheckedChanged += (_, _) => BroadcastColor(); BroadcastColor();
            quality.Width = 270; quality.DropDownWidth = 440; quality.FlatStyle = FlatStyle.Flat; quality.Margin = new Padding(0, 6, 0, 0);
            Card("Демонстрация", 300, broadcast, quality);
            var message = Find("Сообщение всем"); var link = Find("Открыть ссылку у всех");
            Shorten("Сообщение всем", "Сообщение всем", 175); Shorten("Открыть ссылку у всех", "Открыть ссылку", 175);
            Card("Задания классу", 205, message, link);
            var block = Find("Заблокировать экраны"); var unblock = Find("Разблокировать все");
            Shorten("Заблокировать экраны", "Заблокировать", 170); Shorten("Разблокировать все", "Разблокировать", 170);
            Card("Внимание класса", 200, block, unblock);
            Card("Подключение", 255, connect, stop, details, forget);
            var manage = Find("Управлять выбранным");
            if (manage is Button manageButton) { assigned.Add(manageButton); manageButton.Dock = DockStyle.Top; manageButton.AutoSize = false; manageButton.Height = 40; manageButton.Text = "Управлять выбранным"; }
            var side = new Panel { Dock = DockStyle.Left, Width = 228, BackColor = Color.White, Padding = new Padding(14, 12, 14, 14) };
            sidebar = side;
            form.Controls.Remove(students); students.Dock = DockStyle.Fill; students.BorderStyle = BorderStyle.None; students.BackColor = Color.White; students.ForeColor = Ink; students.DrawMode = DrawMode.OwnerDrawFixed; students.ItemHeight = 60; students.IntegralHeight = false;
            students.DrawItem += (_, e) => {
                if (e.Index < 0) return;
                var selected = (e.State & DrawItemState.Selected) != 0;
                using var bg = new SolidBrush(selected ? Color.FromArgb(233, 240, 255) : Color.White); e.Graphics.FillRectangle(bg, e.Bounds);
                var text = students.Items[e.Index].ToString() ?? "Компьютер";
                using var dot = new SolidBrush(Color.FromArgb(34, 163, 109)); e.Graphics.FillEllipse(dot, e.Bounds.X + 8, e.Bounds.Y + 18, 8, 8);
                using var itemFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                TextRenderer.DrawText(e.Graphics, text, itemFont, new Rectangle(e.Bounds.X + 26, e.Bounds.Y + 9, e.Bounds.Width - 30, 23), selected ? Accent : Ink, TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(e.Graphics, "Подключён", students.Font, new Point(e.Bounds.X + 26, e.Bounds.Y + 32), Muted);
                e.DrawFocusRectangle();
            };
            var count = new Label { Dock = DockStyle.Top, Height = 42, Font = new Font("Segoe UI", 11, FontStyle.Bold), Text = "Ученики" };
            var sideTitle = new Label { Dock = DockStyle.Top, Height = 32, Text = "КОМПЬЮТЕРЫ КЛАССА", ForeColor = Muted, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
            side.Controls.Add(students); if (manage != null) side.Controls.Add(manage); side.Controls.Add(count); side.Controls.Add(sideTitle);
            form.Controls.Add(side); side.BringToFront();
            var refresh = new System.Windows.Forms.Timer { Interval = 1000 }; refresh.Tick += (_, _) => count.Text = $"В сети · {students.Items.Count}"; refresh.Start(); form.Disposed += (_, _) => refresh.Dispose();
        } else {
            Card("Просмотр экрана", 320, Find("Масштаб 1:1 (прокрутка)"), Find("На весь экран (выход: Esc)"));
            var permission = Find("Всегда разрешать управление доверенному учителю"); if (permission != null) { permission.AutoSize = false; permission.Width = 300; permission.Height = 46; }
            Card("Доступ учителя", 335, Find("Управление разрешено"), permission);
            Card("Подключение", 330, connect, stop, details, forget, Find("Включить автозапуск"));
            foreach (var card in tools.Controls.OfType<FlowLayoutPanel>()) card.Height = 162;
            tools.Height = 190;
        }
        foreach (var child in original.Where(c => !assigned.Contains(c))) if (child is not Label) { tools.Controls.Add(child); }
        viewer.BackColor = Color.FromArgb(18, 26, 40);
        var screen = viewer.Controls.OfType<PictureBox>().First();
        screen.Paint += (_, e) => { if (screen.Image == null) { using var font = new Font("Segoe UI", 14); TextRenderer.DrawText(e.Graphics, teacher ? "Выберите компьютер ученика\n\nПодключённые компьютеры появятся слева" : "Готово к работе\n\nЭкран учителя появится здесь во время демонстрации", font, screen.ClientRectangle, Color.FromArgb(158, 175, 199), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak); } };
        status.BorderStyle = BorderStyle.None; status.BackColor = Color.White; status.ForeColor = Muted; status.Height = 58; status.Font = new Font("Segoe UI", 9); status.Margin = new Padding(16);
        videoInfo.BackColor = Color.FromArgb(230, 237, 247); videoInfo.ForeColor = Ink; videoInfo.Height = 30; videoInfo.Padding = new Padding(14, 6, 0, 0); videoInfo.Font = new Font("Segoe UI", 9);
        var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Color.FromArgb(21, 39, 66), Padding = new Padding(22, 12, 22, 8) };
        header.Controls.Add(new Label { Dock = DockStyle.Fill, Text = teacher ? "КЛАСС   /   Панель учителя" : "КЛАСС   /   Рабочее место ученика", ForeColor = Color.White, Font = new Font("Segoe UI", 18, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft });
        header.Controls.Add(new Label { Dock = DockStyle.Right, Width = 275, Text = "ЛОКАЛЬНАЯ СЕТЬ  ·  ЗАЩИЩЁННАЯ СВЯЗЬ", ForeColor = Color.FromArgb(157, 184, 223), Font = new Font("Segoe UI", 8.5f), TextAlign = ContentAlignment.MiddleRight });
        form.Controls.Add(header); header.BringToFront();
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 36, BackColor = Surface, Padding = new Padding(16, 0, 16, 0) };
        footer.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Разработчик · Sultanbek Abdykadyrov", ForeColor = Muted, Font = new Font("Segoe UI", 9), TextAlign = ContentAlignment.MiddleLeft });
        var instagram = new LinkLabel { Dock = DockStyle.Right, Width = 200, Text = "Instagram · @sultan_agai", LinkColor = Accent, ActiveLinkColor = Ink, VisitedLinkColor = Accent, Font = new Font("Segoe UI", 9), TextAlign = ContentAlignment.MiddleRight, LinkBehavior = LinkBehavior.HoverUnderline };
        instagram.LinkClicked += (_, _) => {
            try { using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "https://www.instagram.com/sultan_agai?utm_source=ig_web_button_share_sheet&rpxt=ZDNlZDc0MzIxNw==", UseShellExecute = true }); }
            catch (Exception e) { MessageBox.Show(form, "Не удалось открыть Instagram: " + e.Message); }
        };
        footer.Controls.Add(instagram); form.Controls.Add(footer);
        tools.VisibleChanged += (_, _) => { header.Visible = tools.Visible; footer.Visible = tools.Visible; };
        var order = new Control?[] { viewer, sidebar, tools, videoInfo, status, footer, header };
        var index = 0; foreach (var child in order.Where(c => c != null).Cast<Control>()) form.Controls.SetChildIndex(child, index++);
        form.ResumeLayout(true);
    }
}
