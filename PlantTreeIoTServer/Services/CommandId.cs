namespace PlantTreeIoTServer.Services;

/// <summary>
/// Sinh "id" cho mỗi lệnh xmini/control. QoS 1 có thể giao một lệnh hơn một lần; firmware nhớ id của lệnh gần nhất
/// (so 39 ký tự đầu, chỉ nhận chuỗi khác rỗng) và bỏ lệnh trùng (backend-api.md mục 5.4). Dạng khuyến nghị:
/// "c-&lt;epoch_ms&gt;-&lt;seq&gt;" — tối đa 2 + 13 + 1 + 19 = 35 ký tự, luôn dưới giới hạn 39.
/// </summary>
public static class CommandId
{
    public const int MaxLength = 39;

    private static long _seq;

    public static string Next() => Next(DateTimeOffset.UtcNow);

    public static string Next(DateTimeOffset now)
        => $"c-{now.ToUnixTimeMilliseconds()}-{Interlocked.Increment(ref _seq)}";
}
