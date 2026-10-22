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
    [STAThread] static void Main() {
        ApplicationConfiguration.Initialize();
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
    readonly List<Peer> peers = [];
    readonly System.Windows.Forms.Timer timer = new() { Interval = 700 };
    TcpListener? listener;
    X509Certificate2? certificate;
    Peer? connection;
    bool sending;
    bool closing;
    readonly HashSet<byte> heldKeys = [];
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
    public MainWindow()
    {
        Text = IsTeacher ? "Класс — Учитель (прототип)" : "Класс — Ученик (прототип)";
        Width = 1150; Height = 760; Font = new("Segoe UI", 10);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 120, Padding = new(10), AutoScroll = true };
        toolbar.Controls.AddRange([new Label { Text = "Автопоиск компьютеров в локальной сети", AutoSize = true }, connect, stop, forget, details]);
        if (IsTeacher) { toolbar.Controls.Add(broadcast); toolbar.Controls.Add(request); }
        else { toolbar.Controls.Add(control); toolbar.Controls.Add(autoControl); toolbar.Controls.Add(startup); }
        Controls.Add(screen); if (IsTeacher) Controls.Add(students); Controls.Add(toolbar); Controls.Add(status);
        connect.Click += async (_, _) => { paused = false; declined.Clear(); await StartAutomatic(); };
        stop.Click += (_, _) => { paused = true; Disconnect(); };
        request.Click += async (_, _) => { if (students.SelectedItem is Peer p) { if (p.Control) { screen.Focus(); status.Text = "Управление: " + p.Name + ". Щёлкайте по экрану и вводите текст."; } else await SafeSend(p, new("control-request")); } };
        autoControl.CheckedChanged += (_, _) => { settings.AutoControl = autoControl.Checked; SaveSettings(); if (!autoControl.Checked) control.Checked = false; };
        startup.Click += (_, _) => ToggleStartup();
        details.Click += (_, _) => MessageBox.Show(this, "Имя: " + Environment.MachineName + "\nСертификат: " + certificate?.GetCertHashString(HashAlgorithmName.SHA256) + "\nЖурнал: " + Path.Combine(Settings.DirectoryPath, Role + ".log"), "Сведения для первого подтверждения");
        forget.Click += (_, _) => { if (MessageBox.Show(this, "Удалить доверенные компьютеры? При следующем подключении потребуется новое подтверждение.", "Сброс доверия", MessageBoxButtons.YesNo) != DialogResult.Yes) return; paused = true; Disconnect(); settings.Trusted.Clear(); declined.Clear(); SaveSettings(); Report("Доверие сброшено. Нажмите «Возобновить поиск»."); };
        control.CheckedChanged += async (_, _) => { if (!control.Checked) ReleaseKeys(); if (connection != null) await SafeSend(connection, new("control", control.Checked ? "yes" : "no")); };
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
        FormClosed += (_, _) => { closing = true; timer.Stop(); searchTimer.Stop(); Disconnect(); certificate?.Dispose(); tray.Dispose(); };
        searchTimer.Tick += async (_, _) => await Search();
        Shown += async (_, _) => await StartAutomatic();
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
                case "frame":
                    if (!IsTeacher || students.SelectedItem == p) {
                        using var ms = new MemoryStream(Convert.FromBase64String(m.Data)); using var original = Image.FromStream(ms);
                        var image = new Bitmap(original); var old = screen.Image; screen.Image = image; old?.Dispose();
                    } break;
                case "broadcast-stop": screen.Image?.Dispose(); screen.Image = null; break;
                case "control-request":
                    if (!IsTeacher && !control.Checked && MessageBox.Show(this, "Разрешить учителю управлять мышью и клавиатурой? Снять разрешение можно в этом окне.", "Запрос учителя", MessageBoxButtons.YesNo) == DialogResult.Yes) control.Checked = true;
                    break;
                case "control": p.Control = m.Data == "yes"; status.Text = p.Name + (p.Control ? ": управление разрешено" : ": управление выключено"); break;
                case "release-keys": if (!IsTeacher) ReleaseKeys(); break;
                case "click": case "right":
                    if (!IsTeacher && control.Checked) {
                        var bounds = Screen.PrimaryScreen!.Bounds;
                        SetCursorPos(bounds.Left + m.X * bounds.Width / 65535, bounds.Top + m.Y * bounds.Height / 65535);
                        uint down = m.Type == "right" ? 8u : 2u; mouse_event(down, 0, 0, 0, UIntPtr.Zero); mouse_event(down * 2, 0, 0, 0, UIntPtr.Zero);
                    } break;
                case "key-down": case "key-up": if (!IsTeacher && control.Checked && m.X >= 0 && m.X <= 255) { var key = (byte)m.X; if (m.Type == "key-down") heldKeys.Add(key); else heldKeys.Remove(key); keybd_event(key, 0, m.Type == "key-up" ? 2u : 0u, UIntPtr.Zero); } break;
            }
        } } catch (Exception e) { if (!closing && !paused) Report("Соединение закрыто: " + Explain(e)); }
        finally {
            p.Dispose(); peers.Remove(p); students.Items.Remove(p);
            if (connection == p) { connection = null; control.Checked = false; ReleaseKeys(); screen.Image?.Dispose(); screen.Image = null; }
        }
    }
    async Task SafeSend(Peer p, Packet m) { try { await p.Send(m); } catch { p.Dispose(); } }
    async Task SendScreens()
    {
        if (sending || closing) return;
        var targets = IsTeacher ? peers.ToArray() : connection == null ? [] : new[] { connection };
        if (targets.Length == 0) return;
        sending = true;
        try {
            Packet frame;
            if (IsTeacher && !broadcast.Checked) frame = new("ping");
            else {
                var bounds = Screen.PrimaryScreen!.Bounds;
                using var bitmap = new Bitmap(bounds.Width, bounds.Height);
                using (var g = Graphics.FromImage(bitmap)) g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                using var scaled = new Bitmap(bitmap, new Size(1024, Math.Max(1, bounds.Height * 1024 / bounds.Width)));
                using var ms = new MemoryStream(); scaled.Save(ms, ImageFormat.Jpeg);
                frame = new("frame", Convert.ToBase64String(ms.ToArray()), bounds.Width, bounds.Height);
            }
            await Task.WhenAll(targets.Select(p => SafeSend(p, frame)));
        } catch (Exception e) { status.Text = "Захват экрана: " + e.Message; }
        finally { sending = false; }
    }
    void Disconnect()
    {
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
        try { await discovery.Announce(); }
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
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
}
