using PlantTreeIoTServer.Services;
using Xunit;

namespace PlantTreeIoTServer.Tests;

// Phát hiện mất kết nối (hàm thuần theo thời gian truyền vào): khoảng lặng telemetry vs ngưỡng.
public class OfflineMonitorTests
{
    private static readonly System.DateTime Now = new(2026, 7, 26, 12, 0, 0, System.DateTimeKind.Utc);

    [Fact]
    public void Silence_reaches_threshold_fires_offline()
    {
        var lastSeen = Now.AddSeconds(-240);
        Assert.True(OfflineMonitor.ShouldFireOffline(lastSeen, Now, alreadyOffline: false, thresholdSeconds: 240));
    }

    [Fact]
    public void Silence_below_threshold_does_not_fire()
    {
        var lastSeen = Now.AddSeconds(-239);
        Assert.False(OfflineMonitor.ShouldFireOffline(lastSeen, Now, alreadyOffline: false, thresholdSeconds: 240));
    }

    [Fact]
    public void Already_offline_does_not_refire()
    {
        // Đã báo offline rồi thì không báo lại mỗi lần quét.
        var lastSeen = Now.AddSeconds(-600);
        Assert.False(OfflineMonitor.ShouldFireOffline(lastSeen, Now, alreadyOffline: true, thresholdSeconds: 240));
    }
}
