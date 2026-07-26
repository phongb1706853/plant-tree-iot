namespace PlantTreeIoTServer.Services;

/// <summary>
/// Cờ chế độ auto/manual do SERVER làm chủ, theo từng thiết bị. Tách khỏi trường <c>mode</c> trong
/// telemetry (do firmware tự set và bị ép "manual" mỗi khi nhận lệnh pump/light).
///
/// Nguồn sự thật cho việc auto-tưới có chạy hay không, và cho giá trị <c>mode</c> mà server gộp vào
/// mọi lệnh actuator (qua <see cref="ControlModeResolver"/>). Chỉ lệnh mode tường minh
/// (<c>/auto</c>, <c>{"mode":...}</c>, <c>{"auto":...}</c>) mới thay đổi cờ này.
///
/// LƯU BỀN trên <c>Device.ControlMode</c> trong Mongo -> sống sót qua restart/redeploy. Chưa đặt ->
/// mặc định "auto" (khớp mặc định firmware, cũng là mặc định AN TOÀN cho cây: tiếp tục tưới tự động).
/// Đăng ký SINGLETON, dùng chung giữa ControlController và MqttBackgroundService.
/// </summary>
public class DeviceModeStore
{
    public const string Auto = "auto";

    private readonly MongoDbService _mongo;

    public DeviceModeStore(MongoDbService mongo) => _mongo = mongo;

    /// <summary>Mode server đang giữ cho thiết bị (mặc định "auto" khi chưa có lệnh mode nào).</summary>
    public async Task<string> GetAsync(string deviceId)
        => await _mongo.GetDeviceControlModeAsync(deviceId) ?? Auto;

    /// <summary>Ghi mode server giữ cho thiết bị (thường gọi sau <see cref="ControlModeResolver.Apply"/>).</summary>
    public Task SetAsync(string deviceId, string mode)
        => _mongo.SetDeviceControlModeAsync(deviceId, mode);

    /// <summary>Thiết bị có đang ở chế độ auto theo cờ server không.</summary>
    public async Task<bool> IsAutoAsync(string deviceId)
        => await GetAsync(deviceId) == Auto;
}
