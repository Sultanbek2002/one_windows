using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Classroom;
record Packet(string Type, string Data = "", int X = 0, int Y = 0);
sealed class Peer : IDisposable
{
    readonly TcpClient client;
    readonly SslStream stream;
    readonly StreamReader reader;
    readonly SemaphoreSlim gate = new(1);
    public string Name = "Ученик";
    public bool Control { get; set; }
    public Peer(TcpClient c, SslStream s) { client = c; stream = s; reader = new(s, Encoding.UTF8, false, 65536, true); }
    public async Task Send(Packet p)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(p) + "\n");
        await gate.WaitAsync();
        try { await stream.WriteAsync(bytes).AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { gate.Release(); }
    }
    public async Task<Packet?> Read(int timeoutSeconds = 30) { var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(timeoutSeconds)); return line == null ? null : JsonSerializer.Deserialize<Packet>(line); }
    public override string ToString() => Name;
    public void Dispose() { client.Dispose(); }
}
