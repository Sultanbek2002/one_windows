using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Classroom;
static class Program
{
    [STAThread] static void Main(string[] args) {
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--design-preview") { Application.Run(new MainWindow(args[1])); return; }
#if TEACHER
        const string role = "Teacher";
#else
        const string role = "Student";
#endif
        using var single = new Mutex(true, "Local\\Classroom." + role, out var first);
        if (!first) { MessageBox.Show("Программа уже запущена. Найдите открытое окно или значок в области уведомлений."); return; }
        try { Application.Run(new MainWindow()); } catch (Exception e) { MessageBox.Show(e.ToString(), "Ошибка запуска Класс"); }
    }
}
sealed class RemoteScreen : PictureBox
{
    public RemoteScreen() { SetStyle(ControlStyles.Selectable, true); TabStop = true; }
    protected override bool IsInputKey(Keys keyData) => true;
}
sealed class MainWindow : Form
{
#if TEACHER
    static readonly bool IsTeacher = true;
#else
    static readonly bool IsTeacher = false;
#endif
    const int Port = 47831;
    readonly TextBox address = new() { Width = 130, Text = "127.0.0.1" };
    readonly Button connect = new() { Text = "Возобновить поиск", AutoSize = true };
    readonly CheckBox broadcast = new() { Text = "Показывать мой экран", AutoSize = true };
    readonly CheckBox control = new() { Text = "Управление разрешено", AutoSize = true };
    readonly Button request = new() { Text = "Управлять выбранным", AutoSize = true };
    readonly Button stop = new() { Text = "Отключиться / Стоп", AutoSize = true };
    readonly TextBox status = new() { Dock = DockStyle.Bottom, Height = 76, Text = "Готово", Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    readonly ListBox students = new() { Dock = DockStyle.Left, Width = 180 };
    readonly RemoteScreen screen = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(24, 31, 42), TabStop = true };
    readonly Panel screenArea = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(24, 31, 42) };
    readonly List<Peer> peers = [];
    readonly System.Windows.Forms.Timer timer = new() { Interval = 700 };
    readonly ComboBox videoQuality = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    readonly Label videoInfo = new() { Dock = DockStyle.Bottom, Height = 24, Text = "Видео: ожидание кадров", AutoEllipsis = true };
    readonly System.Diagnostics.Stopwatch videoClock = System.Diagnostics.Stopwatch.StartNew();
    int receivedFrames;
    long lastHeartbeat;
    TcpListener? listener;
    X509Certificate2? certificate;
    Peer? connection;
    bool sending;
    bool closing;
    readonly HashSet<byte> heldKeys = [];
    readonly ScreenBlocker blocker = new();
    readonly System.Windows.Forms.Timer lockTimer = new() { Interval = 500 };
    readonly ScreenLockState screenLock = new();
    bool lockAll;
    TeacherMessageWindow? messages;
    readonly Dictionary<(Peer, string), TaskCompletionSource<ActionReceipt>> pendingActions = [];
    readonly CheckBox autoControl = new() { Text = "Всегда разрешать управление доверенному учителю", AutoSize = true };
    readonly Button startup = new() { Text = "Включить автозапуск", AutoSize = true };
    readonly Button forget = new() { Text = "Сбросить доверие", AutoSize = true };
    readonly Button details = new() { Text = "Сведения о компьютере", AutoSize = true };
    readonly NotifyIcon tray = new() { Icon = SystemIcons.Application, Text = "Класс — Ученик", Visible = false };
    readonly System.Windows.Forms.Timer searchTimer = new() { Interval = 3000 };
    readonly HashSet<string> declined = [];
    Settings settings = new();
    Discovery? discovery;
    bool paused, connecting, announcing;
    string Role => IsTeacher ? "Teacher" : "Student";
    public MainWindow(string? previewPath = null)
    {
        Text = IsTeacher ? "Класс — Учитель (прототип)" : "Класс — Ученик (прототип)";
        Width = 1150; Height = 760; Font = new("Segoe UI", 10);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 120, Padding = new(10), AutoScroll = true };
        if (!IsTeacher) {
            var nativeSize = new CheckBox { Text = "Масштаб 1:1 (прокрутка)", AutoSize = true };
            nativeSize.CheckedChanged += (_, _) => { screen.Dock = nativeSize.Checked ? DockStyle.None : DockStyle.Fill; screen.SizeMode = nativeSize.Checked ? PictureBoxSizeMode.AutoSize : PictureBoxSizeMode.Zoom; };
            toolbar.Controls.Add(nativeSize);
            var fullscreen = new Button { Text = "На весь экран (выход: Esc)", AutoSize = true };
            var expanded = false; var previousBounds = Bounds; var previousState = WindowState;
            void ToggleFullscreen() {
                expanded = !expanded;
                if (expanded) { previousBounds = Bounds; previousState = WindowState; WindowState = FormWindowState.Normal; FormBorderStyle = FormBorderStyle.None; WindowState = FormWindowState.Maximized; }
                else { WindowState = FormWindowState.Normal; FormBorderStyle = FormBorderStyle.Sizable; Bounds = previousBounds; WindowState = previousState; }
                toolbar.Visible = !expanded; status.Visible = !expanded; videoInfo.Visible = !expanded;
            }
            fullscreen.Click += (_, _) => ToggleFullscreen();
            screen.DoubleClick += (_, _) => ToggleFullscreen();
            KeyPreview = true;
            KeyDown += (_, e) => { if (expanded && e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; ToggleFullscreen(); } };
            toolbar.Controls.Add(fullscreen);
        }
        toolbar.Controls.AddRange([new Label { Text = "Автопоиск компьютеров в локальной сети", AutoSize = true }, connect, stop, forget, details]);
        if (IsTeacher) { toolbar.Controls.Add(broadcast); toolbar.Controls.Add(request); videoQuality.Items.AddRange(VideoProfile.All); videoQuality.SelectedIndex = 1; toolbar.Controls.Add(videoQuality); }
        else { toolbar.Controls.Add(control); toolbar.Controls.Add(autoControl); toolbar.Controls.Add(startup); }
        if (IsTeacher) {
            var message = new Button { Text = "Сообщение всем", AutoSize = true };
            var link = new Button { Text = "Открыть ссылку у всех", AutoSize = true };
            toolbar.Controls.AddRange([message, link]);
            async Task ComposeAndSend(bool isLink) {
                if (peers.Count == 0) { Report("Нет подключённых учеников."); return; }
                message.Enabled = link.Enabled = false;
                try { var text = TeacherMessageWindow.Compose(this, isLink); if (text != null) await SendClassAction(isLink ? "open-url" : "teacher-message", text); }
                finally { message.Enabled = link.Enabled = true; }
            }
            message.Click += async (_, _) => await ComposeAndSend(false);
            link.Click += async (_, _) => await ComposeAndSend(true);
            var block = new Button { Text = "Заблокировать экраны", AutoSize = true };
            var unblock = new Button { Text = "Разблокировать все", AutoSize = true };
            toolbar.Controls.AddRange([block, unblock]);
            block.Click += async (_, _) => { lockAll = true; foreach (var p in peers.ToArray()) { if (!p.SupportsScreenLock) { Report(p.Name + ": обновите Student для блокировки экрана."); continue; } p.LockRequested = true; p.LockToken = Guid.NewGuid().ToString(); await SafeSend(p, new("lock-screen", p.LockToken)); } };
            unblock.Click += async (_, _) => { lockAll = false; foreach (var p in peers.ToArray()) { p.LockRequested = false; await SafeSend(p, new("unlock-screen")); } };
        }
        blocker.EmergencyExit += () => UnlockStudent("Аварийный выход ученика");
        lockTimer.Tick += (_, _) => { if (blocker.Active && screenLock.Expired(Environment.TickCount64)) UnlockStudent("Истекло ожидание подтверждения от учителя"); };
        lockTimer.Start();
        screenArea.Controls.Add(screen); Controls.Add(screenArea); if (IsTeacher) Controls.Add(students); Controls.Add(toolbar); Controls.Add(videoInfo); Controls.Add(status);
        videoQuality.SelectedIndexChanged += (_, _) => { settings.VideoMode = videoQuality.SelectedIndex; timer.Interval = 1000 / VideoProfile.All[videoQuality.SelectedIndex].FramesPerSecond; SaveSettings(); };
        if (IsTeacher) timer.Interval = 1000 / VideoProfile.All[1].FramesPerSecond;
        connect.Click += async (_, _) => { paused = false; declined.Clear(); await StartAutomatic(); };
        stop.Click += (_, _) => { paused = true; Disconnect(); };
        request.Click += async (_, _) => { if (students.SelectedItem is Peer p) { if (p.Control) { screen.Focus(); status.Text = "Управление: " + p.Name + ". Щёлкайте по экрану и вводите текст."; } else await SafeSend(p, new("control-request")); } };
        autoControl.CheckedChanged += (_, _) => { settings.AutoControl = autoControl.Checked; SaveSettings(); if (!autoControl.Checked) control.Checked = false; };
        startup.Click += (_, _) => ToggleStartup();
        details.Click += (_, _) => MessageBox.Show(this, "Имя: " + Environment.MachineName + "\nСертификат: " + certificate?.GetCertHashString(HashAlgorithmName.SHA256) + "\nЖурнал: " + Path.Combine(Settings.DirectoryPath, Role + ".log"), "Сведения для первого подтверждения");
        forget.Click += (_, _) => { if (MessageBox.Show(this, "Удалить доверенные компьютеры? При следующем подключении потребуется новое подтверждение.", "Сброс доверия", MessageBoxButtons.YesNo) != DialogResult.Yes) return; paused = true; Disconnect(); settings.Trusted.Clear(); declined.Clear(); SaveSettings(); Report("Доверие сброшено. Нажмите «Найти снова»."); };
        control.CheckedChanged += async (_, _) => { if (!control.Checked) { ReleaseKeys(); UnlockStudent("Управление отключено"); } if (connection != null) await SafeSend(connection, new("control", control.Checked ? "yes" : "no")); };
        broadcast.CheckedChanged += async (_, _) => { if (!broadcast.Checked) foreach (var p in peers.ToArray()) await SafeSend(p, new("broadcast-stop")); };
        students.SelectedIndexChanged += async (_, _) => { screen.Image?.Dispose(); screen.Image = null; foreach (var p in peers.Where(p => p.Control).ToArray()) await SafeSend(p, new("release-keys")); };
        screen.LostFocus += async (_, _) => { if (IsTeacher) foreach (var p in peers.Where(p => p.Control).ToArray()) await SafeSend(p, new("release-keys")); };
        screen.MouseDown += async (_, e) => {
            if (!IsTeacher || students.SelectedItem is not Peer p || !p.Control || screen.Image == null) return;
            var scale = Math.Min((double)screen.Width / screen.Image.Width, (double)screen.Height / screen.Image.Height);
            var x = (e.X - (screen.Width - screen.Image.Width * scale) / 2) / scale;
            var y = (e.Y - (screen.Height - screen.Image.Height * scale) / 2) / scale;
            if (x < 0 || y < 0 || x >= screen.Image.Width || y >= screen.Image.Height) return;
            screen.Focus(); await SafeSend(p, new(e.Button == MouseButtons.Right ? "right" : "click", "", (int)(x * 65535 / screen.Image.Width), (int)(y * 65535 / screen.Image.Height)));
        };
        screen.KeyDown += async (_, e) => { if (IsTeacher && students.SelectedItem is Peer p && p.Control) { e.SuppressKeyPress = true; await SafeSend(p, new("key-down", "", (int)e.KeyCode)); } };
        screen.KeyUp += async (_, e) => { if (IsTeacher && students.SelectedItem is Peer p && p.Control) await SafeSend(p, new("key-up", "", (int)e.KeyCode)); };
        timer.Tick += async (_, _) => await SendScreens(); timer.Start();
        tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
        var menu = new ContextMenuStrip(); menu.Items.Add("Открыть", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
        menu.Items.Add("Приостановить доступ", null, (_, _) => { paused = true; Disconnect(); });
        menu.Items.Add("Выход", null, (_, _) => { closing = true; Close(); }); tray.ContextMenuStrip = menu;
        Resize += (_, _) => { if (!IsTeacher && WindowState == FormWindowState.Minimized) { tray.Visible = true; Hide(); } };
        FormClosing += (_, e) => { if (!IsTeacher && !closing && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; tray.Visible = true; Hide(); } };
        FormClosed += (_, _) => { closing = true; timer.Stop(); searchTimer.Stop(); lockTimer.Stop(); Disconnect(); messages?.Dispose(); blocker.Dispose(); certificate?.Dispose(); tray.Dispose(); };
        searchTimer.Tick += async (_, _) => await Search();
        Design.ApplyMain(this, IsTeacher, toolbar, students, screenArea, status, videoInfo, broadcast, videoQuality);
        if (previewPath == null) Shown += async (_, _) => await StartAutomatic();
        else {
            timer.Stop(); lockTimer.Stop();
            if (IsTeacher) students.Items.AddRange(["CLASSROOM-PC01", "CLASSROOM-PC02", "CLASSROOM-PC03"]);
            Shown += (_, _) => BeginInvoke((Action)(() => { using var bitmap = new Bitmap(Width, Height); DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(previewPath); closing = true; Close(); }));
        }
    }
    async Task Connect(string expected)
    {
        if (connecting || paused || connection != null) return;
        connecting = true;
        try {
                var tcp = new TcpClient();
                try {
                    Report("Подключение к " + address.Text + ":" + Port + "…");
                    await tcp.ConnectAsync(address.Text.Trim(), Port).WaitAsync(TimeSpan.FromSeconds(8));
                    var ssl = new SslStream(tcp.GetStream(), false, (_, cert, _, _) => cert != null && string.Equals(cert.GetCertHashString(HashAlgorithmName.SHA256), expected, StringComparison.OrdinalIgnoreCase));
                    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "Classroom", ClientCertificates = new X509CertificateCollection { certificate! } }).WaitAsync(TimeSpan.FromSeconds(8));
                    var p = new Peer(tcp, ssl);
                    await p.Send(new("hello-v2", Environment.MachineName));
                    var reply = await p.Read(180);
                    if (reply?.Type != "accepted") throw new Exception(reply?.Data ?? "Учитель закрыл соединение до подтверждения.");
                    if (paused || closing) { p.Dispose(); return; }
                    p.Name = settings.Trusted[expected]; connection = p; control.Checked = autoControl.Checked;
                    await p.Send(new("control", control.Checked ? "yes" : "no"));
                    await p.Send(new("capabilities", "screen-lock-v1"));
                    await p.Send(new("capabilities", "message-links-v1"));
                    Report("Подключено к " + settings.Trusted[expected] + ". Экран виден учителю. " + (control.Checked ? "Управление разрешено." : "Управление выключено."));
                    _ = ReadLoop(p);
                } catch { tcp.Dispose(); throw; }
        } catch (Exception e) { Report("Подключение " + address.Text + ": " + Explain(e)); }
        finally { connecting = false; }
    }
    async Task AcceptLoop(TcpListener server, X509Certificate2 cert)
    {
        try { while (listener == server) { var tcp = await server.AcceptTcpClientAsync(); _ = AcceptPeer(tcp, cert, server); } }
        catch (Exception e) { if (listener == server && !closing) status.Text = "Сервер: " + e.Message; }
    }
    async Task AcceptPeer(TcpClient tcp, X509Certificate2 cert, TcpListener server)
    {
        Peer? p = null;
        try {
            var ssl = new SslStream(tcp.GetStream(), false, (_, c, _, _) => c != null);
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = cert, ClientCertificateRequired = true }).WaitAsync(TimeSpan.FromSeconds(8));
            p = new Peer(tcp, ssl);
            var hello = await p.Read(180);
            if (hello?.Type != "hello-v2" || hello.Data.Length > 100 || peers.Count >= 40 || listener != server) { await p.Send(new("rejected", "Несовместимая версия, класс закрыт или достигнут лимит 40 подключений.")); return; }
            var pin = ssl.RemoteCertificate!.GetCertHashString(HashAlgorithmName.SHA256);
            if (!Trust(pin, hello.Data, "ученика")) { await p.Send(new("rejected", "Учитель отклонил добавление компьютера.")); return; }
            p.Name = hello.Data; await p.Send(new("accepted")); peers.Add(p); students.Items.Add(p);
            if (students.SelectedIndex < 0) students.SelectedIndex = 0;
            await ReadLoop(p);
        } catch (Exception e) { if (!closing && listener == server) Report("Входящее подключение: " + Explain(e)); }
        finally { p?.Dispose(); tcp.Dispose(); }
    }
    async Task ReadLoop(Peer p)
    {
        try { while (!closing) {
            var m = await p.Read(); if (m == null) { Report(p.Name + ": другая сторона закрыла соединение. Поиск продолжится автоматически."); break; }
            switch (m.Type) {
                case "teacher-message": case "open-url":
                    if (!IsTeacher && connection == p && !paused) await ReceiveClassAction(p, m);
                    break;
                case "action-result":
                    if (IsTeacher) {
                        try { var result = System.Text.Json.JsonSerializer.Deserialize<ActionReceipt>(m.Data); if (result != null && Guid.TryParse(result.Id, out _) && pendingActions.TryGetValue((p, result.Id), out var wait)) wait.TrySetResult(result); }
                        catch (System.Text.Json.JsonException) { Report(p.Name + ": некорректное подтверждение команды."); }
                    } break;
                case "capabilities":
                    if (IsTeacher && m.Data == "message-links-v1") p.SupportsClassActions = true;
                    if (IsTeacher && m.Data == "screen-lock-v1") { p.SupportsScreenLock = true; if (lockAll) { p.LockRequested = true; p.LockToken = Guid.NewGuid().ToString(); await SafeSend(p, new("lock-screen", p.LockToken)); } } break;
                case "lock-screen":
                    if (!IsTeacher && connection == p) {
                        if (!control.Checked) { await SafeSend(p, new("lock-state", "rejected: управление выключено")); break; }
                        if (!screenLock.TryRenew(m.Data, Environment.TickCount64)) { await SafeSend(p, new("lock-state", "Команда блокировки устарела или отменена")); break; }
                        try { var fresh = !blocker.Active; ReleaseKeys(); blocker.Activate(); if (fresh) await SafeSend(p, new("lock-state", "locked")); }
                        catch (Exception e) { UnlockStudent("Ошибка блокировки: " + Explain(e)); await SafeSend(p, new("lock-state", "failed: " + Explain(e))); }
                    } break;
                case "unlock-screen": if (!IsTeacher && connection == p) UnlockStudent("Разблокировано учителем"); break;
                case "lock-state": if (IsTeacher) { if (m.Data != "locked") p.LockRequested = false; Report(p.Name + ": " + m.Data); } break;
                case "frame":
                    if (!IsTeacher || students.SelectedItem == p) {
                        var image = await Task.Run(() => { using var ms = new MemoryStream(Convert.FromBase64String(m.Data)); using var original = Image.FromStream(ms); return new Bitmap(original); });
                        if (closing || paused || (IsTeacher ? students.SelectedItem != p : connection != p)) { image.Dispose(); break; }
                        var old = screen.Image; screen.Image = image; old?.Dispose();
                        receivedFrames++;
                        if (videoClock.ElapsedMilliseconds >= 1000) { videoInfo.Text = $"Приём: {image.Width} × {image.Height} · {receivedFrames * 1000d / videoClock.ElapsedMilliseconds:F1} кадров/с"; receivedFrames = 0; videoClock.Restart(); }
                    } break;
                case "broadcast-stop": screen.Image?.Dispose(); screen.Image = null; break;
                case "control-request":
                    if (!IsTeacher && !control.Checked && MessageBox.Show(this, "Разрешить учителю управлять мышью и клавиатурой? Снять разрешение можно в этом окне.", "Запрос учителя", MessageBoxButtons.YesNo) == DialogResult.Yes) control.Checked = true;
                    break;
                case "control": p.Control = m.Data == "yes"; status.Text = p.Name + (p.Control ? ": управление разрешено" : ": управление выключено"); break;
                case "release-keys": if (!IsTeacher) ReleaseKeys(); break;
                case "click": case "right":
                    if (!IsTeacher && control.Checked && !blocker.Active) {
                        var bounds = Screen.PrimaryScreen!.Bounds;
                        SetCursorPos(bounds.Left + m.X * bounds.Width / 65535, bounds.Top + m.Y * bounds.Height / 65535);
                        uint down = m.Type == "right" ? 8u : 2u; mouse_event(down, 0, 0, 0, UIntPtr.Zero); mouse_event(down * 2, 0, 0, 0, UIntPtr.Zero);
                    } break;
                case "key-down": case "key-up": if (!IsTeacher && control.Checked && !blocker.Active && m.X >= 0 && m.X <= 255) { var key = (byte)m.X; if (m.Type == "key-down") heldKeys.Add(key); else heldKeys.Remove(key); keybd_event(key, 0, m.Type == "key-up" ? 2u : 0u, UIntPtr.Zero); } break;
            }
        } } catch (Exception e) { if (!closing && !paused) Report("Соединение закрыто: " + Explain(e)); }
        finally {
            foreach (var wait in pendingActions.Where(entry => entry.Key.Item1 == p).Select(entry => entry.Value).ToArray()) wait.TrySetException(new IOException("Соединение с учеником закрыто."));
            p.Dispose(); peers.Remove(p); students.Items.Remove(p);
            if (connection == p) { connection = null; screenLock.Release(); blocker.Dispose(); control.Checked = false; ReleaseKeys(); screen.Image?.Dispose(); screen.Image = null; }
        }
    }
    async Task SafeSend(Peer p, Packet m) { try { await p.Send(m); } catch { p.Dispose(); } }
    async Task ReceiveClassAction(Peer p, Packet packet)
    {
        ClassAction? command = null;
        try {
            command = System.Text.Json.JsonSerializer.Deserialize<ClassAction>(packet.Data);
            if (!ClassActions.Valid(command)) throw new InvalidOperationException("Некорректная команда или слишком длинный текст.");
            if (packet.Type == "teacher-message") {
                UnlockStudent("Учитель отправил сообщение");
                messages ??= new TeacherMessageWindow(); messages.Receive(command!.Text);
                await p.Send(new("action-result", System.Text.Json.JsonSerializer.Serialize(new ActionReceipt(command.Id, packet.Type, true, "Сообщение показано"))));
            } else {
                if (!ClassActions.WebAddress(command!.Text, out var uri)) throw new InvalidOperationException("Разрешены только веб-ссылки http:// и https:// без учётных данных.");
                UnlockStudent("Учитель открыл ссылку для работы");
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = uri!.AbsoluteUri, UseShellExecute = true });
                await p.Send(new("action-result", System.Text.Json.JsonSerializer.Serialize(new ActionReceipt(command.Id, packet.Type, true, "Запрос передан браузеру"))));
            }
        } catch (Exception e) {
            Report("Команда учителя: " + Explain(e));
            if (command != null && Guid.TryParse(command.Id, out _)) await SafeSend(p, new("action-result", System.Text.Json.JsonSerializer.Serialize(new ActionReceipt(command.Id, packet.Type, false, Explain(e)))));
        }
    }
    async Task SendClassAction(string kind, string text)
    {
        var targets = peers.ToArray();
        lockAll = false;
        foreach (var p in targets) p.LockRequested = false;
        await Task.WhenAll(targets.Where(p => p.SupportsScreenLock).Select(p => SafeSend(p, new("unlock-screen"))));
        async Task<bool> Deliver(Peer p) {
            if (!p.SupportsClassActions) { Report(p.Name + ": обновите Student для сообщений и ссылок."); return false; }
            var command = new ClassAction(Guid.NewGuid().ToString(), text);
            var completed = new TaskCompletionSource<ActionReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingActions[(p, command.Id)] = completed;
            try {
                await p.Send(new(kind, System.Text.Json.JsonSerializer.Serialize(command)));
                var receipt = await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                if (receipt.Kind != kind) throw new InvalidOperationException("Неверный тип подтверждения.");
                Report(p.Name + ": " + receipt.Detail); return receipt.Success;
            } catch (TimeoutException) { Report(p.Name + ": подтверждение не получено за 10 секунд. Повтор автоматически не отправляется."); return false; }
            catch (Exception e) { Report(p.Name + ": " + Explain(e)); return false; }
            finally { pendingActions.Remove((p, command.Id)); }
        }
        var results = await Task.WhenAll(targets.Select(Deliver));
        Report($"{(kind == "open-url" ? "Открытие ссылки" : "Показ сообщения")}: подтверждено {results.Count(x => x)} из {targets.Length}. Подробности: Teacher.log. " + (kind == "open-url" ? "Загрузка сайта браузером не проверяется." : ""));
    }
    async Task SendScreens()
    {
        if (sending || closing) return;
        var targets = IsTeacher ? peers.ToArray() : connection == null ? [] : new[] { connection };
        if (targets.Length == 0) return;
        sending = true;
        try {
            if (IsTeacher && !broadcast.Checked) {
                if (Environment.TickCount64 - lastHeartbeat >= 2000) { lastHeartbeat = Environment.TickCount64; await Task.WhenAll(targets.Select(p => SafeSend(p, new("ping")))); }
            }
            else {
                var bounds = Screen.PrimaryScreen!.Bounds;
                var profile = IsTeacher ? VideoProfile.All[videoQuality.SelectedIndex] : new VideoProfile("Просмотр ученика", 1024, 2, 80);
                var bytes = await Task.Run(() => ScreenCapture.Capture(bounds, profile));
                if (closing || paused || (IsTeacher && !broadcast.Checked)) return;
                foreach (var p in targets) _ = SendVideo(p, bytes);
                if (IsTeacher) videoInfo.Text = $"Демонстрация: {profile.Name} · кадр {bytes.Length / 1024} КБ · получателей {targets.Length}";
            }
        } catch (Exception e) { status.Text = "Захват экрана: " + e.Message; }
        finally { sending = false; }
    }
    async Task SendVideo(Peer p, byte[] bytes) { try { await p.TrySendFrame(bytes); } catch (Exception e) { if (!closing && !paused) Report("Передача видео: " + Explain(e)); p.Dispose(); } }
    void Disconnect()
    {
        screenLock.Release(); blocker.Dispose(); lockAll = false;
        listener?.Stop(); listener = null;
        discovery?.Dispose(); discovery = null; searchTimer.Stop();
        connection?.Dispose(); connection = null;
        foreach (var p in peers.ToArray()) p.Dispose(); peers.Clear(); students.Items.Clear();
        control.Checked = false; ReleaseKeys(); broadcast.Checked = false; connect.Enabled = true;
        screen.Image?.Dispose(); screen.Image = null; status.Text = "Отключено";
    }
    async Task StartAutomatic()
    {
        try {
            if (discovery != null) { await Search(); return; }
            settings = Settings.Load(Role);
            if (IsTeacher) videoQuality.SelectedIndex = Math.Clamp(settings.VideoMode, 0, VideoProfile.All.Length - 1);
            autoControl.Checked = settings.AutoControl;
            certificate ??= ClassCertificate.LoadIdentity(Role);
            if (IsTeacher) {
                listener = new TcpListener(IPAddress.Any, Port); listener.Start();
                _ = AcceptLoop(listener, certificate);
            } else {
                tray.Visible = true;
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                startup.Text = key?.GetValue("ClassroomStudent") == null ? "Включить автозапуск" : "Отключить автозапуск";
            }
            discovery = new Discovery(IsTeacher, certificate.GetCertHashString(HashAlgorithmName.SHA256));
            _ = discovery.Listen(async (beacon, ip) => {
                if (paused || closing || IsTeacher || connection != null || connecting) return;
                if (!settings.Trusted.ContainsKey(beacon.Pin) && settings.Trusted.Count > 0) {
                    if (settings.Trusted.Values.Contains(beacon.Name)) Report("У найденного учителя изменился сертификат. Подключение остановлено. Проверьте компьютер и при необходимости сбросьте доверие.");
                    return;
                }
                if (!Trust(beacon.Pin, beacon.Name, "учителя")) return;
                address.Text = ip.ToString(); await Connect(beacon.Pin);
            }, e => Report("Автопоиск: " + Explain(e)));
            Report(IsTeacher ? "Класс открыт. Компьютеры появятся автоматически. Для новых учеников подтвердите добавление." : "Поиск учителя… Если он не найден: проверьте запуск Teacher, одну подсеть и разрешение UDP 47832/47833 в частной сети.");
            searchTimer.Start(); await Search();
        } catch (Exception e) { Disconnect(); Report("Запуск автопоиска: " + Explain(e)); }
    }
    async Task Search()
    {
        if (paused || closing || announcing || discovery == null) return;
        announcing = true;
        try { await discovery.Announce(); if (IsTeacher) foreach (var p in peers.Where(p => p.LockRequested).ToArray()) _ = SafeSend(p, new("lock-screen", p.LockToken)); }
        catch (Exception e) { Report("Поиск в сети: " + Explain(e)); }
        finally { announcing = false; }
    }
    bool Trust(string pin, string name, string kind)
    {
        if (settings.Trusted.ContainsKey(pin)) return true;
        if (declined.Contains(pin) || paused || closing) return false;
        if (!IsTeacher) { Show(); WindowState = FormWindowState.Normal; Activate(); }
        var message = $"Добавить {kind} «{name}» в доверенные?\n\nПодтвердите, что это компьютер вашего класса.\nСертификат: {pin}\n\nПосле подтверждения подключение будет автоматическим. " + (!IsTeacher ? "Учитель сможет видеть экран и управлять компьютером, если включено постоянное разрешение. Доступ можно приостановить через значок программы." : "");
        if (MessageBox.Show(this, message, "Первое подключение — подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) { declined.Add(pin); return false; }
        settings.Trusted[pin] = name; SaveSettings(); return true;
    }
    void SaveSettings() { try { settings.Save(Role); } catch (Exception e) { Report("Сохранение настроек: " + Explain(e)); } }
    void ToggleStartup()
    {
        try {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (key.GetValue("ClassroomStudent") != null) { key.DeleteValue("ClassroomStudent"); startup.Text = "Включить автозапуск"; Report("Автозапуск отключён."); }
            else { key.SetValue("ClassroomStudent", "\"" + Environment.ProcessPath + "\""); startup.Text = "Отключить автозапуск"; Report("Автозапуск включён при входе текущего пользователя в Windows. Не перемещайте файлы программы."); }
        } catch (Exception e) { Report("Автозапуск: " + Explain(e)); }
    }
    void Report(string message)
    {
        if (closing) return;
        status.Text = message;
        tray.Text = ("Класс: " + message)[..Math.Min(63, ("Класс: " + message).Length)];
        try { Directory.CreateDirectory(Settings.DirectoryPath); File.AppendAllText(Path.Combine(Settings.DirectoryPath, Role + ".log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}"); } catch { }
    }
    static string Explain(Exception error)
    {
        var e = error; while (e.InnerException != null) e = e.InnerException;
        if (e is SocketException socket) return $"{socket.SocketErrorCode} ({socket.ErrorCode}): {socket.Message}";
        if (e is TimeoutException) return "Время ожидания истекло. Ответ не получен; возможны недоступный компьютер, фильтрация сети или ожидание подтверждения. Это не позволяет точно определить причину.";
        return $"{e.GetType().Name}: {e.Message} (0x{e.HResult:X8})";
    }
    void ReleaseKeys() { foreach (var key in heldKeys) keybd_event(key, 0, 2, UIntPtr.Zero); heldKeys.Clear(); }
    void UnlockStudent(string reason) { var wasLocked = blocker.Active; screenLock.Release(); blocker.Dispose(); if (wasLocked) { Report(reason); if (connection != null) _ = SafeSend(connection, new("lock-state", reason)); } }
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
}
