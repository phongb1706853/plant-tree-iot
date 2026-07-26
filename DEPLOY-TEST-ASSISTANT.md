# Deploy & Test luồng Chat (App → .NET → AI → MCP → tool)

Hướng dẫn dựng đủ 5 mảnh để test endpoint `POST /api/assistant/v1/chat/completions` kích hoạt tool điều khiển thật.

## Sơ đồ & cổng

```
curl/App ──(JWT)──► .NET API (8080) ──► AI server (8787) ──► MCP (8100, streamable-http)
                         ▲                                          │
                         └──────────── gọi ngược /api/control ◄─────┘ (service-account JWT)
                         │
                         └──► MQTT (HiveMQ Cloud) ──► ESP32
                                                   AI server ──► LLM (LM Studio/Ollama :1234/:11434)
```

| Thành phần | Cổng | Image / cách chạy |
|---|---|---|
| .NET API | host **8080** → container 8000 | `ghcr.io/phongb1706853/plant-tree-iot:latest` (docker-compose.deploy.yml) |
| AI server | **8787** | `ghcr.io/phantranthelinh/tree-grow-helper:latest` (repo tree-grow-helper) |
| MCP | **8100** | Python `mcp-server/server.py`, `MCP_TRANSPORT=streamable-http` |
| LLM | 1234 (LM Studio) / 11434 (Ollama) | chạy trên host |
| MQTT | 8883 | HiveMQ Cloud (managed) |

> Các service liên hệ nhau qua `host.docker.internal` (Docker Desktop Mac/Windows tự resolve; Linux đã thêm `extra_hosts`).

---

## Bước 1 — Build image .NET (CI)

CI **chỉ build khi push lên `main`** (đụng `PlantTreeIoTServer/**`). Nhánh feature sẽ không build. Chọn 1:
- **Merge** nhánh `feat/app-control-assistant-proxy` vào `main` → CI tự build & push `:latest`.
- Hoặc chạy tay: GitHub → Actions → *Build & Publish Docker image* → **Run workflow** (workflow_dispatch).

Chờ Actions xanh (đã push `ghcr.io/phongb1706853/plant-tree-iot:latest`).

## Bước 2 — Deploy .NET API (trên máy Mac)

```bash
# .env cùng thư mục docker-compose.deploy.yml phải có JWT_SECRET.
#   AI_SERVER_URL mặc định http://host.docker.internal:8787 (đã set trong compose) — đổi nếu AI server ở nơi khác.
docker compose -f docker-compose.deploy.yml pull
docker compose -f docker-compose.deploy.yml up -d
curl -i http://localhost:8080/api/devices     # 401 = server sống (cần token)
```

## Bước 3 — Chạy MCP ở chế độ streamable-http (port 8100)

```bash
cd plant-tree-iot/mcp-server
python3 -m venv venv && source venv/bin/activate
pip install -r requirements.txt

export PLANT_API_URL=http://localhost:8080          # MCP gọi ngược .NET (host port)
export PLANT_MCP_EMAIL=mcp@plant-tree.local         # service-account MCP
export PLANT_MCP_PASSWORD='<mat-khau-mcp>'
export MCP_TRANSPORT=streamable-http MCP_HOST=0.0.0.0 MCP_PORT=8100
python server.py                                    # phục vụ http://localhost:8100/mcp
```

## Bước 4 — ⚠️ Đăng ký service-account MCP + device thuộc quyền nó (QUAN TRỌNG)

MCP gọi ngược .NET bằng **service-account** (`PLANT_MCP_EMAIL`). Tool điều khiển chỉ chạy khi account này **sở hữu / được chia sẻ** device — nếu không sẽ `404`.

```bash
# 4a. Tạo user service-account (khớp PLANT_MCP_EMAIL/PASSWORD ở Bước 3)
curl -X POST http://localhost:8080/api/auth/register -H 'Content-Type: application/json' \
  -d '{"email":"mcp@plant-tree.local","password":"<mat-khau-mcp>","displayName":"MCP"}'
# -> { "token": "..." }  (lưu lại là MCP_TOKEN)

# 4b. Đăng ký device test dưới quyền service-account (hoặc share device sẵn có cho nó)
curl -X POST http://localhost:8080/api/devices/register \
  -H "Authorization: Bearer <MCP_TOKEN>" -H 'Content-Type: application/json' \
  -d '{"deviceId":"ESP32S3_Zone1","name":"Zone 1"}'
```

## Bước 5 — Chạy LLM + AI server

```bash
# 5a. LM Studio: load 1 model chat (gemma-4-e4b) + 1 embedding (bge-m3); bật server :1234.
#     (hoặc Ollama :11434)

# 5b. AI server
cd tree-grow-helper
docker compose up -d
# Lần đầu: mở http://localhost:8787/setup -> chọn provider
#   LM Studio Base URL: http://host.docker.internal:1234/v1
#   MCP URL:            http://host.docker.internal:8100/mcp
#   -> Kết nối. Config lưu vào ./data (lần sau tự nối lại).
curl http://localhost:8787/health     # -> {"status":"ok","phase":"ready"}
```

---

## Bước 6 — Test endpoint chat E2E

```bash
# Token gọi .NET: ở stack Production, /api/auth/dev-token bị tắt (404) -> dùng login.
# (Local dotnet run/Development: có thể dùng POST /api/auth/dev-token cho nhanh.)
TOKEN=$(curl -s -X POST http://localhost:8080/api/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"mcp@plant-tree.local","password":"<mat-khau-mcp>"}' | jq -r .token)

CC=http://localhost:8080/api/assistant/v1/chat/completions

# (A) Câu hỏi/đọc dữ liệu -> trả lời ngay (choices[].message.content), không tool_calls
curl -s -X POST "$CC" \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"messages":[{"role":"user","content":"Độ ẩm đất của ESP32S3_Zone1 bao nhiêu?"}]}' | jq

# (B) Lệnh điều khiển -> choices[].message có tool_calls + câu hỏi Có/Không (CHƯA thực thi). Nhớ nêu deviceId.
RESP=$(curl -s -X POST "$CC" \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"messages":[{"role":"user","content":"Tưới nước cho ESP32S3_Zone1 giúp mình"}]}')
echo "$RESP" | jq
# Lấy nguyên assistant message (kèm tool_calls) để gửi lại ở lượt xác nhận
ASSISTANT=$(echo "$RESP" | jq '.choices[0].message')

# (C) Xác nhận -> gửi lại messages[]: câu user + assistant (kèm tool_calls) + "có"
#     AI gọi MCP -> MCP gọi /api/control -> MQTT -> bơm chạy
curl -s -X POST "$CC" \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d "$(jq -n --argjson a "$ASSISTANT" '{messages:[
        {role:"user",content:"Tưới nước cho ESP32S3_Zone1 giúp mình"},
        $a,
        {role:"user",content:"có"}
      ]}')" | jq

# (D) Kiểm chứng lệnh đã publish thật
curl -s "http://localhost:8080/api/control/commands/ESP32S3_Zone1?limit=5" \
  -H "Authorization: Bearer $TOKEN" | jq
```

---

## Troubleshooting

| Triệu chứng | Nguyên nhân & cách xử lý |
|---|---|
| `/api/assistant/v1/chat/completions` → **503** "Không kết nối được AI server" | `AI_SERVER_URL` sai hoặc AI server chưa chạy. Từ container .NET dùng `http://host.docker.internal:8787`. |
| **503** "AI server chưa cấu hình" | Chưa `/setup` LLM trên AI server → vào `http://localhost:8787/setup`. |
| Trợ lý báo không điều khiển được / tool trả **404 device** | Service-account MCP không sở hữu device → làm lại **Bước 4** (register/share device cho `PLANT_MCP_EMAIL`). |
| AI server không thấy tool / không gọi được MCP | MCP chưa chạy `streamable-http`, sai `MCP_URL`, hoặc sai cổng 8100. Kiểm tra `curl http://localhost:8100/mcp` sống, và `MCP_URL=http://host.docker.internal:8100/mcp` trong /setup. |
| MCP lỗi khi gọi ngược .NET | `PLANT_API_URL` sai. MCP chạy trên host → `http://localhost:8080`; MCP trong container → `http://host.docker.internal:8080`. |
| Tool chạy nhưng thiết bị không phản ứng | MQTT/thiết bị offline. Xem `GET /api/control/commands/{id}` để chắc đã publish; `GET /api/sensordata/latest/{id}` xem `mode` (auto/manual). |
| Muốn trả thiết bị về tự động sau khi tưới tay | `POST /api/control/{id}/auto` hoặc nhờ trợ lý "chuyển về chế độ auto". |

## Ghi chú prod
- `dev-token` tự tắt ở Production (404) — an toàn. OpenAPI chỉ map ở Development.
- JWT chỉ áp cho lời gọi VÀO .NET. `.NET → AI` và `AI → MCP` là nội bộ (không JWT); MCP → .NET dùng service-account.
- Bảo mật: nên đặt AI server + MCP trong mạng nội bộ, không expose ra internet (chỉ .NET API expose qua Cloudflare Tunnel như hiện tại).
