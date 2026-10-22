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
    await client.Send(new("frame", new string('a', 300000), 1920, 1080));
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
using var studentDiscovery = new Discovery(false, studentCert.GetCertHashString(HashAlgorithmName.SHA256), 49833, 49832);
var found = new TaskCompletionSource<Beacon>(TaskCreationOptions.RunContinuationsAsynchronously);
var teacherLoop = teacherDiscovery.Listen((_, _) => Task.CompletedTask, e => found.TrySetException(e));
var studentLoop = studentDiscovery.Listen((beacon, _) => { found.TrySetResult(beacon); return Task.CompletedTask; }, e => found.TrySetException(e));
await studentDiscovery.Announce();
var discovered = await found.Task.WaitAsync(TimeSpan.FromSeconds(5));
if (discovered.Pin != fingerprint || discovered.Role != "teacher") throw new Exception("Wrong discovery response");
teacherDiscovery.Dispose(); studentDiscovery.Dispose();
await Task.WhenAll(teacherLoop, studentLoop);
Console.WriteLine("PASS: UDP discovery and teacher response on loopback.");
