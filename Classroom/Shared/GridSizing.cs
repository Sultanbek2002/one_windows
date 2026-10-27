namespace Classroom;
static class GridSizing
{
    public static int Columns(int availableWidth) => Math.Clamp((Math.Max(1, availableWidth) + 12) / 212, 1, 4);
    public static int TileWidth(int availableWidth, int columns) => Math.Max(1, (availableWidth - 12 * (columns - 1)) / columns);
}
