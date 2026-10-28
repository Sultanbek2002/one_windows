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
    public bool SupportsScreenLock { get; set; }
    public bool SupportsClassActions { get; set; }
    public bool SupportsShutdown { get; set; }
    public bool SupportsBroadcastStatus { get; set; }
    public bool BroadcastShown { get; set; }
    public bool LockRequested { get; set; }
    public string LockToken { get; set; } = "";
    public Peer(TcpClient c, SslStream s) { client = c; client.NoDelay = true; stream = s; reader = new(s, Encoding.UTF8, false, 65536, true); }
    public static byte[] Encode(Packet p) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(p) + "\n");
    public async Task Send(Packet p)
    {
        var bytes = Encode(p);
        await gate.WaitAsync();
        try { await Write(bytes); }
        finally { gate.Release(); }
    }
    // One in-flight frame per peer. Slow recipients skip new frames rather than queue them.
    public async Task<bool> TrySendFrame(byte[] bytes)
    {
        if (!await gate.WaitAsync(0)) return false;
        try { await Write(bytes); return true; }
        finally { gate.Release(); }
    }
    async Task Write(byte[] bytes)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await stream.WriteAsync(bytes, timeout.Token); }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { Dispose(); throw new TimeoutException("Передача сообщения не завершилась за 15 секунд; соединение закрыто без продолжения неполной записи."); }
        catch { Dispose(); throw; }
    }
    public async Task<Packet?> Read(int timeoutSeconds = 30) { var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(timeoutSeconds)); return line == null ? null : JsonSerializer.Deserialize<Packet>(line); }
    public override string ToString() => Name;
    public void Dispose() { client.Dispose(); }
}
