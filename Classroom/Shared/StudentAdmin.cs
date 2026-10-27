namespace Classroom;
static class StudentAdmin
{
    public static bool Authorize(IWin32Window owner)
    {
        using var dialog = new Form { Text = "ScreenLink USER · Администрирование", Width = 590, Height = 330, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false };
        Design.Dialog(dialog);
        var heading = new Label { Dock = DockStyle.Top, Height = 66, Text = "Настройки администратора\nВведите пароль для изменения доступа", ForeColor = Design.Ink };
        var password = new TextBox { Dock = DockStyle.Top, UseSystemPasswordChar = true, MaxLength = 128, PlaceholderText = "Пароль", Font = new Font("Segoe UI", 13) };
        var feedback = new Label { Dock = DockStyle.Fill, ForeColor = Color.Firebrick, Padding = new Padding(0, 12, 0, 0) };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45 };
        var enter = new Button { Text = "Открыть настройки", AutoSize = true }; var cancel = new Button { Text = "Отмена", AutoSize = true, DialogResult = DialogResult.Cancel };
        Design.Button(enter, true); Design.Button(cancel);
        enter.Click += async (_, _) => {
            if (AdminPassword.Matches(password.Text)) { dialog.DialogResult = DialogResult.OK; return; }
            feedback.Text = "Неверный пароль"; password.Clear(); enter.Enabled = false;
            await Task.Delay(1500); if (!dialog.IsDisposed) { enter.Enabled = true; password.Focus(); }
        };
        actions.Controls.AddRange([enter, cancel]); dialog.Controls.Add(feedback); dialog.Controls.Add(password); dialog.Controls.Add(heading); dialog.Controls.Add(actions);
        dialog.AcceptButton = enter; dialog.CancelButton = cancel; dialog.Shown += (_, _) => password.Focus();
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }
    public static void Open(Form owner, Control[] controls)
    {
        if (!Authorize(owner)) return;
        using var dialog = new Form { Text = "ScreenLink USER · Настройки доступа", Width = 640, Height = 460, StartPosition = FormStartPosition.CenterParent };
        Design.Dialog(dialog);
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        var close = new Button { Text = "Готово", AutoSize = true, DialogResult = DialogResult.OK }; Design.Button(close, true);
        var exit = new Button { Text = "Завершить Student", AutoSize = true, DialogResult = DialogResult.Abort }; Design.Button(exit, danger: true);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48 }; actions.Controls.AddRange([close, exit]);
        dialog.Controls.Add(panel); dialog.Controls.Add(actions);
        foreach (var control in controls) { control.Margin = new Padding(0, 5, 0, 8); if (control is Button) { control.AutoSize = true; control.Width = 220; } panel.Controls.Add(control); }
        var terminate = false;
        try { terminate = dialog.ShowDialog(owner) == DialogResult.Abort; }
        finally { panel.Controls.Clear(); }
        if (terminate) Application.Exit();
    }
}
