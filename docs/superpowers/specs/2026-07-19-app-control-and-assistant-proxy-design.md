# Thiết kế: Endpoint điều khiển cho app + proxy trợ lý AI + dev-login

Ngày: 2026-07-19
Trạng thái: Draft (chờ review)

## 1. Bối cảnh & mục tiêu

Flow gọi được cập nhật:

```
App ──HTTP(JWT)──► .NET server ──┬─► (đa phần) thực thi trực tiếp: devices / sensor / control
                                 └─► /api/assistant/* ─► AI server (tree-grow-helper)
                                                          └─► MCP ─► gọi ngược .NET API (service-account JWT) ─► MQTT ─► ESP32
```

- **App gọi thẳng .NET là chính.** Với lệnh ngôn ngữ tự nhiên, App gọi `.NET /api/assistant/*`, .NET
  proxy tới **AI server** (phán đoán tool) → AI gọi **MCP** → MCP gọi ngược **.NET API** để thực thi.
- **JWT chỉ áp cho các lời gọi vào .NET API.** Lời gọi .NET → AI server là nội bộ, không cần JWT.
  MCP gọi ngược .NET vẫn dùng **service-account** cố định như hiện tại (không đổi).

Ba hạng mục cần làm (đều ở phía .NET, trừ phần demo-dashboard):

- **A.** Endpoint chuyên dụng cho App bật đèn / tưới nước, và trả thiết bị về auto.
- **B.** Endpoint proxy tới AI server để App dùng trợ lý (chat + xác nhận hành động).
- **C.** Dev-login lấy nhanh JWT bearer để debug.

**Không đổi:** firmware ESP32, MCP server (Python), hợp đồng `xmini/control`. AI server đã có sẵn
hợp đồng — ta chỉ *gọi* nó, không sửa nó trong phạm vi này.

## 2. Nguyên tắc "ưu tiên lệnh user hơn auto"

Cơ chế này **đã có sẵn ở firmware**: khi nhận `{"pump":...}` hoặc `{"light":...}`, thiết bị tự chuyển
`mode = "manual"` và giữ nguyên (bỏ qua vòng auto theo ngưỡng) cho tới khi nhận `{"mode":"auto"}`
(xem `esp32-mqtt-client.ino`, khối `onControlMessage`). Vì vậy:

- Backend **không cần lưu thêm state** cho "manual override".
- `mode` (`auto`/`manual`) hiện đã có trong telemetry `xmini/sensor_data` → App đọc `GET /api/sensordata/latest/{deviceId}` để biết thiết bị đang auto hay đang bị người dùng chiếm quyền.
- Endpoint `/auto` (mục A) là cách tường minh để App trả quyền lại cho auto.

## 3. Phần A — Endpoint điều khiển chuyên dụng (`ControlController`)

Thêm 3 action mỏng, tái dùng `PublishAndLogAsync(...)` (đã có) để publish `xmini/control` + ghi log lệnh.
Vẫn `[Authorize]` và kiểm tra `GetAccessibleDeviceAsync(deviceId, UserId)` như các action hiện có.

| Endpoint | Body | Publish xuống `xmini/control` | Ghi chú |
|---|---|---|---|
| `POST /api/control/{deviceId}/water` | `{ "on": true }` | `{ "pump": true }` | Firmware → MANUAL. Tự tắt theo `pump_max_run_s`. |
| `POST /api/control/{deviceId}/light` | `{ "on": true }` **hoặc** `{ "pwm": 0..255 }` | `{ "light": true }` / `{ "light_pwm": n }` | `pwm` ưu tiên nếu có mặt. Firmware → MANUAL (với `light`). |
| `POST /api/control/{deviceId}/auto` | *(rỗng)* | `{ "mode": "auto" }` | Trả thiết bị về tự động. |

Chi tiết body:

- **`/water`** — `WaterRequest { bool On }`. `on=true` → `{"pump":true}`, `on=false` → `{"pump":false}`.
- **`/light`** — `LightRequest { bool? On; int? Pwm }`.
  - Nếu `Pwm` có mặt: clamp `0..255`, publish `{"light_pwm":n}` (giữ hành vi hiện tại: `light_pwm` không đổi mode).
  - Ngược lại nếu `On` có mặt: publish `{"light":on}`.
  - Cả hai đều thiếu → `400`.
- **`/auto`** — không body; publish `{"mode":"auto"}`.

Không thêm "tưới N giây" trong v1: firmware không nhận thời lượng một-lần trong hợp đồng phẳng; thời lượng
do ngưỡng `pump_max_run_s` + auto-off an toàn quyết định (có thể chỉnh qua `PUT /api/control/{deviceId}/config`).

## 4. Phần B — Proxy trợ lý AI (`AssistantController` mới + `AiServerClient`)

### 4.1 `AiServerClient` (typed HttpClient, `Services/AiServerClient.cs`)

- Đăng ký qua `builder.Services.AddHttpClient<AiServerClient>(...)`; `BaseAddress` = URL AI server;
  `Timeout` ≈ 120s (LLM chậm).
- Hai phương thức:
  - `ChatAsync(userId, sessionId, message)` → gọi `POST {AI}/chat` với `{userId, sessionId, message}`.
  - `ConfirmAsync(userId, sessionId, actionId, approved)` → gọi `POST {AI}/chat/confirm`.
- Trả về `AiChatResult { string Reply; JsonElement? PendingAction }` (giữ nguyên `pendingAction` dạng
  `{id, summary, tool, args}` để relay nguyên vẹn cho App).
- Nếu AI trả `503` (`not_configured`) hoặc không kết nối được → ném exception có phân loại để controller
  map thành `503` với message tiếng Việt rõ ràng.

### 4.2 `AssistantController` (`[Authorize]`)

`userId` LUÔN lấy từ JWT (`ClaimTypes.NameIdentifier`) — App không tự khai. `sessionId` do App quản lý
để hỗ trợ multi-turn; thiếu thì mặc định `= userId`. Response luôn trả kèm `sessionId` để App dùng lại.

- **`POST /api/assistant/chat`**
  - Body: `AssistantChatRequest { string Message; string? SessionId }` *(shape đơn giản)*.
  - `400` nếu `Message` rỗng.
  - Gọi `AiServerClient.ChatAsync(userId, sessionId, message)`.
  - Response `200`: `{ reply, pendingAction, sessionId }`.
    - `pendingAction != null` → App hiện nút **Có/Không**, lấy `pendingAction.id` để gọi confirm.
- **`POST /api/assistant/confirm`**
  - Body: `AssistantConfirmRequest { string SessionId; string ActionId; bool Approved }`.
  - Gọi `AiServerClient.ConfirmAsync(userId, sessionId, actionId, approved)`.
  - Response `200`: `{ reply, pendingAction, sessionId }` (thường `pendingAction=null`).

Ghi chú: `history?` trong ý tưởng ban đầu **không cần** — AI server tự nhớ hội thoại theo `sessionId`
(stateful). App chỉ cần gửi `message` + giữ `sessionId`.

**Ngoài phạm vi v1:** streaming (`/chat/stream`, SSE). Có thể thêm `POST /api/assistant/chat/stream`
relay SSE ở lần sau.

## 5. Phần C — Dev-login lấy bearer (`AuthController`)

- **`POST /api/auth/dev-token`** — **chỉ hoạt động khi `IWebHostEnvironment.IsDevelopment()`**;
  môi trường khác trả `404` (ẩn hoàn toàn).
- Hành vi: tìm user `dev@plant-tree.local`; nếu chưa có thì tạo (role `User`, password hash cố định
  dùng nội bộ), rồi trả `AuthResponse { token, email, displayName, role }` như `/login`.
- Mục đích: dán token vào `Authorization: Bearer ...` để test nhanh bằng curl/Swagger, không cần đăng ký.

Bổ sung nhỏ ở OpenAPI: khai báo **bearer security scheme** vào tài liệu (document transformer của
`AddOpenApi`) để công cụ (Swagger UI/Scalar) hiện nút "Authorize".

## 6. Cấu hình

- `appsettings.json`: thêm
  ```json
  "AiServer": { "BaseUrl": "http://localhost:8787" }
  ```
- `Program.cs`: đọc `AI_SERVER_URL` (env) **ưu tiên**, fallback `AiServer:BaseUrl` (giống pattern `JWT_SECRET`).
  Đăng ký `AddHttpClient<AiServerClient>`. Đăng ký OpenAPI bearer scheme.
- `appsettings.Production.json`: đặt `AiServer:BaseUrl` trỏ tới service AI trong mạng nội bộ (vd
  `http://ai-server:8787` khi chạy Docker) — hoặc đặt qua env `AI_SERVER_URL`.

## 7. Demo dashboard (`demo-dashboard.html`)

- Trong khu điều khiển thiết bị: thêm nút **Tưới (bật/tắt)**, **Đèn (bật/tắt + slider PWM)**, **Về Auto** →
  gọi `/api/control/{id}/water|light|auto` bằng token đang đăng nhập.
- Thêm **ô chat trợ lý**: ô nhập + nút gửi → `POST /api/assistant/chat`; nếu response có `pendingAction`
  thì hiện **Có/Không** → gọi `/api/assistant/confirm`. Giữ `sessionId` trong biến trang.

## 8. Xử lý lỗi (tổng hợp)

| Tình huống | Kết quả |
|---|---|
| Device không thuộc quyền user | `404` (như hiện tại) |
| Body `/light` thiếu cả `on` lẫn `pwm` | `400` |
| MQTT publisher chưa kết nối | `503` (đã có trong `PublishAndLogAsync`) |
| AI server chưa cấu hình URL / không kết nối | `503` + message tiếng Việt |
| AI server trả `503 not_configured` | relay `503` (kèm gợi ý `/setup`) |
| `/api/auth/dev-token` ở Production | `404` |

## 9. Kiểm thử / xác minh

- **Firmware/MCP không đổi** → không có test mới ở đó.
- **.NET** (chưa có test project): mở rộng `smoke-test-auth.ps1` (hoặc script smoke mới) phủ:
  dev-token → `/water` → `/light` (on & pwm) → `/auto` → `/assistant/chat` (khi AI server chạy)
  → `/assistant/confirm`. Kiểm tra status code + body chính.
- **End-to-end**: dùng demo-dashboard bấm Tưới/Đèn/Auto và chat để quan sát lệnh publish + phản hồi AI
  (dùng skill `verify`).
- **AiServerClient**: xác minh gọi đúng path (`/chat`, `/chat/confirm`) + relay `pendingAction` nguyên vẹn;
  test đường lỗi 503 khi AI server tắt.

## 10. Ngoài phạm vi

- Streaming SSE cho assistant.
- Truyền JWT của user xuống MCP (giữ service-account như hiện tại).
- Tưới theo thời lượng một-lần ở firmware.
- Sửa AI server / MCP / firmware.

## 11. File dự kiến đụng tới

Mới:
- `PlantTreeIoTServer/Controllers/AssistantController.cs`
- `PlantTreeIoTServer/Services/AiServerClient.cs`

Sửa:
- `PlantTreeIoTServer/Controllers/ControlController.cs` (thêm `/water`, `/light`, `/auto` + DTO)
- `PlantTreeIoTServer/Controllers/AuthController.cs` (thêm `/dev-token`)
- `PlantTreeIoTServer/Program.cs` (AiServer config, AddHttpClient, OpenAPI bearer)
- `PlantTreeIoTServer/appsettings.json` (+ `appsettings.Production.json` nếu cần)
- `demo-dashboard.html` (nút điều khiển + ô chat)
- `API-GUIDE.md` (tài liệu endpoint mới)
- `smoke-test-auth.ps1` (mở rộng smoke)
