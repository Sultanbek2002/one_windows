using System.Security.Cryptography;
namespace Classroom;
record AttachmentOffer(string Id, string Name, int Length, string Sha256);
record AttachmentChunk(string Id, int Offset, string Data);
record ReceivedAttachment(string Name, byte[] Data);
sealed class AttachmentReceiver : IDisposable
{
    public const int MaximumBytes = 10 * 1024 * 1024, ChunkBytes = 64 * 1024;
    AttachmentOffer? offer;
    MemoryStream? buffer;
    long updated;
    public string? PendingId => offer?.Id;
    public static bool ValidName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 200 && name != "." && name != ".." && !name.Any(char.IsControl) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name == Path.GetFileName(name) && name.TrimEnd('.', ' ') == name;
    public void Begin(AttachmentOffer value, long now)
    {
        if (offer != null) throw new IOException("Другая передача файла ещё не завершена.");
        if (!Guid.TryParse(value.Id, out _) || !ValidName(value.Name) || value.Length < 1 || value.Length > MaximumBytes || value.Sha256?.Length != 64 || !value.Sha256.All(Uri.IsHexDigit)) throw new IOException("Некорректное имя, размер или контрольная сумма файла. Максимум — 10 МБ.");
        offer = value; buffer = new MemoryStream(value.Length); updated = now;
    }
    public void Append(AttachmentChunk chunk, long now)
    {
        if (offer == null || buffer == null || chunk.Id != offer.Id || chunk.Offset != buffer.Length || chunk.Data == null || chunk.Data.Length > ((ChunkBytes + 2) / 3) * 4) throw new IOException("Нарушен порядок передачи файла.");
        var bytes = Convert.FromBase64String(chunk.Data);
        if (bytes.Length < 1 || bytes.Length > ChunkBytes || buffer.Length + bytes.Length > offer.Length) throw new IOException("Полученный файл превышает заявленный размер.");
        buffer.Write(bytes); updated = now;
    }
    public ReceivedAttachment Finish(string id)
    {
        if (offer == null || buffer == null || id != offer.Id || buffer.Length != offer.Length) throw new IOException("Файл получен не полностью.");
        var data = buffer.ToArray();
        if (!Convert.ToHexString(SHA256.HashData(data)).Equals(offer.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Контрольная сумма файла не совпала.");
        var result = new ReceivedAttachment(offer.Name, data); Dispose(); return result;
    }
    public bool Expired(long now) => offer != null && now - updated >= 60000;
    public void Dispose() { buffer?.Dispose(); buffer = null; offer = null; }
}
