# Demo Notes — Plant Tree IoT

> Ngày demo: 2026-05-25
> Tunnel URL hiện tại (Quick Tunnel, sẽ đổi mỗi lần restart cloudflared):
> `https://crawford-super-vista-bytes.trycloudflare.com`

---

## 1. Hiện trạng deployment (Mac Mini)

| Component | Port | Cách chạy | Auto-start? |
|---|---|---|---|
| MongoDB (Docker) | 27017 | `docker start mongodb` | ❌ phải start tay |
| Mosquitto MQTT (Docker) | 1883 | `docker start mosquitto` | ❌ |
| .NET API | 80 | `cd ~/plant-tree-iot/PlantTreeIoTServer && dotnet run` | ❌ |
| Cloudflare Tunnel | — | `cloudflared tunnel --url http://localhost:80` | ❌ |
| MCP server | stdio | Ollama tự spawn khi cần | tự động |

**Tất cả 4 process trên đều phải giữ terminal mở suốt demo** (Quick Tunnel = URL chết khi tắt).

---

## 2. Checklist sáng hôm demo (chạy theo thứ tự)

```bash
# T1. Bật Docker Desktop
open -a Docker
# Đợi icon cá voi menubar đứng yên (~20s)

# T2. Start containers (giả định 2 container đã tồn tại từ trước)
docker start mongodb mosquitto
docker ps   # verify cả 2 đang Up

# T3. Chạy .NET API (TERMINAL 1 - giữ mở)
cd ~/plant-tree-iot/PlantTreeIoTServer
dotnet run
# Đợi log: "Now listening on: http://[::]:80"
#          "Connected to MQTT broker: 127.0.0.1:1883"

# T4. Chạy Cloudflare Tunnel (TERMINAL 2 - giữ mở)
cloudflared tunnel --url http://localhost:80
# Copy URL "https://xxx-xxx-xxx-xxx.trycloudflare.com" từ output
# URL này MỚI mỗi lần chạy — cập nhật vào tất cả nơi dùng

# T5. Test từ terminal khác (TERMINAL 3)
curl https://<new-url>.trycloudflare.com/api/devices
# Phải trả về []
```

**Cảnh báo**: nếu tunnel URL đổi → phải cập nhật:
- IoT team đang test → gửi URL mới
- `mcp-server/config.py` → đổi `API_BASE_URL`

---

## 3. Demo cho leader (kịch bản gợi ý)

### Phần A — IoT team gọi HTTP từ bên ngoài (5 phút)

Từ máy Windows / Postman, demo các endpoint chính:

```bash
# Đăng ký device
curl -X POST https://<tunnel-url>/api/devices/register \
  -H "Content-Type: application/json" \
  -d '{"deviceId":"esp32-001","name":"Cay demo","plantType":"Cactus","location":"Phong hop"}'

# Xem device
curl https://<tunnel-url>/api/devices

# Gửi sensor data
curl -X POST https://<tunnel-url>/api/sensordata/upload \
  -H "Content-Type: application/json" \
  -d '{"deviceId":"esp32-001","soilMoisture":20,"lightLevel":15,"temperature":28,"humidity":65}'

# Xem sensor data
curl https://<tunnel-url>/api/sensordata/latest/esp32-001

# Tạo rule tự động tưới
curl -X POST https://<tunnel-url>/api/rules/moisture \
  -H "Content-Type: application/json" \
  -d '{"deviceId":"esp32-001","name":"Tuoi tu dong","minMoisture":30,"maxMoisture":70,"waterDurationMs":5000,"cooldownMinutes":30}'

# Gửi command thủ công
curl -X POST https://<tunnel-url>/api/control/commands \
  -H "Content-Type: application/json" \
  -d '{"deviceId":"esp32-001","command":"WATER_ON","parameters":{"duration":5000}}'

# ESP32 lấy pending commands
curl https://<tunnel-url>/api/control/commands/esp32-001
```

→ Chứng minh: **API public hoạt động đầy đủ, IoT team dùng được**.

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
2. **Postman collection** hoặc danh sách endpoint (xem [README.md](README.md) phần "Quick Reference")
3. **Lưu ý**:
   - URL là tạm thời (Quick Tunnel), production sẽ đổi sang domain riêng
   - ESP32 nếu cùng LAN với Mac → dùng MQTT `192.168.88.126:1883`
   - ESP32 nếu khác LAN → dùng HTTP polling endpoint `/api/control/commands/{deviceId}`

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
| Wi-Fi Mac mất kết nối | Demo offline trên LAN, dùng `http://localhost` thay vì tunnel |
| MongoDB container crash | `docker restart mongodb` |
| `dotnet run` báo port 80 đang dùng | `lsof -i :80` xem process nào, hoặc đổi sang port khác trong `launchSettings.json` |
| Ollama không thấy tool | Check log MCP, verify `config.py` URL đúng, test `curl` từ máy Ollama tới URL |

---

## 7. TODO sau demo (nếu leader approve)

- [ ] Mua domain rẻ (~$1–10/năm) hoặc xin subdomain công ty
- [ ] Setup Cloudflare Tunnel với domain cố định (thay Quick Tunnel)
- [ ] Tạo `launchd` plist để auto-start khi reboot Mac:
  - Docker containers (MongoDB + Mosquitto)
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

# Xem logs .NET API
# (đang chạy foreground trong terminal 1, nhìn trực tiếp output)

# Xem logs cloudflared
# (đang chạy foreground trong terminal 2)

# Restart toàn bộ stack (sau reboot)
docker start mongodb mosquitto
cd ~/plant-tree-iot/PlantTreeIoTServer && dotnet run        # Terminal 1
cloudflared tunnel --url http://localhost:80                # Terminal 2

# IP LAN của Mac (ESP32 cùng LAN dùng IP này)
ipconfig getifaddr en0
```
