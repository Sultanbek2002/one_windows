namespace Classroom;
sealed class TeacherMessageWindow : Form
{
    readonly TextBox body = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = true, Font = new("Segoe UI", 13) };
    readonly ListBox history = new() { Dock = DockStyle.Left, Width = 150 };
    readonly PictureBox picture = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White, Visible = false };
    readonly Label fileInfo = new() { Dock = DockStyle.Top, Height = 48, ForeColor = Design.Muted, AutoEllipsis = true, Visible = false };
    readonly Label feedback = new() { AutoSize = true };
    readonly Button copy = new() { Text = "Скопировать сообщение", AutoSize = true };
    readonly Button save = new() { Text = "Сохранить как…", AutoSize = true, Visible = false };
    sealed record Item(string Text, DateTime Time, ReceivedAttachment? Attachment = null) { public override string ToString() => Time.ToString("HH:mm:ss") + (Attachment == null ? " · Текст" : " · Файл"); }
    public TeacherMessageWindow()
    {
        Text = "Сообщение от учителя"; Width = 700; Height = 430; StartPosition = FormStartPosition.CenterScreen; TopMost = true;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45 };
        var close = new Button { Text = "Закрыть", AutoSize = true };
        copy.Click += (_, _) => { try { if (body.Text.Length > 0) Clipboard.SetText(body.Text); feedback.Text = "Скопировано"; } catch (Exception e) { feedback.Text = "Не удалось скопировать: " + e.Message; } };
        close.Click += (_, _) => Hide();
        save.Click += async (_, _) => {
            if (history.SelectedItem is not Item { Attachment: not null } selected) return;
            var attachment = selected.Attachment;
            using var dialog = new SaveFileDialog { Title = "Сохранить файл от учителя", FileName = attachment.Name, Filter = "Исходный файл|*.*", OverwritePrompt = true, AddExtension = false };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            save.Enabled = false;
            try { await File.WriteAllBytesAsync(dialog.FileName, attachment.Data); feedback.Text = "Файл сохранён"; }
            catch (Exception e) { feedback.Text = "Не удалось сохранить: " + e.Message; }
            finally { if (!IsDisposed) save.Enabled = true; }
        };
        buttons.Controls.AddRange([copy, save, close, feedback]);
        var content = new Panel { Dock = DockStyle.Fill }; content.Controls.Add(body); content.Controls.Add(picture); content.Controls.Add(fileInfo);
        Controls.Add(content); Controls.Add(history); Controls.Add(buttons);
        history.SelectedIndexChanged += async (_, _) => await SelectItem();
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        Design.Dialog(this); Width = 780; Height = 500;
        body.BorderStyle = BorderStyle.None; body.BackColor = Color.White; body.ForeColor = Design.Ink;
        history.BorderStyle = BorderStyle.None; history.BackColor = Design.Surface; history.ForeColor = Design.Muted; history.ItemHeight = 32; history.Font = new Font("Segoe UI", 11);
        buttons.Height = 76; buttons.Padding = new Padding(0, 10, 0, 0); Design.Button(copy, true); Design.Button(save, true); Design.Button(close);
        feedback.ForeColor = Design.Muted;
        Controls.Add(new Label { Text = "Сообщения учителя", Dock = DockStyle.Top, Height = 58, ForeColor = Design.Ink, Font = new Font("Segoe UI", 20, FontStyle.Bold) });
        Disposed += (_, _) => picture.Image?.Dispose();
    }
    public void Receive(string text)
    {
        Push(new Item(text, DateTime.Now));
    }
    public void Receive(ReceivedAttachment attachment) => Push(new Item("", DateTime.Now, attachment));
    void Push(Item item)
    {
        history.Items.Add(item);
        var bytes = history.Items.Cast<Item>().Sum(i => (long)(i.Attachment?.Data.Length ?? 0));
        while (history.Items.Count > 100 || bytes > 32 * 1024 * 1024) { bytes -= ((Item)history.Items[0]).Attachment?.Data.Length ?? 0; history.Items.RemoveAt(0); }
        history.SelectedIndex = history.Items.Count - 1;
        Show(); WindowState = FormWindowState.Normal; Activate(); body.Focus(); body.SelectAll();
    }
    async Task SelectItem()
    {
        picture.Image?.Dispose(); picture.Image = null; picture.Visible = false; body.Visible = true; feedback.Text = "";
        var item = history.SelectedItem as Item; var attachment = item?.Attachment;
        copy.Visible = attachment == null; save.Visible = attachment != null; fileInfo.Visible = attachment != null;
        body.Text = item?.Text ?? "";
        if (attachment == null) return;
        fileInfo.Text = attachment.Name + "\n" + (attachment.Data.Length / 1024d).ToString("F1") + " КБ · Получено от учителя";
        body.Text = "Файл готов к сохранению.\r\n\r\nНажмите «Сохранить как…», затем выберите папку.\r\nДокумент не запускается автоматически.";
        if (new[] { ".txt", ".csv" }.Contains(Path.GetExtension(attachment.Name).ToLowerInvariant())) { body.Text = System.Text.Encoding.UTF8.GetString(attachment.Data, 0, Math.Min(attachment.Data.Length, 65536)); if (attachment.Data.Length > 65536) body.Text += "\r\n\r\n[Предпросмотр сокращён; сохраните файл для полного текста]"; }
        if (!new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" }.Contains(Path.GetExtension(attachment.Name).ToLowerInvariant())) return;
        Bitmap? preview = null;
        try {
            preview = await Task.Run(() => {
                using var memory = new MemoryStream(attachment.Data); using var image = Image.FromStream(memory);
                if ((long)image.Width * image.Height > 24000000) throw new IOException("Фото слишком большое для предпросмотра.");
                var scale = Math.Min(1d, Math.Min(1200d / image.Width, 900d / image.Height));
                return new Bitmap(image, new Size(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale))));
            });
            if (IsDisposed || !ReferenceEquals(history.SelectedItem, item)) { preview.Dispose(); return; }
            var previous = picture.Image; picture.Image = preview; previous?.Dispose(); picture.Visible = true; body.Visible = false;
        } catch (Exception) { preview?.Dispose(); if (!IsDisposed && ReferenceEquals(history.SelectedItem, item)) feedback.Text = "Предпросмотр недоступен — файл можно сохранить"; }
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
