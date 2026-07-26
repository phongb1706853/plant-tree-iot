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

## 4. Phần B — Proxy trợ lý AI (`AssistantController` + `AiServerClient`)

> **Cập nhật 2026-07-22:** AI server (tree-grow-helper) đã thay `/chat` + `/chat/confirm` (stateful theo
> `sessionId`) bằng **một** endpoint OpenAI-compatible **STATELESS** `POST /v1/chat/completions`. Mục 4
> dưới đây phản ánh thiết kế mới; phần lịch sử (sessionId/pendingAction/confirm) không còn dùng.

**Nguyên tắc mới:** App giữ toàn bộ `messages[]` và gửi lại mỗi lượt. .NET là proxy **mỏng, stateless** —
không lưu hội thoại, không còn bước `/confirm` riêng. Xác nhận điều khiển: AI trả assistant message kèm
`tool_calls` + câu hỏi "(Có/Không)"; App **giữ nguyên** assistant message đó trong `messages[]` rồi thêm
câu trả lời user (`"có"`/`"không"`) ở lượt sau — AI thực thi hoặc huỷ.

### 4.1 `AiServerClient` (typed HttpClient, `Services/AiServerClient.cs`)

- Đăng ký qua `builder.Services.AddHttpClient<AiServerClient>(...)`; `BaseAddress` = URL AI server;
  `Timeout` ≈ 120s (LLM chậm). *(Không đổi.)*
- Một phương thức: `ChatCompletionsAsync(string userId, JsonObject body, ct) → JsonNode`.
  - Nhận request OpenAI thô của App, rồi **ép server-side** (ghi đè giá trị App gửi):
    - `user = userId` (từ JWT — không tin App) để AI scope "thiết bị của bạn".
    - `stream = false` (v1 chưa relay SSE).
    - `model = "plant-assistant"` nếu App không gửi (passthrough nếu có).
  - `POST {AI}/v1/chat/completions`; trả **nguyên response JSON** (`choices[]`, `usage`, …) để relay.
- Nếu AI trả `503` (`not_configured`) hoặc không kết nối được → ném exception có phân loại để controller
  map thành `503` với message tiếng Việt rõ ràng; lỗi khác → `502`; parse lỗi → `502` (BadGateway).

### 4.2 `AssistantController` (`[Authorize]`)

`userId` LUÔN lấy từ JWT (`ClaimTypes.NameIdentifier`) — App không tự khai, .NET ép vào trường `user`.

- **`POST /api/assistant/v1/chat/completions`** (đường dẫn mirror OpenAI để App có thể trỏ SDK vào
  `{host}/api/assistant/v1`).
  - Body: request OpenAI thô, bind `[FromBody] JsonNode` — `{ model?, messages: [...] }`.
  - `400` nếu body không phải JSON object, hoặc `messages` thiếu/rỗng.
  - Gọi `AiServerClient.ChatCompletionsAsync(userId, body)`.
  - Response `200`: **nguyên** JSON từ AI server (`{ choices: [ { message: { content, tool_calls? } } ], usage, … }`).
    - `tool_calls` xuất hiện → App hiện **Có/Không**, giữ lại assistant message để gửi ở lượt xác nhận.

**Ngoài phạm vi v1:** streaming (relay SSE khi `stream:true`). Có thể thêm sau.

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
- Thêm **ô chat trợ lý**: ô nhập + nút gửi → `POST /api/assistant/v1/chat/completions`, giữ `messages[]`
  trong biến trang. Nếu `choices[].message` có `tool_calls` thì hiện **Có/Không** → append assistant message
  đó + user `"có"`/`"không"` vào `messages[]` rồi gọi lại cùng endpoint. *(Cập nhật 2026-07-22.)*

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
- **.NET** (chưa có test project): mở rộng smoke script phủ:
  dev-token → `/water` → `/light` (on & pwm) → `/auto` → `/assistant/v1/chat/completions` (khi AI server chạy).
  Kiểm tra status code + body chính.
- **End-to-end**: dùng demo-dashboard bấm Tưới/Đèn/Auto và chat để quan sát lệnh publish + phản hồi AI
  (dùng skill `verify`).
- **AiServerClient**: xác minh gọi đúng path (`/v1/chat/completions`), ép `user`/`stream`/`model` đúng,
  relay response nguyên vẹn; test đường lỗi 503 khi AI server tắt.

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
