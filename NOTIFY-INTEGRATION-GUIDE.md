# Hướng dẫn tích hợp Notify — Plant Tree IoT

> Tài liệu này là **hợp đồng chung** giữa **server .NET (Phong)** và **team Notify** cho luồng thông báo:
> server .NET phát hiện sự kiện của cây → **bắn thông số thô** sang Notify → Notify **tự format** câu chữ/UI
> rồi push lên màn hình kiosk / app (qua Firebase).
>
> **Điểm mấu chốt (khác doc Notify cũ):** trước đây Notify là *pass-through* (Phong gửi sẵn `title`/`body`,
> Notify chỉ đẩy). **Nay format chuyển sang Notify** — Phong gửi `event` + `data`, Notify giữ 1 lớp
> **template** map `event → câu chữ`. Đây là phần Notify cần build thêm.
>
> Liên quan: [AUTH-INTEGRATION-GUIDE.md](AUTH-INTEGRATION-GUIDE.md) · [API-GUIDE.md](API-GUIDE.md) · [MqttBackgroundService.cs](PlantTreeIoTServer/Services/MqttBackgroundService.cs)

---

## 🗺️ Sơ đồ luồng — ai làm gì

```
[ESP32 Xmini] ──telemetry (MQTT xmini/sensor_data ~10s)──► [.NET server]
                                                              │  phát hiện sự kiện (bắt edge,
                                                              │  so ngưỡng, giữ trạng thái)
                                                              ▼
                                    POST {NOTIFY_URL}/internal/notify   (header x-api-key)
                                    body = { deviceId, event, severity, id, data{...} }
                                                              │
                                                              ▼
                                              [Notify service]  ── map event → template ──►
                                              lưu lịch sử (Mongo) + push Firebase
                                                              │
                                                              ▼
                                         [Kiosk web cạnh cây] / [App]  (theo đúng deviceId)
```

**Ranh giới trách nhiệm (đừng lẫn):**

| Việc | Ai làm | Vì sao |
|---|---|---|
| Quyết định **KHI NÀO** báo (bắt edge, so ngưỡng, cooldown, giữ trạng thái) | **.NET (Phong)** | Ngưỡng + logic + state nằm ở server; Notify không có dữ liệu này |
| Gửi **mã sự kiện + thông số** (`event` + `data` + `severity`) | **.NET (Phong)** | Nguồn số liệu + hiểu nghiệp vụ |
| **Câu chữ / UI / icon / dịch ngôn ngữ** (map `event → title/body`) | **Notify** | Sở hữu trải nghiệm hiển thị trên app |

> ⚠️ *Phát hiện* sự kiện **luôn** ở .NET. Notify **không** so ngưỡng, **không** tự quyết định lúc nào bắn —
> chỉ nhận event và hiển thị.

---

## 1. Transport (đã chốt)

- **Cách:** webhook HTTP — **.NET POST thẳng vào URL endpoint của Notify** mỗi khi có sự kiện. Realtime, không polling, không MCP.
- **Endpoint:** `POST {NOTIFY_URL}/internal/notify`
- **Xác thực:** header `x-api-key: <INTERNAL_API_KEY>`
- **Content-Type:** `application/json`
- **Phản hồi mong đợi:** `202 { success, deviceId, recipients }` — `recipients: 0` = không màn hình nào khớp `deviceId` (thường do sai `deviceId`).

### Cần Notify cung cấp
1. `NOTIFY_URL` đầy đủ (prod)
2. Tên header + giá trị API key (mặc định `x-api-key`)
3. Xác nhận đã build **lớp template** (mục 3) — vì đây là điểm khác doc cũ

---

## 2. Payload — dạng "sự kiện có cấu trúc"

```json
{
  "deviceId": "ESP32S3_Zone1",
  "event": "water.empty",
  "severity": "critical",
  "occurredAt": "2026-07-26T09:15:00Z",
  "id": "ESP32S3_Zone1:water.empty:1",
  "data": {
    "soilPercent": 18,
    "waterOk": false,
    "batteryPercent": 64
  }
}
```

| Field | Bắt buộc | Ý nghĩa |
|---|---|---|
| `deviceId` | ✅ | Định danh cây/kiosk — **dùng chung `device_id` của MQTT** |
| `event` | ✅ | Mã sự kiện cố định (máy đọc) — xem từ điển mục 4 |
| `severity` | ✅ | `info` · `warning` · `critical` — .NET set (Notify đọc để tô màu/âm thanh) |
| `id` | ✅ | Chống trùng khi retry — ổn định theo từng **đợt** sự kiện |
| `occurredAt` | nên có | Thời điểm phát hiện (ISO-8601 UTC) |
| `data` | tùy event | Thông số thô để Notify ghép câu (xem từng event) |
| `body` | tùy chọn | Câu fallback — nếu Notify **chưa có template** cho `event` thì hiện tạm, khỏi tin trống |

> Quy ước tên field trong `data`: camelCase (`soilPercent`, `batteryPercent`...). Có thể chốt lại nếu Notify muốn khác.

---

## 3. Phía Notify: lớp template (phần cần build)

Notify giữ 1 bảng map `event → { title, body }`, chèn số từ `data`:

```
water.empty  → title "Hết nước tưới"  body "Bình chứa cạn (đất {soilPercent}%) — cần châm nước"
battery.low  → title "Pin yếu"        body "Pin còn {batteryPercent}%, cần sạc sớm"
pump.on      → title "Đang tưới cây"  body "Đất {soilPercent}% dưới ngưỡng {soilOnPct}% → bơm đang chạy"
```

- Template + wording + icon + i18n là **của Notify** — sửa chữ không cần đụng server .NET.
- Nếu thiếu template cho 1 `event`: fallback về `body` (nếu có), hoặc hiện `event` thô + log để bổ sung.

---

## 4. Từ điển sự kiện (hợp đồng chung — cần 2 bên chốt)

Điều kiện **"edge"** = chỉ bắn khi trạng thái *chuyển*, không lặp mỗi 10s. Tên field trong "Kích hoạt" là field telemetry thật (`xmini/sensor_data`). Cột "Template gợi ý" là **bản nháp cho Notify** (Notify được sửa).

### A. Nước & tưới

| `event` | Kích hoạt (edge) | `data` | severity | Template gợi ý (title — body) |
|---|---|---|---|---|
| `pump.on` | `pump_on` 0→1 | `soilPercent`, `soilOnPct` | info | Đang tưới cây — Đất {soilPercent}% dưới ngưỡng {soilOnPct}%, bơm đang chạy |
| `pump.off` | `pump_on` 1→0 | `soilPercent` | info | Đã tưới xong — Độ ẩm đất đạt {soilPercent}% |
| `water.needed` | `soil_percent` < ngưỡng **và** (server ở `manual` **hoặc** `water_ok`=false) | `soilPercent`, `mode`, `waterOk` | warning | Cây thiếu nước — Đất {soilPercent}% cần tưới nhưng đang chế độ tay / hết nước |
| `water.empty` | `water_ok` true→false | `soilPercent`, `waterOk` | critical | Hết nước tưới — Bình chứa cạn, không thể tưới, cần châm nước |

### B. Pin

| `event` | Kích hoạt (edge) | `data` | severity | Template gợi ý |
|---|---|---|---|---|
| `battery.low` | `low_batt` 0→1 | `batteryPercent` | warning | Pin yếu — Pin còn {batteryPercent}%, cần sạc sớm |
| `battery.cut` | `batt_cut` 0→1 | `batteryPercent`, `batteryVoltageV` | critical | Pin cạn — thiết bị tạm ngắt — Bơm/đèn tạm dừng đến khi sạc lại |
| `battery.full` *(tùy chọn)* | `batt_full` 0→1 | `batteryPercent` | info | Pin đã đầy — Có thể rút sạc |

### C. Môi trường

| `event` | Kích hoạt (edge + hysteresis) | `data` | severity | Template gợi ý |
|---|---|---|---|---|
| `temp.high` | `temperature` > **NGƯỠNG⚠️** (vd 38°C), hạ báo khi <35°C | `temperature` | warning | Nhiệt độ cao — {temperature}°C, cây có thể bị stress nhiệt |
| `temp.low` | `temperature` < **NGƯỠNG⚠️** (vd 8°C) | `temperature` | warning | Nhiệt độ thấp — {temperature}°C, coi chừng lạnh cây |
| `light.low` *(tùy chọn)* | `light_lux` thấp liên tục > X giờ | `lightLux` | info | Thiếu sáng — Nên bổ sung đèn / đưa ra chỗ sáng |

### D. Kết nối & sức khỏe

| `event` | Kích hoạt (edge) | `data` | severity | Template gợi ý |
|---|---|---|---|---|
| `device.offline` | không có telemetry > **N phút** (telemetry ~10s → N=3–5) | `lastSeenSecondsAgo` | warning | Mất kết nối thiết bị — Không nhận dữ liệu hơn {N} phút |
| `device.online` | telemetry trở lại sau khi mất | `offlineSeconds` | info | Thiết bị online lại |
| `sensor.error` | `battery_percent`=-1 hoặc `soil_percent` null kéo dài | `field` | warning | Cảm biến lỗi — Số đọc pin/đất không hợp lệ |

> ⚠️ Ngưỡng nhiệt độ/ánh sáng **chưa có trong code**, phụ thuộc loại cây → cần 2 bên chốt (lý tưởng: theo `plantType`).

---

## 5. Nguyên tắc kỹ thuật (.NET tuân thủ)

1. **Edge-triggered bắt buộc.** Telemetry ~10s/lần → chỉ bắn khi trạng thái *chuyển* (server giữ trạng thái cũ, như `_lastPumpOn` trong [MqttBackgroundService.cs](PlantTreeIoTServer/Services/MqttBackgroundService.cs)). Không gửi theo mức → tránh spam 6 tin/phút.
2. **Hysteresis cho ngưỡng analog** (nhiệt độ, ánh sáng): dùng 2 mức bật/tắt cách nhau (giống soil on=30 / off=60) để không báo rung quanh ngưỡng.
3. **`id` ổn định theo đợt** → retry mạng không tạo tin trùng (Notify chống trùng qua `eventId`).
4. **`deviceId` nhất quán** với `device_id` của MQTT.

---

## 6. Lộ trình triển khai

| Pha | Sự kiện | Ghi chú |
|---|---|---|
| **1 — làm ngay** | `water.empty` · `battery.cut` · `battery.low` · `device.offline` | Dùng cờ firmware sẵn có, không cần chốt ngưỡng mới |
| **1.5 — gắn kết** | `pump.on` · `pump.off` | Tin "đang tưới / tưới xong" cho người dùng thấy hoạt động |
| **2 — cần chốt ngưỡng** | `temp.high` · `temp.low` · `light.low` · `sensor.error` | Ngưỡng theo loại cây |

---

## 7. Việc cần chốt (checklist 2 bên)

- [ ] **Notify:** `NOTIFY_URL` prod + tên/giá trị API key
- [ ] **Notify:** xác nhận build lớp template `event → title/body` (mục 3)
- [ ] **Chung:** danh sách `event` + `data` fields (mục 4) — khóa lại làm hợp đồng
- [ ] **Chung:** ai set `severity` (đề xuất: .NET set) + severity mặc định mỗi event
- [ ] **Chung:** quy ước tên field trong `data` (camelCase?)
- [ ] **Chung:** ngưỡng `temp.high/low`, `light.low`, `device.offline` (N phút)

---

## 8. Ví dụ

**Hết nước (critical):**
```json
{ "deviceId": "ESP32S3_Zone1", "event": "water.empty", "severity": "critical",
  "occurredAt": "2026-07-26T09:15:00Z", "id": "ESP32S3_Zone1:water.empty:1",
  "data": { "soilPercent": 18, "waterOk": false } }
```

**Bắt đầu tưới (info):**
```json
{ "deviceId": "ESP32S3_Zone1", "event": "pump.on", "severity": "info",
  "id": "ESP32S3_Zone1:pump.on:1737012345",
  "data": { "soilPercent": 22, "soilOnPct": 30 } }
```

**Mất kết nối (warning):**
```json
{ "deviceId": "ESP32S3_Zone1", "event": "device.offline", "severity": "warning",
  "id": "ESP32S3_Zone1:device.offline:1",
  "data": { "lastSeenSecondsAgo": 210 } }
```

**Test nhanh bằng curl:**
```bash
curl -X POST {NOTIFY_URL}/internal/notify \
  -H "Content-Type: application/json" -H "x-api-key: <INTERNAL_API_KEY>" \
  -d '{"deviceId":"ESP32S3_Zone1","event":"water.empty","severity":"critical","id":"t1","data":{"soilPercent":18,"waterOk":false}}'
```
