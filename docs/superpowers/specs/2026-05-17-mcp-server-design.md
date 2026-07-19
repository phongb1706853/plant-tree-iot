> ⚠️ **SUPERSEDED (2026-07-15).** Tài liệu lịch sử — mô tả thiết kế cũ. Hợp đồng thiết bị hiện hành là `mqtt-api.md` (mô hình device-native: thiết bị tự chạy auto; backend chỉ đọc telemetry, đọc/đặt ngưỡng, gửi lệnh thủ công dạng khoá phẳng pump/light/light_pwm/mode/config/message trên topic `xmini/control`). KHÔNG dùng `WATER_ON/LIGHT_ON/FAN_*`, không có rule-engine phía server.

# MCP Server Design — Plant Tree IoT

**Date:** 2026-05-17  
**Author:** Phong Nguyen  
**Status:** Approved

---

## 1. Mục tiêu

Build một MCP server bằng Python để kết nối Ollama (local AI model) với REST API backend của hệ thống tưới cây IoT. Cho phép người dùng điều khiển và giám sát thiết bị ESP32 thông qua hội thoại tự nhiên với AI.

---

## 2. Kiến trúc tổng thể

```
┌─────────────────────────────── Mac Mini ───────────────────────────────┐
│                                                                          │
│   Ollama Model                                                           │
│       ↓  MCP Protocol (stdio)                                           │
│   MCP Server (Python)          ← project này                           │
│       ↓  HTTP (localhost:5000)                                          │
│   .NET REST API                ← plant-tree backend                    │
│       ↓  MQTT (192.168.88.126:1883)                                    │
│   Mosquitto Docker                                                       │
│                                                                          │
└──────────────────────────────────────────────────────────────────────────┘
                                      ↓ WiFi
                                   ESP32 (phần cứng)
```

**Transport:** stdio (Ollama chạy local, không cần HTTP server riêng)  
**Approach:** MCP server gọi REST API — toàn bộ business logic giữ ở .NET backend

---

## 3. Cấu trúc thư mục

```
plant-tree-mcp/
├── server.py           # Entry point — khởi tạo MCP, đăng ký tất cả tools
├── config.py           # Cấu hình URL, timeout, server metadata
├── tools/
│   ├── __init__.py
│   ├── sensors.py      # Tools đọc dữ liệu cảm biến
│   ├── control.py      # Tools gửi lệnh điều khiển thiết bị
│   ├── rules.py        # Tools quản lý automation rules
│   └── devices.py      # Tools xem thông tin thiết bị
└── requirements.txt
```

Mỗi module trong `tools/` độc lập — thêm capability mới chỉ cần thêm file, không sửa code cũ.

---

## 4. Danh sách Tools

### 4.1 Sensors (`tools/sensors.py`)

| Tool | Input | Mô tả |
|---|---|---|
| `get_latest_sensor` | `device_id: str` | Đọc sensor mới nhất (temp, humidity, soil, light, pH, water level) |
| `get_sensor_history` | `device_id: str`, `limit: int = 10` | Lấy N bản ghi gần nhất |

**API calls:**
- `GET /api/sensordata/latest/{deviceId}`
- `GET /api/sensordata/{deviceId}?limit={limit}`

---

### 4.2 Control (`tools/control.py`)

| Tool | Input | Mô tả |
|---|---|---|
| `send_command` | `device_id: str`, `command: str`, `duration: int = 0` | Gửi lệnh thủ công |
| `get_pending_commands` | `device_id: str` | Xem lệnh đang chờ thực thi |
| `auto_water` | `device_id: str` | Kích hoạt tưới tự động theo threshold |

**Commands hợp lệ:** `WATER_ON`, `WATER_OFF`, `LIGHT_ON`, `LIGHT_OFF`, `FAN_ON`, `FAN_OFF`

**API calls:**
- `POST /api/control/commands`
- `GET /api/control/commands/{deviceId}`
- `POST /api/control/auto-water/{deviceId}`

---

### 4.3 Rules (`tools/rules.py`)

| Tool | Input | Mô tả |
|---|---|---|
| `get_moisture_rule` | `device_id: str` | Xem rule tưới nước hiện tại |
| `set_moisture_rule` | `device_id: str`, `min_threshold: float`, `max_threshold: float`, `duration: int` | Đặt/cập nhật rule tưới |
| `get_light_rule` | `device_id: str` | Xem rule đèn hiện tại |
| `set_light_rule` | `device_id: str`, `min_threshold: float`, `max_threshold: float` | Đặt/cập nhật rule đèn |

**API calls:**
- `GET /api/rules/moisture/{deviceId}`
- `POST /api/rules/moisture`
- `GET /api/rules/light/{deviceId}`
- `POST /api/rules/light`

---

### 4.4 Devices (`tools/devices.py`)

| Tool | Input | Mô tả |
|---|---|---|
| `list_devices` | _(none)_ | Liệt kê tất cả thiết bị đã đăng ký |
| `get_device_info` | `device_id: str` | Chi tiết thiết bị (status, LastSeen, location) |

**API calls:**
- `GET /api/devices`
- `GET /api/devices/{deviceId}`

---

## 5. Config

```python
# config.py
API_BASE_URL = "http://localhost:5000"   # .NET REST API local
REQUEST_TIMEOUT = 10                      # seconds
MCP_SERVER_NAME = "plant-tree-mcp"
MCP_SERVER_VERSION = "1.0.0"
```

Thay đổi `API_BASE_URL` để trỏ sang server khác khi cần (staging, production).

---

## 6. Dependencies

```
mcp>=1.0.0
httpx>=0.27.0
```

- `mcp` — Anthropic MCP Python SDK
- `httpx` — async HTTP client để gọi REST API

---

## 7. Setup & Chạy trên Mac Mini

```bash
# 1. Clone và cài
git clone <repo> && cd plant-tree-mcp
python -m venv venv
source venv/bin/activate
pip install -r requirements.txt

# 2. Kết nối với Ollama — thêm vào Ollama config
{
  "mcpServers": {
    "plant-tree": {
      "command": "python",
      "args": ["/path/to/plant-tree-mcp/server.py"]
    }
  }
}

# 3. Thứ tự khởi động trên Mac Mini
# Step 1: Docker (Mosquitto) — tự chạy
# Step 2: dotnet run (REST API — port 5000)
# Step 3: ollama serve (port 11434)
# Step 4: MCP server tự khởi động khi Ollama cần
```

---

## 8. Ví dụ conversation

```
User: "Cây phòng khách có ổn không?"
AI:   [gọi list_devices → get_latest_sensor(device_id)]
AI:   "Độ ẩm đất 18% (hơi khô), nhiệt độ 31°C, ánh sáng đủ.
       Bạn có muốn tôi tưới không?"

User: "Tưới 30 giây đi"
AI:   [gọi send_command(WATER_ON, duration=30)]
AI:   "Đã gửi lệnh tưới 30 giây thành công."

User: "Đặt rule tự động tưới khi đất khô dưới 25%"
AI:   [gọi set_moisture_rule(min_threshold=25, max_threshold=70, duration=60)]
AI:   "Đã đặt rule: tưới khi độ ẩm < 25%, dừng khi > 70%, mỗi lần 60 giây."
```

---

## 9. Upgrade Path

| Version | Nội dung |
|---|---|
| v1.0 | Approach A — MCP gọi REST API (spec này) |
| v1.1 | Thêm `tools/mqtt.py` — gửi lệnh trực tiếp qua MQTT (nhanh hơn) |
| v2.0 | Thêm AI reasoning — phân tích trend sensor, gợi ý rule tự động, cảnh báo bất thường |

---

## 10. Out of Scope (v1.0)

- Authentication / API key cho MCP server
- Dashboard UI
- Notification (email, Telegram)
- Multi-device batch commands
