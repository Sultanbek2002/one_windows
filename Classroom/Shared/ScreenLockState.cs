namespace Classroom;
sealed class ScreenLockState
{
    string token = "", rejected = "";
    long deadline;
    public bool TryRenew(string value, long now)
    {
        if (!Guid.TryParse(value, out _) || value == rejected) return false;
        token = value; deadline = now + 12000; return true;
    }
    public bool Expired(long now) => deadline != 0 && now >= deadline;
    public void Release() { rejected = token; deadline = 0; }
}
