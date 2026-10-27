namespace Classroom;
sealed class ShutdownCountdown : Form
{
    readonly ShutdownState state = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    readonly Label caption = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new("Segoe UI", 16) };
    public event Action<string>? Status;
    public ShutdownCountdown()
    {
        Text = "Учитель завершает работу класса"; Width = 630; Height = 300; TopMost = true; StartPosition = FormStartPosition.CenterScreen;
        Design.Dialog(this);
        var cancel = new Button { Text = "Отменить выключение", Dock = DockStyle.Bottom, Height = 44 }; Design.Button(cancel); cancel.Height = 44;
        cancel.Click += (_, _) => Cancel("Ученик отменил выключение"); Controls.Add(caption); Controls.Add(cancel);
        FormClosing += (_, _) => { state.Cancel(); timer.Stop(); };
        timer.Tick += async (_, _) => {
            caption.Text = $"Компьютер выключится через {Math.Max(0, (int)Math.Ceiling((state.Deadline - Environment.TickCount64) / 1000d))} с\n\nСохраните открытые документы.\nМожно отменить выключение.";
            if (!state.Pending || Environment.TickCount64 < state.Deadline) return;
            timer.Stop();
            try {
                System.Diagnostics.Process? process = null;
                state.ExecuteIfDue(Environment.TickCount64, () => {
                    var info = new System.Diagnostics.ProcessStartInfo { FileName = Path.Combine(Environment.SystemDirectory, "shutdown.exe"), UseShellExecute = false, CreateNoWindow = true };
                    // Keep the countdown in this app: Windows implies /f when /t > 0.
                    info.ArgumentList.Add("/s"); info.ArgumentList.Add("/t"); info.ArgumentList.Add("0");
                    process = System.Diagnostics.Process.Start(info);
                });
                if (process == null) throw new IOException("Не удалось запустить системное выключение.");
                using (process) { await process.WaitForExitAsync(); if (process.ExitCode != 0) throw new IOException("Windows отказала в выключении, код " + process.ExitCode); }
                Status?.Invoke("Запрос выключения передан Windows"); Close();
            } catch (Exception e) { Status?.Invoke("Ошибка выключения: " + e.Message); caption.Text = "Выключение не выполнено.\n\n" + e.Message; }
        };
        Disposed += (_, _) => timer.Dispose();
    }
    public void Schedule() { state.Schedule(Environment.TickCount64); caption.Text = "Выключение через 60 секунд. Сохраните документы."; Show(); Activate(); timer.Start(); }
    public void Cancel(string reason) { state.Cancel(); timer.Stop(); Status?.Invoke(reason); Hide(); }
}
