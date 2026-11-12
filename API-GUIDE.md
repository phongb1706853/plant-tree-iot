# Plant Tree IoT — API & MQTT Guide

> Bám hợp đồng **firmware Xmini 2.1.0** (`backend-api.md`, cập nhật 2026-11-08): bản lắp chạy **adapter 9 V, luôn thức**,
> màn chỉ hiển thị (không cảm ứng).

## System Architecture

```
IoT Device (xmini, fw 2.1.0) — TỰ chạy AUTO (tưới theo liều + đèn tự động) theo cấu hình lưu trong NVS
  │
  ├─ Sức khoẻ + nguồn (online / sleeping / LWT offline) ─► MQTT: xmini/status (retained) ─► Server (DeviceStatus)
  ├─ Telemetry 10 s ─────────────────────────────────────► MQTT: xmini/sensor_data ─────► Server (SensorData)
  ├─ Cấu hình hiện tại (25 khoá, + rejected) ────────────► MQTT: xmini/config (retained) ► Server (DeviceConfig)
  │
  └─ Lệnh tay / đổi cấu hình / tin nhắn / QR ◄─ MQTT: xmini/control (QoS 1, KHÔNG retained, luôn có "id") ◄─ Server
```

- **Thiết bị thật làm chủ AUTO.** Server **không** gửi lệnh tưới/đèn tự động cho thiết bị thật. Mọi lệnh `pump` / `light` /
  `light_pwm` đưa thiết bị về **MANUAL** (tự động hoá đứng yên); chỉ `{"mode":"auto"}` / `{"auto":true}` (`POST .../auto`)
  trả lại AUTO. Reboot cũng về AUTO. Server đọc `mode` từ telemetry, không tự giả định.
- **`{"pump":true}` = đúng 1 liều** (`pump_dose_ms`, mặc định 2 s) rồi tự tắt. Không có bơm vô hạn.
- Thiết bị ảo `SIM_*` (tab **📟 Thiết bị ảo** của `demo-dashboard.html`) **giữ hợp đồng cũ**: server tự tưới / tự bật đèn và
  làm chủ mode, cấu hình 15 khoá cũ. Xem [Thiết bị ảo](#thiết-bị-ảo-namespace-xmini-sim).
- `WATER_ON` / `LIGHT_ON` / `FAN_*` kiểu cũ đã bị gỡ bỏ.

---

## 🔐 Authentication

HTTP API chỉ dùng **một** cách xác thực: **JWT** (người dùng / app / MCP). Thiết bị ESP32 **không gọi HTTP API** — nó nói chuyện hoàn toàn qua MQTT và xác thực bằng credential broker. Vì vậy **không có device token / device secret**.

### Người dùng (JWT)

```
POST /api/auth/register   { "email", "password", "displayName" }   -> { token }
POST /api/auth/login       { "email", "password" }                 -> { token }
POST /api/auth/dev-token   (không body)                            -> { token }   # CHỈ Development
```

Gắn `Authorization: Bearer <token>` cho: devices, sensordata, control, assistant.

> **`POST /api/auth/dev-token`** — lấy nhanh JWT để debug (curl/Swagger) mà không cần đăng ký. Tự seed user `dev@plant-tree.local`. **Chỉ hoạt động khi `ASPNETCORE_ENVIRONMENT=Development`**; Production trả `404`. OpenAPI (`/openapi/v1.json`) đã khai báo bearer scheme để công cụ hiện nút "Authorize".

### Sở hữu & chia sẻ device (owner + members)

Mỗi device có **1 owner** (người đăng ký) và danh sách **members** (được chia sẻ). Cả owner lẫn member đều xem dữ liệu + gửi lệnh được; chỉ **owner** mới share / thu hồi / xoá device.

```
POST   /api/devices/{deviceId}/share            { "email": "<user cần chia sẻ>" }   # owner-only
GET    /api/devices/{deviceId}/members                                              # owner + members
DELETE /api/devices/{deviceId}/share/{memberId}                                     # owner-only, thu hồi
```

- Truy cập device của người khác khi chưa được chia sẻ → `404` (ẩn sự tồn tại, không lộ `403`).
- `GET /api/devices` trả về **cả device mình sở hữu lẫn device được chia sẻ**.

---

## MQTT Configuration

| Mục | Giá trị |
|---|---|
| Broker | `<broker-host>` (một cluster HiveMQ Cloud) — **lấy từ người phụ trách thiết bị**, đặt qua env `MQTT_BROKER` |
| Port | `8883`, luôn TLS (`MQTT_PORT`, `MQTT_USE_TLS`) |
| Username / Password | `<mqtt-username>` / `<mqtt-password>` — env `MQTT_USERNAME` / `MQTT_PASSWORD`, không commit |
| Client-ID server | `planttree-server-<guid>` / `planttree-publisher-<guid>` — **không** dùng tiền tố `ESP32S3_Client_ID_` (sẽ đá thiết bị ra) |

> Tài liệu này cố ý **không** ghi host, user, mật khẩu. Backend và firmware có thể dùng **login khác nhau trên cùng broker**.

---

## MQTT Topics

| Topic | Chiều | QoS | Retained | Khi nào |
|---|---|---|---|---|
| `xmini/status` | Thiết bị → Server | 0 | **có** | `"online"` lúc nối + mỗi 60 s; `"sleeping"` trước khi ngủ (chỉ bản pin); `"offline"` = LWT do broker phát |
| `xmini/sensor_data` | Thiết bị → Server | 0 | không | Mỗi **10 s** (bản adapter luôn thức). Bản pin ở eco: 1 bản mỗi lần thức |
| `xmini/config` | Thiết bị → Server | 0 | **có** | Lần nối đầu sau boot, và sau **mỗi** lệnh có khoá `config` (kể cả `{}`) |
| `xmini/control` | Server → Thiết bị | **1** | **không** | Khi server ra lệnh. Luôn kèm `"id"` duy nhất |

- Server subscribe `xmini/status` **trước**, rồi `xmini/sensor_data`, `xmini/config` (+ 2 filter `xmini-sim/+/…`).
- Bản tin **retained** (cờ retain = 1, nhận lúc vừa subscribe) có tuổi không rõ: server **lưu** nhưng không dùng để báo
  offline, tính hạn chót, hay đọc `rejected`.
- `xmini/config` không có `device_id` → server gán cho thiết bị gần nhất vừa thấy trên `xmini/status` / `xmini/sensor_data`.
  Config tới trước cả hai thì được giữ tạm và áp khi biết id.
- **Một thiết bị** trên bộ topic `xmini/*`: mọi firmware dùng `device_id = "ESP32S3_Zone1"` cố định (hạn chế §9 của firmware).

### Thiết bị ảo (namespace `xmini-sim/`)

Thiết bị ảo (tab **📟 Thiết bị ảo** trên `demo-dashboard.html`) dùng topic **riêng theo từng thiết bị**. Id bắt buộc tiền tố `SIM_` (vd `SIM_Zone1`).

| Topic | Chiều | Payload |
|-------|-----------|---------|
| `xmini-sim/{deviceId}/sensor_data` | Thiết bị ảo → Server | Như `xmini/sensor_data` |
| `xmini-sim/{deviceId}/config` | Thiết bị ảo → Server | 15 khoá cũ (có `pump_max_run_s`, `pump_cooldown_s`) |
| `xmini-sim/{deviceId}/control` | Server → Thiết bị ảo | Như `xmini/control` (QoS 1, có `id`) |
| `xmini-sim/{deviceId}/notify` | Server → dashboard | Sự kiện Notify giả lập |

- Lệnh cho id `SIM_*` đi `xmini-sim/{id}/control` (`SimTopics.ControlTopicFor`), **không bao giờ** lên `xmini/control` — ESP32 thật không nhận.
- Server chỉ nhận telemetry/config ảo khi `device_id` khớp topic và thiết bị **đã đăng ký trên chính server đó**.
- **Giữ luật cũ:** server tự tưới (`AutoWateringDecider`, cooldown `pump_cooldown_s`) và tự bật đèn (`AutoLightDecider`), gộp
  `mode` hiện tại vào lệnh `pump` / `light` để không đổi chế độ. `PUT .../config` cho `SIM_*` nhận 15 khoá cũ, chỉ cần là số
  (thiết bị ảo tự kẹp).
- **Thông báo giả lập:** sự kiện Notify của `SIM_*` được publish lên `xmini-sim/{deviceId}/notify`; mặc định **không** gửi
  webhook Notify thật. Bật/tắt: `PUT /api/devices/{deviceId}/sim-notify` body `{"forwardToNotify": true}`.

---

## Payload Formats

### 1. `xmini/sensor_data` (Thiết bị → Server)

**30 trường phẳng snake_case.** Ví dụ bản adapter (mặc định):

```json
{
  "device_id": "ESP32S3_Zone1",
  "temperature_c": 28.61523, "humidity_percent": 71.2318, "pressure_hpa": 1008.42, "altitude_m": 41.27,
  "light_lux": 320.83, "soil_percent": 44,
  "power_source": "adapter", "supply_voltage_v": 9.12, "supply_current_ma": 168.4,
  "battery_voltage_v": null, "battery_current_ma": null, "battery_power_mw": null, "battery_percent": -1,
  "temperature_bmp_c": 29.14, "soil_dry_flag": false,
  "light_on": false, "light_pwm": 255, "pump_on": false, "mode": "auto",
  "low_batt": false, "batt_full": false, "batt_cut": false, "water_ok": null,
  "soil_ok": true, "soil_fault": false, "safe_mode": false,
  "doses_today": 2, "light_min_today": 0, "power_state": "awake"
}
```

| Trường mới (2.1.0) | Ý nghĩa |
|---|---|
| `power_source` | `"adapter"` \| `"battery"`. Adapter → mọi trường pin `null`, `battery_percent = -1`, cờ pin luôn `false` |
| `supply_voltage_v` / `supply_current_ma` | Áp / dòng nguồn vào (adapter ~9 V). `null` = INA219 lỗi hoặc đang chạy USB |
| `soil_ok` | `false` = que đất hỏng/rút dây → **không tin `soil_percent`**, chặn tự tưới |
| `soil_fault` | Tưới đủ đợt mà đất không ẩm lên → không mở đợt mới (gỡ bằng `/auto`) |
| `safe_mode` | ≥ 3 lần reset bất thường liên tiếp → chặn tự tưới |
| `doses_today` | Số liều bơm trong 24 h cuốn chiếu (tay + tự động). Tăng = lệnh bơm đã chạy |
| `light_min_today` | Số phút đèn bật trong ~24 h (về 0 sau reboot) |
| `power_state` | `"awake"` \| `"eco"`. Bản adapter luôn `"awake"` |

- `null` / `battery_percent = -1` = **không có số** (server lưu `-1` thành `null`). `water_ok` luôn `null` ở bản hiện tại
  (chưa lắp cảm biến nước).
- Số thực là float 32-bit, có thể có đuôi (`8.30000019`) — làm tròn khi hiển thị.
- **Không có timestamp**: server gắn giờ nhận.

**HTTP (đường phụ):** `POST /api/sensordata/upload` (Bearer) với cùng body snake_case → `{ "message", "timestamp" }`.

### 2. `xmini/status` (Thiết bị → Server, retained)

```json
{"state":"online","device_id":"ESP32S3_Zone1","fw":"2.1.0","power_source":"adapter","power_mode":"auto",
 "power_state":"awake","plugged_guess":true,"uptime_s":742,"boot_count":37,"reset_reason":"poweron",
 "free_heap":187432,"min_free_heap":151208,"max_alloc":110580,"rssi":-61,"wifi_fail_count":0,"wake_ms":0,"safe_mode":false}
```

- `"sleeping"` (chỉ bản pin) có thêm `next_wake_s`. `plugged_guess` chỉ có khi `power_mode = "auto"`.
- LWT do broker phát, đúng nguyên văn: `{"state":"offline","device_id":"ESP32S3_Zone1"}`.
- Server: LWT **live** → bắn `device.offline` ngay; `sleeping` live → không báo offline trước 3 × (`next_wake_s` + 60 s).
- Đọc qua `GET /api/devices/{deviceId}/status`.

### 3. `xmini/config` (Thiết bị → Server, retained)

```json
{
  "config": {
    "soil_on_pct": 30, "soil_off_pct": 60, "pump_dose_ms": 2000, "soak_s": 600,
    "max_doses_per_cycle": 4, "max_doses_per_day": 8,
    "lux_on": 50, "lux_off": 200, "light_auto": false, "light_auto_pwm": 255,
    "light_min_batt_pct": 50, "light_max_min_per_day": 240,
    "batt_warn_pct": 15, "batt_recover_pct": 20,
    "batt_full_on_v": 8.3, "batt_full_off_v": 8, "batt_crit_v": 6.8, "batt_crit_recover_v": 7.2,
    "soil_dry": 3200, "soil_wet": 1200,
    "power_mode": "auto", "plugged_on_v": 8.25, "plugged_off_v": 8.1,
    "wake_interval_s": 300, "screen_timeout_s": 0
  },
  "rejected": ["pump_dose_ms"]
}
```

- `rejected` (mảng tên khoá) hoặc `rejected_count` (số, khi mảng làm bản tin ≥ 1024 byte): chỉ có khi lệnh `config` vừa xử
  lý có khoá bị từ chối. Server chỉ lưu `rejected` từ bản tin **live**; bản live không có `rejected` thì xoá thông tin cũ.

### 4. `xmini/control` (Server → Thiết bị)

**QoS 1, retain = false, 1 JSON object phẳng ≤ 1024 byte.** Server luôn thêm `"id"` duy nhất (`c-<epoch_ms>-<seq>`, ≤ 39 ký
tự) — QoS 1 có thể giao lặp, firmware bỏ lệnh trùng `id`. `id` client gửi bị bỏ qua.

| Khoá | Kiểu | Tác dụng | Về MANUAL? |
|---|---|---|---|
| `pump` | bool | `true` = **1 liều** `pump_dose_ms`; `false` = dừng liều đang chạy | **Có** |
| `light` | bool | Bật/tắt đèn ở độ sáng hiện tại (độ sáng 0 thì bật ở 255) | **Có** |
| `light_pwm` | int 0–255 | Đặt độ sáng; > 0 bật, 0 tắt (server kẹp) | **Có** |
| `mode` | `"auto"` \| `"manual"` | Đổi chế độ (server chỉ nhận đúng 2 chuỗi này, chữ thường) | — |
| `auto` | bool | Như `mode`; có cả hai thì `auto` thắng | — |
| `config` | object | Khoá ở [bảng cấu hình](#5-cấu-hình-thiết-bị-25-khoá). `{}` = hỏi lại cấu hình | Không |
| `message` | string | Chữ trên màn TFT; `""` = xoá. Server **bỏ dấu tiếng Việt / emoji, cắt ≤ 90 ký tự** | Không |
| `message_secs` | int ≥ 0 | Tự ẩn tin sau N giây (0 = giữ) | Không |
| `show_qr` | bool \| int | Hiện QR trang cấu hình N giây (`true` = 300, `false`/0 = tắt, kẹp 0–3600) | Không |
| `stay_awake_s` | int 0–3600 | Thức + online N giây (chỉ cần với bản pin ở eco). 0 = huỷ | Không |

Thứ tự firmware xử lý: `id` → `light_pwm` → `light` → `pump` → (MANUAL) → `mode` → `auto` → `config` → `message` → `show_qr` → `stay_awake_s`.
Vd `{"pump":true,"mode":"auto"}` = tưới 1 liều rồi vẫn ở AUTO.

> **Không có ACK.** Xác nhận qua telemetry / config kế tiếp: `pump` → `doses_today` tăng; `light` → `light_on` / `light_pwm`;
> `mode` → `mode`; `config` → `xmini/config` live (+ `rejected`).

### 5. Cấu hình thiết bị (25 khoá)

Miền là khoảng đóng. **Giá trị sai bị TỪ CHỐI, không kẹp.** Server kiểm trước theo đúng bảng firmware và trả `400`.

| Nhóm | Khoá | Kiểu | Miền | Mặc định |
|---|---|---|---|---|
| Tưới | `soil_on_pct` | int | 0–100 | 30 |
| Tưới | `soil_off_pct` | int | 0–100 | 60 |
| Tưới | `pump_dose_ms` | int | 200–10000 | 2000 |
| Tưới | `soak_s` | int | 60–7200 | 600 |
| Tưới | `max_doses_per_cycle` | int | 1–20 | 4 |
| Tưới | `max_doses_per_day` | int | 0–48 (0 = tắt tự tưới) | 8 |
| Tưới | `soil_dry` | int | 0–4095 | 3200 |
| Tưới | `soil_wet` | int | 0–4095 | 1200 |
| Đèn | `light_auto` | bool | true/false (hoặc 0/1) | false |
| Đèn | `lux_on` | float | 0–100000 | 50 |
| Đèn | `lux_off` | float | 0–100000 | 200 |
| Đèn | `light_auto_pwm` | int | 1–255 | 255 |
| Đèn | `light_min_batt_pct` | int | 0–100 (chỉ bản pin) | 50 |
| Đèn | `light_max_min_per_day` | int | 0–1440 | 240 |
| Pin | `batt_warn_pct` | int | 0–100 | 15 |
| Pin | `batt_recover_pct` | int | 0–100 | 20 |
| Pin | `batt_full_on_v` | float | 4.0–9.0 | 8.3 |
| Pin | `batt_full_off_v` | float | 4.0–9.0 | 8.0 |
| Pin | `batt_crit_v` | float | 6.0–7.5 | 6.8 |
| Pin | `batt_crit_recover_v` | float | 6.2–8.0 | 7.2 |
| Nguồn | `power_mode` | enum | `eco` \| `always_on` \| `auto` (hoặc 0/1/2) | auto |
| Nguồn | `wake_interval_s` | int | 60–3600 (chỉ bản pin) | 300 |
| Nguồn | `plugged_on_v` | float | 7.0–8.6 (chỉ bản pin) | 8.25 |
| Nguồn | `plugged_off_v` | float | 7.0–8.6 (chỉ bản pin) | 8.1 |
| Màn | `screen_timeout_s` | int | 0–3600 (0 = luôn sáng) | 0 |

- int nhận số thực (làm tròn, kiểm miền **trước** khi làm tròn). `null` = bỏ qua.
- **Ràng buộc cặp** — muốn dời cả cặp thì gửi **cả hai khoá trong cùng một lệnh** (server kiểm khi đủ cặp; nếu chỉ gửi 1 khoá,
  thiết bị kiểm với giá trị đang lưu):
  `soil_on_pct + 5 ≤ soil_off_pct` · `lux_on < lux_off` · `batt_warn_pct < batt_recover_pct` · `batt_full_off_v < batt_full_on_v` ·
  `batt_crit_recover_v ≥ batt_crit_v + 0.2` (để dư, vd +0.21) · `|soil_dry − soil_wet| ≥ 300` · `plugged_off_v < plugged_on_v`.
- **Đã bỏ:** `pump_max_run_s`, `pump_cooldown_s` → server trả `400` cho thiết bị thật (dùng `pump_dose_ms`, `soak_s`,
  `max_doses_per_cycle`). Khoá lạ khác cũng `400`.

---

## REST API

Mọi endpoint cần `Authorization: Bearer <token>`. Device không thuộc user → `404`. Endpoint điều khiển: MQTT chưa nối → `503`.

### Devices

```
POST   /api/devices/register           { "deviceId", "name", "location"?, "plantType"? }   -> 201
GET    /api/devices                                                                         -> Device[]
GET    /api/devices/{deviceId}                                                              -> Device
GET    /api/devices/{deviceId}/status                                                       -> trạng thái kết nối (mới)
POST   /api/devices/{deviceId}/claim
POST   /api/devices/{deviceId}/heartbeat
DELETE /api/devices/{deviceId}                                                              # owner-only
PUT    /api/devices/{deviceId}/sim-notify  { "forwardToNotify": bool }                      # chỉ SIM_*
```

**`GET /api/devices/{deviceId}/status`** — trạng thái theo máy trạng thái §7.4 của firmware:

```json
{
  "deviceId": "ESP32S3_Zone1",
  "connection": "online",
  "lastHeardAt": "2026-11-12T04:40:10Z",
  "expectedBy": "2026-11-12T04:41:40Z",
  "lastTelemetryAt": "2026-11-12T04:40:10Z",
  "mode": "auto",
  "status": { "state": "online", "fw": "2.1.0", "powerSource": "adapter", "powerMode": "auto", "powerState": "awake",
              "pluggedGuess": true, "nextWakeS": null, "uptimeS": 742, "bootCount": 37, "resetReason": "poweron",
              "freeHeap": 187432, "minFreeHeap": 151208, "maxAlloc": 110580, "rssi": -61, "wifiFailCount": 0,
              "wakeMs": 0, "safeMode": false, "retained": false, "receivedAt": "2026-11-12T04:40:01Z" }
}
```

| `connection` | Khi nào |
|---|---|
| `online` | Nghe thấy telemetry / status live trong ≤ 90 s |
| `sleeping` | Bản pin báo `sleeping`, chưa quá giờ nhận + `next_wake_s` + 60 s |
| `late` | Im lặng > 90 s (thức) hoặc quá hạn chót ngủ |
| `offline` | LWT mới hơn telemetry cuối, hoặc im lặng > 240 s (`NOTIFY_OFFLINE_SECONDS`), hoặc ngủ quá 3 chu kỳ |
| `unknown` | Chưa nghe được gì live (vd server vừa khởi động, chỉ có bản retained) |

### Sensor data

```
POST /api/sensordata/upload                                   body = telemetry snake_case
GET  /api/sensordata/latest/{deviceId}                        -> SensorData mới nhất
GET  /api/sensordata/history/{deviceId}?limit=50              -> SensorData[] (mới nhất trước)
GET  /api/sensordata/range/{deviceId}?startDate=..&endDate=.. -> SensorData[] (cũ trước)
```

### Control

| Method | URL | Body | Gửi xuống thiết bị |
|---|---|---|---|
| POST | `/api/control/{deviceId}/water` | `{ "on": true }` | `{"id":…,"pump":true}` — 1 liều, → MANUAL |
| POST | `/api/control/{deviceId}/light` | `{ "on": true }` \| `{ "pwm": 180 }` | `{"id":…,"light":true}` \| `{"id":…,"light_pwm":180}` — → MANUAL |
| POST | `/api/control/{deviceId}/auto` | — | `{"id":…,"mode":"auto"}` |
| POST | `/api/control/{deviceId}/show-qr` | `{ "seconds": 300 }` (tuỳ chọn; 0 = tắt) | `{"id":…,"show_qr":300}` |
| POST | `/api/control/{deviceId}` | object khoá phẳng (bảng §4) | như body + `id` |
| GET | `/api/control/{deviceId}/config` | — | — (đọc cấu hình đã lưu) |
| PUT | `/api/control/{deviceId}/config` | object khoá cấu hình | `{"id":…,"config":{…}}` |
| POST | `/api/control/{deviceId}/config/refresh` | — | `{"id":…,"config":{}}` |
| GET | `/api/control/commands/{deviceId}?limit=50` | — | — (nhật ký lệnh, mới nhất trước) |

**Response của mọi lệnh:**

```json
{
  "message": "Đã gửi lệnh xuống xmini/control",
  "deviceId": "ESP32S3_Zone1",
  "commandId": "c-1762925000123-7",
  "expectedMode": "manual",
  "published": { "id": "c-1762925000123-7", "pump": true }
}
```

- `expectedMode` = mode thiết bị **sẽ** ở sau lệnh (server lưu tạm; telemetry kế tiếp xác nhận / sửa lại).
- `400` (chuỗi) cho lỗi kiểu khoá lệnh (`pump` không phải bool, `mode` khác `"auto"`/`"manual"`, `message` chỉ toàn emoji,
  lệnh > 1024 byte…). `400` (object) cho cấu hình sai:

```json
{ "error": "Cấu hình không hợp lệ — lệnh KHÔNG được gửi xuống thiết bị.",
  "errors": ["'pump_dose_ms' = 50 nằm ngoài miền 200–10000.", "Cần soil_on_pct + 5 ≤ soil_off_pct (đang gửi 25 và 28)."] }
```

**`GET /api/control/{deviceId}/config`** → `DeviceConfig` (camelCase): `soilOnPct`, `soilOffPct`, `pumpDoseMs`, `soakS`, …,
`powerMode`, `screenTimeoutS`, `rejected`, `rejectedCount`, `rejectedAt`, `updatedAt`. Chưa nghe được `xmini/config` →
`{ "deviceId", "config": null, "note": "..." }`.

---

## 🤖 Trợ lý AI (Assistant proxy)

Cho phép App gửi câu lệnh **ngôn ngữ tự nhiên**. Luồng:

```
App ──(JWT)──► .NET /api/assistant/* ──► AI server (tree-grow-helper) ──► MCP ──► gọi ngược .NET API ──► MQTT ──► ESP32
```

Endpoint **tương thích OpenAI** và **STATELESS**: App giữ toàn bộ `messages[]` và gửi lại mỗi lượt. `userId` LUÔN lấy từ JWT (App không tự khai) — .NET ép vào trường `user`, đồng thời ép `stream=false` và mặc định `model="plant-assistant"` nếu App không gửi.

```
POST /api/assistant/v1/chat/completions   { "model"?, "messages": [ { "role", "content" }, ... ] }
    -> { "choices": [ { "message": { "role", "content", "tool_calls"? } } ], "usage", ... }
```

- **Xác nhận điều khiển (turnkey):** khi cần xác nhận, `choices[].message.content` là câu hỏi "(Có/Không)" và kèm `tool_calls` mã hoá hành động. Lượt sau, App **giữ nguyên** assistant message đó (kèm `tool_calls`) trong `messages[]` rồi thêm câu trả lời của user (`"có"`/`"không"`) — AI server sẽ thực thi hoặc huỷ. Nếu **không giữ** `tool_calls`, `"có"` bị coi là yêu cầu mới (an toàn: không thực thi nhầm). Không còn endpoint `/confirm` riêng.
- **Streaming:** chưa hỗ trợ ở proxy .NET (v1). App gửi `stream:true` cũng bị ép về `false`.
- **Xác thực:** JWT chỉ áp cho lời gọi VÀO .NET. `.NET → AI server` là nội bộ (không JWT); MCP gọi ngược .NET dùng service-account như hiện tại.
- **Cấu hình AI server:** env `AI_SERVER_URL` (ưu tiên) hoặc `AiServer:BaseUrl` (appsettings), mặc định `http://localhost:8787`.
- **Lỗi:** AI server chưa cấu hình/không kết nối → `503`; AI server trả lỗi khác → `502`; chưa đăng nhập → `401`; body không phải JSON object hoặc `messages` rỗng/thiếu → `400`.

Ví dụ:
```bash
TOKEN=$(curl -s -X POST http://localhost:8000/api/auth/dev-token | jq -r .token)
curl -X POST http://localhost:8000/api/assistant/v1/chat/completions \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"messages":[{"role":"user","content":"Tưới nước cho ESP32S3_Zone1 giúp mình"}]}'
```

---

## Luồng Auto

### Thiết bị thật (fw 2.1.0) — thiết bị tự làm

- **Tưới theo liều (dose-and-soak), chỉ ở AUTO:** đất < `soil_on_pct` mở đợt; mỗi liều `pump_dose_ms`, chờ `soak_s` giữa 2 liều,
  tối đa `max_doses_per_cycle` liều / đợt và `max_doses_per_day` liều / 24 h; đất ≥ `soil_off_pct` đóng đợt. Bị chặn khi
  `soil_ok = false`, `soil_fault`, `safe_mode`, `low_batt` / `batt_cut` (bản pin), `max_doses_per_day = 0`.
- **Đèn tự động, chỉ ở AUTO:** chỉ khi `light_auto = true` (mặc định **false**). Lux < `lux_on` bật ở `light_auto_pwm`; lux >
  `lux_off` tắt; tối đa `light_max_min_per_day` phút / ngày.
- Server chỉ lưu telemetry, đồng bộ `mode`, bắn Notify. Muốn đổi hành vi → `PUT /api/control/{deviceId}/config`.
- Lệnh tay (`/water`, `/light`) → MANUAL **vô thời hạn** (tự động hoá đứng yên). App nên hiện rõ khi `mode = "manual"` và có nút
  "Trả về tự động" (`POST .../auto`).

### Thiết bị ảo `SIM_*` — server làm

Như trước: `AutoWateringDecider` (hysteresis `soil_on_pct` / `soil_off_pct`, cooldown `pump_cooldown_s`, chặn khi bồn cạn /
`batt_cut`) và `AutoLightDecider` (vượt ngưỡng `lux_on` / `lux_off`) gửi lệnh kèm `"mode":"auto"` khi mode server giữ là `auto`.

---

## Curl Examples

> Deploy Docker: http://localhost:8080 · chạy dev bằng dotnet run: http://localhost:8000

```bash
API=http://localhost:8000; DEVICE=ESP32S3_Zone1
TOKEN=$(curl -s -X POST $API/api/auth/dev-token | jq -r .token)
H=(-H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json")

curl "${H[@]}" $API/api/devices/$DEVICE/status                                        # còn sống không?
curl "${H[@]}" -X POST $API/api/control/$DEVICE/water -d '{"on":true}'                # 1 liều -> MANUAL
curl "${H[@]}" -X POST $API/api/control/$DEVICE/auto                                  # trả AUTO
curl "${H[@]}" -X PUT  $API/api/control/$DEVICE/config -d '{"soil_on_pct":35,"soil_off_pct":65}'
curl "${H[@]}" $API/api/control/$DEVICE/config                                        # xem lại (+ rejected)
curl "${H[@]}" -X POST $API/api/control/$DEVICE -d '{"message":"Cảm ơn bạn!","message_secs":30}'
curl "${H[@]}" -X POST $API/api/control/$DEVICE/show-qr -d '{"seconds":120}'
```

---

## Database Schema

| Collection | Nội dung |
|---|---|
| `SensorData` | 1 bản / telemetry: `deviceId`, `timestamp` (giờ server nhận), `temperature`, `humidity`, `pressure`, `altitude`, `temperatureBmp`, `lightLevel`, `soilPercent`, `soilDryFlag`, `powerSource`, `supplyVoltageV`, `supplyCurrentMa`, `powerState`, `batteryVoltageV`, `batteryCurrentMa`, `batteryPowerMw`, `batteryPercent`, `lightOn`, `lightPwm`, `pumpOn`, `mode`, `waterOk`, `lowBatt`, `battFull`, `battCut`, `soilOk`, `soilFault`, `safeMode`, `dosesToday`, `lightMinToday` |
| `DeviceStatuses` | 1 bản / thiết bị (unique `deviceId`): bản `xmini/status` gần nhất + `retained` + `receivedAt` |
| `DeviceConfigs` | 1 bản / thiết bị (unique `deviceId`): 25 khoá camelCase (+ `pumpMaxRunS` / `pumpCooldownS` cho `SIM_*`), `rejected`, `rejectedCount`, `rejectedAt`, `updatedAt` |
| `ControlCommands` | Nhật ký lệnh đã publish: `deviceId`, `payload` (có `id`), `createdAt` |
| `Devices` | `deviceId`, `name`, `ownerId`, `members`, `controlMode`, `lastSeen`, … |

---

## Troubleshooting

### Thiết bị không tự tưới

1. `mode` trong telemetry = `"manual"`? → `POST /api/control/{deviceId}/auto` (lệnh tay trước đó đã đưa về MANUAL).
2. `soil_ok = false` (que đất hỏng), `soil_fault = true`, `safe_mode = true`? → sửa phần cứng / gọi `/auto` để gỡ.
3. `max_doses_per_day = 0` hoặc `doses_today` đã chạm hạn mức? `GET /api/control/{deviceId}/config`.
4. Đất chưa đủ khô: `soil_percent` ≥ `soil_on_pct`.

### Đèn không tự bật

`light_auto` mặc định `false` → `PUT .../config` `{"light_auto":true}`.

### Đổi cấu hình mà không thấy đổi

`GET .../config` xem `rejected` / `rejectedCount` (khoá bị thiết bị từ chối). Đổi cặp ngưỡng thì gửi cả hai khoá.

### Lệnh không tới thiết bị

1. `GET /api/devices/{deviceId}/status` → `connection` có `online` không?
2. Log server: `Connected to MQTT broker`, `Published control to xmini/control`.
3. Lệnh `400`? Đọc thông báo lỗi; lệnh > 1024 byte bị chặn.
4. Thiết bị bỏ lệnh trùng `id` — server luôn sinh `id` mới, đừng tự publish lại cùng `id`.
