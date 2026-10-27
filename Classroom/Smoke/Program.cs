using Classroom;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using var cert = ClassCertificate.Create();
using var studentCert = ClassCertificate.Create();
var fingerprint = cert.GetCertHashString(HashAlgorithmName.SHA256);
var actionSamples = new[] { new ClassAction(Guid.NewGuid().ToString(), "Задание №1\nСкопируйте этот текст: Привет!"), new ClassAction(Guid.NewGuid().ToString(), "https://example.com/lesson?q=1&lang=ru") };
var server = new TcpListener(IPAddress.Loopback, 0);
server.Start();
try
{
    var port = ((IPEndPoint)server.LocalEndpoint).Port;
    var receive = Task.Run(async () => {
        using var tcp = await server.AcceptTcpClientAsync();
        using var ssl = new SslStream(tcp.GetStream(), false, (_, c, _, _) => c?.GetCertHashString(HashAlgorithmName.SHA256) == studentCert.GetCertHashString(HashAlgorithmName.SHA256));
        try { await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = cert, ClientCertificateRequired = true }); }
        catch (Exception e) { Console.Error.WriteLine("SERVER TLS: " + e); throw; }
        using var peer = new Peer(tcp, ssl);
        var hello = await peer.Read();
        if (hello != new Packet("hello-v2", "Ученик")) throw new Exception("Handshake payload corrupted");
        await peer.Send(new("accepted"));
        for (var i = 0; i < actionSamples.Length; i++) {
            var kind = i == 0 ? "teacher-message" : "open-url";
            await peer.Send(new(kind, System.Text.Json.JsonSerializer.Serialize(actionSamples[i])));
            var ack = await peer.Read();
            var receipt = ack?.Type == "action-result" ? System.Text.Json.JsonSerializer.Deserialize<ActionReceipt>(ack.Data) : null;
            if (receipt?.Id != actionSamples[i].Id || receipt.Kind != kind || !receipt.Success) throw new Exception("Message/link delivery acknowledgement corrupted");
        }
        var frame = await peer.Read();
        if (frame?.Type != "frame" || frame.Data.Length != 300000 || frame.X != 1920) throw new Exception("Large frame corrupted");
        await peer.Send(new("control", "no"));
        if ((await peer.Read()) != null) throw new Exception("EOF expected");
    });
    using var tcp = new TcpClient(); await tcp.ConnectAsync(IPAddress.Loopback, port);
    using var ssl = new SslStream(tcp.GetStream(), false, (_, c, _, _) => c != null && c.GetCertHashString(HashAlgorithmName.SHA256) == fingerprint);
    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "Classroom", ClientCertificates = new X509CertificateCollection { studentCert } });
    using var client = new Peer(tcp, ssl);
    await client.Send(new("hello-v2", "Ученик"));
    if ((await client.Read())?.Type != "accepted") throw new Exception("Handshake rejected");
    for (var i = 0; i < actionSamples.Length; i++) {
        var packet = await client.Read(); var kind = i == 0 ? "teacher-message" : "open-url";
        var action = packet?.Type == kind ? System.Text.Json.JsonSerializer.Deserialize<ClassAction>(packet.Data) : null;
        if (!ClassActions.Valid(action) || action != actionSamples[i]) throw new Exception("Teacher message/link text corrupted");
        if (i == 1 && !ClassActions.WebAddress(action!.Text, out _)) throw new Exception("Valid web URL rejected");
        await client.Send(new("action-result", System.Text.Json.JsonSerializer.Serialize(new ActionReceipt(action!.Id, kind, true, "Simulated receiver accepted payload"))));
    }
    if (!await client.TrySendFrame(Peer.Encode(new("frame", new string('a', 300000), 1920, 1080)))) throw new Exception("Idle video channel rejected a frame");
    if ((await client.Read()) != new Packet("control", "no")) throw new Exception("Control revocation corrupted");
    client.Dispose(); await receive.WaitAsync(TimeSpan.FromSeconds(15));
    Console.WriteLine("PASS: mutual TLS with pinned teacher and student certificates, code-free Unicode handshake, large frame, control revocation packet, disconnect.");
    Console.WriteLine("PASS: Unicode/multiline teacher message, web link and correlated acknowledgements over TLS (no UI/browser actions).");
}
finally { server.Stop(); }

var rejectServer = new TcpListener(IPAddress.Loopback, 0);
rejectServer.Start();
try {
    var check = Task.Run(async () => {
        using var tcp = await rejectServer.AcceptTcpClientAsync();
        using var ssl = new SslStream(tcp.GetStream(), false);
        try { await ssl.AuthenticateAsServerAsync(cert); }
        catch (System.Security.Authentication.AuthenticationException) { }
        catch (IOException) { }
    });
    using var tcp = new TcpClient(); await tcp.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)rejectServer.LocalEndpoint).Port);
    using var ssl = new SslStream(tcp.GetStream(), false, (_, c, _, _) => c?.GetCertHashString(HashAlgorithmName.SHA256) == new string('0', 64));
    var rejected = false;
    try { await ssl.AuthenticateAsClientAsync("Classroom"); }
    catch (System.Security.Authentication.AuthenticationException) { rejected = true; }
    if (!rejected) throw new Exception("An untrusted teacher certificate was accepted");
    await check.WaitAsync(TimeSpan.FromSeconds(5));
    Console.WriteLine("PASS: changed/untrusted teacher certificate rejected during TLS.");
} finally { rejectServer.Stop(); }

// Test discovery on isolated test ports; no startup registry or real trust store is modified.
using var teacherDiscovery = new Discovery(true, fingerprint, 49832, 49833);
var found = new TaskCompletionSource<Beacon>(TaskCreationOptions.RunContinuationsAsynchronously);
var teacherLoop = teacherDiscovery.Listen((_, _) => Task.CompletedTask, e => found.TrySetException(e));
await teacherDiscovery.Announce(); // Student's UDP port is still closed: reproduce Windows ICMP reset.
await Task.Delay(300);
if (teacherLoop.IsCompleted) throw new Exception("Discovery stopped while no student was running");
using var studentDiscovery = new Discovery(false, studentCert.GetCertHashString(HashAlgorithmName.SHA256), 49833, 49832);
var studentLoop = studentDiscovery.Listen((beacon, _) => { found.TrySetResult(beacon); return Task.CompletedTask; }, e => found.TrySetException(e));
await studentDiscovery.Announce();
var discovered = await found.Task.WaitAsync(TimeSpan.FromSeconds(5));
if (discovered.Pin != fingerprint || discovered.Role != "teacher") throw new Exception("Wrong discovery response");
teacherDiscovery.Dispose(); studentDiscovery.Dispose();
await Task.WhenAll(teacherLoop, studentLoop);
Console.WriteLine("PASS: UDP discovery survives absent student and discovers it when started later.");

using var slide = new Bitmap(2560, 1440);
using (var graphics = Graphics.FromImage(slide)) { graphics.Clear(Color.White); using var font = new Font("Segoe UI", 22); graphics.DrawString("Demonstration: text 123 ABC", font, Brushes.Black, 40, 60); }
foreach (var profile in VideoProfile.All) {
    var wire = ScreenCapture.Encode(slide, profile);
    var packet = System.Text.Json.JsonSerializer.Deserialize<Packet>(System.Text.Encoding.UTF8.GetString(wire))!;
    using var buffer = new MemoryStream(Convert.FromBase64String(packet.Data)); using var image = Image.FromStream(buffer);
    var expectedWidth = Math.Min(slide.Width, profile.MaxWidth);
    if (image.Width != expectedWidth || image.Height != expectedWidth * 1440 / 2560 || packet.X != 2560 || packet.Y != 1440) throw new Exception("Frame dimensions or source coordinates changed");
}
using var small = new Bitmap(640, 480);
var smallPacket = System.Text.Json.JsonSerializer.Deserialize<Packet>(System.Text.Encoding.UTF8.GetString(ScreenCapture.Encode(small, VideoProfile.All[1])))!;
using var smallBytes = new MemoryStream(Convert.FromBase64String(smallPacket.Data)); using var smallImage = Image.FromStream(smallBytes);
if (smallImage.Width != 640 || smallImage.Height != 480) throw new Exception("Smaller monitor was upscaled");
Console.WriteLine("PASS: JPEG video profiles preserve aspect ratio, source coordinates and native resolution of smaller monitors.");
using var pixels = new Bitmap(73, 51);
for (var y = 0; y < pixels.Height; y++) for (var x = 0; x < pixels.Width; x++) pixels.SetPixel(x, y, Color.FromArgb((x * 31 + y * 7) % 256, (x * 13 + y * 47) % 256, (x * 83 + y * 19) % 256));
var losslessPacket = System.Text.Json.JsonSerializer.Deserialize<Packet>(System.Text.Encoding.UTF8.GetString(ScreenCapture.Encode(pixels, VideoProfile.All.Last())))!;
using var pngBuffer = new MemoryStream(Convert.FromBase64String(losslessPacket.Data)); using var restored = new Bitmap(pngBuffer);
for (var y = 0; y < pixels.Height; y++) for (var x = 0; x < pixels.Width; x++) if (pixels.GetPixel(x, y).ToArgb() != restored.GetPixel(x, y).ToArgb()) throw new Exception("Lossless mode changed a pixel");
Console.WriteLine("PASS: PNG maximum-quality mode preserves every source pixel exactly.");
var lockState = new ScreenLockState(); var firstLock = Guid.NewGuid().ToString();
if (lockState.TryRenew("invalid", 1000)) throw new Exception("Malformed lock command accepted");
if (!lockState.TryRenew(firstLock, 1000) || lockState.Expired(12999) || !lockState.Expired(13000)) throw new Exception("Lock lease does not expire in 12 seconds");
if (!lockState.TryRenew(firstLock, 4000) || lockState.Expired(13000) || !lockState.Expired(16000)) throw new Exception("Lock heartbeat failed to renew lease");
lockState.Release();
if (lockState.TryRenew(firstLock, 17000)) throw new Exception("Cancelled lock reactivated by delayed heartbeat");
if (!lockState.TryRenew(Guid.NewGuid().ToString(), 18000)) throw new Exception("A new explicit lock could not be activated");
lockState.Release();
Console.WriteLine("PASS: screen-lock expiry, heartbeat renewal, emergency/unlock cancellation and explicit re-lock.");
foreach (var invalid in new[] { "file:///C:/Windows/notepad.exe", "javascript:alert(1)", "data:text/html,hello", "shell:startup", "ftp://example.com", "https://user:pass@example.com", "https://example.com/\r\nhello", "not a url" }) if (ClassActions.WebAddress(invalid, out _)) throw new Exception("Unsafe or malformed URL accepted: " + invalid);
foreach (var valid in new[] { "https://example.com/lesson?q=hello%20world", "http://192.168.0.79:8080/" }) if (!ClassActions.WebAddress(valid, out _)) throw new Exception("Valid web URL rejected");
if (ClassActions.Valid(new ClassAction(Guid.NewGuid().ToString(), new string('x', 20001))) || ClassActions.Valid(new ClassAction(Guid.NewGuid().ToString(), "  "))) throw new Exception("Invalid teacher message accepted");
Console.WriteLine("PASS: web URL scheme/credential/control-character validation and message length limits.");
var powerState = new ShutdownState(); var shutdownCalls = 0;
powerState.Schedule(1000);
if (powerState.ExecuteIfDue(60999, () => shutdownCalls++)) throw new Exception("Shutdown occurred before countdown finished");
powerState.Schedule(30000);
if (!powerState.ExecuteIfDue(61000, () => shutdownCalls++) || shutdownCalls != 1) throw new Exception("Shutdown countdown or duplicate scheduling failed");
if (powerState.ExecuteIfDue(62000, () => shutdownCalls++)) throw new Exception("Shutdown executed twice");
powerState.Schedule(70000); powerState.Cancel();
if (powerState.ExecuteIfDue(200000, () => shutdownCalls++) || shutdownCalls != 1) throw new Exception("Cancelled shutdown executed");
Console.WriteLine("PASS: 60-second shutdown countdown, cancellation and one-shot execution with fake callback; no system shutdown performed.");
if (!AdminPassword.Matches("sultan_nice") || AdminPassword.Matches("") || AdminPassword.Matches("wrong-password") || AdminPassword.Matches("SULTAN_NICE")) throw new Exception("Administrator password verification failed");
if (!new Settings().AutoControl) throw new Exception("New student configuration must grant trusted-teacher control by default");
Console.WriteLine("PASS: administrator password matching/rejection and default trusted-teacher access; no user settings modified.");
foreach (var width in new[] { 100, 210, 450, 670, 900, 1400 }) {
    var columns = GridSizing.Columns(width); var card = GridSizing.TileWidth(width, columns);
    if (columns < 1 || columns > 4 || card * columns + 12 * (columns - 1) > width) throw new Exception("Student grid exceeds available width");
}
if (GridSizing.Columns(900) != 4 || GridSizing.Columns(670) != 3 || GridSizing.Columns(450) != 2 || GridSizing.Columns(210) != 1) throw new Exception("Student grid is not responsive");
Console.WriteLine("PASS: responsive student grid uses one to four columns without horizontal overflow.");
if (StartupRegistration.Command(@"C:\School Apps\Student.exe") != "\"C:\\School Apps\\Student.exe\"") throw new Exception("Startup executable path was not quoted");
if (new Settings().StartupConfiguredVersion != 0) throw new Exception("New or old settings must enroll automatic startup once");
Console.WriteLine("PASS: startup path quoting and enrollment defaults; registry was not modified and reboot was not tested.");
