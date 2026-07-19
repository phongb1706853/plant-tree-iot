# Plant Tree IoT — API & MQTT Guide

## System Architecture

```
IoT Device (xmini) — TỰ chạy AUTO theo ngưỡng lưu trong NVS
  │
  ├─ Telemetry ~10s ─────────► MQTT: xmini/sensor_data ─► Server (lưu MongoDB, cho app/MCP đọc)
  │
  ├─ Ngưỡng auto hiện tại ────► MQTT: xmini/config ──────► Server (đọc ngưỡng)
  │
  └─ Lệnh thủ công / đổi ngưỡng ◄── MQTT: xmini/control ◄─ Server (publish object JSON phẳng)

Thiết bị TỰ quyết định tưới / chiếu sáng dựa trên ngưỡng NVS.
Backend KHÔNG chạy rule tự tưới (WATER_ON/LIGHT_ON đã bị gỡ bỏ).
Muốn đổi hành vi auto → ghi NGƯỠNG qua {"config":{...}}; hoặc gửi lệnh thủ công pump/light/mode/message.
```

---

## 🔐 Authentication

HTTP API yêu cầu xác thực: **JWT** cho người dùng, **Device Token** cho ESP32. Kênh MQTT xác thực riêng bằng credential broker HiveMQ (không đổi).

### Người dùng (JWT)

```
POST /api/auth/register   { "email", "password", "displayName" }   -> { token }
POST /api/auth/login       { "email", "password" }                 -> { token }
POST /api/auth/dev-token   (không body)                            -> { token }   # CHỈ Development
```

Gắn `Authorization: Bearer <token>` cho: devices, sensordata (đọc), control (gửi lệnh thủ công / đọc-đặt ngưỡng), assistant. Mỗi user chỉ thấy device mình sở hữu.

> **`POST /api/auth/dev-token`** — lấy nhanh JWT để debug (curl/Swagger) mà không cần đăng ký. Tự seed user `dev@plant-tree.local`. **Chỉ hoạt động khi `ASPNETCORE_ENVIRONMENT=Development`**; Production trả `404`. OpenAPI (`/openapi/v1.json`) đã khai báo bearer scheme để công cụ hiện nút "Authorize".

### Thiết bị ESP32 (Device Token)

User đăng ký device (JWT) → nhận `deviceSecret` **1 lần**. ESP32 gửi header:

```
X-Device-Id: <deviceId>
X-Device-Secret: <deviceSecret>
```

cho: `POST /api/sensordata/upload`, `POST /api/devices/{id}/heartbeat`.

> Thiết bị nhận lệnh điều khiển qua **MQTT** (subscribe `xmini/control`), không poll qua HTTP. Endpoint `GET /api/control/commands/{deviceId}` chỉ là **nhật ký lệnh (audit log)** cho app/MCP dùng Bearer token.

---

## MQTT Configuration

**Broker:** HiveCloud (HiveMQ Cloud)
- **Host:** `ba4fbc53bce842ffb0fcd51178d78414.s1.eu.hivemq.cloud`
- **Port:** `8883` (TLS)

> Backend và firmware có thể dùng **login khác nhau trên CÙNG broker** này. Dùng đúng credential được cấp cho từng phía — không tự bịa user/pass.

---

## MQTT Topics

Chỉ **3 topic**, đều dùng chung tiền tố `xmini/`. **QoS 0**, **không retained**.

| Topic | Direction | Source | Description |
|-------|-----------|--------|-------------|
| `xmini/sensor_data` | Device → Server | IoT Device | Telemetry ~10s (21 trường phẳng snake_case) |
| `xmini/config` | Device → Server | IoT Device | 15 ngưỡng auto hiện tại (khi kết nối + sau mỗi lần đổi) |
| `xmini/control` | Server → Device | Server | Lệnh điều khiển — **1 object JSON phẳng** (pump / light / light_pwm / mode / config / message) |

> Không còn topic `planttree/{deviceId}/sensors` hay `planttree/{deviceId}/commands`.

---

## Payload Formats

### 1. Sensor Data (Device → Server)

**Topic:** `xmini/sensor_data`

**Payload (JSON) — 21 trường phẳng snake_case:**
```json
{
  "device_id": "ESP32S3_Zone1",
  "temperature_c": 28,
  "humidity_percent": 65,
  "pressure_hpa": 1014.28,
  "altitude_m": 38.2,
  "light_lux": 57.5,
  "soil_percent": 45,
  "battery_voltage_v": 3.9,
  "battery_current_ma": 120,
  "battery_power_mw": 468,
  "battery_percent": 82,
  "temperature_bmp_c": 28.1,
  "soil_dry_flag": false,
  "light_on": false,
  "light_pwm": 0,
  "pump_on": false,
  "mode": "auto",
  "low_batt": false,
  "batt_full": false,
  "batt_cut": false,
  "water_ok": true
}
```

- `soil_percent` là **int 0–100** (KHÔNG null; tên là `soil_percent`, KHÔNG phải `soil_moisture_percent`).
- Trường cảm biến lỗi = `null`; riêng `battery_percent` = `-1` khi lỗi.
- **KHÔNG có** trường `soil_moisture_raw` / `relay_on`.

**Also supports HTTP POST:**
```
POST /api/sensordata/upload
Content-Type: application/json

{
  "device_id": "ESP32S3_Zone1",
  "temperature_c": 28,
  "humidity_percent": 65,
  "light_lux": 57.5,
  "soil_percent": 45,
  "pump_on": false,
  "mode": "auto"
}
```

**Response:**
```json
{
  "message": "Data uploaded successfully",
  "timestamp": "2026-06-21T10:30:00Z"
}
```

> Không còn `triggeredCommands` — server không tự tính lệnh khi nhận telemetry.

---

### 2. Config (Device → Server)

**Topic:** `xmini/config`

Thiết bị gửi 15 ngưỡng auto hiện tại (khi kết nối + sau mỗi lần đổi), **bọc trong khoá `config`:**
```json
{
  "config": {
    "soil_on_pct": 30,
    "soil_off_pct": 60,
    "pump_max_run_s": 20,
    "pump_cooldown_s": 1800,
    "lux_on": 25,
    "lux_off": 60,
    "light_auto_pwm": 180,
    "batt_warn_pct": 20,
    "batt_recover_pct": 30,
    "soil_dry": 3000,
    "soil_wet": 1200,
    "batt_full_on_v": 4.15,
    "batt_full_off_v": 4.05,
    "batt_crit_v": 3.3,
    "batt_crit_recover_v": 3.5
  }
}
```

> Đây là các ngưỡng thiết bị dùng để TỰ chạy auto. Backend đọc lại qua `GET /api/control/{deviceId}/config`.

---

### 3. Control Commands (Server → Device)

**Topic:** `xmini/control` (QoS 0, không retained)

Payload là **1 JSON object phẳng**, chỉ gồm các khoá được hỗ trợ (khoá lạ bị bỏ qua):

- **Chấp hành** (→ ép thiết bị sang MANUAL): `{"pump": true|false}` · `{"light": true|false}` · `{"light_pwm": 0-255}`
- **Chế độ:** `{"mode": "auto"|"manual"}` hoặc `{"auto": true|false}`
- **Ngưỡng auto** (lưu NVS, không đổi mode): `{"config": { ... }}` — gửi `{"config": {}}` để **yêu cầu thiết bị gửi lại** cấu hình
- **Màn hình TFT** (ASCII, `""` để xoá): `{"message": "..."}` + tuỳ chọn `{"message_secs": N}`

Có thể **gộp nhiều khoá** trong 1 object.

**Ví dụ:**
```json
{ "pump": true }
```
```json
{ "light_pwm": 180 }
```
```json
{ "mode": "auto" }
```
```json
{ "message": "Tuoi cho toi nhe", "message_secs": 15 }
```

> **TUYỆT ĐỐI KHÔNG** dùng `{"command":"WATER_ON",...}` / `WATER_OFF` / `LIGHT_ON` / `LIGHT_OFF` / `FAN_ON` / `FAN_OFF`, hay bọc `{"command":...,"parameters":...}` — thiết bị **không hiểu** (no-op). Không có quạt (FAN).

**Via HTTP API:**
```
POST /api/control/{deviceId}
Content-Type: application/json
Authorization: Bearer <token>

{ "pump": true }
```

Body là object điều khiển phẳng. Khoá cho phép: `pump`, `light`, `light_pwm`, `mode`, `auto`, `message`, `message_secs`, `config`.

**Response:**
```json
{
  "message": "Command published",
  "deviceId": "ESP32S3_Zone1",
  "published": { "pump": true }
}
```

#### Endpoint chuyên dụng cho App (bọc gọn quanh các khoá phẳng)

```
POST /api/control/{deviceId}/water    { "on": true }                 -> {"pump": true}
POST /api/control/{deviceId}/light    { "on": true } | { "pwm": 180 } -> {"light": true} | {"light_pwm": 180}
POST /api/control/{deviceId}/auto     (không body)                    -> {"mode": "auto"}
```

- **Ưu tiên lệnh user hơn auto:** lệnh `/water` hoặc `/light` (với `on`) khiến **firmware tự chuyển MANUAL** và giữ nguyên (bỏ qua vòng auto) cho tới khi gọi **`/auto`** để trả quyền lại. Backend không lưu thêm state; đọc `mode` hiện tại (`auto`/`manual`) trong `GET /api/sensordata/latest/{deviceId}`.
- `/light` bắt buộc có `on` **hoặc** `pwm` (thiếu cả hai → `400`). `pwm` ưu tiên nếu có; `pwm` chỉ đổi độ sáng, không đổi mode (bám firmware).
- Yêu cầu Bearer + quyền trên device (không sở hữu → `404`). MQTT chưa kết nối → `503`.

---

### 4. Ngưỡng auto thiết bị (Device Config API)

Thiết bị TỰ chạy auto theo 15 ngưỡng lưu trong NVS. Backend **không còn** moisture/light rule; thay vào đó đọc/ghi các ngưỡng này.

**Đọc ngưỡng hiện tại** (BE nghe được từ `xmini/config`):
```
GET /api/control/{deviceId}/config
```
```json
{ "config": { "soil_on_pct": 30, "soil_off_pct": 60, "pump_cooldown_s": 1800, "...": "..." } }
```
> Nếu BE chưa nhận được cấu hình: `{ "config": null, "note": "..." }`.

**Đổi ngưỡng** (publish `{"config":{...}}` xuống `xmini/control`):
```
PUT /api/control/{deviceId}/config
Content-Type: application/json

{
  "soil_on_pct": 30,
  "soil_off_pct": 60,
  "pump_max_run_s": 20,
  "pump_cooldown_s": 1800,
  "lux_on": 25,
  "lux_off": 60,
  "light_auto_pwm": 180
}
```
Body = các khoá ngưỡng snake_case muốn đổi (chỉ cần gửi khoá cần thay).

**Yêu cầu thiết bị gửi lại cấu hình** (publish `{"config":{}}`):
```
POST /api/control/{deviceId}/config/refresh
```

**15 khoá ngưỡng:** `soil_on_pct`, `soil_off_pct`, `pump_max_run_s`, `pump_cooldown_s`, `lux_on`, `lux_off`, `light_auto_pwm`, `batt_warn_pct`, `batt_recover_pct`, `soil_dry`, `soil_wet`, `batt_full_on_v`, `batt_full_off_v`, `batt_crit_v`, `batt_crit_recover_v`.

---

## 🤖 Trợ lý AI (Assistant proxy)

Cho phép App gửi câu lệnh **ngôn ngữ tự nhiên**. Luồng:

```
App ──(JWT)──► .NET /api/assistant/* ──► AI server (tree-grow-helper) ──► MCP ──► gọi ngược .NET API ──► MQTT ──► ESP32
```

`userId` LUÔN lấy từ JWT (App không tự khai). `sessionId` do App giữ để hỗ trợ multi-turn; thiếu thì mặc định `= userId`; response luôn kèm `sessionId`.

```
POST /api/assistant/chat      { "message", "sessionId"? }                 -> { reply, pendingAction, sessionId }
POST /api/assistant/confirm   { "sessionId", "actionId", "approved" }     -> { reply, pendingAction, sessionId }
```

- **Cơ chế 2 bước:** câu hỏi/đọc dữ liệu → trả `reply` ngay. Lệnh **điều khiển** → `pendingAction` khác `null` (CHƯA thực thi), App hiện Có/Không → gọi `/confirm` với `pendingAction.id`. (Có thể thay `/confirm` bằng cách gửi tiếp `message:"có"/"không"` cùng `sessionId`.)
- **Xác thực:** JWT chỉ áp cho lời gọi VÀO .NET. `.NET → AI server` là nội bộ (không JWT); MCP gọi ngược .NET dùng service-account như hiện tại.
- **Cấu hình AI server:** env `AI_SERVER_URL` (ưu tiên) hoặc `AiServer:BaseUrl` (appsettings), mặc định `http://localhost:8787`.
- **Lỗi:** AI server chưa cấu hình/không kết nối → `503`; AI server trả lỗi khác → `502`; chưa đăng nhập → `401`; `message` rỗng → `400`.

Ví dụ:
```bash
TOKEN=$(curl -s -X POST http://localhost:8000/api/auth/dev-token | jq -r .token)
curl -X POST http://localhost:8000/api/assistant/chat \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"message":"Tưới nước cho ESP32S3_Zone1 giúp mình"}'
# -> { "reply": "Bạn xác nhận... (Có/Không)", "pendingAction": { "id": "...", ... }, "sessionId": "..." }
```

---

## Luồng Auto (Device-Native)

### Khi telemetry đến server:

1. **Server nhận** telemetry (MQTT `xmini/sensor_data` hoặc HTTP `POST /api/sensordata/upload`)
2. **Lưu MongoDB**
3. **Trả về** `{ message, timestamp }` — **KHÔNG còn** `triggeredCommands`

### Ai quyết định tưới / chiếu sáng?

- **THIẾT BỊ tự quyết định**, dựa trên các ngưỡng NVS (`soil_on_pct`/`soil_off_pct`, `lux_on`/`lux_off`, ...).
- **Backend KHÔNG chạy rule tự tưới.** Muốn đổi hành vi auto → ghi ngưỡng qua `PUT /api/control/{deviceId}/config` (`{"config":{...}}`).
- Có thể can thiệp **thủ công** (ép MANUAL): `pump` / `light` / `light_pwm` / `mode` / `message` qua `POST /api/control/{deviceId}`.

---

## Cooldown (ngưỡng thiết bị)

**Cooldown** = khoảng thời gian tối thiểu giữa 2 lần bơm auto liên tiếp, do **THIẾT BỊ** áp dụng theo ngưỡng `pump_cooldown_s` (giây) lưu trong NVS.

**Ví dụ** (`pump_cooldown_s = 1800` → 30 phút):
- 10:00 → `soil_percent` < `soil_on_pct` → thiết bị bơm ✓
- 10:05 → vẫn khô → thiết bị **chờ** (cooldown chưa hết) ✗
- 10:30 → cooldown hết → thiết bị bơm lại ✓

**Vì sao?** Tránh bật/tắt bơm liên tục. Đổi giá trị qua `PUT /api/control/{deviceId}/config`.

---

## Device Commands

### Nhận lệnh qua MQTT (kênh chính)
Thiết bị subscribe `xmini/control` và nhận lệnh (object JSON phẳng) theo thời gian thực.

### Nhật ký lệnh đã gửi (audit log — cho app/MCP)
```
GET /api/control/commands/{deviceId}?limit=N
Authorization: Bearer <token>

Response:
[
  {
    "deviceId": "ESP32S3_Zone1",
    "payload": { "pump": true },
    "createdAt": "2026-06-21T10:30:00Z"
  }
]
```
> Đây chỉ là **nhật ký** các lệnh backend đã publish; KHÔNG phải hàng đợi để thiết bị poll.

---

## Testing with Interactive Dashboard

### Access Dashboard
```
file:///c:/Work/my-project/plant-tree-iot/interactive-api-dashboard.html
```

### Configure Connection
- **Host:** localhost
- **Port:** 8000
- **Device ID:** ESP32S3_Zone1 (in header)

### Test Flow
1. **Devices** → Register device
2. **Sensors** → Upload sensor data
3. **Control** → Đọc / đổi ngưỡng auto (config) của thiết bị
4. **Control** → Gửi lệnh thủ công (`pump` / `light` / `mode` / `message`) → publish MQTT
5. Kiểm tra thiết bị subscribe `xmini/control` để nhận lệnh

---

## Curl Examples

> Deploy Docker: http://localhost:8080 · chạy dev bằng dotnet run: http://localhost:8000

### Upload Sensor Data
```bash
curl -X POST http://localhost:8080/api/sensordata/upload \
  -H "Content-Type: application/json" \
  -d '{
    "device_id": "ESP32S3_Zone1",
    "temperature_c": 28,
    "humidity_percent": 65,
    "light_lux": 57.5,
    "soil_percent": 45,
    "pump_on": false,
    "mode": "auto"
  }'
```

### Đổi ngưỡng auto (config)
```bash
curl -X PUT http://localhost:8080/api/control/ESP32S3_Zone1/config \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <token>" \
  -d '{
    "soil_on_pct": 30,
    "soil_off_pct": 60,
    "pump_cooldown_s": 1800
  }'
```

### Gửi lệnh thủ công via API
```bash
curl -X POST http://localhost:8080/api/control/ESP32S3_Zone1 \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <token>" \
  -d '{ "pump": true }'
```

---

## Database Schema

### SensorData
```javascript
{
  _id: ObjectId,
  deviceId: "ESP32S3_Zone1",
  timestamp: ISODate,
  temperature: 28,
  humidity: 65,
  pressure: 1014.28,
  altitude: 38.2,
  temperatureBmp: 28.1,
  lightLevel: 57.5,
  soilPercent: 45,
  soilDryFlag: false,
  batteryVoltageV: 3.9,
  batteryCurrentMa: 120,
  batteryPowerMw: 468,
  batteryPercent: 82,
  lightOn: false,
  lightPwm: 0,
  pumpOn: false,
  mode: "auto",
  waterOk: true,
  lowBatt: false,
  battFull: false,
  battCut: false,
  location: null
}
```

### DeviceConfig (ngưỡng auto BE nghe từ `xmini/config`)
```javascript
{
  _id: ObjectId,
  deviceId: "ESP32S3_Zone1",
  config: {
    soil_on_pct: 30, soil_off_pct: 60,
    pump_max_run_s: 20, pump_cooldown_s: 1800,
    lux_on: 25, lux_off: 60, light_auto_pwm: 180,
    batt_warn_pct: 20, batt_recover_pct: 30,
    soil_dry: 3000, soil_wet: 1200,
    batt_full_on_v: 4.15, batt_full_off_v: 4.05,
    batt_crit_v: 3.3, batt_crit_recover_v: 3.5
  },
  updatedAt: ISODate
}
```

### ControlCommands (nhật ký lệnh đã publish)
```javascript
{
  _id: ObjectId,
  deviceId: "ESP32S3_Zone1",
  payload: { pump: true },
  createdAt: ISODate
}
```

---

## Troubleshooting

### Thiết bị không tự tưới / chiếu sáng

1. **Kiểm tra thiết bị đang ở AUTO:**
   - Telemetry `mode` = `"auto"` (nếu `"manual"` thì auto tạm dừng)
   - Đặt lại: `POST /api/control/{deviceId}` với body `{"mode":"auto"}`

2. **Kiểm tra ngưỡng auto:**
   - `GET /api/control/{deviceId}/config`
   - VD `soil_on_pct: 30`, `soil_percent` hiện tại `45` → chưa đủ khô để bơm (đúng)

3. **Kiểm tra cooldown:**
   - `pump_cooldown_s` chưa hết thì thiết bị chưa bơm lại

4. **Đổi hành vi auto:**
   - `PUT /api/control/{deviceId}/config` với ngưỡng mới

### Lệnh không tới thiết bị

1. **MQTT Connected?**
   - Check server logs for: `Connected to MQTT broker`

2. **Device Subscribed?**
   - Verify device subscribes to `xmini/control`

3. **Topic Name Correct?**
   - xmini devices: `xmini/control` (chỉ 3 topic `xmini/*`; không còn `planttree/*`)

4. **Payload đúng dạng phẳng?**
   - VD `{"pump":true}` — KHÔNG dùng `{"command":"WATER_ON"}`

---

## Next Steps

- Cấu hình thiết bị subscribe topic MQTT `xmini/control`
- Đặt ngưỡng auto phù hợp cây trồng qua `PUT /api/control/{deviceId}/config`
- Theo dõi telemetry và tinh chỉnh ngưỡng
- Test end-to-end với phần cứng thật
