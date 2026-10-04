# Thiết bị ảo (ESP32 giả lập trong trình duyệt) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Thêm thiết bị ảo `SIM_*` chạy trong tab mới của `demo-dashboard.html`. Thiết bị ảo nói chuyện với server qua HiveMQ thật, trên namespace `xmini-sim/{id}/...`, để test tay end-to-end và demo mà không cần ESP32.

**Architecture:**
- **Server** (.NET): thêm helper thuần `SimTopics`.
  - Publisher gửi lệnh cho id `SIM_*` sang `xmini-sim/{id}/control`. Thiết bị thật vẫn dùng `xmini/control`, không đổi.
  - `MqttBackgroundService` subscribe thêm `xmini-sim/+/sensor_data|config` và đưa vào **cùng pipeline** với telemetry thật (lưu DB, lastSeen, Notify, tự tưới).
- **Trình duyệt:**
  - Lõi mô phỏng là JS thuần, đặt trong block `<script id="sim-core">`, có test bằng `node --test`.
  - Bộ điều khiển tab (mqtt.js qua WebSocket, Start/Stop, log, cảnh SVG, nút kịch bản, nút gọi API) nằm trong một IIFE riêng.

**Tech Stack:** .NET 10 + MQTTnet + xUnit (server); HTML/JS thuần + mqtt.js 5 (CDN jsDelivr) + `node:test` (Node ≥ 18; máy dev có v24).

**Spec:** `docs/superpowers/specs/2026-10-04-device-simulator-design.md`

## Global Constraints

- ID thiết bị ảo:
  - Server coi là thiết bị ảo khi id bắt đầu bằng `SIM_` (phân biệt hoa thường) và có ≥ 1 ký tự sau tiền tố.
  - Trang chỉ chấp nhận id theo regex `^SIM_[A-Za-z0-9_-]{1,40}$`. Mặc định `SIM_Zone1`.
- Topic thiết bị ảo:
  - `xmini-sim/{deviceId}/sensor_data`, `xmini-sim/{deviceId}/config` (thiết bị → server).
  - `xmini-sim/{deviceId}/control` (server → thiết bị).
  - QoS 0, retain false.
- **Bất biến an toàn:** server **không bao giờ** publish lệnh của id `SIM_*` lên `xmini/control`. Đường đi của thiết bị thật giữ nguyên.
- Payload y hệt hợp đồng `xmini/`:
  - Telemetry: 21 trường snake_case.
  - Config: `{"config":{...}}`.
  - Lệnh: một object khoá phẳng (`pump`, `light`, `light_pwm`, `mode`, `auto`, `config`, `message`, `message_secs`).
- **Thiết bị ảo không tự đổi mode.** `pump`/`light`/`light_pwm` không đổi mode; chỉ khoá `mode` / `auto` mới đổi.
- MQTT trong trình duyệt:
  - Kết nối `wss://{host}:8884/mqtt`.
  - Host mặc định `ba4fbc53bce842ffb0fcd51178d78414.s1.eu.hivemq.cloud`.
  - Credential chỉ lưu trong localStorage key `planttree-sim-mqtt`, **không bao giờ ghi vào file**.
- Mô phỏng:
  - Tick 10 giây (dt cố định 10s).
  - Đất +8 mỗi 10s khi bơm chạy và còn nước; −1 mỗi 10s khi không tưới.
  - Pin −0.1 mỗi 10s, thêm −0.5 mỗi 10s khi bơm chạy.
  - Volt = 6.4 + 2.0 × % / 100.
- Mọi nội dung đến từ broker hoặc server được hiển thị bằng `textContent`. **Không dùng `innerHTML`** cho dữ liệu động.
- Thư viện: `https://cdn.jsdelivr.net/npm/mqtt@5/dist/mqtt.min.js`. Ngoài ra không thêm package nào (không có `package.json`).
- Comment viết tiếng Việt, theo giọng code hiện có. Commit message theo kiểu repo: `feat: mo ta tieng viet khong dau`.
- **Không commit** thay đổi đang có sẵn của `plant-tree.sln`. Chỉ `git add` đúng các file của task.

## Review Focus

1. **ID có ký tự lạ** (`SIM_a/b`, `SIM_x+`): lệnh vẫn không bao giờ rơi vào `xmini/control`, và trang chặn ngay ID không hợp lệ. Có test `Odd_sim_ids_never_fall_back_to_real_topic` ở Task 1 và bước kiểm tra tay ở Task 4.
2. **Payload chứa HTML** (`{"message":"<img src=x onerror=alert(1)>"}`): log và màn TFT hiện nguyên văn, không chạy script. Kiểm tra ở Task 4 bước 6 mục 5 (log) và Task 5 bước 5 mục 5 (TFT).
3. **Sai credential MQTT hoặc mất mạng:** badge báo lỗi, client tự thử lại mỗi 5 giây, log không bị lụt vì các dòng trùng được gộp thành `×N`. Kiểm tra ở Task 4 bước 6 mục 6.
4. **Token hết hạn hoặc ID thuộc user khác:** hiện thông báo rõ, nút Start bật lại. Kiểm tra ở Task 4 bước 6 mục 6.
5. **Lệnh đến khi đang "Mất kết nối" giả lập hoặc đã Stop:** không đổi state, không gửi gì. Có guard `if (!st || linkDown) return` trong `onControl` (Task 4) và bước kiểm tra tay ở Task 5.

---

## File Structure

| File | Trách nhiệm |
|---|---|
| `PlantTreeIoTServer/Services/SimTopics.cs` (mới) | Hàm thuần: nhận diện id ảo, chọn topic lệnh, parse topic ảo, khớp payload↔topic |
| `PlantTreeIoTServer/Services/MqttPublisherService.cs` (sửa) | Chọn topic qua `SimTopics.ControlTopicFor` |
| `PlantTreeIoTServer/Services/MqttBackgroundService.cs` (sửa) | Subscribe `xmini-sim/+/...`, định tuyến và nhận telemetry/config ảo vào pipeline chung |
| `PlantTreeIoTServer.Tests/SimTopicsTests.cs` (mới) | xUnit cho `SimTopics` |
| `demo-dashboard.html` (sửa) | `<script id="sim-core">` (lõi mô phỏng thuần) + tab "📟 Thiết bị ảo" (HTML/CSS/IIFE điều khiển) + sửa CSS header bị dán nhầm + expose `window.plantTester` |
| `tests-js/sim-core.test.mjs` (mới) | `node:test` cho lõi mô phỏng, nạp trực tiếp từ `demo-dashboard.html` |
| `API-GUIDE.md`, `DEMO-NOTES.md` (sửa) | Tài liệu topic `xmini-sim/` và kịch bản demo |

---

### Task 1: `SimTopics` + định tuyến lệnh cho thiết bị ảo

**Files:**
- Create: `PlantTreeIoTServer/Services/SimTopics.cs`
- Modify: `PlantTreeIoTServer/Services/MqttPublisherService.cs:88-117`
- Test: `PlantTreeIoTServer.Tests/SimTopicsTests.cs`

**Interfaces:**
- Consumes: không có.
- Produces (namespace `PlantTreeIoTServer.Services`, `public static class SimTopics`):
  - `const string Prefix = "xmini-sim"`, `DevicePrefix = "SIM_"`, `RealControlTopic = "xmini/control"`
  - `const string KindSensorData = "sensor_data"`, `KindConfig = "config"`
  - `const string SensorDataFilter = "xmini-sim/+/sensor_data"`, `ConfigFilter = "xmini-sim/+/config"`
  - `bool IsSimDevice(string? deviceId)`
  - `string ControlTopicFor(string deviceId)`
  - `bool TryParse(string topic, out string deviceId, out string kind)`

- [ ] **Step 1: Viết test lỗi**

Tạo `PlantTreeIoTServer.Tests/SimTopicsTests.cs`:

```csharp
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
```

- [ ] **Step 2: Chạy test, xác nhận lỗi**

Run: `dotnet test PlantTreeIoTServer.Tests/PlantTreeIoTServer.Tests.csproj --filter "FullyQualifiedName~SimTopicsTests"`
Expected: build FAIL, lỗi `CS0103: The name 'SimTopics' does not exist in the current context`.

- [ ] **Step 3: Viết `SimTopics`**

Tạo `PlantTreeIoTServer/Services/SimTopics.cs`:

```csharp
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
}
```

- [ ] **Step 4: Cho publisher dùng `SimTopics`**

Trong `PlantTreeIoTServer/Services/MqttPublisherService.cs`, thay đoạn từ comment topic tới hết `PublishControlAsync`.

Thay:
```csharp
    // Topic điều khiển dùng chung theo hợp đồng (mqtt-api.md): thiết bị subscribe xmini/control.
    private const string ControlTopic = "xmini/control";

    /// <summary>
    /// Publish một object JSON PHẲNG xuống xmini/control (QoS 0, không retained) — đúng hợp đồng
```
bằng:
```csharp
    /// <summary>
    /// Publish một object JSON PHẲNG xuống topic điều khiển (QoS 0, không retained) — đúng hợp đồng
    /// firmware. Thiết bị thật: xmini/control (dùng chung). Thiết bị ảo SIM_*: xmini-sim/{id}/control
    /// (xem SimTopics) — nên lệnh cho thiết bị ảo KHÔNG BAO GIỜ tới ESP32 thật. Payload y hệt nhau.
    /// Theo hợp đồng
```

Thay:
```csharp
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(ControlTopic)
```
bằng:
```csharp
            var topic = SimTopics.ControlTopicFor(deviceId);
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
```

Thay:
```csharp
            _logger.LogInformation("Published control to {Topic} for {DeviceId}: {Payload}", ControlTopic, deviceId, payload);
```
bằng:
```csharp
            _logger.LogInformation("Published control to {Topic} for {DeviceId}: {Payload}", topic, deviceId, payload);
```

Sau khi sửa, đoạn XML doc phải đọc liền mạch: "... Payload y hệt nhau. Theo hợp đồng firmware. Thiết bị chỉ hiểu các khoá phẳng: pump / light / ..." (phần đuôi giữ nguyên).

- [ ] **Step 5: Chạy test, xác nhận pass**

Run: `dotnet test PlantTreeIoTServer.Tests/PlantTreeIoTServer.Tests.csproj --filter "FullyQualifiedName~SimTopicsTests"`
Expected: PASS, 0 failed.

Run: `dotnet test PlantTreeIoTServer.Tests/PlantTreeIoTServer.Tests.csproj`
Expected: PASS toàn bộ (các test cũ không đổi).

- [ ] **Step 6: Commit**

```bash
git add PlantTreeIoTServer/Services/SimTopics.cs PlantTreeIoTServer/Services/MqttPublisherService.cs PlantTreeIoTServer.Tests/SimTopicsTests.cs
git commit -m "feat: SimTopics + gui lenh cho thiet bi ao SIM_* sang xmini-sim/{id}/control"
```

---

### Task 2: Server nhận telemetry/config của thiết bị ảo

**Files:**
- Modify: `PlantTreeIoTServer/Services/SimTopics.cs` (thêm `PayloadMatchesTopic`)
- Modify: `PlantTreeIoTServer/Services/MqttBackgroundService.cs:12-20, 138-145, 170-212, cuối file`
- Test: `PlantTreeIoTServer.Tests/SimTopicsTests.cs`

**Interfaces:**
- Consumes: `SimTopics.SensorDataFilter`, `ConfigFilter`, `TryParse`, `KindSensorData`, `IsSimDevice` (Task 1); `MongoDbService.GetDeviceAsync(string) : Task<Device?>`, `UpsertDeviceConfigAsync(DeviceConfig)` (có sẵn).
- Produces: `SimTopics.PayloadMatchesTopic(string topicDeviceId, string? payloadDeviceId) : bool`. Các hàm private mới `ProcessTelemetryAsync`, `HandleSimSensorDataAsync`, `HandleSimConfigAsync` không dùng ngoài file.

- [ ] **Step 1: Viết test lỗi**

Thêm vào cuối class `SimTopicsTests` (trước dấu `}` đóng class):

```csharp
    [Fact]
    public void Payload_device_id_must_match_topic_exactly()
    {
        Assert.True(SimTopics.PayloadMatchesTopic("SIM_Zone1", "SIM_Zone1"));
        Assert.False(SimTopics.PayloadMatchesTopic("SIM_Zone1", "SIM_Zone2"));
        Assert.False(SimTopics.PayloadMatchesTopic("SIM_Zone1", "sim_zone1"));
        Assert.False(SimTopics.PayloadMatchesTopic("SIM_Zone1", null));
        Assert.False(SimTopics.PayloadMatchesTopic("ESP32S3_Zone1", "ESP32S3_Zone1")); // không phải id ảo
    }
```

- [ ] **Step 2: Chạy test, xác nhận lỗi**

Run: `dotnet test PlantTreeIoTServer.Tests/PlantTreeIoTServer.Tests.csproj --filter "FullyQualifiedName~SimTopicsTests"`
Expected: build FAIL, `'SimTopics' does not contain a definition for 'PayloadMatchesTopic'`.

- [ ] **Step 3: Thêm `PayloadMatchesTopic`**

Trong `SimTopics.cs`, thêm sau `TryParse` (trước `}` đóng class):

```csharp

    /// <summary>
    /// Telemetry ảo chỉ hợp lệ khi device_id trong payload TRÙNG deviceId trong topic (chống nhầm/giả topic).
    /// </summary>
    public static bool PayloadMatchesTopic(string topicDeviceId, string? payloadDeviceId)
        => IsSimDevice(topicDeviceId) && string.Equals(topicDeviceId, payloadDeviceId, StringComparison.Ordinal);
```

- [ ] **Step 4: Chạy test, xác nhận pass**

Run: `dotnet test PlantTreeIoTServer.Tests/PlantTreeIoTServer.Tests.csproj --filter "FullyQualifiedName~SimTopicsTests"`
Expected: PASS.

- [ ] **Step 5: Subscribe topic ảo**

Trong `MqttBackgroundService.cs`, thay:
```csharp
                    var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter(TopicSensorData, MqttQualityOfServiceLevel.AtMostOnce)
                        .WithTopicFilter(TopicConfig, MqttQualityOfServiceLevel.AtMostOnce)
                        .Build();
                    await _mqttClient.SubscribeAsync(subscribeOptions, stoppingToken);
                    _logger.LogInformation("Subscribed to {S} and {C} (QoS 0)", TopicSensorData, TopicConfig);
```
bằng:
```csharp
                    var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter(TopicSensorData, MqttQualityOfServiceLevel.AtMostOnce)
                        .WithTopicFilter(TopicConfig, MqttQualityOfServiceLevel.AtMostOnce)
                        // Thiết bị ảo SIM_* (namespace riêng theo từng thiết bị, xem SimTopics).
                        .WithTopicFilter(SimTopics.SensorDataFilter, MqttQualityOfServiceLevel.AtMostOnce)
                        .WithTopicFilter(SimTopics.ConfigFilter, MqttQualityOfServiceLevel.AtMostOnce)
                        .Build();
                    await _mqttClient.SubscribeAsync(subscribeOptions, stoppingToken);
                    _logger.LogInformation("Subscribed to {S}, {C}, {SimS}, {SimC} (QoS 0)",
                        TopicSensorData, TopicConfig, SimTopics.SensorDataFilter, SimTopics.ConfigFilter);
```

- [ ] **Step 6: Định tuyến message ảo**

Trong `HandleMessageAsync`, thay:
```csharp
            if (topic == TopicSensorData)
                await HandleSensorDataAsync(payload);
            else if (topic == TopicConfig)
                await HandleConfigAsync(payload);
            else
                _logger.LogDebug("Ignoring message on unexpected topic {Topic}", topic);
```
bằng:
```csharp
            if (topic == TopicSensorData)
                await HandleSensorDataAsync(payload);
            else if (topic == TopicConfig)
                await HandleConfigAsync(payload);
            else if (SimTopics.TryParse(topic, out var simDeviceId, out var simKind))
            {
                if (simKind == SimTopics.KindSensorData)
                    await HandleSimSensorDataAsync(simDeviceId, payload);
                else
                    await HandleSimConfigAsync(simDeviceId, payload);
            }
            else
                _logger.LogDebug("Ignoring message on unexpected topic {Topic}", topic);
```

- [ ] **Step 7: Tách pipeline chung + thêm handler telemetry ảo**

Thay toàn bộ hàm `HandleSensorDataAsync` hiện tại:
```csharp
    private async Task HandleSensorDataAsync(string payload)
    {
        var telemetry = JsonSerializer.Deserialize<XminiTelemetry>(payload, JsonOpts);
        if (telemetry?.DeviceId == null)
        {
            _logger.LogWarning("xmini/sensor_data without device_id, skipping");
            return;
        }

        _lastSeenDeviceId = telemetry.DeviceId;

        var sensorData = telemetry.ToSensorData();
        await _mongoDbService.InsertSensorDataAsync(sensorData);
        await _mongoDbService.UpdateDeviceLastSeenAsync(telemetry.DeviceId);

        _logger.LogInformation(
            "Telemetry from {DeviceId}: mode={Mode} soil={Soil}% light={Lux}lux batt={Batt}% pump={Pump} lightOn={Light}",
            telemetry.DeviceId, telemetry.Mode, telemetry.SoilPercent, telemetry.LightLux,
            sensorData.BatteryPercent, telemetry.PumpOn, telemetry.LightOn);

        await EvaluateNotifyEventsAsync(telemetry, sensorData);
        await EvaluateAutoWateringAsync(telemetry);
    }
```
bằng:
```csharp
    private async Task HandleSensorDataAsync(string payload)
    {
        var telemetry = JsonSerializer.Deserialize<XminiTelemetry>(payload, JsonOpts);
        if (telemetry?.DeviceId == null)
        {
            _logger.LogWarning("xmini/sensor_data without device_id, skipping");
            return;
        }

        _lastSeenDeviceId = telemetry.DeviceId;
        await ProcessTelemetryAsync(telemetry);
    }

    // Thiết bị ảo: chỉ nhận khi device_id khớp topic VÀ thiết bị có trong DB của server NÀY — server khác
    // cùng nghe broker (vd Mac) sẽ bỏ qua thiết bị ảo không phải của mình. KHÔNG ghi _lastSeenDeviceId,
    // để xmini/config của thiết bị THẬT không bị gán nhầm sang thiết bị ảo.
    private async Task HandleSimSensorDataAsync(string deviceId, string payload)
    {
        var telemetry = JsonSerializer.Deserialize<XminiTelemetry>(payload, JsonOpts);
        if (!SimTopics.PayloadMatchesTopic(deviceId, telemetry?.DeviceId))
        {
            _logger.LogWarning("Sim telemetry on topic of {DeviceId} carries device_id={PayloadId}, skipping",
                deviceId, telemetry?.DeviceId);
            return;
        }

        if (await _mongoDbService.GetDeviceAsync(deviceId) == null)
        {
            _logger.LogDebug("Sim telemetry for {DeviceId} not registered on this server, skipping", deviceId);
            return;
        }

        await ProcessTelemetryAsync(telemetry!);
    }

    // Pipeline chung cho telemetry thật và ảo: lưu, lastSeen, Notify, tự tưới.
    private async Task ProcessTelemetryAsync(XminiTelemetry telemetry)
    {
        var sensorData = telemetry.ToSensorData();
        await _mongoDbService.InsertSensorDataAsync(sensorData);
        await _mongoDbService.UpdateDeviceLastSeenAsync(telemetry.DeviceId!);

        _logger.LogInformation(
            "Telemetry from {DeviceId}: mode={Mode} soil={Soil}% light={Lux}lux batt={Batt}% pump={Pump} lightOn={Light}",
            telemetry.DeviceId, telemetry.Mode, telemetry.SoilPercent, telemetry.LightLux,
            sensorData.BatteryPercent, telemetry.PumpOn, telemetry.LightOn);

        await EvaluateNotifyEventsAsync(telemetry, sensorData);
        await EvaluateAutoWateringAsync(telemetry);
    }
```

- [ ] **Step 8: Thêm handler config ảo**

Thêm hàm sau ngay sau `HandleConfigAsync` (trước `}` đóng class):

```csharp

    // Config thiết bị ảo: deviceId lấy TỪ TOPIC (không đoán theo telemetry gần nhất như xmini/config thật).
    private async Task HandleSimConfigAsync(string deviceId, string payload)
    {
        var envelope = JsonSerializer.Deserialize<XminiConfigEnvelope>(payload, JsonOpts);
        if (envelope?.Config == null)
        {
            _logger.LogWarning("Sim config for {DeviceId} without 'config' object, skipping", deviceId);
            return;
        }

        if (await _mongoDbService.GetDeviceAsync(deviceId) == null)
        {
            _logger.LogDebug("Sim config for {DeviceId} not registered on this server, skipping", deviceId);
            return;
        }

        await _mongoDbService.UpsertDeviceConfigAsync(envelope.Config.ToDeviceConfig(deviceId));
        _logger.LogInformation("Stored device config for {DeviceId} from {Topic}", deviceId,
            $"{SimTopics.Prefix}/{deviceId}/{SimTopics.KindConfig}");
    }
```

- [ ] **Step 9: Cập nhật doc comment đầu class**

Thay:
```csharp
/// để firmware không rớt khỏi auto khi nhận pump. Điều khiển tay vẫn qua ControlController.
/// </summary>
public class MqttBackgroundService : BackgroundService
```
bằng:
```csharp
/// để firmware không rớt khỏi auto khi nhận pump. Điều khiển tay vẫn qua ControlController.
/// Thiết bị ẢO (SIM_*, giả lập trong demo-dashboard) dùng xmini-sim/{id}/sensor_data|config — cùng
/// pipeline, chỉ khác cách lấy deviceId (từ topic) và phải đã đăng ký trên server này. Xem SimTopics.
/// </summary>
public class MqttBackgroundService : BackgroundService
```

- [ ] **Step 10: Build + chạy toàn bộ test**

Run: `dotnet build PlantTreeIoTServer/PlantTreeIoTServer.csproj`
Expected: `Build succeeded`, không có warning mới về nullable ở các dòng vừa sửa.

Run: `dotnet test PlantTreeIoTServer.Tests/PlantTreeIoTServer.Tests.csproj`
Expected: PASS toàn bộ.

- [ ] **Step 11: Commit**

```bash
git add PlantTreeIoTServer/Services/SimTopics.cs PlantTreeIoTServer/Services/MqttBackgroundService.cs PlantTreeIoTServer.Tests/SimTopicsTests.cs
git commit -m "feat: server nhan telemetry/config thiet bi ao qua xmini-sim/+/... vao pipeline chung"
```

---

### Task 3: Lõi mô phỏng JS (`<script id="sim-core">`) + test node

**Files:**
- Modify: `demo-dashboard.html` (chèn block mới ngay trước `</body>`)
- Test: `tests-js/sim-core.test.mjs`

**Interfaces:**
- Consumes: không có.
- Produces: `window.SimCore` gồm
  - `DEFAULT_CONFIG` (object 15 ngưỡng), `SCENARIOS` (string[])
  - `createState(deviceId: string, mode?: string) → state`. Các trường state: `deviceId, mode, soil, tempC, humidity, pressure, lux, prevLux, battPct, waterOk, pumpOn, pumpOnSince, lightOn, lightPwm, lowBatt, battFull, battCut, message, messageUntil, config`
  - `applyControl(state, cmd, nowMs) → { changed: boolean, publishConfig: boolean }` (sửa trực tiếp state)
  - `step(state, dtSec, nowMs, rand?) → void`
  - `applyScenario(state, name) → boolean` (tên trong `SCENARIOS`; `false` nếu không biết tên)
  - `toTelemetry(state) → object` (21 trường snake_case)
  - `configPayload(state) → { config: {...} }`
  - `voltage(state) → number`

- [ ] **Step 1: Viết test lỗi**

Tạo `tests-js/sim-core.test.mjs`:

```js
// Test lõi thiết bị ảo (hàm thuần) — chạy: node --test tests-js/sim-core.test.mjs
// Lõi nằm TRONG demo-dashboard.html (block <script id="sim-core">) để dashboard vẫn là 1 file mang đi được;
// test nạp block đó trong CÙNG realm (new Function) để deepStrictEqual so prototype đúng.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const html = readFileSync(new URL('../demo-dashboard.html', import.meta.url), 'utf8');
const block = html.match(/<script id="sim-core">([\s\S]*?)<\/script>/);
if (!block) throw new Error('Không tìm thấy <script id="sim-core"> trong demo-dashboard.html');
const win = {};
new Function('window', block[1])(win);
const Sim = win.SimCore;

const T0 = 1_000_000;
const fresh = (mode = 'auto') => Sim.createState('SIM_Test', mode);
const noRand = () => 0.5; // dao động = 0 -> kết quả xác định

const TELEMETRY_FIELDS = [
  'device_id', 'temperature_c', 'humidity_percent', 'pressure_hpa', 'altitude_m', 'temperature_bmp_c',
  'light_lux', 'soil_percent', 'soil_dry_flag', 'battery_voltage_v', 'battery_current_ma', 'battery_power_mw',
  'battery_percent', 'light_on', 'light_pwm', 'pump_on', 'mode', 'low_batt', 'batt_full', 'batt_cut', 'water_ok',
];

test('telemetry has exactly the 21 contract fields', () => {
  const t = Sim.toTelemetry(fresh());
  assert.deepEqual(Object.keys(t).sort(), [...TELEMETRY_FIELDS].sort());
  assert.equal(t.device_id, 'SIM_Test');
  assert.equal(t.soil_percent, 45);
  assert.equal(t.mode, 'auto');
  assert.equal(t.battery_voltage_v, 8);
});

test('pump / light / light_pwm commands never change mode', () => {
  for (const mode of ['auto', 'manual']) {
    const s = fresh(mode);
    Sim.applyControl(s, { pump: true }, T0);
    Sim.applyControl(s, { light: true }, T0);
    Sim.applyControl(s, { light_pwm: 90 }, T0);
    assert.equal(s.mode, mode);
    assert.equal(s.pumpOn, true);
    assert.equal(s.lightOn, true);
    assert.equal(s.lightPwm, 90);
  }
});

test('manual off overrides a running pump while staying in auto', () => {
  const s = fresh('auto');
  Sim.applyControl(s, { pump: true, mode: 'auto' }, T0);
  const r = Sim.applyControl(s, { pump: false, mode: 'auto' }, T0 + 3000);
  assert.equal(r.changed, true);
  assert.equal(s.pumpOn, false);
  assert.equal(s.mode, 'auto');
});

test('only mode / auto keys change mode', () => {
  const s = fresh('auto');
  Sim.applyControl(s, { mode: 'manual' }, T0); assert.equal(s.mode, 'manual');
  Sim.applyControl(s, { auto: true }, T0); assert.equal(s.mode, 'auto');
  Sim.applyControl(s, { auto: false }, T0); assert.equal(s.mode, 'manual');
  Sim.applyControl(s, { mode: 'turbo' }, T0); assert.equal(s.mode, 'manual'); // giá trị lạ -> bỏ qua
});

test('pump switches itself off after pump_max_run_s (20s)', () => {
  const s = fresh();
  Sim.applyControl(s, { pump: true }, T0);
  Sim.step(s, 10, T0 + 10_000, noRand);
  assert.equal(s.pumpOn, true);
  Sim.step(s, 10, T0 + 20_000, noRand);
  assert.equal(s.pumpOn, false);
  assert.equal(Math.round(s.soil), 61); // 45 + 8 + 8
});

test('soil dries 1% per 10s and does not rise when the tank is empty', () => {
  const s = fresh();
  Sim.step(s, 10, T0, noRand);
  assert.equal(Math.round(s.soil), 44);
  Sim.applyScenario(s, 'water-empty');
  Sim.applyControl(s, { pump: true }, T0);
  Sim.step(s, 10, T0 + 10_000, noRand);
  assert.equal(Math.round(s.soil), 43);
  assert.equal(s.pumpOn, true); // bơm chạy không
});

test('dry-soil scenario sets soil 15 and soil_dry_flag', () => {
  const s = fresh();
  assert.equal(Sim.applyScenario(s, 'dry-soil'), true);
  const t = Sim.toTelemetry(s);
  assert.equal(t.soil_percent, 15);
  assert.equal(t.soil_dry_flag, true);
});

test('low battery raises low_batt; voltage follows percent (2S)', () => {
  const s = fresh();
  Sim.applyScenario(s, 'low-battery');
  const t = Sim.toTelemetry(s);
  assert.equal(t.low_batt, true);
  assert.equal(t.battery_percent, 15);
  assert.equal(t.battery_voltage_v, 6.7);
  assert.equal(t.batt_cut, false);
});

test('battery cut switches loads off in any mode and ignores turn-on; full charge recovers', () => {
  for (const mode of ['auto', 'manual']) {
    const s = fresh(mode);
    Sim.applyControl(s, { pump: true, light: true }, T0);
    Sim.applyScenario(s, 'battery-cut');
    assert.equal(s.battCut, true);
    assert.equal(s.pumpOn, false);
    assert.equal(s.lightOn, false);
    Sim.applyControl(s, { pump: true, light: true }, T0);
    assert.equal(s.pumpOn, false);
    assert.equal(s.lightOn, false);
    Sim.applyScenario(s, 'battery-full');
    assert.equal(s.battCut, false);
    assert.equal(s.battFull, true);
  }
});

test('auto light reacts only when lux crosses a threshold; manual override sticks', () => {
  const s = fresh('auto');
  Sim.applyScenario(s, 'night');
  assert.equal(s.lightOn, true);
  assert.equal(s.lightPwm, 180);
  Sim.applyControl(s, { light: false, mode: 'auto' }, T0);
  Sim.step(s, 10, T0 + 10_000, noRand);
  Sim.step(s, 10, T0 + 20_000, noRand);
  assert.equal(s.lightOn, false); // vẫn tôn trọng lệnh tay
  Sim.applyScenario(s, 'day');
  assert.equal(s.lightOn, false);
  Sim.applyScenario(s, 'night');
  assert.equal(s.lightOn, true); // lần vượt ngưỡng kế tiếp
});

test('auto light does nothing in manual mode', () => {
  const s = fresh('manual');
  Sim.applyScenario(s, 'night');
  assert.equal(s.lightOn, false);
});

test('config {} asks for an echo; partial config is clamped, merged, unknown keys ignored', () => {
  const s = fresh();
  const r1 = Sim.applyControl(s, { config: {} }, T0);
  assert.equal(r1.publishConfig, true);
  assert.deepEqual(Sim.configPayload(s).config, Sim.DEFAULT_CONFIG);
  Sim.applyControl(s, { config: { soil_on_pct: 150, light_auto_pwm: -4, batt_crit_v: 12, foo: 1, lux_on: 'x' } }, T0);
  assert.equal(s.config.soil_on_pct, 100);
  assert.equal(s.config.light_auto_pwm, 0);
  assert.equal(s.config.batt_crit_v, 9);
  assert.equal(s.config.lux_on, 120);
  assert.equal('foo' in s.config, false);
  assert.equal(Sim.DEFAULT_CONFIG.soil_on_pct, 30); // mặc định không bị sửa lây
});

test('message shows on TFT and clears after message_secs', () => {
  const s = fresh();
  Sim.applyControl(s, { message: 'Hello', message_secs: 15 }, T0);
  assert.equal(s.message, 'Hello');
  Sim.step(s, 10, T0 + 10_000, noRand);
  assert.equal(s.message, 'Hello');
  Sim.step(s, 10, T0 + 20_000, noRand);
  assert.equal(s.message, '');
});

test('garbage commands are ignored without changing state', () => {
  const s = fresh();
  for (const cmd of [null, 42, 'pump', [1], { pump: 'yes', light: 1, light_pwm: 'x' }]) {
    const r = Sim.applyControl(s, cmd, T0);
    assert.equal(r.changed, false);
    assert.equal(r.publishConfig, false);
  }
  assert.equal(s.pumpOn, false);
  assert.equal(s.lightOn, false);
  assert.equal(s.lightPwm, 180);
});

test('reset restores the initial state but keeps the server-owned mode', () => {
  const s = fresh('manual');
  Sim.applyScenario(s, 'dry-soil');
  Sim.applyControl(s, { pump: true }, T0);
  assert.equal(Sim.applyScenario(s, 'reset'), true);
  assert.equal(s.mode, 'manual');
  assert.equal(Math.round(s.soil), 45);
  assert.equal(s.pumpOn, false);
});

test('unknown scenario returns false', () => {
  assert.equal(Sim.applyScenario(fresh(), 'meteor'), false);
});
```

- [ ] **Step 2: Chạy test, xác nhận lỗi**

Run: `node --test tests-js/sim-core.test.mjs`
Expected: FAIL với `Error: Không tìm thấy <script id="sim-core"> trong demo-dashboard.html`.

- [ ] **Step 3: Chèn lõi mô phỏng vào dashboard**

Trong `demo-dashboard.html`, thay đoạn cuối file:
```html
  })();
</script>
</body>
</html>
```
bằng:
```html
  })();
</script>

<script id="sim-core">
  // ===== Lõi thiết bị ảo (HÀM THUẦN — không DOM, không MQTT) =====
  // Mô phỏng ESP32 Xmini cho tab "📟 Thiết bị ảo". Test: node --test tests-js/sim-core.test.mjs
  // Spec: docs/superpowers/specs/2026-10-04-device-simulator-design.md (mục 3).
  // KHÁC firmware thật: thiết bị ảo KHÔNG tự đổi mode khi nhận pump/light — chỉ khoá mode/auto mới đổi
  // (server làm chủ mode); và gửi telemetry ngay sau lệnh (bộ điều khiển tab lo việc đó).
  (function () {
    const DEFAULT_CONFIG = {
      soil_on_pct: 30, soil_off_pct: 60, pump_max_run_s: 20, pump_cooldown_s: 300,
      lux_on: 120, lux_off: 300, light_auto_pwm: 180,
      batt_warn_pct: 20, batt_recover_pct: 25,
      soil_dry: 2800, soil_wet: 1200,
      batt_full_on_v: 8.3, batt_full_off_v: 8.1, batt_crit_v: 6.4, batt_crit_recover_v: 6.8,
    };
    // Dải clamp cho khoá config nhận qua lệnh {"config":{...}}.
    const CONFIG_RANGES = {
      soil_on_pct: [0, 100], soil_off_pct: [0, 100], batt_warn_pct: [0, 100], batt_recover_pct: [0, 100],
      pump_max_run_s: [0, 3600], pump_cooldown_s: [0, 3600],
      lux_on: [0, 65535], lux_off: [0, 65535],
      light_auto_pwm: [0, 255],
      soil_dry: [0, 4095], soil_wet: [0, 4095],
      batt_full_on_v: [5, 9], batt_full_off_v: [5, 9], batt_crit_v: [5, 9], batt_crit_recover_v: [5, 9],
    };
    const INITIAL = { soil: 45, tempC: 27, humidity: 60, pressure: 1008, lux: 600, battPct: 80 };
    const SCENARIOS = ['dry-soil', 'water-empty', 'water-refill', 'low-battery', 'battery-cut',
                       'battery-full', 'night', 'day', 'reset'];

    const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
    const round = (v, d) => { const f = 10 ** d; return Math.round(v * f) / f; };

    function createState(deviceId, mode) {
      return {
        deviceId,
        mode: mode === 'manual' ? 'manual' : 'auto',
        soil: INITIAL.soil, tempC: INITIAL.tempC, humidity: INITIAL.humidity,
        pressure: INITIAL.pressure, lux: INITIAL.lux, prevLux: INITIAL.lux,
        battPct: INITIAL.battPct, waterOk: true,
        pumpOn: false, pumpOnSince: null,
        lightOn: false, lightPwm: DEFAULT_CONFIG.light_auto_pwm,
        lowBatt: false, battFull: false, battCut: false,
        message: '', messageUntil: null,
        config: { ...DEFAULT_CONFIG },
      };
    }

    // Pin 2S: 0% = 6.4V, 100% = 8.4V.
    const voltage = s => round(6.4 + 2.0 * s.battPct / 100, 2);

    // Cờ pin có trễ (hysteresis). batt_cut cắt bơm + đèn ở MỌI mode.
    function updateBatteryFlags(s) {
      const c = s.config, v = voltage(s);
      if (s.battPct < c.batt_warn_pct) s.lowBatt = true;
      else if (s.battPct >= c.batt_recover_pct) s.lowBatt = false;
      if (v >= c.batt_full_on_v) s.battFull = true;
      else if (v <= c.batt_full_off_v) s.battFull = false;
      if (v <= c.batt_crit_v) s.battCut = true;
      else if (s.battCut && v >= c.batt_crit_recover_v) s.battCut = false;
      if (s.battCut) { s.pumpOn = false; s.pumpOnSince = null; s.lightOn = false; }
    }

    // Đèn auto: CHỈ ở mode auto, và chỉ phản ứng lúc lux VƯỢT ngưỡng (edge) — lệnh tay được giữ
    // tới lần vượt ngưỡng kế tiếp.
    function autoLight(s) {
      const c = s.config;
      if (s.mode === 'auto' && !s.battCut) {
        if (s.prevLux >= c.lux_on && s.lux < c.lux_on) { s.lightOn = true; s.lightPwm = c.light_auto_pwm; }
        else if (s.prevLux <= c.lux_off && s.lux > c.lux_off) { s.lightOn = false; }
      }
      s.prevLux = s.lux;
    }

    // Áp lệnh từ xmini-sim/{id}/control. Thứ tự như firmware: pump, light_pwm, light, mode/auto, config, message.
    function applyControl(s, cmd, now) {
      const r = { changed: false, publishConfig: false };
      if (!cmd || typeof cmd !== 'object' || Array.isArray(cmd)) return r;

      if (typeof cmd.pump === 'boolean') {
        r.changed = true;
        if (!(cmd.pump && s.battCut)) { s.pumpOn = cmd.pump; s.pumpOnSince = cmd.pump ? now : null; }
      }
      if (typeof cmd.light_pwm === 'number' && Number.isFinite(cmd.light_pwm)) {
        r.changed = true;
        s.lightPwm = clamp(Math.round(cmd.light_pwm), 0, 255);
      }
      if (typeof cmd.light === 'boolean') {
        r.changed = true;
        if (!(cmd.light && s.battCut)) s.lightOn = cmd.light;
      }
      if (cmd.mode === 'auto' || cmd.mode === 'manual') { r.changed = true; s.mode = cmd.mode; }
      else if (typeof cmd.auto === 'boolean') { r.changed = true; s.mode = cmd.auto ? 'auto' : 'manual'; }

      if (cmd.config && typeof cmd.config === 'object' && !Array.isArray(cmd.config)) {
        r.publishConfig = true; // {} = yêu cầu gửi lại; {...} = gộp + clamp rồi echo
        for (const [k, v] of Object.entries(cmd.config)) {
          const range = CONFIG_RANGES[k];
          if (!range || typeof v !== 'number' || !Number.isFinite(v)) continue;
          const val = k.endsWith('_v') ? round(v, 2) : Math.round(v);
          s.config[k] = clamp(val, range[0], range[1]);
        }
      }
      if (typeof cmd.message === 'string') {
        r.changed = true;
        s.message = cmd.message;
        const secs = typeof cmd.message_secs === 'number' ? Math.max(0, cmd.message_secs) : 0;
        s.messageUntil = cmd.message && secs > 0 ? now + secs * 1000 : null;
      }
      return r;
    }

    // Một bước mô phỏng dtSec giây. Tốc độ tính theo mỗi 10s: đất +8 khi tưới (còn nước), -1 khi không.
    function step(s, dtSec, now, rand) {
      rand = rand || Math.random;
      const k = dtSec / 10;
      const watering = s.pumpOn && s.waterOk;
      s.soil = clamp(s.soil + (watering ? 8 : -1) * k, 0, 100);
      s.battPct = clamp(s.battPct - (0.1 + (s.pumpOn ? 0.5 : 0)) * k, 0, 100);
      s.tempC = clamp(s.tempC + (rand() - 0.5) * 0.4, 24, 31);
      s.humidity = clamp(s.humidity + (rand() - 0.5) * 1.0 + (watering ? 0.5 : 0), 40, 90);
      s.pressure = clamp(s.pressure + (rand() - 0.5) * 0.4, 1000, 1016);
      if (s.pumpOn && s.pumpOnSince != null && now - s.pumpOnSince >= s.config.pump_max_run_s * 1000) {
        s.pumpOn = false; s.pumpOnSince = null; // tự tắt an toàn (giống firmware)
      }
      if (s.messageUntil != null && now >= s.messageUntil) { s.message = ''; s.messageUntil = null; }
      autoLight(s);
      updateBatteryFlags(s);
    }

    // Kịch bản ép giá trị. 'reset' về trạng thái ban đầu nhưng GIỮ mode (mode là của server).
    function applyScenario(s, name) {
      switch (name) {
        case 'dry-soil': s.soil = 15; break;
        case 'water-empty': s.waterOk = false; break;
        case 'water-refill': s.waterOk = true; break;
        case 'low-battery': s.battPct = 15; break;
        case 'battery-cut': s.battPct = 0; break;
        case 'battery-full': s.battPct = 100; break;
        case 'night': s.lux = 5; break;
        case 'day': s.lux = 600; break;
        case 'reset': Object.assign(s, createState(s.deviceId, s.mode)); break;
        default: return false;
      }
      autoLight(s);
      updateBatteryFlags(s);
      return true;
    }

    // Telemetry 21 trường snake_case — y hệt xmini/sensor_data.
    function toTelemetry(s) {
      const v = voltage(s);
      const currentMa = 120 + (s.pumpOn ? 800 : 0) + (s.lightOn ? s.lightPwm * 1.2 : 0);
      return {
        device_id: s.deviceId,
        temperature_c: round(s.tempC, 1),
        humidity_percent: round(s.humidity, 1),
        pressure_hpa: round(s.pressure, 1),
        altitude_m: round(44330 * (1 - Math.pow(s.pressure / 1013.25, 0.1903)), 1),
        temperature_bmp_c: round(s.tempC + 0.3, 1),
        light_lux: round(s.lux, 1),
        soil_percent: Math.round(s.soil),
        soil_dry_flag: Math.round(s.soil) < s.config.soil_on_pct,
        battery_voltage_v: v,
        battery_current_ma: round(currentMa, 1),
        battery_power_mw: round(v * currentMa, 1),
        battery_percent: Math.round(s.battPct),
        light_on: s.lightOn,
        light_pwm: s.lightPwm,
        pump_on: s.pumpOn,
        mode: s.mode,
        low_batt: s.lowBatt,
        batt_full: s.battFull,
        batt_cut: s.battCut,
        water_ok: s.waterOk,
      };
    }

    const configPayload = s => ({ config: { ...s.config } });

    window.SimCore = {
      DEFAULT_CONFIG, SCENARIOS, createState, applyControl, step, applyScenario, toTelemetry, configPayload, voltage,
    };
  })();
</script>
</body>
</html>
```

- [ ] **Step 4: Chạy test, xác nhận pass**

Run: `node --test tests-js/sim-core.test.mjs`
Expected: `ℹ pass 16`, `ℹ fail 0`.

- [ ] **Step 5: Commit**

```bash
git add demo-dashboard.html tests-js/sim-core.test.mjs
git commit -m "feat: loi mo phong thiet bi ao (JS thuan) trong demo-dashboard + test node"
```

---

### Task 4: Tab "📟 Thiết bị ảo": Start/Stop, MQTT WebSocket, log message

**Files:**
- Modify: `demo-dashboard.html`. Các vùng sửa: CSS header dòng 16–17; CSS trước `</style>`; nav nhóm MQTT; section mới trước `</main>`; expose `window.plantTester` trong IIFE Live Tester; script mới sau block `sim-core`.

**Interfaces:**
- Consumes:
  - `window.SimCore` (Task 3).
  - `window.plantTester = { cfg, authHeaders }` (tạo ở bước 2 của task này). `cfg()` trả `{ base, device, token }`; `authHeaders()` trả header có `Authorization: Bearer …`.
  - API có sẵn: `POST /api/devices/register` `{deviceId, name}` (201 / 409), `GET /api/devices/{id}` (200 trả object có `controlMode` / 404).
- Produces (dùng ở Task 5, cùng IIFE): biến `st`, `linkDown`; hàm `$`, `publish(kind)`, `connectMqtt()`, `disconnectMqtt()`, `serverCall(method, path, body)`, `showError(msg)`, `setBadge(text, color)`, `render()`.

- [ ] **Step 1: Sửa CSS header bị dán nhầm**

Trong `demo-dashboard.html`, thay 2 dòng:
```
    background: linear-gradient(13Crash — nod-api không có key dataprocessor (chỉ có SharedTopic) → vẫn phải thêm 1 key vào nod-api
5deg, #065f46 0%, #047857 100%);
```
bằng:
```
    background: linear-gradient(135deg, #065f46 0%, #047857 100%);
```

- [ ] **Step 2: Expose `window.plantTester`**

Trong IIFE Live Tester, thay:
```js
    function authHeaders() {
      const h = { 'Content-Type': 'application/json' };
      if (authToken) h['Authorization'] = 'Bearer ' + authToken;
      return h;
    }
```
bằng:
```js
    function authHeaders() {
      const h = { 'Content-Type': 'application/json' };
      if (authToken) h['Authorization'] = 'Bearer ' + authToken;
      return h;
    }
    // Cho tab "📟 Thiết bị ảo" dùng chung host + token của tab Kết nối.
    window.plantTester = { cfg, authHeaders };
```

- [ ] **Step 3: Thêm nav + CSS**

Thay:
```html
    <div class="group-label">MQTT</div>
    <button class="nav-btn" data-target="mqtt-connect">Kết nối MQTT (IoT team)</button>
```
bằng:
```html
    <div class="group-label">MQTT</div>
    <button class="nav-btn" data-target="mqtt-connect">Kết nối MQTT (IoT team)</button>

    <div class="group-label">Giả lập</div>
    <button class="nav-btn" data-target="sim-device">📟 Thiết bị ảo</button>
```

Thay:
```css
  .status-line .ms { color: #94a3b8; font-family: monospace; }
</style>
```
bằng:
```css
  .status-line .ms { color: #94a3b8; font-family: monospace; }

  /* ===== Thiết bị ảo ===== */
  .sim-mqtt-cfg { margin: 8px 0 4px; color: #94a3b8; font-size: 13px; }
  .sim-mqtt-cfg summary { cursor: pointer; }
  .sim-layout { display: grid; grid-template-columns: minmax(0, 1.1fr) minmax(0, 1fr); gap: 14px; margin-top: 14px; }
  @media (max-width: 900px) { .sim-layout { grid-template-columns: 1fr; } }
  .sim-panel { background: #1e293b; border: 1px solid #334155; border-radius: 8px; padding: 12px; min-width: 0; }
  .sim-chips { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 10px; }
  .sim-chip { background: #0f172a; border: 1px solid #334155; border-radius: 999px; padding: 2px 10px; font-size: 12px; color: #cbd5e1; white-space: nowrap; }
  .sim-chip.on { border-color: #fbbf24; color: #fde68a; }
  .sim-chip.pump { border-color: #3b82f6; color: #93c5fd; }
  .sim-chip.warn { border-color: #ef4444; color: #fca5a5; }
  .sim-log-panel { background: #020617; }
  .sim-log-head { font-size: 12px; color: #94a3b8; margin-bottom: 6px; display: flex; gap: 8px; }
  .sim-log-legend { margin-left: auto; }
  .sim-log { font-family: "Menlo", "Consolas", monospace; font-size: 11.5px; line-height: 1.45; max-height: 420px; overflow-y: auto; }
  .sim-log details { border-bottom: 1px solid #0f172a; padding: 2px 0; }
  .sim-log summary { cursor: pointer; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; list-style: none; }
  .sim-log summary::-webkit-details-marker { display: none; }
  .sim-log pre { white-space: pre-wrap; word-break: break-all; color: #cbd5e1; margin: 4px 0 4px 14px; }
  .sim-log .up summary { color: #4ade80; }
  .sim-log .down summary { color: #93c5fd; }
  .sim-log .cfg summary { color: #fcd34d; }
  .sim-log .err summary { color: #fca5a5; }
</style>
```

- [ ] **Step 4: Thêm section tab**

Thay:
```html
  </main>
</div>
```
bằng:
```html
<section id="sim-device">
  <h2>📟 Thiết bị ảo (ESP32 giả lập qua MQTT)</h2>
  <p>Trang này <strong>chính là một thiết bị</strong>: nối HiveMQ qua WebSocket, publish telemetry lên <code>xmini-sim/{id}/sensor_data</code> mỗi 10s và nhận lệnh trên <code>xmini-sim/{id}/control</code>. ESP32 thật (nghe <code>xmini/control</code>) không bao giờ nhận lệnh của thiết bị ảo.</p>
  <div class="alert info">
    Cần: đăng nhập ở tab <b>Kết nối</b> (host + token của server), credential HiveMQ <b>riêng cho giả lập</b> (vd <code>planttree-sim</code>) và internet. Khi demo hãy <b>giữ tab này hiển thị</b> — tab ẩn lâu bị trình duyệt giãn nhịp gửi.
  </div>

  <div class="conn-bar">
    <div class="field"><label for="sim-id">Device ID</label><input id="sim-id" style="width:170px" value="SIM_Zone1"></div>
    <div class="field"><label for="sim-name">Tên</label><input id="sim-name" style="width:190px" value="Thiết bị ảo Zone 1"></div>
    <button class="run-btn" id="sim-start">▶ Start</button>
    <button class="run-btn" id="sim-stop" style="background:#334155;color:#e2e8f0" disabled>■ Stop</button>
    <div class="conn-status">Trạng thái: <b id="sim-badge">chưa chạy</b> · Mode: <b id="sim-mode">—</b></div>
  </div>

  <details class="sim-mqtt-cfg">
    <summary>⚙️ MQTT (HiveMQ WebSocket) — lưu trong trình duyệt, không ghi vào file</summary>
    <div class="conn-bar">
      <div class="field"><label for="sim-mqtt-host">Host</label><input id="sim-mqtt-host" style="width:360px"></div>
      <div class="field"><label for="sim-mqtt-user">User</label><input id="sim-mqtt-user" style="width:150px" placeholder="planttree-sim"></div>
      <div class="field"><label for="sim-mqtt-pass">Password</label><input id="sim-mqtt-pass" type="password" style="width:150px"></div>
    </div>
  </details>

  <div id="sim-error" class="alert danger" style="display:none"></div>

  <div class="sim-layout">
    <div class="sim-panel" id="sim-left">
      <div id="sim-chips" class="sim-chips"></div>
    </div>
    <div class="sim-panel sim-log-panel">
      <div class="sim-log-head">MQTT · <span id="sim-log-filter">xmini-sim/…/#</span><span class="sim-log-legend">↑ gửi · ↓ nhận · bấm dòng để xem đủ</span></div>
      <div id="sim-log" class="sim-log"></div>
    </div>
  </div>
</section>

  </main>
</div>
```

- [ ] **Step 5: Thêm mqtt.js + IIFE điều khiển tab**

Thay đoạn cuối file (ngay sau block `sim-core`):
```html
      DEFAULT_CONFIG, SCENARIOS, createState, applyControl, step, applyScenario, toTelemetry, configPayload, voltage,
    };
  })();
</script>
</body>
```
bằng:
```html
      DEFAULT_CONFIG, SCENARIOS, createState, applyControl, step, applyScenario, toTelemetry, configPayload, voltage,
    };
  })();
</script>

<script src="https://cdn.jsdelivr.net/npm/mqtt@5/dist/mqtt.min.js"></script>
<script>
  // ===== Tab "📟 Thiết bị ảo": nối MQTT (WebSocket) + vòng tick + log (lõi mô phỏng ở <script id="sim-core">) =====
  (function () {
    const $ = id => document.getElementById(id);
    if (!$('sim-start') || !window.SimCore) return;
    const Sim = window.SimCore;
    const ID_RE = /^SIM_[A-Za-z0-9_-]{1,40}$/;
    const LS_MQTT = 'planttree-sim-mqtt';
    const DEFAULT_HOST = 'ba4fbc53bce842ffb0fcd51178d78414.s1.eu.hivemq.cloud';
    const TICK_MS = 10000, LOG_MAX = 50;

    let st = null, client = null, tickTimer = null, linkDown = false;
    const logEntries = [];

    // --- Cấu hình MQTT: chỉ lưu localStorage, KHÔNG ghi vào file ---
    function loadMqttCfg() { try { return JSON.parse(localStorage.getItem(LS_MQTT)) || {}; } catch { return {}; } }
    function saveMqttCfg() {
      try {
        localStorage.setItem(LS_MQTT, JSON.stringify({
          host: $('sim-mqtt-host').value.trim(), user: $('sim-mqtt-user').value.trim(), pass: $('sim-mqtt-pass').value,
        }));
      } catch {}
    }
    const savedMqtt = loadMqttCfg();
    $('sim-mqtt-host').value = savedMqtt.host || DEFAULT_HOST;
    $('sim-mqtt-user').value = savedMqtt.user || '';
    $('sim-mqtt-pass').value = savedMqtt.pass || '';
    ['sim-mqtt-host', 'sim-mqtt-user', 'sim-mqtt-pass'].forEach(id => $(id).addEventListener('input', saveMqttCfg));

    const topics = id => ({
      sensor: `xmini-sim/${id}/sensor_data`, config: `xmini-sim/${id}/config`, control: `xmini-sim/${id}/control`,
    });

    function setBadge(text, color) { $('sim-badge').textContent = text; $('sim-badge').style.color = color; }
    function showError(msg) { const e = $('sim-error'); e.textContent = msg || ''; e.style.display = msg ? 'block' : 'none'; }

    // --- Log MQTT: dựng DOM bằng textContent (payload từ broker KHÔNG bao giờ được chèn như HTML).
    // Dòng trùng liên tiếp (vd lỗi kết nối mỗi 5s) được gộp thành ×N để log không bị lụt.
    function addLog(kind, topic, payload) {
      const last = logEntries[0];
      if (last && last.kind === kind && last.topic === topic && last.payload === payload) {
        last.at = new Date(); last.count += 1;
      } else {
        logEntries.unshift({ kind, topic, payload, at: new Date(), count: 1 });
        if (logEntries.length > LOG_MAX) logEntries.length = LOG_MAX;
      }
      renderLog();
    }
    function renderLog() {
      $('sim-log').replaceChildren(...logEntries.map(e => {
        const d = document.createElement('details');
        d.className = e.kind;
        const s = document.createElement('summary');
        const arrow = { up: '↑', cfg: '↑', down: '↓', err: '✗' }[e.kind];
        const short = e.topic.replace(/^xmini-sim\/[^/]+\//, '');
        s.textContent = `${arrow} ${e.at.toLocaleTimeString()} ${short}${e.count > 1 ? ' ×' + e.count : ''}  ${e.payload}`;
        const p = document.createElement('pre');
        let body = e.payload;
        try { body = JSON.stringify(JSON.parse(e.payload), null, 2); } catch {}
        p.textContent = `${e.topic}\n${body}`;
        d.append(s, p);
        return d;
      }));
    }

    // --- Gọi server bằng host + token của tab Kết nối ---
    async function serverCall(method, path, body) {
      const t = window.plantTester;
      const opts = { method, headers: t.authHeaders() };
      if (body !== undefined) opts.body = JSON.stringify(body);
      const res = await fetch(t.cfg().base + path, opts);
      const text = await res.text();
      let data = text; try { data = JSON.parse(text); } catch {}
      return { res, data, text };
    }
    async function ensureRegistered(id, name) {
      let r = await serverCall('POST', '/api/devices/register', { deviceId: id, name });
      if (r.res.status === 401) throw new Error('Token hết hạn hoặc chưa đăng nhập — đăng nhập lại ở tab Kết nối.');
      if (r.res.status !== 201 && r.res.status !== 409) throw new Error(`Đăng ký thiết bị lỗi ${r.res.status}: ${r.text.slice(0, 200)}`);
      r = await serverCall('GET', `/api/devices/${encodeURIComponent(id)}`);
      if (r.res.status === 404) throw new Error(`ID ${id} đã thuộc user khác — hãy đổi ID.`);
      if (!r.res.ok) throw new Error(`Đọc thiết bị lỗi ${r.res.status}: ${r.text.slice(0, 200)}`);
      return r.data;
    }

    // --- MQTT ---
    function publish(kind) {
      if (!st || linkDown) return; // đang giả "mất kết nối": không gửi gì
      const tp = topics(st.deviceId);
      const topic = kind === 'config' ? tp.config : tp.sensor;
      const payload = JSON.stringify(kind === 'config' ? Sim.configPayload(st) : Sim.toTelemetry(st));
      if (!client || !client.connected) { addLog('err', topic, 'Chưa nối MQTT — bỏ qua bản tin này'); return; }
      client.publish(topic, payload, { qos: 0, retain: false }, err =>
        addLog(err ? 'err' : (kind === 'config' ? 'cfg' : 'up'), topic, err ? `${err.message} · ${payload}` : payload));
    }

    function connectMqtt() {
      const host = $('sim-mqtt-host').value.trim();
      const tp = topics(st.deviceId);
      const c = mqtt.connect(`wss://${host}:8884/mqtt`, {
        username: $('sim-mqtt-user').value.trim(),
        password: $('sim-mqtt-pass').value,
        clientId: `planttree-sim-${st.deviceId}-${Math.random().toString(16).slice(2, 8)}`,
        clean: true, reconnectPeriod: 5000, connectTimeout: 10000,
      });
      client = c;
      c.on('connect', () => {
        setBadge('● online (MQTT)', '#4ade80');
        c.subscribe(tp.control, { qos: 0 }, err => { if (err) addLog('err', tp.control, 'Subscribe lỗi: ' + err.message); });
        publish('config'); // như firmware: gửi cấu hình mỗi lần (re)connect
        publish('sensor');
      });
      c.on('reconnect', () => { if (client === c) setBadge('… đang nối lại MQTT', '#fbbf24'); });
      c.on('close', () => { if (client === c) setBadge('○ mất MQTT (tự thử lại mỗi 5s)', '#fca5a5'); });
      c.on('error', err => addLog('err', tp.control, 'MQTT: ' + err.message));
      c.on('message', (topic, buf) => onControl(topic, buf.toString()));
    }
    function disconnectMqtt() { const c = client; client = null; if (c) c.end(true); }

    function onControl(topic, text) {
      addLog('down', topic, text);
      if (!st || linkDown) return; // đã Stop / đang giả mất kết nối: bỏ qua lệnh
      let cmd;
      try { cmd = JSON.parse(text); } catch { addLog('err', topic, 'JSON lỗi — bỏ qua lệnh'); return; }
      const r = Sim.applyControl(st, cmd, Date.now());
      if (r.publishConfig) publish('config');
      if (r.changed) publish('sensor'); // KHÁC firmware: gửi ngay để demo phản hồi tức thì
      render();
    }

    function tick() {
      Sim.step(st, TICK_MS / 1000, Date.now());
      publish('sensor');
      render();
    }

    function render() {
      if (!st) return;
      $('sim-mode').textContent = st.mode.toUpperCase();
      const t = Sim.toTelemetry(st);
      const chip = (txt, cls) => {
        const c = document.createElement('span');
        c.className = 'sim-chip' + (cls ? ' ' + cls : '');
        c.textContent = txt;
        return c;
      };
      $('sim-chips').replaceChildren(
        chip(`🌱 đất ${t.soil_percent}%`, t.soil_dry_flag ? 'warn' : ''),
        chip(t.pump_on ? '🚿 bơm BẬT' : '🚿 bơm tắt', t.pump_on ? 'pump' : ''),
        chip(t.light_on ? `💡 đèn ${t.light_pwm}` : '💡 đèn tắt', t.light_on ? 'on' : ''),
        chip(t.water_ok ? '🪣 bồn OK' : '🚱 hết nước', t.water_ok ? '' : 'warn'),
        chip(`🔋 ${t.battery_percent}% · ${t.battery_voltage_v}V`, t.batt_cut || t.low_batt ? 'warn' : ''),
        chip(`☀️ ${t.light_lux} lux`),
        chip(`🌡 ${t.temperature_c}°C`),
        chip(`💧 ${t.humidity_percent}%`),
        chip(`⏲ ${t.pressure_hpa} hPa`),
      );
    }

    async function start() {
      showError('');
      const id = $('sim-id').value.trim();
      const name = $('sim-name').value.trim() || id;
      if (!ID_RE.test(id)) return showError('Device ID phải bắt đầu bằng SIM_ và chỉ gồm chữ, số, _ hoặc - (vd SIM_Zone1).');
      if (!window.plantTester || !window.plantTester.cfg().token) return showError('Đăng nhập ở tab "Kết nối" trước (cần host + token của server).');
      if (!$('sim-mqtt-host').value.trim() || !$('sim-mqtt-user').value.trim() || !$('sim-mqtt-pass').value) {
        $('sim-mqtt-host').closest('details').open = true;
        return showError('Nhập MQTT host / user / password của credential giả lập.');
      }
      if (!window.mqtt) return showError('Không tải được thư viện mqtt.js (cần internet để lấy từ CDN).');

      $('sim-start').disabled = true;
      setBadge('… đăng ký thiết bị trên server', '#93c5fd');
      try {
        const device = await ensureRegistered(id, name);
        st = Sim.createState(id, device && device.controlMode); // mode khớp mode server đang giữ
      } catch (e) {
        $('sim-start').disabled = false;
        setBadge('chưa chạy', '#94a3b8');
        const hint = e instanceof TypeError ? ' — không gọi được server, kiểm tra Host/Port ở tab Kết nối.' : '';
        return showError(e.message + hint);
      }
      linkDown = false;
      logEntries.length = 0;
      renderLog();
      $('sim-log-filter').textContent = `xmini-sim/${id}/#`;
      setBadge('… đang nối MQTT', '#93c5fd');
      connectMqtt();
      tickTimer = setInterval(tick, TICK_MS);
      $('sim-stop').disabled = false;
      $('sim-id').disabled = true;
      render();
    }

    function stop() {
      clearInterval(tickTimer);
      tickTimer = null;
      disconnectMqtt();
      linkDown = false;
      st = null; // lệnh đến muộn sau Stop bị bỏ qua (onControl kiểm tra st)
      setBadge('■ đã dừng', '#94a3b8');
      $('sim-start').disabled = false;
      $('sim-stop').disabled = true;
      $('sim-id').disabled = false;
    }

    $('sim-start').addEventListener('click', start);
    $('sim-stop').addEventListener('click', stop);
  })();
</script>
</body>
```

- [ ] **Step 6: Kiểm tra tự động + kiểm tra tay**

Run: `node --test tests-js/sim-core.test.mjs`
Expected: `ℹ fail 0` (block `sim-core` không đổi).

Kiểm tra tay. Cần MongoDB local và HiveMQ. Nếu chưa có credential `planttree-sim`, có thể tạm dùng credential server **chỉ nhập trên trang**, không ghi vào file.
1. `docker run -d --name planttree-mongo -p 27017:27017 mongo:7.0` (bỏ qua nếu Mongo đã chạy), rồi `dotnet run --project PlantTreeIoTServer --launch-profile http`. Chờ log `Subscribed to xmini/sensor_data, xmini/config, xmini-sim/+/sensor_data, xmini-sim/+/config`.
2. Mở `demo-dashboard.html` trong Chrome. Header phải có lại nền gradient xanh. Ở tab **Kết nối**: Host `localhost`, Port `8000`, đăng ký hoặc đăng nhập.
3. Tab **📟 Thiết bị ảo**: mở ⚙️ MQTT, nhập user/pass, bấm Start. Expected: badge `● online (MQTT)`; log có `↑ config` rồi `↑ sensor_data`, sau đó ~10 giây lại có một `↑ sensor_data`; log server có `Telemetry from SIM_Zone1` và `Stored device config for SIM_Zone1 from xmini-sim/SIM_Zone1/config`.
4. Lấy token rồi gửi lệnh tưới:
   ```bash
   TOKEN=$(curl -s -X POST http://localhost:8000/api/auth/login -H 'Content-Type: application/json' -d '{"email":"<email>","password":"<pw>"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
   curl -s -X POST http://localhost:8000/api/control/SIM_Zone1/water -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"on":true}'
   ```
   Expected: log có `↓ control {"pump":true,"mode":"auto"}`, ngay sau đó `↑ sensor_data` với `"pump_on":true`; chip `🚿 bơm BẬT`; mode vẫn `AUTO`. Log server có `Published control to xmini-sim/SIM_Zone1/control`.
5. Gửi message chứa HTML:
   ```bash
   curl -s -X POST http://localhost:8000/api/control/SIM_Zone1 -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"message":"<img src=x onerror=alert(1)>","message_secs":30}'
   ```
   Expected: dòng log hiện nguyên văn `<img src=x onerror=alert(1)>`, **không** có hộp alert.
6. Các tình huống lỗi:
   - Sai password MQTT → badge `○ mất MQTT…`, log có `✗ … MQTT: …` gộp thành `×N`, không lụt.
   - ID `SIM_a/b` hoặc `ESP32_x` → báo lỗi ngay, không gọi server.
   - Chưa có token: DevTools Console chạy `localStorage.removeItem('planttree-tester-cfg'); location.reload()`, rồi Start → báo "Đăng nhập ở tab "Kết nối" trước".
   - Token hỏng hoặc hết hạn: Console chạy `const c=JSON.parse(localStorage.getItem('planttree-tester-cfg')); c.token='x'; localStorage.setItem('planttree-tester-cfg', JSON.stringify(c)); location.reload()`, rồi Start → báo "Token hết hạn hoặc chưa đăng nhập…"; nút Start bật lại.
   - Đăng nhập bằng user B, Start với ID mà user A đã tạo → báo "đã thuộc user khác".
7. Bấm Stop → badge `■ đã dừng`, log ngừng tăng.

- [ ] **Step 7: Commit**

```bash
git add demo-dashboard.html
git commit -m "feat: tab Thiet bi ao tren demo-dashboard (MQTT WebSocket, Start/Stop, log) + sua CSS header"
```

---

### Task 5: Cảnh SVG, nút kịch bản, nút "Điều khiển như App"

**Files:**
- Modify: `demo-dashboard.html` (CSS thiết bị ảo, `#sim-left`, sau `.sim-layout`, IIFE tab)

**Interfaces:**
- Consumes (từ Task 4, cùng IIFE): `st`, `linkDown`, `$`, `publish`, `connectMqtt`, `disconnectMqtt`, `serverCall`, `showError`, `setBadge`, `render`; `Sim.applyScenario` (Task 3). API có sẵn: `POST /api/control/{id}/water` `{on}`, `/light` `{on}`, `/auto` (không body), `POST /api/control/{id}` `{"mode":"manual"}`.
- Produces: `renderScene(t)` và các handler kịch bản / app. Không task nào sau dùng.

- [ ] **Step 1: CSS cho cảnh và nút**

Thay:
```css
  .sim-log .err summary { color: #fca5a5; }
</style>
```
bằng:
```css
  .sim-log .err summary { color: #fca5a5; }
  .sim-scene { background: #0f172a; border-radius: 8px; overflow: hidden; }
  .sim-scene svg { display: block; width: 100%; height: auto; }
  .sim-btnrow { display: flex; flex-wrap: wrap; gap: 6px; align-items: center; margin-bottom: 8px; }
  .sim-btn { background: #0f172a; color: #e2e8f0; border: 1px solid #334155; border-radius: 6px; padding: 6px 10px; font-size: 13px; cursor: pointer; }
  .sim-btn:hover { border-color: #10b981; }
  .sim-app-status { font-family: monospace; font-size: 12px; margin-left: 6px; }
  @keyframes simDrop { from { transform: translateY(-4px); opacity: 1; } to { transform: translateY(10px); opacity: 0.15; } }
  .sv-drop { animation: simDrop 0.8s linear infinite; }
  .sv-drop:nth-child(2) { animation-delay: 0.2s; }
  .sv-drop:nth-child(3) { animation-delay: 0.4s; }
  .sv-drop:nth-child(4) { animation-delay: 0.6s; }
  @media (prefers-reduced-motion: reduce) { .sv-drop { animation: none; } }
</style>
```

- [ ] **Step 2: SVG cảnh + 2 hàng nút**

Thay:
```html
    <div class="sim-panel" id="sim-left">
      <div id="sim-chips" class="sim-chips"></div>
    </div>
```
bằng:
```html
    <div class="sim-panel" id="sim-left">
      <div class="sim-scene">
        <svg viewBox="0 0 300 200" role="img" aria-label="Cảnh thiết bị ảo: chậu cây, đèn, bơm, bồn nước, pin">
          <rect id="sv-sky" x="0" y="0" width="300" height="200" rx="8" fill="#e0f2fe"/>
          <g id="sv-sun"><circle cx="268" cy="26" r="13" fill="#facc15"/></g>
          <g id="sv-moon" style="display:none"><circle cx="268" cy="26" r="12" fill="#e2e8f0"/><circle cx="274" cy="22" r="10" fill="#1e293b"/></g>
          <text id="sv-lux" x="232" y="54" font-size="9" fill="#475569">— lux</text>
          <line x1="150" y1="0" x2="150" y2="22" stroke="#475569" stroke-width="2"/>
          <path d="M132 22 h36 l-6 12 h-24z" fill="#334155"/>
          <circle id="sv-bulb" cx="150" cy="38" r="8" fill="#64748b"/>
          <polygon id="sv-glow" points="128,40 172,40 205,124 95,124" fill="#fde047" opacity="0"/>
          <text id="sv-pwm" x="176" y="34" font-size="9" fill="#a16207"></text>
          <path d="M150 132 C150 110 150 98 150 82" stroke="#15803d" stroke-width="3" fill="none"/>
          <g id="sv-leaves" fill="#4ade80">
            <ellipse cx="136" cy="98" rx="14" ry="6" transform="rotate(-25 136 98)"/>
            <ellipse cx="164" cy="92" rx="14" ry="6" transform="rotate(25 164 92)"/>
            <ellipse cx="140" cy="114" rx="12" ry="5" transform="rotate(-15 140 114)"/>
            <ellipse cx="161" cy="110" rx="12" ry="5" transform="rotate(15 161 110)"/>
          </g>
          <path d="M118 132 h64 l-8 44 h-48z" fill="#c2410c"/>
          <rect id="sv-soil" x="114" y="127" width="72" height="9" rx="2" fill="#78350f"/>
          <text id="sv-soil-txt" x="150" y="160" font-size="11" text-anchor="middle" fill="#fff">—</text>
          <rect x="20" y="106" width="42" height="70" rx="5" fill="#e2e8f0" stroke="#94a3b8"/>
          <rect id="sv-water" x="22" y="124" width="38" height="50" rx="4" fill="#60a5fa"/>
          <text id="sv-tank-txt" x="41" y="100" font-size="9" text-anchor="middle" fill="#1d4ed8">bồn OK</text>
          <rect id="sv-pump" x="70" y="154" width="24" height="16" rx="3" fill="#475569"/>
          <text id="sv-pump-txt" x="82" y="165" font-size="8" text-anchor="middle" fill="#fff">OFF</text>
          <path d="M62 162 h8 M94 162 h8 v-64 h22" stroke="#3b82f6" stroke-width="3" fill="none"/>
          <g id="sv-drops" fill="#3b82f6" style="display:none">
            <circle class="sv-drop" cx="126" cy="106" r="2"/><circle class="sv-drop" cx="131" cy="114" r="2"/>
            <circle class="sv-drop" cx="124" cy="119" r="2"/><circle class="sv-drop" cx="134" cy="123" r="2"/>
          </g>
          <rect x="226" y="146" width="50" height="22" rx="4" fill="#fff" stroke="#64748b"/>
          <rect x="276" y="153" width="4" height="8" fill="#64748b"/>
          <rect id="sv-batt" x="229" y="149" width="34" height="16" rx="2" fill="#22c55e"/>
          <text id="sv-batt-txt" x="251" y="182" font-size="9" text-anchor="middle" fill="#334155">—</text>
          <rect x="8" y="8" width="118" height="22" rx="3" fill="#020617" stroke="#334155"/>
          <text id="sv-tft" x="14" y="23" font-size="9" fill="#6ee7b7" font-family="monospace">Xmini SIM</text>
        </svg>
      </div>
      <div id="sim-chips" class="sim-chips"></div>
    </div>
```

Thay (cuối section `sim-device`):
```html
      <div id="sim-log" class="sim-log"></div>
    </div>
  </div>
</section>
```
bằng:
```html
      <div id="sim-log" class="sim-log"></div>
    </div>
  </div>

  <h3>Kịch bản <span style="font-size:12px;color:#94a3b8">(ép giá trị rồi gửi telemetry ngay)</span></h3>
  <div class="sim-btnrow" id="sim-scenarios">
    <button class="sim-btn" data-scenario="dry-soil">🏜 Đất khô</button>
    <button class="sim-btn" data-scenario="water-empty">🚱 Hết nước</button>
    <button class="sim-btn" data-scenario="water-refill">💧 Đổ nước</button>
    <button class="sim-btn" data-scenario="low-battery">🪫 Pin yếu</button>
    <button class="sim-btn" data-scenario="battery-cut">🔌 Pin cạn</button>
    <button class="sim-btn" data-scenario="battery-full">🔋 Sạc đầy</button>
    <button class="sim-btn" data-scenario="night">🌙 Ban đêm</button>
    <button class="sim-btn" data-scenario="day">☀️ Ban ngày</button>
    <button class="sim-btn" data-scenario="link-down">📴 Mất kết nối</button>
    <button class="sim-btn" data-scenario="link-up">📶 Kết nối lại</button>
    <button class="sim-btn" data-scenario="reset">↺ Reset</button>
  </div>

  <h3>Điều khiển như App <span style="font-size:12px;color:#94a3b8">(gọi /api/control trên server → MQTT → thiết bị ảo)</span></h3>
  <div class="sim-btnrow" id="sim-app">
    <button class="sim-btn" data-app="water-on">🚿 Tưới</button>
    <button class="sim-btn" data-app="water-off">⏹ Tắt bơm</button>
    <button class="sim-btn" data-app="light-on">💡 Bật đèn</button>
    <button class="sim-btn" data-app="light-off">🌑 Tắt đèn</button>
    <button class="sim-btn" data-app="auto-on">🤖 Bật auto</button>
    <button class="sim-btn" data-app="auto-off">✋ Tắt auto</button>
    <span id="sim-app-status" class="sim-app-status"></span>
  </div>

  <div class="alert info">
    Muốn <b>AI chat / MCP</b> điều khiển thiết bị ảo: chia sẻ nó cho service-account MCP — <code>POST /api/devices/SIM_Zone1/share</code> với body <code>{"email":"mcp@plant-tree.local"}</code>.
  </div>
</section>
```

- [ ] **Step 3: Vẽ cảnh trong `render()`**

Trong IIFE tab, thay:
```js
        chip(`⏲ ${t.pressure_hpa} hPa`),
      );
    }
```
bằng:
```js
        chip(`⏲ ${t.pressure_hpa} hPa`),
      );
      renderScene(t);
    }

    // --- Cảnh SVG (chỉ setAttribute/textContent — message từ broker không bao giờ thành HTML) ---
    const lerp = (a, b, k) => Math.round(a + (b - a) * k);
    function soilColor(pct) { // khô (vàng nâu) -> ướt (nâu đậm)
      const k = Math.min(1, Math.max(0, pct / 100));
      return `rgb(${lerp(180, 69, k)},${lerp(130, 36, k)},${lerp(70, 10, k)})`;
    }
    function renderScene(t) {
      const night = t.light_lux < st.config.lux_on;
      $('sv-sky').setAttribute('fill', night ? '#1e293b' : '#e0f2fe');
      $('sv-sun').style.display = night ? 'none' : '';
      $('sv-moon').style.display = night ? '' : 'none';
      $('sv-lux').textContent = `${t.light_lux} lux`;
      $('sv-lux').setAttribute('fill', night ? '#cbd5e1' : '#475569');
      $('sv-bulb').setAttribute('fill', t.light_on ? '#fde047' : '#64748b');
      $('sv-glow').setAttribute('opacity', t.light_on ? (0.12 + 0.4 * t.light_pwm / 255).toFixed(2) : '0');
      $('sv-pwm').textContent = t.light_on ? `PWM ${t.light_pwm}` : '';
      $('sv-soil').setAttribute('fill', soilColor(t.soil_percent));
      $('sv-soil-txt').textContent = `đất ${t.soil_percent}%`;
      $('sv-leaves').setAttribute('fill', t.soil_dry_flag ? '#a3a33a' : '#4ade80'); // lá héo khi đất khô
      $('sv-water').setAttribute('height', t.water_ok ? '50' : '4');
      $('sv-water').setAttribute('y', t.water_ok ? '124' : '170');
      $('sv-tank-txt').textContent = t.water_ok ? 'bồn OK' : 'HẾT NƯỚC';
      $('sv-tank-txt').setAttribute('fill', t.water_ok ? '#1d4ed8' : '#dc2626');
      $('sv-pump').setAttribute('fill', t.pump_on ? '#2563eb' : '#475569');
      $('sv-pump-txt').textContent = t.pump_on ? 'ON' : 'OFF';
      $('sv-drops').style.display = t.pump_on && t.water_ok ? '' : 'none';
      $('sv-batt').setAttribute('width', String(Math.max(1, Math.round(34 * t.battery_percent / 100))));
      $('sv-batt').setAttribute('fill', t.batt_cut ? '#dc2626' : t.low_batt ? '#f59e0b' : '#22c55e');
      $('sv-batt-txt').textContent = t.batt_cut ? `CẠN ${t.battery_voltage_v}V` : `pin ${t.battery_percent}%`;
      $('sv-tft').textContent = st.message ? st.message.slice(0, 22) : (linkDown ? '-- offline --' : 'Xmini SIM');
    }

    // --- Nút kịch bản ---
    function onScenario(name) {
      if (!st) return showError('Bấm Start trước.');
      if (name === 'link-down') {
        if (linkDown) return;
        linkDown = true;
        disconnectMqtt();
        setBadge('📴 mất kết nối (giả lập) — server báo offline sau ~240s', '#fca5a5');
      } else if (name === 'link-up') {
        if (!linkDown) return;
        linkDown = false;
        setBadge('… đang nối MQTT', '#93c5fd');
        connectMqtt(); // khi connect sẽ tự gửi config + telemetry
      } else if (Sim.applyScenario(st, name)) {
        publish('sensor');
      }
      render();
    }
    $('sim-scenarios').addEventListener('click', e => {
      const b = e.target.closest('[data-scenario]');
      if (b) onScenario(b.dataset.scenario);
    });

    // --- Nút "Điều khiển như App": gọi API server thật (lệnh đi server -> MQTT -> thiết bị ảo) ---
    const APP_ACTIONS = {
      'water-on':  [id => `/api/control/${id}/water`, { on: true }],
      'water-off': [id => `/api/control/${id}/water`, { on: false }],
      'light-on':  [id => `/api/control/${id}/light`, { on: true }],
      'light-off': [id => `/api/control/${id}/light`, { on: false }],
      'auto-on':   [id => `/api/control/${id}/auto`, undefined],
      'auto-off':  [id => `/api/control/${id}`, { mode: 'manual' }],
    };
    async function onApp(action) {
      if (!st) return showError('Bấm Start trước.');
      const [path, body] = APP_ACTIONS[action];
      const out = $('sim-app-status');
      out.textContent = '… đang gọi server';
      out.style.color = '#93c5fd';
      try {
        const r = await serverCall('POST', path(encodeURIComponent(st.deviceId)), body);
        out.textContent = r.res.ok ? `✓ ${r.res.status} — chờ lệnh ↓ trên MQTT` : `✗ ${r.res.status} ${r.text.slice(0, 120)}`;
        out.style.color = r.res.ok ? '#4ade80' : '#fca5a5';
      } catch (e) {
        out.textContent = '✗ ' + e.message;
        out.style.color = '#fca5a5';
      }
    }
    $('sim-app').addEventListener('click', e => {
      const b = e.target.closest('[data-app]');
      if (b) onApp(b.dataset.app);
    });
```

- [ ] **Step 4: Kiểm tra tự động**

Run: `node --test tests-js/sim-core.test.mjs`
Expected: `ℹ fail 0`.

- [ ] **Step 5: Kiểm tra tay** (server local như Task 4 bước 6, MQTTx subscribe `xmini-sim/#` và `xmini/#`)

1. Start `SIM_Zone1`. Expected: cảnh có trời sáng, mặt trời, đất 45%, bồn OK, pin 80%, TFT `Xmini SIM`.
2. 🏜 Đất khô (mode AUTO). Expected:
   - Log có `↑ sensor_data` với `soil_percent:15`, rồi `↓ control {"pump":true,"mode":"auto"}` do server tự tưới.
   - Trên cảnh: bơm ON, giọt nước rơi, đất tăng 8% mỗi tick.
   - Sau 20 giây bơm tự tắt.
3. ✋ Tắt auto rồi 🚿 Tưới. Expected: lệnh `{"pump":true,"mode":"manual"}`, badge mode `MANUAL`, bơm bật. 🤖 Bật auto → mode `AUTO`.
4. 🌙 Ban đêm. Expected: trời tối, trăng, đèn sáng `PWM 180`. 🌑 Tắt đèn → đèn tắt và vẫn tắt qua 2 tick. ☀️ rồi 🌙 → đèn bật lại.
5. Gửi message HTML như Task 4 bước 6 mục 5. Expected: TFT hiện nguyên văn `<img src=x onerror=` (cắt 22 ký tự), không có alert.
6. 🚱 Hết nước → bồn `HẾT NƯỚC`, không có giọt nước dù bơm bật. 💧 Đổ nước. 🪫 Pin yếu → pin màu cam. 🔌 Pin cạn → pin đỏ `CẠN 6.4V`, bơm và đèn tắt; bấm 🚿 Tưới thì bơm vẫn tắt. 🔋 Sạc đầy → bình thường.
7. 📴 Mất kết nối. Expected:
   - Badge đỏ, log ngừng `↑`, TFT `-- offline --`.
   - Bấm 🚿 Tưới (server trả 200) nhưng thiết bị **không** đổi trạng thái.
   - Sau ~240 giây, log server có `device.offline` (nếu Notify bật) hoặc dòng quét offline.
   - 📶 Kết nối lại → `↑ config`, `↑ sensor_data`, server log `(online)`.
8. Toàn bộ quá trình: MQTTx **không** thấy message nào trên `xmini/control`.
9. Thu hẹp cửa sổ < 900px: hai cột xếp chồng, không có thanh cuộn ngang.

- [ ] **Step 6: Commit**

```bash
git add demo-dashboard.html
git commit -m "feat: thiet bi ao co canh SVG, nut kich ban va nut dieu khien nhu App"
```

---

### Task 6: Tài liệu + kiểm tra toàn bộ

**Files:**
- Modify: `API-GUIDE.md` (sau bảng "## MQTT Topics")
- Modify: `DEMO-NOTES.md` (thêm mục cuối)

**Interfaces:**
- Consumes: toàn bộ Task 1–5.
- Produces: không có.

- [ ] **Step 1: API-GUIDE.md**

Thay:
```markdown
> Không còn topic `planttree/{deviceId}/sensors` hay `planttree/{deviceId}/commands`.
```
bằng:
```markdown
> Không còn topic `planttree/{deviceId}/sensors` hay `planttree/{deviceId}/commands`.

### Thiết bị ảo (namespace `xmini-sim/`)

Thiết bị ảo (tab **📟 Thiết bị ảo** trên `demo-dashboard.html`) dùng topic **riêng theo từng thiết bị**, payload y hệt 3 topic trên. Id bắt buộc tiền tố `SIM_` (vd `SIM_Zone1`).

| Topic | Direction | Payload |
|-------|-----------|---------|
| `xmini-sim/{deviceId}/sensor_data` | Thiết bị ảo → Server | Như `xmini/sensor_data` |
| `xmini-sim/{deviceId}/config` | Thiết bị ảo → Server | Như `xmini/config` |
| `xmini-sim/{deviceId}/control` | Server → Thiết bị ảo | Như `xmini/control` |

- Lệnh cho id `SIM_*` đi `xmini-sim/{id}/control` (`SimTopics.ControlTopicFor`), **không bao giờ** lên `xmini/control` — ESP32 thật không nhận.
- Server chỉ nhận telemetry/config ảo khi `device_id` khớp topic và thiết bị **đã đăng ký trên chính server đó**; telemetry ảo đi cùng pipeline với thiết bị thật (lưu, Notify, tự tưới).
- Trang giả lập dùng credential HiveMQ riêng (vd `planttree-sim`), nhập trên trang và chỉ lưu localStorage.
```

- [ ] **Step 2: DEMO-NOTES.md**

Thêm vào **cuối file**:
```markdown

---

## 10. Demo bằng thiết bị ảo (không cần ESP32)

Dùng khi không có phần cứng hoặc Mac tắt. Thiết bị ảo chạy trong trình duyệt và đi qua HiveMQ thật (xem bằng MQTTx được).

1. Chạy server: Mac (`https://api.windy-dev.site`, cần image có tính năng này) **hoặc** local `dotnet run --project PlantTreeIoTServer --launch-profile http` (+ MongoDB local).
2. Mở `demo-dashboard.html` → tab **Kết nối**: host/port của server, đăng nhập.
3. Tab **📟 Thiết bị ảo** → ⚙️ MQTT: nhập credential `planttree-sim` (1 lần) → **Start** (`SIM_Zone1`).
4. (Tuỳ chọn) MQTTx subscribe `xmini-sim/#` để người xem thấy message thật.
5. Kịch bản gợi ý: 🏜 Đất khô → server tự tưới (↓ `{"pump":true,"mode":"auto"}`) → giọt nước, đất tăng, bơm tự tắt sau 20s · 🌙 Ban đêm → đèn auto · 🚱 Hết nước / 🪫 Pin yếu → Notify · hàng nút "Điều khiển như App".
6. AI chat / MCP: chia sẻ thiết bị cho service-account MCP — `POST /api/devices/SIM_Zone1/share` body `{"email":"mcp@plant-tree.local"}`.

> Giữ tab hiển thị khi demo (tab ẩn bị trình duyệt giãn nhịp gửi). Thiết bị ảo không bao giờ gửi lên `xmini/control` nên ESP32 thật an toàn.
```

- [ ] **Step 3: Chạy toàn bộ test**

Run: `dotnet test PlantTreeIoTServer.Tests/PlantTreeIoTServer.Tests.csproj`
Expected: PASS toàn bộ.

Run: `node --test tests-js/sim-core.test.mjs`
Expected: `ℹ fail 0`.

- [ ] **Step 4: Checklist test tay theo spec §6**

Chạy lại 10 bước "Test tay" trong spec, đặc biệt:
- Bước 9: không có message nào trên `xmini/control`.
- Bước 10: mở `demo-dashboard.html` trên **một máy khác** (không phải máy chạy server), host trỏ về server, rồi lặp lại bước 3–4.

Ghi kết quả (pass/fail từng bước) vào mô tả PR.

- [ ] **Step 5: Commit**

```bash
git add API-GUIDE.md DEMO-NOTES.md
git commit -m "docs: huong dan topic xmini-sim va demo bang thiet bi ao"
```
