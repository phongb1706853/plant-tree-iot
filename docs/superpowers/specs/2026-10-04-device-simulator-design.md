# Thiết kế: Thiết bị ảo (ESP32 giả lập) chạy trong trình duyệt

Ngày: 2026-10-04
Trạng thái: Draft (chờ review)

## 1. Bối cảnh & mục tiêu

Test tay end-to-end (App / demo-dashboard / MCP / AI chat) hiện phụ thuộc ESP32 thật, và thiết bị
không phải lúc nào cũng có sẵn (Mac Mini tắt, thiết bị không cắm). Cần một **thiết bị ảo** cư xử như
firmware Xmini để test và demo mà không cần phần cứng.

Yêu cầu đã chốt:

- **Mục đích:** test tay end-to-end và demo. Không phải test tự động trong CI.
- **Bắt buộc đi qua MQTT thật** (HiveMQ Cloud). Demo phải thấy được message thật chạy qua broker,
  ví dụ xem bằng MQTTx. Không được bơm dữ liệu thẳng vào server.
- **Chạy trên máy bất kỳ**, không phải máy server. Thiết bị ảo chạy **trong trình duyệt**, ngay trong
  tab mới của `demo-dashboard.html`: mở trang ở máy nào thì máy đó là thiết bị.
- **Có giao diện trực quan:** cảnh minh hoạ (chậu cây, đèn, bơm, bồn nước, pin) kèm luồng message MQTT
  trực tiếp (bố cục "C" đã chọn qua mockup).
- **ID bắt buộc tiền tố `SIM_`** (mặc định `SIM_Zone1`), không bao giờ trùng tên thiết bị thật.
- **Không được đụng ESP32 thật.**

## 2. Kiến trúc

```
 Máy demo bất kỳ (trình duyệt)                         Server .NET (Mac hoặc local)
 ┌──────────────────────────────┐                     ┌──────────────────────────────────┐
 │ demo-dashboard.html          │                     │ MqttBackgroundService            │
 │  tab "📟 Thiết bị ảo"         │                     │  nghe xmini/... (thật, giữ nguyên)│
 │  - mô phỏng SIM_Zone1 (JS)   │   wss :8884/mqtt     │  + xmini-sim/+/sensor_data|config │
 │  - mqtt.js ──────────────────┼──► HiveMQ Cloud ◄───┤  -> lưu DB · lastSeen · Notify ·  │
 │  - nút kịch bản              │                     │     tự tưới                       │
 │  - nút "Điều khiển như App" ─┼── HTTP (JWT) ──────►│ ControlController / MCP / auto    │
 └──────────────────────────────┘                     │  -> MqttPublisherService          │
                                                      │     SIM_* -> xmini-sim/{id}/control│
                                                      │     khác  -> xmini/control (cũ)    │
                                                      └──────────────────────────────────┘
```

### 2.1 Topic

Thiết bị ảo dùng **namespace riêng, theo từng thiết bị**:

| Topic | Chiều | Payload |
|---|---|---|
| `xmini-sim/{deviceId}/sensor_data` | thiết bị → server | Y hệt `xmini/sensor_data` (21 trường snake_case, có `device_id`) |
| `xmini-sim/{deviceId}/config` | thiết bị → server | Y hệt `xmini/config`: `{"config":{...}}` |
| `xmini-sim/{deviceId}/control` | server → thiết bị | Y hệt `xmini/control`: một object khoá phẳng |

ESP32 thật chỉ subscribe `xmini/control`, nên **không bao giờ nhận lệnh của thiết bị ảo**, và ngược lại.
Có `deviceId` trong topic nên nhiều thiết bị ảo chạy cùng lúc cũng không dính lệnh của nhau.
QoS 0, không retained (giống hợp đồng thật).

### 2.2 Thay đổi phía server (nhỏ, có test)

1. **`SimTopics` (mới, hàm thuần)**
   - Hằng `Prefix = "xmini-sim"` và `DevicePrefix = "SIM_"`.
   - `IsSimDevice(deviceId)`: id bắt đầu bằng `SIM_` và có ít nhất 1 ký tự sau tiền tố.
   - `ControlTopicFor(deviceId)`: thiết bị ảo → `xmini-sim/{id}/control`; còn lại → `xmini/control`.
   - `PayloadMatchesTopic(topicDeviceId, payloadDeviceId)`: kiểm tra `device_id` trong payload trùng topic.
   - Filter subscribe: `xmini-sim/+/sensor_data` và `xmini-sim/+/config`.
   - `TryParse(topic, out deviceId, out kind)`: tách deviceId và loại message (`sensor_data` / `config`)
     từ topic.
2. **`MqttPublisherService.PublishControlAsync`**: topic = `SimTopics.ControlTopicFor(id)`. Payload
   không đổi. Vì mọi đường gửi lệnh (App
   `/api/control/*`, MCP, AI chat, tự tưới) đều đi qua hàm này, thiết bị ảo tự động dùng được hết.
3. **`MqttBackgroundService`**
   - Subscribe thêm 2 filter của `SimTopics`.
   - Tách phần xử lý chung của `HandleSensorDataAsync` (lưu, lastSeen, Notify, tự tưới) thành một hàm
     dùng chung cho cả telemetry thật và ảo.
   - Telemetry ảo **chỉ được nhận khi** đủ 3 điều kiện: `device_id` trong payload trùng deviceId trong
     topic; id có tiền tố `SIM_`; thiết bị **có trong DB của server này** (`GetDeviceAsync`). Nhờ đó,
     server khác cùng nghe broker (ví dụ Mac) sẽ bỏ qua thiết bị ảo không phải của mình.
   - Telemetry ảo **không ghi** `_lastSeenDeviceId`, để `xmini/config` của thiết bị thật không bị gán
     nhầm sang thiết bị ảo.
   - Config ảo lấy deviceId **từ topic**, không đoán theo telemetry gần nhất. Cũng phải là thiết bị có
     trong DB.
4. Không thêm endpoint mới. Thiết bị ảo đăng ký bằng `POST /api/devices/register` sẵn có.

Sự kiện Notify (`water.empty`, `battery.low`, `battery.cut`, `device.offline`) của thiết bị `SIM_*`
(cập nhật 2026-10-04, `NotifyDispatcher`):
- **Mặc định KHÔNG gửi webhook Notify thật.** Server publish đúng payload Notify (`deviceId, event, severity,
  occurredAt, id, data`) lên `xmini-sim/{id}/notify` cho khung "🔔 Thông báo" trên dashboard.
- Tuỳ chọn **gửi cả sang Notify thật**: cờ `Device.SimNotifyForward`, bật/tắt bằng
  `PUT /api/devices/{id}/sim-notify {"forwardToNotify": true|false}` (chỉ id `SIM_*`; trả kèm `notifyConfigured`).
- Thiết bị thật: không đổi (webhook Notify), và sự kiện của thiết bị thật không bao giờ lên MQTT.

## 3. Thiết bị ảo (JS trong demo-dashboard.html)

### 3.1 Kết nối

- Dùng thư viện **mqtt.js** từ CDN, nối `wss://{host}:8884/mqtt`. Host điền sẵn là cluster hiện tại
  (`ba4fbc53bce842ffb0fcd51178d78414.s1.eu.hivemq.cloud`; host không phải bí mật). Client id
  `planttree-sim-{deviceId}-{random}`, tự kết nối lại mỗi 5 giây.
- **Credential HiveMQ riêng cho giả lập.** Tạo trên HiveMQ Cloud (ví dụ user `planttree-sim`), chỉ
  cấp quyền publish/subscribe trên `xmini-sim/#`. Người demo nhập host/user/password vào trang **một lần**,
  lưu trong `localStorage` (key `planttree-sim-mqtt`). **Không ghi credential vào file HTML.** Nếu gói
  HiveMQ không giới hạn được quyền theo topic, vẫn dùng credential riêng để có thể thu hồi độc lập (xem
  mục 7).
- Server dùng credential hiện có. Credential đó cần đọc `xmini-sim/+/...` và ghi `xmini-sim/+/control`.

### 3.2 Bắt đầu / dừng

**Start:**
1. Đọc host và token từ tab "Kết nối".
2. Gọi `POST /api/devices/register` `{deviceId, name}`.
   - `201`: ok.
   - `409` (đã tồn tại): gọi `GET /api/devices/{id}`. Trả `200` thì dùng tiếp. Trả `404` thì báo
     "ID này đã thuộc user khác, hãy đổi ID".
3. Lấy mode khởi đầu từ `controlMode` của device (`GET /api/devices/{id}`; `null` thì `"auto"`), để
   mode của thiết bị ảo khớp với mode server đang giữ.
4. Nối MQTT, subscribe `xmini-sim/{id}/control`, publish config (giống firmware khi mới kết nối),
   publish telemetry đầu tiên, rồi chạy tick mỗi 10 giây.

**Stop:** ngắt MQTT và dừng tick. Record Device và dữ liệu trên server giữ nguyên.

Trạng thái thiết bị ảo chỉ sống trong trang. Tải lại trang thì về trạng thái ban đầu.

### 3.3 Trạng thái ban đầu

Đất 45%, 27°C, ẩm 60%, 1008 hPa, 600 lux, pin 80%, `water_ok=true`, bơm và đèn tắt, `light_pwm=180`,
`mode="auto"`.

Config mặc định theo firmware: `soil_on_pct=30`, `soil_off_pct=60`, `pump_max_run_s=20`,
`pump_cooldown_s=300`, `lux_on=120`, `lux_off=300`, `light_auto_pwm=180`, `batt_warn_pct=20`. Các
ngưỡng còn lại (`batt_recover_pct`, `soil_dry`, `soil_wet`, `batt_full_on_v`, `batt_full_off_v`,
`batt_crit_v`, `batt_crit_recover_v`) đặt giá trị hợp lý cho pin 2S (ví dụ `batt_crit_v=6.4`,
`batt_crit_recover_v=6.8`).

### 3.4 Mô phỏng (mỗi tick 10 giây, tăng tốc để demo)

| Đại lượng | Quy tắc |
|---|---|
| Đất | Bơm chạy và `water_ok`: **+8%/tick**. Bình thường: **−1%/tick**. Kẹp 0–100. Hết nước thì bơm chạy không, đất không lên. `soil_dry_flag = soil < soil_on_pct`. |
| Bơm | Tự tắt sau `pump_max_run_s`, tính từ lúc bật (an toàn, giống firmware). |
| Đèn auto | Chỉ khi `mode="auto"`, và **chỉ phản ứng khi lux vượt ngưỡng**: lux đi xuống dưới `lux_on` thì bật với PWM = `light_auto_pwm`; lux đi lên trên `lux_off` thì tắt. Không ép liên tục, nên lệnh tay vẫn giữ tới lần vượt ngưỡng kế tiếp. **Ngoại lệ (xét theo MỨC lux hiện tại):** lúc chuyển manual → auto, lúc hết `batt_cut` khi đang auto, và lúc đổi `lux_on`/`lux_off` khi đang auto — vd bật auto giữa đêm thì đèn bật ngay. |
| Pin | State giữ **%**; volt luôn suy ra từ %: volt = 6.4 + 2.0 × % / 100 (2S: 0% = 6.4V, 100% = 8.4V). Tụt 0.1%/tick, tụt thêm 0.5%/tick khi bơm chạy, kẹp 0–100. Dòng và công suất suy từ tải. `low_batt` bật khi % < `batt_warn_pct`, tắt khi % ≥ `batt_recover_pct`. `batt_full` khi volt ≥ `batt_full_on_v`. `batt_cut` khi volt ≤ `batt_crit_v` (mặc định 6.4V, tức 0%), hết khi volt ≥ `batt_crit_recover_v` (6.8V, tức 20%). Khi `batt_cut`, **cắt bơm và đèn ở mọi mode**, và lệnh bật bị bỏ qua. |
| Môi trường | Nhiệt độ, ẩm, áp suất dao động nhẹ ngẫu nhiên quanh giá trị nền. Ẩm nhích lên khi đang tưới. `altitude_m` suy từ áp suất. `temperature_bmp_c` ≈ nhiệt độ ± 0.3. |

### 3.5 Nhận lệnh trên `xmini-sim/{id}/control`

**Thiết bị ảo không tự đổi mode.** Đây là điểm khác firmware thật (firmware ép `manual` khi nhận
pump/light). Lý do: server đã làm chủ mode, còn lệnh tay chỉ đè lên trạng thái đang chạy.

1. `pump` (bool): bật/tắt bơm, đè trạng thái hiện tại, mode giữ nguyên. Bật bơm thì bắt đầu đếm
   `pump_max_run_s`.
2. `light` (bool): bật/tắt đèn, mode giữ nguyên.
3. `light_pwm` (0–255, clamp): đặt độ sáng.
4. `mode` (`"auto"`/`"manual"`) hoặc `auto` (bool): **chỉ** các khoá này đổi mode.
5. `config`: `{}` thì publish lại config hiện tại. `{...}` thì gộp các khoá hợp lệ, clamp (% về 0–100,
   PWM 0–255, giây 0–3600, lux 0–65535, `soil_dry`/`soil_wet` 0–4095, volt 5.0–9.0), rồi publish config.
6. `message` + `message_secs`: hiện chữ trên màn TFT ảo trong `message_secs` giây (0 = tới khi bị
   xoá). `""` để xoá.
7. Khoá lạ: bỏ qua. JSON lỗi: ghi vào log, bỏ qua.

Sau mỗi lệnh có làm đổi trạng thái, thiết bị ảo **publish telemetry ngay**, không chờ tick (khác
firmware, để demo phản hồi tức thì).

### 3.6 Kịch bản (ép giá trị, rồi publish telemetry ngay)

| Nút | Ép | Kết quả mong đợi trên server |
|---|---|---|
| 🏜 Đất khô | soil = 15 | Tự tưới: `{"pump":true,"mode":"auto"}` (nếu đang auto và hết cooldown) |
| 🚱 Hết nước / 💧 Đổ nước | `water_ok` = false / true | Notify `water.empty` |
| 🪫 Pin yếu | pin 15% | Notify `battery.low` |
| 🔌 Pin cạn | pin 0% (volt 6.4 ≤ `batt_crit_v`, bật `batt_cut`) | Notify `battery.cut`, tắt bơm và đèn |
| 🔋 Sạc đầy | pin 100% | `batt_full`; nếu đang `batt_cut` thì hết `batt_cut` |
| 🌙 Ban đêm / ☀️ Ban ngày | lux 5 / 600 | Đèn auto bật / tắt |
| 📴 Mất kết nối / 📶 Kết nối lại | **Ngắt hẳn MQTT** (không gửi, không nhận lệnh; mô phỏng vẫn chạy trong trang) / nối lại | Sau `NOTIFY_OFFLINE_SECONDS` (240s) thì có `device.offline`; nối lại thì server log online |
| ↺ Reset | Về trạng thái ban đầu (mục 3.3), **giữ nguyên mode** (mode là của server) | |

## 4. Giao diện tab "📟 Thiết bị ảo"

Thêm nhóm "Giả lập" ở menu trái của `demo-dashboard.html`, với 1 tab:

- **Thanh trên:** ô Device ID (mặc định `SIM_Zone1`), ô Tên. Ô MQTT host/user/password có thể thu gọn
  và được lưu lại. Nút Start/Stop. Badge online/offline và `AUTO`/`MANUAL`.
- **Trái:** cảnh SVG đổi theo state: đèn sáng theo PWM, giọt nước khi bơm chạy, màu và % đất, mức bồn
  (cạn khi `water_ok=false`), pin, nắng/trăng theo lux, màn TFT. Bên dưới là chip nhiệt, ẩm, áp suất.
- **Phải:** luồng MQTT, gồm 50 message gần nhất mà trang **thực sự** gửi/nhận. ↑ là thiết bị gửi, ↓ là
  lệnh nhận. Mỗi dòng có giờ, topic rút gọn và payload rút gọn; bấm vào để xem đầy đủ.
- **Màu log theo nguồn** (có chú thích ở đầu khung): ⏱ telemetry tự gửi 10s (xanh mờ) · 🧪 telemetry do kịch bản
  môi trường (tím) · ↑ telemetry ngay sau lệnh (xanh) · ↓ lệnh (xanh dương) · ↑ config (vàng) · ⚠ lệnh bật / đèn
  auto bị `batt_cut` chặn (cam) · ✗ lỗi (đỏ). Khi `batt_cut`: băng đỏ "PIN CẠN" trên cảnh.
- **Hàng nút kịch bản** (mục 3.6). Reset gửi lại config để server không giữ ngưỡng cũ.
- **Khung "🔔 Thông báo"** (cột thứ ba; màn hình < 1500px thì xuống dưới): mặc định TẮT, trang nhớ lựa chọn. Bật thì
  mở kết nối MQTT RIÊNG (như một chiếc điện thoại) subscribe `xmini-sim/{id}/notify` — vẫn nhận `device.offline` khi
  thiết bị ảo offline/đã Stop. Thẻ: icon + tiêu đề tiếng Việt + màu theo severity + giờ + chi tiết; bấm xem payload gốc.
  Ô "📤 Gửi cả sang Notify thật" gọi `PUT /api/devices/{id}/sim-notify`; cảnh báo nếu server chưa cấu hình Notify.
- **Khung "🤖 Chế độ auto & ngưỡng":** nút Bật/Tắt auto + 9 ngưỡng (tưới: `soil_on_pct`, `soil_off_pct`,
  `pump_max_run_s`, `pump_cooldown_s`; đèn: `lux_on`, `lux_off`, `light_auto_pwm`; pin: `batt_warn_pct`,
  `batt_recover_pct`). Ô lấy giá trị thiết bị ảo đang dùng; "💾 Lưu ngưỡng" chỉ gửi ô đã sửa qua
  `PUT /api/control/{id}/config` (server → MQTT → thiết bị clamp → echo config). Ô đang sửa không bị ghi đè mỗi tick.
- **Hàng nút "Điều khiển như App":** Tưới, Tắt bơm, Bật đèn, Tắt đèn, Bật auto, Tắt auto. Các nút
  gọi `/api/control/{id}/water|light|auto` và `POST /api/control/{id}` `{"mode":"manual"}` trên server
  bằng token của tab "Kết nối". Nhờ vậy demo được trọn luồng App → server → MQTT → thiết bị.
- **Ghi chú trên trang:** muốn AI chat / MCP điều khiển được thiết bị ảo, chia sẻ nó cho service-account
  MCP qua `POST /api/devices/{id}/share`. Khi demo, giữ tab ở trạng thái hiển thị.

Mockup tham khảo: `.superpowers/brainstorm/614-1791101956/content/device-layout.html` (phương án C).

## 5. Xử lý lỗi

| Tình huống | Xử lý |
|---|---|
| Chưa có host/token ở tab "Kết nối" | Start báo "Đăng nhập ở tab Kết nối trước". |
| Register hoặc GET trả 401 | Báo token hết hạn, cần đăng nhập lại. |
| ID không có tiền tố `SIM_` | Trang chặn ngay, không gọi server. |
| ID đã thuộc user khác | Báo đổi ID (mục 3.2). |
| MQTT sai credential hoặc không nối được | Hiện lỗi trên badge, tự thử lại mỗi 5 giây. Tick vẫn chạy nhưng không publish (đánh dấu trong log). |
| Server nhận telemetry ảo của thiết bị không có trong DB | Bỏ qua, log debug. |
| Tab bị ẩn lâu | Chrome giãn timer (có thể còn 1 tick/phút). Vẫn dưới ngưỡng offline 240s. Trang ghi chú "giữ tab hiển thị khi demo". |

## 6. Test

**xUnit (server, logic thuần, theo mẫu test hiện có):**
- `SimTopics.IsSimDevice`: `SIM_Zone1` → true; `ESP32S3_Zone1`, `sim_x`, `""` → false.
- `SimTopics.ControlTopicFor("SIM_Zone1")` → `xmini-sim/SIM_Zone1/control`.
- `SimTopics.TryParse`: tách đúng deviceId và loại message từ `xmini-sim/SIM_Zone1/sensor_data` và
  `.../config`; từ chối `xmini/sensor_data`, `xmini-sim/x`, `xmini-sim/SIM_Zone1/control`, và id
  không có tiền tố `SIM_`.
- `ControlTopicFor` (publisher dùng hàm này): `SIM_*` đi topic ảo, id khác đi `xmini/control`.
- `PayloadMatchesTopic`: khớp đúng từng ký tự; `null` hoặc khác hoa thường thì false.

**Node (lõi JS của thiết bị ảo, không cần cài gì: `node --test tests-js/sim-core.test.mjs`):** lõi mô phỏng nằm trong
block `<script id="sim-core">` của dashboard (hàm thuần, không DOM/MQTT). Test nạp block đó từ file HTML
và kiểm tra: 21 trường telemetry; pump/light không đổi mode; chỉ `mode`/`auto` đổi mode; tự tắt bơm;
hết nước; `batt_cut`; đèn auto theo cạnh; config echo + clamp; message hết hạn; lệnh rác bị bỏ qua.

**Test tay (checklist):**
1. Chạy server local (`dotnet run`, profile `http`) với MongoDB local. Đăng nhập ở tab "Kết nối".
2. MQTTx subscribe `xmini-sim/#` và `xmini/#`.
3. Tab Thiết bị ảo: nhập credential sim, Start `SIM_Zone1`. Thấy config và telemetry mỗi 10 giây trên
   MQTTx; `GET /api/sensordata/latest/SIM_Zone1` có dữ liệu.
4. Bấm "Đất khô": thấy `{"pump":true,"mode":"auto"}` trên `xmini-sim/SIM_Zone1/control`, cảnh có giọt
   nước, đất tăng, bơm tự tắt sau 20 giây.
5. Bấm "Tắt auto" rồi "Tưới": bơm bật, mode vẫn `manual`. Bấm "Bật auto": mode thành `auto`.
6. "Ban đêm": đèn auto bật. Tắt đèn tay: đèn vẫn tắt ở các tick sau.
7. "Hết nước", "Pin yếu", "Pin cạn": thấy log Notify trên server (nếu đã cấu hình Notify).
8. "Mất kết nối" 4 phút: server log `device.offline`. "Kết nối lại": log online.
9. Suốt quá trình, **không có message nào trên `xmini/control`** do thiết bị ảo gây ra.
10. Mở trang ở một máy khác (không phải máy chạy server): lặp lại bước 3–4.

## 7. Việc ngoài code (người vận hành làm)

- Tạo credential HiveMQ `planttree-sim`. Nếu gói HiveMQ hỗ trợ, giới hạn quyền ở `xmini-sim/#`.
  Kiểm tra credential server hiện tại có quyền trên `xmini-sim/#`.
- Muốn demo với server trên Mac: merge vào `main`, chờ CI build image, rồi `docker compose pull && up -d`
  trên Mac.

## 8. Ngoài phạm vi

- Topic theo từng thiết bị cho ESP32 **thật** (`xmini/control` vẫn dùng chung, vẫn giữ giới hạn cũ).
- REST endpoint `/api/simulator/*` hoặc app simulator chạy riêng (đã cân nhắc, không chọn).
- Lưu bền trạng thái thiết bị ảo; phát hiện 2 tab cùng giả lập một ID.
- Test tự động cho phần giao diện/MQTT của tab (chỉ test lõi mô phỏng, xem mục 6).
