namespace Classroom;
static class Branding
{
    public static Image Logo(bool teacher)
    {
        using var resource = typeof(Branding).Assembly.GetManifestResourceStream("ScreenLink.logo.png") ?? throw new IOException("Логотип программы не найден.");
        using var image = Image.FromStream(resource); return new Bitmap(image);
    }
}
