using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Classroom;
record VideoProfile(string Name, int MaxWidth, int FramesPerSecond, long JpegQuality)
{
    public static readonly VideoProfile[] All = [
        new("Экономный · 720p / до 10 кадров/с", 1280, 10, 78),
        new("Чёткий · 1080p / до 15 кадров/с", 1920, 15, 90),
        new("Текст · до 4K / до 8 кадров/с", 3840, 8, 95)
    ];
    public override string ToString() => Name;
}
static class ScreenCapture
{
    static readonly ImageCodecInfo Jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
    public static byte[] Capture(Rectangle bounds, VideoProfile profile)
    {
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bitmap)) g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        return Encode(bitmap, profile);
    }
    public static byte[] Encode(Bitmap source, VideoProfile profile)
    {
        // Never upscale a smaller monitor: that would add bandwidth without adding detail.
        var scale = Math.Min(1d, (double)profile.MaxWidth / source.Width);
        var size = new Size(Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)));
        using var image = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(image)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(source, new Rectangle(Point.Empty, size)); }
        using var options = new EncoderParameters(1);
        options.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, profile.JpegQuality);
        using var memory = new MemoryStream(); image.Save(memory, Jpeg, options);
        return Peer.Encode(new("frame", Convert.ToBase64String(memory.ToArray()), source.Width, source.Height));
    }
}
