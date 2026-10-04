namespace PlantTreeIoTServer.Services;

/// <summary>
/// Namespace MQTT riêng cho THIẾT BỊ ẢO (giả lập trong trình duyệt — tab "📟 Thiết bị ảo" của demo-dashboard).
/// Thiết bị ảo có id tiền tố <c>SIM_</c> và dùng topic THEO TỪNG THIẾT BỊ:
///   xmini-sim/{deviceId}/sensor_data | config   (thiết bị -> server)
///   xmini-sim/{deviceId}/control                (server -> thiết bị)
/// Payload y hệt hợp đồng xmini/ thật. ESP32 thật chỉ nghe xmini/control nên KHÔNG BAO GIỜ nhận lệnh
/// của thiết bị ảo. HÀM THUẦN, không I/O. Xem docs/superpowers/specs/2026-10-04-device-simulator-design.md.
/// </summary>
public static class SimTopics
{
    public const string Prefix = "xmini-sim";
    public const string DevicePrefix = "SIM_";
    public const string RealControlTopic = "xmini/control";

    public const string KindSensorData = "sensor_data";
    public const string KindConfig = "config";

    public const string SensorDataFilter = Prefix + "/+/" + KindSensorData;
    public const string ConfigFilter = Prefix + "/+/" + KindConfig;

    /// <summary>Id thuộc thiết bị ảo: bắt đầu bằng "SIM_" (phân biệt hoa thường) và có ít nhất 1 ký tự sau tiền tố.</summary>
    public static bool IsSimDevice(string? deviceId)
        => deviceId != null
           && deviceId.Length > DevicePrefix.Length
           && deviceId.StartsWith(DevicePrefix, StringComparison.Ordinal);

    /// <summary>
    /// Topic điều khiển: thiết bị ảo -> xmini-sim/{id}/control; còn lại -> xmini/control (dùng chung).
    /// Id ảo có ký tự lạ ('/', '+', '#') vẫn ở trong xmini-sim/ — publish có thể lỗi nhưng không rò sang thiết bị thật.
    /// </summary>
    public static string ControlTopicFor(string deviceId)
        => IsSimDevice(deviceId) ? $"{Prefix}/{deviceId}/control" : RealControlTopic;

    /// <summary>Tách deviceId + loại message (sensor_data / config) từ topic thiết bị ảo gửi lên.</summary>
    public static bool TryParse(string topic, out string deviceId, out string kind)
    {
        deviceId = string.Empty;
        kind = string.Empty;

        var parts = topic.Split('/');
        if (parts.Length != 3 || parts[0] != Prefix || !IsSimDevice(parts[1]))
            return false;
        if (parts[2] != KindSensorData && parts[2] != KindConfig)
            return false;

        deviceId = parts[1];
        kind = parts[2];
        return true;
    }

    /// <summary>
    /// Telemetry ảo chỉ hợp lệ khi device_id trong payload TRÙNG deviceId trong topic (chống nhầm/giả topic).
    /// </summary>
    public static bool PayloadMatchesTopic(string topicDeviceId, string? payloadDeviceId)
        => IsSimDevice(topicDeviceId) && string.Equals(topicDeviceId, payloadDeviceId, StringComparison.Ordinal);
}
