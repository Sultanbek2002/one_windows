namespace Classroom;
static class BroadcastPacing
{
    // Estimate aggregate unicast payload, including the JSON/base64 wire representation.
    // Reserve headroom on a typical 100 Mbps classroom uplink; this is pacing, not a strict traffic shaper.
    public const long BudgetBitsPerSecond = 40000000;
    public static long Interval(int frameBytes, int recipients, int requestedFps) => Math.Max(
        (long)Math.Ceiling(1000d / Math.Max(1, requestedFps)),
        (long)Math.Ceiling(Math.Max(0, frameBytes) * (double)Math.Max(1, recipients) * 8000d / BudgetBitsPerSecond));
}
