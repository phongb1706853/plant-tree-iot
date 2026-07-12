# Plant Tree MCP Server

Python MCP server connecting Ollama to Plant Tree IoT REST API.

---

## Checklist cho Mac Mini (làm theo thứ tự)

### Bước 1 — Clone repo

```bash
git clone https://github.com/phongb1706853/plant-tree-iot.git
cd plant-tree-iot
```

### Bước 2 — Cài .NET 10 Runtime (nếu chưa có)

```bash
# Kiểm tra xem đã có chưa
dotnet --version

# Nếu chưa có, tải tại: https://dotnet.microsoft.com/download/dotnet/10.0
# Chọn: macOS - Arm64 (nếu Mac M1/M2/M3) hoặc x64 (nếu Mac Intel)
```

### Bước 3 — Cài Python 3.11+ (nếu chưa có)

```bash
# Kiểm tra
python3 --version

# Nếu chưa có
brew install python@3.11
```

### Bước 4 — Cài dependencies MCP server

```bash
cd plant-tree-iot/mcp-server
python3 -m venv venv
source venv/bin/activate
pip install -r requirements.txt
```

### Bước 5 — Chạy tests để verify

```bash
pytest tests/ -v
```

Expected: **13 passed**

### Bước 6 — Chạy .NET REST API

```bash
cd plant-tree-iot/PlantTreeIoTServer
dotnet run
# API chạy tại http://localhost:8000
# Verify: curl http://localhost:8000/api/devices
```

> Chạy bằng `dotnet run` thì API ở cổng **8000**, còn `config.py` mặc định `PLANT_API_URL=http://localhost:8080` (cổng deploy Docker). Khi dùng `dotnet run`, set `export PLANT_API_URL=http://localhost:8000` cho MCP server.

### Bước 7 — Kết nối Ollama với MCP server

Tìm đường dẫn tuyệt đối trước:

```bash
# Lấy đường dẫn python trong venv
which python   # phải đang activate venv

# Lấy đường dẫn server.py
pwd   # chạy trong thư mục mcp-server
```

Thêm vào Ollama MCP config (tùy UI đang dùng: Open WebUI, Msty, AnythingLLM...):

```json
{
  "mcpServers": {
    "plant-tree": {
      "command": "/absolute/path/to/mcp-server/venv/bin/python",
      "args": ["/absolute/path/to/mcp-server/server.py"]
    }
  }
}
```

Ví dụ nếu clone vào `/Users/phong/plant-tree-iot`:
```json
{
  "mcpServers": {
    "plant-tree": {
      "command": "/Users/phong/plant-tree-iot/mcp-server/venv/bin/python",
      "args": ["/Users/phong/plant-tree-iot/mcp-server/server.py"]
    }
  }
}
```

### Bước 8 — Test với Ollama

```
>>> Liệt kê tất cả thiết bị IoT đang có
>>> Cây phòng khách có cần tưới không?
>>> Tưới cây 30 giây đi
>>> Đặt rule tự động tưới khi độ ẩm đất < 25%
```

---

## Thứ tự khởi động hàng ngày

```
1. dotnet run (PlantTreeIoTServer)          → REST API port 8000
2. ollama serve                             → AI model port 11434
3. MCP server                               → tự khởi động khi Ollama cần
```

---

## Tools Available (12 total)

| Group | Tools |
|---|---|
| Devices | `list_devices`, `get_device_info` |
| Sensors | `get_latest_sensor`, `get_sensor_history` |
| Control | `send_command`, `get_pending_commands`, `auto_water`, `auto_light` |
| Rules | `get_moisture_rule`, `set_moisture_rule`, `get_light_rule`, `set_light_rule` |

---

## Upgrade Path

| Version | Feature |
|---|---|
| v1.1 | API Key auth, direct MQTT commands |
| v2.0 | AI analytics, trend detection, smart rule suggestions |
