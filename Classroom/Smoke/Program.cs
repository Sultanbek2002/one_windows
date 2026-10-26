using Classroom;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using var cert = ClassCertificate.Create();
using var studentCert = ClassCertificate.Create();
var fingerprint = cert.GetCertHashString(HashAlgorithmName.SHA256);
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
    if (!await client.TrySendFrame(Peer.Encode(new("frame", new string('a', 300000), 1920, 1080)))) throw new Exception("Idle video channel rejected a frame");
    if ((await client.Read()) != new Packet("control", "no")) throw new Exception("Control revocation corrupted");
    client.Dispose(); await receive.WaitAsync(TimeSpan.FromSeconds(15));
    Console.WriteLine("PASS: mutual TLS with pinned teacher and student certificates, code-free Unicode handshake, large frame, control revocation packet, disconnect.");
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
