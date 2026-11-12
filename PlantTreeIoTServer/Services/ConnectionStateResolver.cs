using PlantTreeIoTServer.Models;

namespace PlantTreeIoTServer.Services;

/// <summary>Kết luận kết nối của một thiết bị tại một thời điểm.</summary>
/// <param name="State">"online" | "sleeping" | "late" | "offline" | "unknown".</param>
/// <param name="LastHeardAt">Giờ server nhận bản tin LIVE gần nhất (telemetry hoặc status), null nếu chưa nghe.</param>
/// <param name="ExpectedBy">Hạn chót server chờ bản tin kế tiếp (sleeping: giờ nhận + next_wake_s + ân hạn).</param>
public sealed record ConnectionInfo(string State, DateTime? LastHeardAt, DateTime? ExpectedBy);

/// <summary>
/// Máy trạng thái online / sleeping / trễ / offline theo backend-api.md mục 7.4 — HÀM THUẦN theo thời gian truyền vào.
/// <list type="bullet">
///   <item>LWT "offline" mới hơn telemetry cuối -> offline ngay.</item>
///   <item>"sleeping" live: hạn chót = giờ nhận + next_wake_s + 60 s ân hạn. Quá hạn -> late; quá 3 × chu kỳ -> offline.</item>
///   <item>Thức (online / awake): im lặng &gt; 90 s -> late; &gt; ngưỡng offline (mặc định 240 s) -> offline.</item>
/// </list>
/// Bản tin RETAINED không dùng để tính thời gian (tuổi không rõ), chỉ dùng để biết state cuối thiết bị báo.
/// </summary>
public static class ConnectionStateResolver
{
    public const int SleepGraceS = 60;
    public const int LateAfterS = 90;
    public const int LostAfterCycles = 3;

    public const string Online = "online";
    public const string Sleeping = "sleeping";
    public const string Late = "late";
    public const string Offline = "offline";
    public const string Unknown = "unknown";

    /// <summary>Hạn chót của một lần ngủ có kế hoạch: giờ nhận + next_wake_s + ân hạn nối mạng.</summary>
    public static DateTime SleepDeadline(DateTime receivedAtUtc, int nextWakeS)
        => receivedAtUtc.AddSeconds(Math.Max(0, nextWakeS) + SleepGraceS);

    /// <summary>Mốc coi là MẤT LIÊN LẠC khi đang ngủ: quá 3 × chu kỳ dự kiến (next_wake_s + ân hạn).</summary>
    public static DateTime SleepLostAfter(DateTime receivedAtUtc, int nextWakeS)
        => receivedAtUtc.AddSeconds(LostAfterCycles * (Math.Max(0, nextWakeS) + SleepGraceS));

    public static ConnectionInfo Resolve(DeviceStatus? status, DateTime? lastTelemetryUtc, DateTime nowUtc, int offlineAfterS)
    {
        var liveStatusAt = status is { Retained: false } ? status.ReceivedAt : (DateTime?)null;
        var lastHeard = Max(lastTelemetryUtc, liveStatusAt);

        // Status mới hơn telemetry cuối thì state của nó là thông tin mới nhất (kể cả retained, khi chưa có telemetry).
        var statusIsNewest = status != null && (lastTelemetryUtc == null || status.ReceivedAt >= lastTelemetryUtc);

        if (statusIsNewest && status!.State == Offline)
            return new ConnectionInfo(Offline, lastHeard, null);

        if (statusIsNewest && status!.State == Sleeping && !status.Retained && status.NextWakeS.HasValue)
        {
            var deadline = SleepDeadline(status.ReceivedAt, status.NextWakeS.Value);
            if (nowUtc <= deadline) return new ConnectionInfo(Sleeping, lastHeard, deadline);
            var state = nowUtc <= SleepLostAfter(status.ReceivedAt, status.NextWakeS.Value) ? Late : Offline;
            return new ConnectionInfo(state, lastHeard, deadline);
        }

        if (lastHeard == null)
            return new ConnectionInfo(Unknown, null, null);

        var silenceS = (nowUtc - lastHeard.Value).TotalSeconds;
        var awakeState = silenceS <= LateAfterS ? Online : silenceS <= offlineAfterS ? Late : Offline;
        return new ConnectionInfo(awakeState, lastHeard, lastHeard.Value.AddSeconds(LateAfterS));
    }

    private static DateTime? Max(DateTime? a, DateTime? b)
        => a == null ? b : b == null ? a : (a > b ? a : b);
}
