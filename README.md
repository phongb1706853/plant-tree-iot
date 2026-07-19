# Plant Tree IoT

Hệ thống IoT trồng cây thông minh: **ESP32 ⇄ HiveMQ Cloud (MQTT/TLS) ⇄ Server .NET 10 ⇄ MongoDB**, kèm web dashboard và một MCP server cho AI/Ollama.

## Tổng quan

| Thành phần | Vai trò |
|---|---|
| **ESP32** (`esp32-mqtt-client.ino`) | Đọc cảm biến, **tự chạy auto tưới/chiếu sáng theo ngưỡng lưu trong NVS**, publish qua MQTT, nhận lệnh điều khiển |
| **HiveMQ Cloud** | Broker MQTT **managed, chạy trên internet** (TLS 8883) |
| **Server .NET 10** (`PlantTreeIoTServer/`) | Subscribe telemetry + config, đọc/đặt ngưỡng auto, publish lệnh thủ công, REST API |
| **MongoDB** | Lưu dữ liệu cảm biến, thiết bị, cấu hình ngưỡng, user |
| **Web dashboard** (`demo-dashboard.html`) | UI demo + trình test REST API |
| **MCP server** (`mcp-server/`) | Cầu nối Ollama/AI → REST API (xem [mcp-server/README.md](mcp-server/README.md)) |

### Vì sao dùng HiveMQ Cloud?

Broker nằm trên internet nên **ESP32 và server KHÔNG cần cùng mạng WiFi/LAN**. Mạch có thể đặt ở bất kỳ đâu có internet (ví dụ chuyển đi Đà Lạt) và vẫn gặp server qua HiveMQ Cloud — cả hai đều kết nối *ra ngoài* tới broker.

```
   ESP32 (WiFi bất kỳ)                          Server .NET (Docker / Mac)
        │  publish xmini/sensor_data, xmini/config   │  subscribe xmini/sensor_data, xmini/config
        ▼                                            ▼
        └──────────►  HiveMQ Cloud  (TLS 8883)  ◄────┘
                          ▲   │  publish lệnh (xmini/control)
                          └───┘
```

### MQTT topics

Cả 3 topic đều **QoS 0**, **không retained**.

| Topic | Hướng | Nội dung |
|---|---|---|
| `xmini/sensor_data` | Device → Server | Telemetry ~10s (21 trường phẳng snake_case, gồm `soil_percent`) |
| `xmini/config` | Device → Server | 15 ngưỡng auto hiện tại (khi kết nối + sau mỗi lần đổi) |
| `xmini/control` | Server → Device | Lệnh, dạng **1 JSON object phẳng** |

Thiết bị **tự chạy auto tưới/chiếu sáng** theo ngưỡng lưu trong NVS. Server không có rule engine; nó chỉ gửi lệnh **thủ công** dạng khoá phẳng xuống `xmini/control`: `{"pump":true}`, `{"light":true}`, `{"light_pwm":180}`, `{"mode":"auto"}`, `{"message":"..."}`. Muốn đổi hành vi auto thì **chỉnh NGƯỠNG** (`xmini/config`), KHÔNG gửi `WATER_ON`/`LIGHT_ON`.

## Deploy (Docker trên Mac)

Luồng chuẩn: GitHub Actions build image → GHCR → Mac pull về chạy. **Trên Mac không cần source code**, chỉ cần Docker Desktop + file `docker-compose.deploy.yml`.

```bash
docker compose -f docker-compose.deploy.yml up -d     # chạy (tự pull image + mongo:7.0)
docker compose -f docker-compose.deploy.yml pull      # lấy bản mới nhất
docker compose -f docker-compose.deploy.yml logs -f server   # thấy "Connected to MQTT broker..." là OK
```

- API: **http://localhost:8080** (host `8080` → container `8000`).
- Chi tiết & chuẩn bị GHCR: xem [DEPLOY-MAC.md](DEPLOY-MAC.md).

### Truy cập từ xa qua Cloudflare Tunnel

```bash
cloudflared tunnel --url http://localhost:8080
# -> https://<random>.trycloudflare.com  (URL đổi mỗi lần restart cloudflared)
```

Chỉ tunnel HTTP; MQTT không cần tunnel vì HiveMQ Cloud đã public.

### CI/CD — build image

`.github/workflows/docker-publish.yml`: mỗi lần push `main` (đụng `PlantTreeIoTServer/**`, `Dockerfile`, hoặc file workflow) sẽ build image **multi-arch (amd64 + arm64)** và push:

```
ghcr.io/phongb1706853/plant-tree-iot:latest
```

## Chạy dev local

```bash
cd PlantTreeIoTServer
dotnet run
```

Server dev nghe ở **http://localhost:8000** (`ASPNETCORE_ENVIRONMENT=Development`, `JWT_SECRET` không bắt buộc — có fallback dev).

## Cấu hình (biến môi trường)

Đặt trong `.env` cạnh `docker-compose.deploy.yml` (xem mẫu [.env.example](.env.example)). Default MQTT/Mongo đã nhúng sẵn trong compose.

| Biến | Ý nghĩa |
|---|---|
| `JWT_SECRET` | **Bắt buộc ở Production**, ≥ 32 ký tự (fail-closed nếu thiếu). Sinh: `openssl rand -base64 48` |
| `MONGO_URL` | Connection string MongoDB (stack Docker: `mongodb://mongodb:27017`) |
| `MQTT_BROKER` / `MQTT_PORT` / `MQTT_USERNAME` / `MQTT_PASSWORD` | HiveMQ Cloud (TLS 8883) |
| `MQTT_USE_TLS` / `MQTT_ALLOW_INVALID_CERT` | Bật TLS / bỏ qua xác thực cert (chỉ dev) |
| `PORT` | Cổng app nghe trong container (mặc định `8000`) |

## Xác thực

- **JWT Bearer** — cho user / dashboard / MCP service account (`POST /api/auth/register`, `POST /api/auth/login`).
- **DeviceKey** — cho ESP32 gọi HTTP (header `X-Device-Id` + `X-Device-Secret`).
- Kênh MQTT xác thực riêng bằng credential của broker HiveMQ.

Dữ liệu scoped theo owner (mỗi user chỉ thấy thiết bị của mình).

## Tài liệu

- [DEPLOY-MAC.md](DEPLOY-MAC.md) — deploy Docker trên Mac (GHCR pull)
- [API-GUIDE.md](API-GUIDE.md) — tham chiếu REST API + MQTT
- [AUTH-INTEGRATION-GUIDE.md](AUTH-INTEGRATION-GUIDE.md) — tích hợp xác thực
- [mcp-server/README.md](mcp-server/README.md) — MCP server cho AI/Ollama
