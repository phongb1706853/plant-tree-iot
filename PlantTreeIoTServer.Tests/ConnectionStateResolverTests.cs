using PlantTreeIoTServer.Models;
using PlantTreeIoTServer.Services;
using Xunit;

namespace PlantTreeIoTServer.Tests;

// Máy trạng thái kết nối theo backend-api.md mục 7.4 (hàm thuần theo thời gian truyền vào).
public class ConnectionStateResolverTests
{
    private const int OfflineAfterS = 240;
    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private static DeviceStatus Status(string state, int secondsAgo, bool retained = false, int? nextWakeS = null) => new()
    {
        DeviceId = "ESP32S3_Zone1",
        State = state,
        NextWakeS = nextWakeS,
        Retained = retained,
        ReceivedAt = Now.AddSeconds(-secondsAgo),
    };

    private static string Resolve(DeviceStatus? status, int? telemetrySecondsAgo)
        => ConnectionStateResolver.Resolve(status,
            telemetrySecondsAgo.HasValue ? Now.AddSeconds(-telemetrySecondsAgo.Value) : null,
            Now, OfflineAfterS).State;

    [Fact]
    public void Nothing_heard_is_unknown()
    {
        Assert.Equal("unknown", Resolve(null, null));
    }

    [Fact]
    public void Recent_telemetry_is_online()
    {
        Assert.Equal("online", Resolve(null, telemetrySecondsAgo: 10));
    }

    [Fact]
    public void Silence_over_90s_is_late()
    {
        Assert.Equal("late", Resolve(null, telemetrySecondsAgo: 91));
    }

    [Fact]
    public void Silence_over_threshold_is_offline()
    {
        Assert.Equal("offline", Resolve(null, telemetrySecondsAgo: 241));
    }

    [Fact]
    public void Lwt_newer_than_telemetry_is_offline_at_once()
    {
        Assert.Equal("offline", Resolve(Status("offline", secondsAgo: 5), telemetrySecondsAgo: 30));
    }

    [Fact]
    public void Telemetry_after_lwt_means_back_online()
    {
        Assert.Equal("online", Resolve(Status("offline", secondsAgo: 30), telemetrySecondsAgo: 5));
    }

    [Fact]
    public void Live_online_status_counts_as_heard()
    {
        Assert.Equal("online", Resolve(Status("online", secondsAgo: 20), telemetrySecondsAgo: 200));
    }

    [Fact]
    public void Sleeping_within_deadline_is_sleeping()
    {
        // Hạn chót = giờ nhận + next_wake_s + 60 s.
        Assert.Equal("sleeping", Resolve(Status("sleeping", secondsAgo: 350, nextWakeS: 294), telemetrySecondsAgo: 355));
    }

    [Fact]
    public void Sleeping_past_deadline_is_late()
    {
        Assert.Equal("late", Resolve(Status("sleeping", secondsAgo: 400, nextWakeS: 294), telemetrySecondsAgo: 405));
    }

    [Fact]
    public void Sleeping_past_three_cycles_is_offline()
    {
        Assert.Equal("offline", Resolve(Status("sleeping", secondsAgo: 1100, nextWakeS: 294), telemetrySecondsAgo: 1105));
    }

    [Fact]
    public void Retained_status_is_not_used_for_timing()
    {
        // Bản retained nhận lúc vừa subscribe: tuổi không rõ -> không tính là "vừa nghe thấy".
        Assert.Equal("unknown", Resolve(Status("online", secondsAgo: 1, retained: true), null));
        Assert.Equal("unknown", Resolve(Status("sleeping", secondsAgo: 1, retained: true, nextWakeS: 300), null));
    }

    [Fact]
    public void Retained_lwt_still_says_offline()
    {
        Assert.Equal("offline", Resolve(Status("offline", secondsAgo: 1, retained: true), null));
    }
}
