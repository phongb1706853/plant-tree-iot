using PlantTreeIoTServer.Services;
using Xunit;

namespace PlantTreeIoTServer.Tests;

// Kiểm thử namespace MQTT của thiết bị ảo (hàm thuần).
// Bất biến quan trọng nhất: lệnh của thiết bị SIM_* KHÔNG BAO GIỜ đi xmini/control — topic ESP32 thật
// đang nghe (dùng chung, không mang device_id) — nên thiết bị ảo không thể làm bơm thật chạy.
public class SimTopicsTests
{
    [Theory]
    [InlineData("SIM_Zone1", true)]
    [InlineData("SIM_a", true)]
    [InlineData("ESP32S3_Zone1", false)]
    [InlineData("sim_Zone1", false)] // phân biệt hoa thường
    [InlineData("SIM_", false)]      // thiếu phần sau tiền tố
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSimDevice_requires_SIM_prefix(string? id, bool expected)
        => Assert.Equal(expected, SimTopics.IsSimDevice(id));

    [Fact]
    public void Sim_device_control_goes_to_its_own_topic()
        => Assert.Equal("xmini-sim/SIM_Zone1/control", SimTopics.ControlTopicFor("SIM_Zone1"));

    [Theory]
    [InlineData("ESP32S3_Zone1")]
    [InlineData("sim_Zone1")]
    public void Real_device_control_stays_on_shared_topic(string id)
        => Assert.Equal("xmini/control", SimTopics.ControlTopicFor(id));

    [Theory]
    [InlineData("SIM_a/b")]
    [InlineData("SIM_x+")]
    [InlineData("SIM_#")]
    public void Odd_sim_ids_never_fall_back_to_real_topic(string id)
        => Assert.StartsWith("xmini-sim/", SimTopics.ControlTopicFor(id));

    [Theory]
    [InlineData("xmini-sim/SIM_Zone1/sensor_data", "SIM_Zone1", "sensor_data")]
    [InlineData("xmini-sim/SIM_Zone1/config", "SIM_Zone1", "config")]
    public void TryParse_extracts_device_and_kind(string topic, string id, string kind)
    {
        Assert.True(SimTopics.TryParse(topic, out var d, out var k));
        Assert.Equal(id, d);
        Assert.Equal(kind, k);
    }

    [Theory]
    [InlineData("xmini/sensor_data")]                   // topic thật
    [InlineData("xmini-sim/SIM_Zone1")]                 // thiếu loại
    [InlineData("xmini-sim/SIM_Zone1/control")]         // chiều server -> thiết bị, server không xử lý
    [InlineData("xmini-sim/ESP32S3_Zone1/sensor_data")] // id không phải SIM_
    [InlineData("xmini-sim/SIM_a/b/sensor_data")]       // thừa cấp
    [InlineData("other/SIM_Zone1/sensor_data")]
    public void TryParse_rejects_non_sim_topics(string topic)
        => Assert.False(SimTopics.TryParse(topic, out _, out _));
}
