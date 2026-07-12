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

1. Đăng ký device → 2. Gửi sensor data (payload IoT team đang dùng, snake_case) → 3. Tạo light rule trigger → 4. Gửi lại sensor data, response có `triggeredCommands` chứa `LIGHT_ON`.

→ Chứng minh: **API public hoạt động đầy đủ, rule engine real-time, accept payload IoT team đang gửi**.

### Phần B — Ollama gọi MCP tool đúng chuẩn (5 phút)

Trong Ollama UI (Claude Desktop / Msty / Open WebUI đã config MCP):

```
>>> Liệt kê tất cả thiết bị IoT đang có
>>> Cây phòng họp hiện tại có cần tưới không?
>>> Tưới cây esp32-001 trong 10 giây
>>> Đặt rule tự động tưới khi độ ẩm < 25%
>>> Cho tôi xem lịch sử 10 lần đo cảm biến gần nhất
```

→ Chứng minh: **Ollama tự gọi đúng tool MCP, không phải prompt hack**.

---

## 4. Kết nối với IoT team (việc cần làm trước demo)

Gửi cho IoT team:

1. **URL public**: `https://<tunnel-url>.trycloudflare.com`
2. **MQTT HiveCloud**: `ba4fbc53bce842ffb0fcd51178d78414.s1.eu.hivemq.cloud:8883` (username: `nod-iot-plant`)
3. **Postman collection** hoặc danh sách endpoint (xem [README.md](README.md) phần "Quick Reference")
4. **Lưu ý**:
   - URL là tạm thời (Quick Tunnel), production sẽ đổi sang domain riêng
   - MQTT dùng HiveCloud (quản lý cloud) → hoạt động everywhere, không phụ thuộc LAN
   - ESP32 có thể dùng HTTP polling endpoint `/api/control/commands/{deviceId}` nếu không muốn dùng MQTT

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
# Upload sensor (server tự eval rule, trả triggeredCommands)
curl -X POST $API/api/sensordata/upload \
  -H "Content-Type: application/json" \
  -d '{
    "device_id":"ESP32S3_Zone1",
    "temperature_c":28.4,
    "pressure_hpa":1008.6,
    "altitude_m":38.2,
    "light_lux":420.5,
    "soil_moisture_percent":61.8,
    "soil_moisture_raw":2540,
    "relay_on":false
  }'

# Test scenario "đất khô" (trigger WATER_ON nếu có moisture rule)
curl -X POST $API/api/sensordata/upload \
  -H "Content-Type: application/json" \
  -d '{"device_id":"ESP32S3_Zone1","temperature_c":30,"soil_moisture_percent":20,"light_lux":500}'

# Test scenario "thiếu sáng" (trigger LIGHT_ON nếu có light rule)
curl -X POST $API/api/sensordata/upload \
  -H "Content-Type: application/json" \
  -d '{"device_id":"ESP32S3_Zone1","temperature_c":25,"soil_moisture_percent":60,"light_lux":50}'

# Xem dữ liệu mới nhất
curl $API/api/sensordata/latest/$DEVICE

# Xem 50 record gần nhất
curl "$API/api/sensordata/history/$DEVICE?limit=50"

# Xem theo khoảng thời gian
curl "$API/api/sensordata/range/$DEVICE?startDate=2026-05-24T00:00:00Z&endDate=2026-05-25T00:00:00Z"
```

### 9.3 Rules — Độ ẩm (Moisture)

```bash
# Tạo rule tưới: tưới khi < 30%, dừng khi > 70%, mỗi lần 5 giây, cooldown 30 phút
curl -X POST $API/api/rules/moisture \
  -H "Content-Type: application/json" \
  -d '{"deviceId":"ESP32S3_Zone1","name":"Tuoi tu dong","minMoisture":30,"maxMoisture":70,"waterDurationMs":5000,"cooldownMs":1800000}'

# Tạo rule cố tình trigger với soil hiện tại (61.8%): min=70 → sẽ tưới
curl -X POST $API/api/rules/moisture \
  -H "Content-Type: application/json" \
  -d '{"deviceId":"ESP32S3_Zone1","name":"Demo tuoi","minMoisture":70,"maxMoisture":80,"waterDurationMs":5000,"cooldownMs":60000}'

# Xem rules
curl $API/api/rules/moisture/$DEVICE

# Cập nhật rule (thay <ruleId>)
curl -X PUT $API/api/rules/moisture/<ruleId> \
  -H "Content-Type: application/json" \
  -d '{"name":"Tuoi tu dong","minMoisture":25,"maxMoisture":70,"waterDurationMs":8000,"isEnabled":true,"cooldownMs":1800000}'

# Xoá rule
curl -X DELETE $API/api/rules/moisture/<ruleId>
```

### 9.4 Rules — Ánh sáng (Light)

```bash
# Tạo rule đèn: bật khi lux < 25, tắt khi > 60
curl -X POST $API/api/rules/light \
  -H "Content-Type: application/json" \
  -d '{"deviceId":"ESP32S3_Zone1","name":"Den chieu sang","minLight":25,"maxLight":60,"isEnabled":true,"cooldownMs":600000}'

# Rule cố tình trigger LIGHT_ON với light hiện tại (420.5 lux): min=500
curl -X POST $API/api/rules/light \
  -H "Content-Type: application/json" \
  -d '{"deviceId":"ESP32S3_Zone1","name":"Demo bat den","minLight":500,"maxLight":600,"isEnabled":true,"cooldownMs":60000}'

# Rule cố tình trigger LIGHT_OFF với light hiện tại: max=400
curl -X POST $API/api/rules/light \
  -H "Content-Type: application/json" \
  -d '{"deviceId":"ESP32S3_Zone1","name":"Demo tat den","minLight":100,"maxLight":400,"isEnabled":true,"cooldownMs":60000}'

# Xem rules
curl $API/api/rules/light/$DEVICE

# Cập nhật
curl -X PUT $API/api/rules/light/<ruleId> \
  -H "Content-Type: application/json" \
  -d '{"name":"Den chieu sang","minLight":20,"maxLight":70,"isEnabled":true,"cooldownMs":600000}'

# Xoá
curl -X DELETE $API/api/rules/light/<ruleId>
```

### 9.5 Control — Điều khiển thủ công

```bash
# Bật máy bơm 5 giây
curl -X POST $API/api/control/commands \
  -H "Content-Type: application/json" \
  -d '{"deviceId":"ESP32S3_Zone1","command":"WATER_ON","parameters":{"duration":5000}}'

# Tắt bơm
curl -X POST $API/api/control/commands \
  -H "Content-Type: application/json" \
  -d '{"deviceId":"ESP32S3_Zone1","command":"WATER_OFF"}'

# Bật đèn
curl -X POST $API/api/control/commands \
  -H "Content-Type: application/json" \
  -d '{"deviceId":"ESP32S3_Zone1","command":"LIGHT_ON"}'

# Tắt đèn
curl -X POST $API/api/control/commands \
  -H "Content-Type: application/json" \
  -d '{"deviceId":"ESP32S3_Zone1","command":"LIGHT_OFF"}'

# ESP32 polling: lấy lệnh đang chờ
curl $API/api/control/commands/$DEVICE

# ESP32 báo đã thực hiện xong
curl -X POST $API/api/control/commands/<commandId>/executed

# Auto-water (ép tưới nếu soil < threshold)
curl -X POST "$API/api/control/auto-water/$DEVICE?threshold=30.0"

# Auto-light (ép bật đèn nếu light < threshold)
curl -X POST "$API/api/control/auto-light/$DEVICE?threshold=200.0"
```

### 9.6 Demo flow gợi ý (chạy theo thứ tự)

```bash
# 1. Đăng ký device
curl -X POST $API/api/devices/register -H "Content-Type: application/json" \
  -d '{"deviceId":"ESP32S3_Zone1","name":"Zone 1","plantType":"Mixed","location":"IoT lab"}'

# 2. Gửi sensor data lần 1 (không có rule → triggeredCommands rỗng)
curl -X POST $API/api/sensordata/upload -H "Content-Type: application/json" \
  -d '{"device_id":"ESP32S3_Zone1","temperature_c":28.4,"light_lux":420.5,"soil_moisture_percent":61.8}'

# 3. Tạo rule light cố tình trigger
curl -X POST $API/api/rules/light -H "Content-Type: application/json" \
  -d '{"deviceId":"ESP32S3_Zone1","name":"Demo bat den","minLight":500,"maxLight":600,"isEnabled":true,"cooldownMs":60000}'

# 4. Gửi lại sensor data → response có triggeredCommands: [LIGHT_ON]
curl -X POST $API/api/sensordata/upload -H "Content-Type: application/json" \
  -d '{"device_id":"ESP32S3_Zone1","temperature_c":28.4,"light_lux":420.5,"soil_moisture_percent":61.8}'

# 5. ESP32 polling lấy lệnh
curl $API/api/control/commands/$DEVICE
```
