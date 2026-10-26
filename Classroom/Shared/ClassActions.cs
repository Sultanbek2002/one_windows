using System.Text.Json;
namespace Classroom;
record ClassAction(string Id, string Text);
record ActionReceipt(string Id, string Kind, bool Success, string Detail);
static class ClassActions
{
    public static bool Valid(ClassAction? action) => action != null && Guid.TryParse(action.Id, out _) && !string.IsNullOrWhiteSpace(action.Text) && action.Text.Length <= 20000;
    public static bool WebAddress(string text, out Uri? uri)
    {
        uri = null;
        if (text.Length > 8192 || text.Any(char.IsControl)) return false;
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var result) || result.Scheme is not ("http" or "https") || string.IsNullOrEmpty(result.Host) || !string.IsNullOrEmpty(result.UserInfo)) return false;
        uri = result; return true;
    }
}
