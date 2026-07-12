# Hướng dẫn tích hợp Authentication — Plant Tree IoT

> Tài liệu này mô tả **kiến trúc auth mới (JWT + device token + ownership)** và **các bước từng bên cần làm** để hệ thống kết nối lại đúng: .NET API, MCP server (12 tool), AI server (`tree-grow-helper`), và ESP32.
>
> Liên quan: [plan chi tiết](docs/superpowers/plans/2026-07-05-auth-multi-user.md) · [API-GUIDE.md](API-GUIDE.md) · [smoke-test-auth.ps1](smoke-test-auth.ps1)

---

## 🗺️ Sơ đồ tổng thể — ai gọi ai, auth ở đâu

```
                                     Bearer JWT (service account)
  [tree-grow-helper] ──MCP/HTTP──► [MCP server] ──────────────────► [.NET API]
   (AI server, Node)   /mcp         (12 tool Python)                 (MongoDB, rule engine)
        ▲              KHÔNG auth                                       ▲   ▲
        │ chat                                                         │   │
     người dùng                       X-Device-Id/Secret ─────────────┘   │ Bearer JWT
                          [ESP32 HTTP] ──────────────────────────────────┘
                          [ESP32 MQTT] ──broker creds──► [HiveMQ] ◄──► [.NET MQTT bg service]
                          [Dashboard / App] ──Bearer JWT──────────────► [.NET API]
```

**Điểm mấu chốt:** `tree-grow-helper` **không gọi .NET trực tiếp** → nó gọi **MCP server (12 tool)**, và MCP server mới là bên xác thực với .NET.

### MCP server (12 tool) là "người phiên dịch", không phải nơi chứa dữ liệu
Mỗi tool trong `mcp-server/` không tự có dữ liệu — nó **gọi tiếp sang .NET API** bằng HTTP:
```python
def list_devices() -> list:
    return request("GET", "/api/devices")                       # -> .NET
def send_command(device_id, command, ...):
    return request("POST", "/api/control/commands", json=...)   # -> .NET
```
Vì dữ liệu + rule engine + auth nằm ở **.NET**, nên 12 tool phải **kèm JWT** khi gọi .NET. Phần này đã được wire sẵn trong [mcp-server/tools/api_client.py](mcp-server/tools/api_client.py) (tự login, gắn `Bearer`, tự login lại khi 401).

### Trace đầy đủ 1 câu hỏi
Người dùng hỏi AI *"liệt kê thiết bị đang có"*:
```
1. [tree-grow-helper]  LLM quyết định gọi tool  → callTool("list_devices")
2. [MCP server]        chạy list_devices()      → request("GET","/api/devices")
3. [api_client]        đính  Authorization: Bearer <JWT service account>
4. [.NET API]          kiểm JWT + lọc theo OwnerId → trả JSON danh sách device
5. [MCP server]        trả kết quả tool về cho AI
6. [tree-grow-helper]  LLM đọc kết quả → trả lời người dùng bằng tiếng Việt
```

### Hai "đường" khác nhau — đừng lẫn
| Đường | Ai ↔ ai | Auth |
|---|---|---|
| AI ↔ MCP | tree-grow-helper ↔ 12 tool | MCP protocol — **hiện chưa có auth** (chỉ cần đúng URL + MCP chạy HTTP) |
| MCP ↔ .NET | 12 tool ↔ .NET API | **JWT Bearer** (service account) |
| ESP32 ↔ .NET (HTTP) | thiết bị ↔ .NET API | **Device token** (`X-Device-Id` + `X-Device-Secret`) |
| Dashboard/App ↔ .NET | người dùng ↔ .NET API | **JWT Bearer** (đăng nhập) |
| ESP32 ↔ MQTT | thiết bị ↔ HiveMQ broker | Creds broker (không đổi, out-of-scope) |

---

## ✅ A. Việc cần làm tiếp theo (checklist)

### 1. Trên .NET server
- [ ] Đặt `JWT_SECRET` (≥ 32 ký tự ngẫu nhiên) ở môi trường Production (Docker). **Thiếu → server không khởi động** (fail-closed).
- [x] Merge branch `feat/multi-user-auth` → `main` — **đã xong** (auth đã có trong `main`, CI đã build image kèm auth).
- [ ] Tạo **1 tài khoản service account** cho MCP: `POST /api/auth/register` (vd `mcp@plant-tree.local`).
- [ ] Đăng ký / claim các device **dưới tài khoản đó** (MCP chỉ thấy device nó sở hữu).

### 2. Trên MCP server (`plant-tree-iot/mcp-server`)
- [ ] Chạy ở chế độ **HTTP** (để `tree-grow-helper` kết nối được) + set env service account:
```bash
export MCP_TRANSPORT=streamable-http      # BẮT BUỘC để phục vụ HTTP /mcp (mặc định là stdio)
export MCP_HOST=0.0.0.0
export MCP_PORT=8100                       # KHÁC port .NET (8000) để không đụng nhau
export PLANT_API_URL=http://localhost:8080 # URL .NET server THẬT (deploy Docker; dotnet run = 8000; đổi theo môi trường)
export PLANT_MCP_EMAIL=mcp@plant-tree.local
export PLANT_MCP_PASSWORD=<mật khẩu service account>
python server.py                           # -> phục vụ http://<host>:8100/mcp
```

### 3. Trên AI server (`tree-grow-helper`)
- [ ] **Không sửa code.** Chỉ trỏ MCP URL tới MCP server HTTP ở trên:
  - Cùng máy: `http://localhost:8100/mcp`
  - `tree-grow-helper` trong Docker, MCP ở host: `http://host.docker.internal:8100/mcp`
  - Đặt qua **/setup UI** (ô "MCP URL" → "Kiểm tra MCP" → Connect) hoặc env `MCP_URL`.
- [ ] ⚠️ Default của họ là `:8000/mcp` — **trùng port .NET**. Đổi sang `:8100`.

### 4. Trên IoT team (ESP32)
- [ ] Bạn đăng ký device hộ → đưa họ `DEVICE_ID` + `deviceSecret` (chỉ hiện 1 lần).
- [ ] Họ nạp secret vào firmware và gửi header (xem mục C).

---

## ⚠️ B. Lưu ý quan trọng
1. **Breaking change**: mọi endpoint .NET giờ cần auth. Client cũ không gửi token/secret → **401**.
2. **`JWT_SECRET` bắt buộc ở Production** — thiếu là server chết (fail-closed, đúng thiết kế). Local (Development) có fallback trong code, không cần set.
3. **Ownership**: MCP (và mỗi user) chỉ thấy device mình sở hữu. Muốn AI điều khiển device nào → device đó phải thuộc tài khoản MCP. **Mẹo demo**: dùng chung 1 tài khoản cho cả dashboard lẫn MCP.
4. **MCP protocol không có auth**: ai truy cập được `http://host:8100/mcp` là gọi được tool (với quyền service account). → **Đừng mở port 8100 ra Internet**; giữ trong LAN/localhost, hoặc đặt sau reverse-proxy có auth.
5. **Token hết hạn (24h)**: MCP server **tự đăng nhập lại** khi gặp 401 → trong suốt với AI server.
6. **MQTT không đổi**: ESP32 qua MQTT vẫn xác thực bằng creds HiveMQ như cũ.

---

## 📡 C. Chi tiết từng bên

### IoT team (ESP32 — dùng HTTP)
Thêm 2 header vào **mọi** request HTTP (upload sensor, poll lệnh, báo executed, heartbeat):
```cpp
http.addHeader("X-Device-Id", DEVICE_ID);
http.addHeader("X-Device-Secret", DEVICE_SECRET);  // nhận từ bạn khi đăng ký device
```
- ESP32 **không tự đăng ký** nữa (register cần JWT của người dùng) → bạn đăng ký hộ rồi giao secret.
- File firmware mẫu: [esp32-mqtt-client.ino](esp32-mqtt-client.ino).
- Nếu team dùng MQTT ([esp32-mqtt-client.ino](esp32-mqtt-client.ino)) → **không cần header**, vẫn dùng creds HiveMQ.

### AI server (`tree-grow-helper`)
- Thực chất là **Node/TypeScript**, kết nối MCP qua **Streamable HTTP** (`src/mcp/client.ts`).
- **Chỉ cần đúng MCP URL** (mục A.3). Không đụng auth vì nó không gọi .NET trực tiếp.
- 12 tool phía MCP server đã tự lo JWT — AI server không thấy gì khác biệt.

### MCP server (12 tool)
- File chính: [mcp-server/server.py](mcp-server/server.py) (transport stdio/HTTP), [mcp-server/config.py](mcp-server/config.py) (env), [mcp-server/tools/api_client.py](mcp-server/tools/api_client.py) (login + Bearer + retry 401).
- Mỗi tool = 1 lệnh gọi REST tới .NET; xem bảng endpoint trong plan.

---

## 📋 Bảng port
| Thành phần | Port | Ghi chú |
|---|---|---|
| .NET API | 8080 (deploy) / 8000 (dev) | dữ liệu + auth |
| MCP server (HTTP) | **8100** | tránh trùng .NET |
| tree-grow-helper (AI) | 8787 | |
| LM Studio / Ollama | 1234 / 11434 | LLM |

## 📋 Bảng biến môi trường
| Nơi đặt | Biến | Ý nghĩa |
|---|---|---|
| .NET (Production) | `JWT_SECRET` | Khóa ký JWT, ≥32 ký tự. Bắt buộc, fail-closed nếu thiếu |
| MCP server | `MCP_TRANSPORT` | `stdio` (mặc định) hoặc `streamable-http` |
| MCP server | `MCP_HOST` / `MCP_PORT` | Host/port khi chạy HTTP (mặc định `127.0.0.1` / `8100`) |
| MCP server | `PLANT_API_URL` | URL .NET server (mặc định `http://localhost:8080`; dotnet run = 8000 — đổi cho đúng) |
| MCP server | `PLANT_MCP_EMAIL` / `PLANT_MCP_PASSWORD` | Tài khoản service account để login .NET |
| tree-grow-helper | `MCP_URL` | URL MCP server (prefill; /setup lưu đè) |

---

## 🧪 Kiểm thử nhanh
- .NET auth end-to-end (cần MongoDB local + `dotnet build`): `powershell -ExecutionPolicy Bypass -File .\smoke-test-auth.ps1`
- MCP tools (cần Python): `cd mcp-server && pytest -v`
- MCP HTTP phục vụ đúng: chạy MCP với `MCP_TRANSPORT=streamable-http` rồi ở `tree-grow-helper` bấm **"Kiểm tra MCP"** trong /setup (kỳ vọng "✓ tìm thấy N tool").
