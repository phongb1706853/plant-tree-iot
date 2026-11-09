namespace PlantTreeIoTServer.Services;

/// <summary>
/// Chọn nơi gửi một sự kiện thông báo (NotifyEventDetector / quét offline đã quyết định GỬI GÌ, ở đây chỉ GỬI ĐI ĐÂU).
///   - Thiết bị THẬT: webhook Notify (NotifyClient) như trước — KHÔNG BAO GIỜ lên MQTT.
///   - Thiết bị ẢO SIM_*: luôn publish lên xmini-sim/{id}/notify cho khung "🔔 Thông báo" trên dashboard; chỉ gửi
///     thêm webhook Notify thật khi người dùng bật "gửi cả sang Notify thật" (Device.SimNotifyForward), để demo
///     không làm phiền người dùng thật.
/// </summary>
public class NotifyDispatcher
{
    private readonly MqttPublisherService _publisher;
    private readonly NotifyClient _notifyClient;

    public NotifyDispatcher(MqttPublisherService publisher, NotifyClient notifyClient)
    {
        _publisher = publisher;
        _notifyClient = notifyClient;
    }

    public async Task DispatchAsync(NotifyEvent evt, bool forwardSimToNotify)
    {
        if (!SimTopics.IsSimDevice(evt.DeviceId))
        {
            await _notifyClient.SendAsync(evt);
            return;
        }

        await _publisher.PublishSimNotifyAsync(evt);
        if (forwardSimToNotify)
            await _notifyClient.SendAsync(evt);
    }
}
