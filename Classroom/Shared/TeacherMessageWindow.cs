namespace Classroom;
sealed class TeacherMessageWindow : Form
{
    readonly TextBox body = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = true, Font = new("Segoe UI", 13) };
    readonly ListBox history = new() { Dock = DockStyle.Left, Width = 150 };
    sealed record Item(string Text, DateTime Time) { public override string ToString() => Time.ToString("HH:mm:ss"); }
    public TeacherMessageWindow()
    {
        Text = "Сообщение от учителя"; Width = 700; Height = 430; StartPosition = FormStartPosition.CenterScreen; TopMost = true;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45 };
        var copy = new Button { Text = "Скопировать сообщение", AutoSize = true };
        var close = new Button { Text = "Закрыть", AutoSize = true };
        var feedback = new Label { AutoSize = true };
        copy.Click += (_, _) => { try { if (body.Text.Length > 0) Clipboard.SetText(body.Text); feedback.Text = "Скопировано"; } catch (Exception e) { feedback.Text = "Не удалось скопировать: " + e.Message; } };
        close.Click += (_, _) => Hide();
        buttons.Controls.AddRange([copy, close, feedback]);
        Controls.Add(body); Controls.Add(history); Controls.Add(buttons);
        history.SelectedIndexChanged += (_, _) => { body.Text = (history.SelectedItem as Item)?.Text ?? ""; feedback.Text = ""; };
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        Design.Dialog(this); Width = 780; Height = 500;
        body.BorderStyle = BorderStyle.None; body.BackColor = Color.White; body.ForeColor = Design.Ink;
        history.BorderStyle = BorderStyle.None; history.BackColor = Design.Surface; history.ForeColor = Design.Muted; history.ItemHeight = 32; history.Font = new Font("Segoe UI", 11);
        buttons.Height = 56; buttons.Padding = new Padding(0, 10, 0, 0); Design.Button(copy, true); Design.Button(close);
        feedback.ForeColor = Design.Muted;
        Controls.Add(new Label { Text = "Сообщения учителя", Dock = DockStyle.Top, Height = 58, ForeColor = Design.Ink, Font = new Font("Segoe UI", 20, FontStyle.Bold) });
    }
    public void Receive(string text)
    {
        if (history.Items.Count >= 100) history.Items.RemoveAt(0);
        history.Items.Add(new Item(text, DateTime.Now)); history.SelectedIndex = history.Items.Count - 1;
        Show(); WindowState = FormWindowState.Normal; Activate(); body.Focus(); body.SelectAll();
    }
    public static string? Compose(IWin32Window owner, bool link)
    {
        using var dialog = new Form { Text = link ? "Открыть ссылку у всех учеников" : "Сообщение всем ученикам", Width = 650, Height = 350, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var hint = new Label { Dock = DockStyle.Top, Height = 48, Text = link ? "Введите полный адрес https://… или http://…\nСсылка откроется в браузере подключённых учеников." : "Сообщение появится отдельным окном. Ученики смогут скопировать текст." };
        var input = new TextBox { Dock = DockStyle.Fill, Multiline = !link, MaxLength = link ? 8192 : 20000, ScrollBars = link ? ScrollBars.None : ScrollBars.Vertical };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42 };
        var send = new Button { Text = link ? "Открыть у всех" : "Отправить всем", AutoSize = true };
        var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, AutoSize = true };
        send.Click += (_, _) => { if (string.IsNullOrWhiteSpace(input.Text)) return; if (link && !ClassActions.WebAddress(input.Text, out _)) { MessageBox.Show(dialog, "Нужен адрес http:// или https:// без логина и пароля."); return; } dialog.DialogResult = DialogResult.OK; };
        actions.Controls.AddRange([send, cancel]); dialog.Controls.Add(input); dialog.Controls.Add(hint); dialog.Controls.Add(actions); dialog.CancelButton = cancel;
        Design.Dialog(dialog); dialog.Width = 720; dialog.Height = link ? 290 : 450;
        hint.Height = 72; hint.ForeColor = Design.Muted; input.BorderStyle = BorderStyle.FixedSingle; input.Font = new Font("Segoe UI", 12);
        input.PlaceholderText = link ? "https://example.com/lesson" : "Напишите задание или объявление для класса…";
        actions.Height = 56; actions.Padding = new Padding(0, 10, 0, 0); Design.Button(send, true); Design.Button(cancel);
        return dialog.ShowDialog(owner) == DialogResult.OK ? input.Text : null;
    }
}
