namespace Classroom;
sealed class ShutdownState
{
    public long Deadline { get; private set; }
    public bool Pending => Deadline != 0;
    public void Schedule(long now) { if (!Pending) Deadline = now + 60000; }
    public void Cancel() => Deadline = 0;
    public bool ExecuteIfDue(long now, Action execute)
    {
        if (!Pending || now < Deadline) return false;
        Cancel(); execute(); return true;
    }
}
