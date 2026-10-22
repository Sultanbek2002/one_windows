using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace Classroom;
record Beacon(string Protocol, string Role, string Name, string Pin);
sealed class Discovery : IDisposable
{
    public const int TeacherPort = 47832, StudentPort = 47833;
    readonly UdpClient udp;
    readonly Beacon self;
    readonly int remotePort;
    readonly CancellationTokenSource stop = new();
    public Discovery(bool teacher, string pin, int localPort = 0, int targetPort = 0)
    {
        self = new("classroom-v2", teacher ? "teacher" : "student", Environment.MachineName, pin);
        remotePort = targetPort != 0 ? targetPort : teacher ? StudentPort : TeacherPort;
        udp = new UdpClient(new IPEndPoint(IPAddress.Any, localPort != 0 ? localPort : teacher ? TeacherPort : StudentPort)) { EnableBroadcast = true };
        // A discovery probe may reach a PC where Student is not running yet.
        // Windows otherwise surfaces its ICMP Port Unreachable as receive error 10054.
        if (OperatingSystem.IsWindows()) {
            try { udp.Client.IOControl(unchecked((int)0x9800000C), new byte[4], null); } // SIO_UDP_CONNRESET = false
            catch { udp.Dispose(); throw; }
        }
    }
    public async Task Listen(Func<Beacon, IPAddress, Task> receive, Action<Exception> failure)
    {
        try {
            while (!stop.IsCancellationRequested) {
                UdpReceiveResult packet;
                try { packet = await udp.ReceiveAsync(stop.Token); }
                catch (SocketException e) when (e.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionRefused) { continue; }
                if (packet.Buffer.Length > 2048) continue;
                Beacon? beacon;
                try { beacon = JsonSerializer.Deserialize<Beacon>(packet.Buffer); } catch (JsonException) { continue; }
                if (beacon?.Protocol != self.Protocol || beacon.Role is not ("teacher" or "student") || beacon.Role == self.Role || beacon.Pin?.Length != 64 || !beacon.Pin.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(beacon.Name) || beacon.Name.Length > 100) continue;
                if (self.Role == "teacher") {
                    try { await udp.SendAsync(JsonSerializer.SerializeToUtf8Bytes(self), packet.RemoteEndPoint); }
                    catch (SocketException e) when (e.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionRefused) { continue; }
                }
                await receive(beacon, packet.RemoteEndPoint.Address);
            }
        } catch (OperationCanceledException) { } catch (ObjectDisposedException) { }
        catch (Exception e) { if (!stop.IsCancellationRequested) failure(e); }
    }
    public async Task Announce()
    {
        var destinations = new HashSet<IPAddress> { IPAddress.Broadcast, IPAddress.Loopback };
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
            foreach (var u in nic.GetIPProperties().UnicastAddresses.Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)) {
                var ip = u.Address.GetAddressBytes(); var mask = u.IPv4Mask.GetAddressBytes();
                destinations.Add(new IPAddress(ip.Zip(mask, (a, b) => (byte)(a | ~b)).ToArray()));
            }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(self);
        Exception? last = null; var sent = false;
        foreach (var ip in destinations) try { await udp.SendAsync(bytes, new IPEndPoint(ip, remotePort)); sent = true; } catch (SocketException e) { last = e; }
        if (!sent && last != null) throw last;
    }
    public void Dispose() { stop.Cancel(); udp.Dispose(); }
}
