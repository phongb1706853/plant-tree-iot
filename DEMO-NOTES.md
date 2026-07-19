# Demo Notes — Plant Tree IoT

> Ngày demo: 2026-05-25
> Tunnel URL hiện tại (Quick Tunnel, sẽ đổi mỗi lần restart cloudflared):
> `https://crawford-super-vista-bytes.trycloudflare.com`

---

## 1. Hiện trạng deployment (Mac Mini)

| Component | Location | Cách chạy | Auto-start? |
|---|---|---|---|
| MongoDB (Docker) | Local:27017 | `docker start mongodb` | ❌ phải start tay |
| MQTT Broker | HiveCloud | Quản lý bởi HiveMQ (không cần chạy local) | ✅ tự động |
| .NET API | Local:8080 | `cd ~/plant-tree-iot/PlantTreeIoTServer && dotnet run` (bare `dotnet run` nghe ở 8000; bản deploy truy cập ở 8080) | ❌ |
| Cloudflare Tunnel | — | `cloudflared tunnel --url http://localhost:8080` | ❌ |
| MCP server | stdio | Ollama tự spawn khi cần | tự động |

**Tất cả 3 process trên đều phải giữ terminal mở suốt demo** (Quick Tunnel = URL chết khi tắt).

---

## 2. Checklist sáng hôm demo (chạy theo thứ tự)

```bash
# T1. Bật Docker Desktop
open -a Docker
# Đợi icon cá voi menubar đứng yên (~20s)

# T2. Start MongoDB container (MQTT đã được quản lý bởi HiveCloud)
docker start mongodb
docker ps   # verify mongodb đang Up

# T3. Chạy .NET API (TERMINAL 1 - giữ mở)
cd ~/plant-tree-iot/PlantTreeIoTServer
dotnet run
# Đợi log: "Now listening on: http://[::]:8000"
#          "Connected to MQTT broker: ba4fbc53bce842ffb0fcd51178d78414.s1.eu.hivemq.cloud:8883"

# T4. Chạy Cloudflare Tunnel (TERMINAL 2 - giữ mở)
cloudflared tunnel --url http://localhost:8080
# Copy URL "https://xxx-xxx-xxx-xxx.trycloudflare.com" từ output
# URL này MỚI mỗi lần chạy — cập nhật vào tất cả nơi dùng

# T5. Test từ terminal khác (TERMINAL 3)
curl https://<new-url>.trycloudflare.com/api/devices
# Phải trả về []
```

**Cảnh báo**: nếu tunnel URL đổi → phải cập nhật:
- IoT team đang test → gửi URL mới
- `mcp-server/config.py` → đổi `API_BASE_URL`

**MQTT Status**: Được kết nối tới HiveCloud (`ba4fbc53bce842ffb0fcd51178d78414.s1.eu.hivemq.cloud`). Không cần start container riêng.

---

## 3. Demo cho leader (kịch bản gợi ý)

### Phần A — IoT team gọi HTTP từ bên ngoài (5 phút)

Mọi lệnh ở phần 9 bên dưới (Curl Library). Highlight cho demo:

1. Đăng ký device → 2. Gửi sensor data (payload IoT team đang dùng, snake_case, 21 trường) → 3. Đặt ngưỡng auto cho thiết bị (`PUT /api/control/{deviceId}/config`) → 4. Gửi lệnh thủ công (`POST /api/control/{deviceId}`, vd `{"pump":true}` / `{"light":true}`), xem lại ở nhật ký lệnh.

→ Chứng minh: **API public hoạt động đầy đủ, mô hình device-native (thiết bị tự chạy auto theo ngưỡng NVS, server chỉ đọc telemetry / đặt ngưỡng / gửi lệnh thủ công), accept payload IoT team đang gửi**.

### Phần B — Ollama gọi MCP tool đúng chuẩn (5 phút)

Trong Ollama UI (Claude Desktop / Msty / Open WebUI đã config MCP):

```
>>> Liệt kê tất cả thiết bị IoT đang có
>>> Cây phòng họp hiện tại có cần tưới không?
>>> Bật bơm tưới cho cây esp32-001
>>> Đặt ngưỡng tự động tưới khi độ ẩm < 25%
>>> Cho tôi xem lịch sử 10 lần đo cảm biến gần nhất
```

→ Chứng minh: **Ollama tự gọi đúng tool MCP, không phải prompt hack**.

---

## 4. Kết nối với IoT team (việc cần làm trước demo)

Gửi cho IoT team:

1. **URL public**: `https://<tunnel-url>.trycloudflare.com`
2. **MQTT HiveCloud**: `ba4fbc53bce842ffb0fcd51178d78414.s1.eu.hivemq.cloud:8883` (backend và firmware có thể dùng login riêng trên CÙNG broker này — dùng credentials được cấp riêng, không hardcode ở đây)
3. **Postman collection** hoặc danh sách endpoint (xem [README.md](README.md) phần "Quick Reference")
4. **Lưu ý**:
   - URL là tạm thời (Quick Tunnel), production sẽ đổi sang domain riêng
   - MQTT dùng HiveCloud (quản lý cloud) → hoạt động everywhere, không phụ thuộc LAN
   - ESP32 dùng MQTT (QoS 0, không retained) với 3 topic: `xmini/sensor_data` (telemetry), `xmini/config` (ngưỡng auto), `xmini/control` (lệnh thủ công). Thiết bị TỰ chạy auto theo ngưỡng lưu NVS; endpoint `/api/control/commands/{deviceId}` chỉ là nhật ký lệnh đã gửi

---

## 5. Kết nối với Ollama (việc cần làm trước demo)

### Trên máy nào dùng Ollama (Mac hoặc máy khác)

1. **Cài Ollama**: tải từ https://ollama.com (không cần sudo trên Mac)
2. **Clone repo + setup MCP server** (giống bước đã làm trên Mac này):
   ```bash
   git clone https://github.com/phongb1706853/plant-tree-iot.git
   cd plant-tree-iot/mcp-server
   uv venv --python 3.11
   source .venv/bin/activate
   uv pip install -r requirements.txt
   ```
3. **Cập nhật [mcp-server/config.py](mcp-server/config.py)** trỏ URL tunnel:
   ```python
   API_BASE_URL = "https://<tunnel-url>.trycloudflare.com"
   REQUEST_TIMEOUT = 15
   MCP_SERVER_NAME = "plant-tree-mcp"
   ```
4. **Config MCP client** (Claude Desktop / Msty / Open WebUI):
   ```json
   {
     "mcpServers": {
       "plant-tree": {
         "command": "/path/to/mcp-server/.venv/bin/python",
         "args": ["/path/to/mcp-server/server.py"]
       }
     }
   }
   ```
5. **Restart Ollama UI** → tools `plant-tree` phải xuất hiện trong danh sách MCP tools

### Test trước demo

Trong UI hỏi: "Liệt kê thiết bị" → AI phải tự gọi tool `list_devices` và trả về data từ server.

---

## 6. Failure modes — chuẩn bị plan B

| Tình huống | Plan B |
|---|---|
| Tunnel URL đổi sát giờ demo | Cập nhật URL trong `config.py`, gửi IoT team URL mới |
| Cloudflare throttle Quick Tunnel | Restart cloudflared, lấy URL mới |
| Wi-Fi Mac mất kết nối | Demo offline trên LAN, dùng `http://localhost:8080` thay vì tunnel |
| MongoDB container crash | `docker restart mongodb` |
| `dotnet run` báo port 8000 đang dùng | `lsof -i :8000` xem process nào, hoặc đổi sang port khác trong `launchSettings.json` |
| Ollama không thấy tool | Check log MCP, verify `config.py` URL đúng, test `curl` từ máy Ollama tới URL |

---

## 7. TODO sau demo (nếu leader approve)

- [ ] Mua domain rẻ (~$1–10/năm) hoặc xin subdomain công ty
- [ ] Setup Cloudflare Tunnel với domain cố định (thay Quick Tunnel)
- [ ] Tạo `launchd` plist để auto-start khi reboot Mac:
  - Docker container (MongoDB) — MQTT dùng HiveCloud (managed), không có Mosquitto local
  - .NET API
  - cloudflared
- [ ] Bật firewall macOS + allow chỉ những port cần thiết
- [ ] Thêm authentication cho API (API key hoặc OAuth) — hiện tại endpoint public không bảo vệ
- [ ] Backup MongoDB định kỳ (volume Docker hoặc dump ra file)
- [ ] ESP32: flash code production, đổi `SERVER_URL` về domain mới
- [ ] Document URL/credentials cho IoT team trong wiki nội bộ

---

## 8. Lệnh tham khảo nhanh

```bash
# Xem tất cả container đang chạy
docker ps

# Restart toàn bộ stack (sau reboot)
docker start mongodb
cd ~/plant-tree-iot/PlantTreeIoTServer && dotnet run        # Terminal 1
cloudflared tunnel --url http://localhost:8080                # Terminal 2

# IP LAN của Mac (ESP32 cùng LAN có thể dùng IP này nếu muốn kết nối trực tiếp HTTP)
ipconfig getifaddr en0
```

---

## 9. Curl Library — tất cả endpoint

> Đặt biến trước (Mac/Linux):
> ```bash
> export API=https://crawford-super-vista-bytes.trycloudflare.com   # đổi sang URL tunnel mới
> export DEVICE=ESP32S3_Zone1
> ```
> Windows PowerShell:
> ```powershell
> $API="https://crawford-super-vista-bytes.trycloudflare.com"
> $DEVICE="ESP32S3_Zone1"
> ```

### 9.1 Devices

```bash
# Đăng ký device
curl -X POST $API/api/devices/register \
  -H "Content-Type: application/json" \
  -d '{"deviceId":"ESP32S3_Zone1","name":"Zone 1","plantType":"Mixed","location":"IoT lab"}'

# Xem tất cả
curl $API/api/devices

# Xem 1 device
curl $API/api/devices/$DEVICE

# Heartbeat (ESP32 gọi định kỳ để báo còn sống)
curl -X POST $API/api/devices/$DEVICE/heartbeat
```

### 9.2 Sensor Data — payload IoT team (snake_case, đã support)

```bash
# Upload sensor (telemetry snake_case đúng hợp đồng, 21 trường) → response {message, timestamp}
curl -X POST $API/api/sensordata/upload \
  -H "Content-Type: application/json" \
  -d '{
    "device_id":"ESP32S3_Zone1",
    "temperature_c":28.4,
    "humidity_percent":65.2,
    "pressure_hpa":1008.6,
    "altitude_m":38.2,
    "light_lux":420.5,
    "soil_percent":62,
    "battery_voltage_v":4.05,
    "battery_current_ma":120.0,
    "battery_power_mw":486.0,
    "battery_percent":88,
    "temperature_bmp_c":28.9,
    "soil_dry_flag":false,
    "light_on":false,
    "light_pwm":0,
    "pump_on":false,
    "mode":"auto",
    "low_batt":false,
    "batt_full":false,
    "batt_cut":false,
    "water_ok":true
  }'

# Ví dụ telemetry "đất khô" (soil_dry_flag=true) — thiết bị TỰ quyết tưới theo ngưỡng NVS
curl -X POST $API/api/sensordata/upload \
  -H "Content-Type: application/json" \
  -d '{"device_id":"ESP32S3_Zone1","temperature_c":30,"soil_percent":20,"soil_dry_flag":true,"light_lux":500}'

# Ví dụ telemetry "thiếu sáng" — thiết bị TỰ bật đèn theo ngưỡng lux
curl -X POST $API/api/sensordata/upload \
  -H "Content-Type: application/json" \
  -d '{"device_id":"ESP32S3_Zone1","temperature_c":25,"soil_percent":60,"light_lux":50}'

# Xem dữ liệu mới nhất
curl $API/api/sensordata/latest/$DEVICE

# Xem 50 record gần nhất
curl "$API/api/sensordata/history/$DEVICE?limit=50"

# Xem theo khoảng thời gian
curl "$API/api/sensordata/range/$DEVICE?startDate=2026-05-24T00:00:00Z&endDate=2026-05-25T00:00:00Z"
```

### 9.3 Ngưỡng auto thiết bị — Tưới theo độ ẩm (device config)

> Rule-engine phía server đã bị GỠ BỎ. Thiết bị TỰ chạy auto theo ngưỡng lưu NVS.
> Muốn đổi hành vi auto → chỉnh NGƯỠNG qua config (không gửi lệnh WATER_ON).

```bash
# Xem ngưỡng auto hiện tại BE nghe được từ thiết bị (topic xmini/config)
curl $API/api/control/$DEVICE/config

# Đặt ngưỡng tưới: bật bơm khi soil <= 30%, tắt khi >= 70%, chạy tối đa 5s, cooldown 30 phút
curl -X PUT $API/api/control/$DEVICE/config \
  -H "Content-Type: application/json" \
  -d '{"soil_on_pct":30,"soil_off_pct":70,"pump_max_run_s":5,"pump_cooldown_s":1800}'

# Đổi ngưỡng nhạy hơn (tưới sớm hơn khi soil <= 25%)
curl -X PUT $API/api/control/$DEVICE/config \
  -H "Content-Type: application/json" \
  -d '{"soil_on_pct":25}'

# Yêu cầu thiết bị gửi lại toàn bộ cấu hình ngưỡng hiện tại
curl -X POST $API/api/control/$DEVICE/config/refresh
```

### 9.4 Ngưỡng auto thiết bị — Đèn theo ánh sáng (device config)

> Cũng chỉnh qua config; thiết bị TỰ bật/tắt đèn theo ngưỡng lux lưu NVS.

```bash
# Đặt ngưỡng đèn: bật khi lux <= 25 (lux_on), tắt khi >= 60 (lux_off), PWM auto 180
curl -X PUT $API/api/control/$DEVICE/config \
  -H "Content-Type: application/json" \
  -d '{"lux_on":25,"lux_off":60,"light_auto_pwm":180}'

# Đổi độ sáng đèn auto (PWM 0–255)
curl -X PUT $API/api/control/$DEVICE/config \
  -H "Content-Type: application/json" \
  -d '{"light_auto_pwm":220}'

# Xem lại ngưỡng hiện tại
curl $API/api/control/$DEVICE/config
```

### 9.5 Control — Điều khiển thủ công (khoá phẳng → publish `xmini/control`, QoS 0)

> Lệnh chấp hành (`pump`/`light`/`light_pwm`) sẽ ép thiết bị sang chế độ MANUAL.
> Có thể GỘP nhiều khoá trong 1 body. Response: `{message, deviceId, published:{...}}`.

```bash
# Bật máy bơm (thủ công)
curl -X POST $API/api/control/$DEVICE \
  -H "Content-Type: application/json" \
  -d '{"pump":true}'

# Tắt bơm
curl -X POST $API/api/control/$DEVICE \
  -H "Content-Type: application/json" \
  -d '{"pump":false}'

# Bật đèn
curl -X POST $API/api/control/$DEVICE \
  -H "Content-Type: application/json" \
  -d '{"light":true}'

# Tắt đèn
curl -X POST $API/api/control/$DEVICE \
  -H "Content-Type: application/json" \
  -d '{"light":false}'

# Chỉnh độ sáng đèn (PWM 0–255)
curl -X POST $API/api/control/$DEVICE \
  -H "Content-Type: application/json" \
  -d '{"light_pwm":180}'

# Trả thiết bị về chế độ AUTO
curl -X POST $API/api/control/$DEVICE \
  -H "Content-Type: application/json" \
  -d '{"mode":"auto"}'

# Hiện chữ lên màn hình TFT (ASCII), tự xoá sau 15s
curl -X POST $API/api/control/$DEVICE \
  -H "Content-Type: application/json" \
  -d '{"message":"Tuoi cho toi nhe","message_secs":15}'

# Yêu cầu thiết bị gửi lại cấu hình ngưỡng (body config rỗng)
curl -X POST $API/api/control/$DEVICE \
  -H "Content-Type: application/json" \
  -d '{"config":{}}'

# Xem nhật ký lệnh đã gửi (mới nhất)
curl "$API/api/control/commands/$DEVICE?limit=20"
```

### 9.6 Demo flow gợi ý (chạy theo thứ tự)

```bash
# 1. Đăng ký device
curl -X POST $API/api/devices/register -H "Content-Type: application/json" \
  -d '{"deviceId":"ESP32S3_Zone1","name":"Zone 1","plantType":"Mixed","location":"IoT lab"}'

# 2. Gửi sensor data (telemetry snake_case đúng hợp đồng) → response {message, timestamp}
curl -X POST $API/api/sensordata/upload -H "Content-Type: application/json" \
  -d '{"device_id":"ESP32S3_Zone1","temperature_c":28.4,"light_lux":420.5,"soil_percent":62,"mode":"auto"}'

# 3. Đặt ngưỡng auto cho thiết bị (đèn bật khi lux thấp)
curl -X PUT $API/api/control/$DEVICE/config -H "Content-Type: application/json" \
  -d '{"lux_on":500,"lux_off":600,"light_auto_pwm":180}'

# 4. Gửi lệnh thủ công bật đèn (ép MANUAL) → publish xuống xmini/control
curl -X POST $API/api/control/$DEVICE -H "Content-Type: application/json" \
  -d '{"light":true}'

# 5. Xem nhật ký lệnh đã gửi
curl "$API/api/control/commands/$DEVICE?limit=20"
```
