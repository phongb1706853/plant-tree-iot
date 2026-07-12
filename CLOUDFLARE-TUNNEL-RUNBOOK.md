# Cloudflare Tunnel — Runbook khôi phục (Plant Tree IoT)

> Hướng dẫn dựng lại **domain cố định `https://api.windy-dev.site`** khi bị mất/xóa/hỏng.
> Deploy trên **Mac Mini** (user `phamthinh`). Liên quan: [AUTH-INTEGRATION-GUIDE.md](AUTH-INTEGRATION-GUIDE.md) · [DEMO-NOTES.md](DEMO-NOTES.md)

## Kiến trúc

```
Internet → https://api.windy-dev.site → [Cloudflare edge, Proxied 🟠]
        → tunnel "planttree" (cloudflared chạy nền trên Mac)
        → http://localhost:8080  (Docker: planttree-iot-server-1, map 8080→8000)
        → .NET API (auth JWT + device token)
```

## Thông số quan trọng (ghi lại để khôi phục)

| Mục | Giá trị |
|---|---|
| Cloudflare account | `learnbestplaybest@gmail.com` |
| Domain (zone) | `windy-dev.site` → subdomain `api.windy-dev.site` |
| Nameservers Cloudflare | `christina.ns.cloudflare.com`, `clayton.ns.cloudflare.com` |
| Tunnel name | `planttree` |
| Tunnel ID (UUID) | `5f01dca9-c0cb-4639-b094-872d414ff6ff` |
| CNAME target (record `api`) | `5f01dca9-c0cb-4639-b094-872d414ff6ff.cfargotunnel.com` |
| Proxy status record `api` | **Proxied (mây CAM)** — BẮT BUỘC |
| cloudflared binary | `/Users/phamthinh/.local/bin/cloudflared` |
| Config tunnel | `~/.cloudflared/planttree.yml` |
| Credentials tunnel | `~/.cloudflared/5f01dca9-c0cb-4639-b094-872d414ff6ff.json` |
| LaunchAgent (chạy nền) | `~/Library/LaunchAgents/com.planttree.cloudflared.plist` |
| Server | `docker compose -f docker-compose.deploy.yml` (port host 8080 → container 8000) |

> ⚠️ **KHÔNG đụng** tunnel `sdl-internal` (ID `5dbb4bfb-...`, file `~/.cloudflared/config.yml`, hostname `sdlinternal.learnbestplaybest.com` → localhost:3001). Đó là project KHÁC, cùng account nhưng độc lập.

---

## Kịch bản khôi phục

### A. Zone `windy-dev.site` bị xóa / mất domain
1. Cloudflare (đúng account `learnbestplaybest`) → **Websites/Domains → Add domain** → `windy-dev.site` → gói **Free**.
2. Cloudflare hiện 2 nameserver → phải là **christina/clayton** (cặp của account này). Vào **registrar** (nơi mua domain) set nameservers = đúng 2 cái đó.
3. Đợi zone chuyển **Active** (Overview → **"Check nameservers now"**). Kiểm lan truyền:
   ```bash
   dig NS windy-dev.site +short @1.1.1.1
   ```
   → ra `christina.ns.cloudflare.com` + `clayton.ns.cloudflare.com` là xong.
4. Tạo lại record `api` (2 cách):
   - **Cách GUI (chắc ăn):** DNS → **Add record** → Type **CNAME**, Name **`api`**, Target **`5f01dca9-c0cb-4639-b094-872d414ff6ff.cfargotunnel.com`**, Proxy **🟠 Proxied**.
   - **Cách CLI:**
     ```bash
     cloudflared tunnel --config ~/.cloudflared/planttree.yml route dns planttree api.windy-dev.site
     ```
     ⚠️ Sau đó **kiểm lại** record `api` phải trỏ tunnel `planttree` (5f01dca9) và **mây CAM** — CLI đôi khi lấy nhầm tunnel từ `config.yml` mặc định.
5. Verify (mục cuối).

### B. Tunnel không chạy (`curl` timeout, hoặc dig ra `cfargotunnel` mà vẫn lỗi)
1. Kiểm tiến trình:
   ```bash
   pgrep -fl cloudflared
   ```
   → phải có 1 dòng `...planttree`.
2. Không có → khởi động lại LaunchAgent:
   ```bash
   launchctl unload ~/Library/LaunchAgents/com.planttree.cloudflared.plist
   launchctl load ~/Library/LaunchAgents/com.planttree.cloudflared.plist
   ```
3. Nếu file config/credentials/agent bị mất → tạo lại (mục "Tạo lại file" bên dưới).

### C. Server .NET không chạy / crash `JWT_SECRET`
Lỗi `JWT secret chưa cấu hình hoặc < 32 ký tự` = chạy **KHÔNG qua compose** (thiếu `.env`).
```bash
cd ~/plant-tree-iot
docker compose -f docker-compose.deploy.yml up -d
```
- **LUÔN** dùng lệnh compose trên. **ĐỪNG** `docker run` / bấm **Run ▶** trong Docker Desktop (thiếu env → crash).
- `.env` (cạnh compose) phải có `JWT_SECRET` ≥ 32 ký tự:
  ```bash
  cat ~/plant-tree-iot/.env
  ```
  Thiếu thì:
  ```bash
  cd ~/plant-tree-iot && echo "JWT_SECRET=$(openssl rand -base64 48)" >> .env && docker compose -f docker-compose.deploy.yml up -d
  ```

### D. Record `api` sai tunnel hoặc để "DNS only" (mây xám)
- Dashboard → windy-dev.site → **DNS → Records** → record `api`:
  - Content/Tunnel = **planttree** (`5f01dca9-...`), KHÔNG phải `5dbb4bfb` (sdl-internal).
  - Proxy status = **🟠 Proxied** (mây xám → domain KHÔNG kết nối được, `port 443 failed`).

### E. Zone "pending" (chưa Active dù đã add)
- Cảnh báo vàng "pending until... verify ownership" = Cloudflare chưa verify nameserver.
- Vào **Overview → "Check nameservers now"** → đợi. Đảm bảo registrar trỏ đúng christina/clayton.

---

## Tạo lại file (nếu mất)

**`~/.cloudflared/planttree.yml`:**
```yaml
tunnel: 5f01dca9-c0cb-4639-b094-872d414ff6ff
credentials-file: /Users/phamthinh/.cloudflared/5f01dca9-c0cb-4639-b094-872d414ff6ff.json
ingress:
  - hostname: api.windy-dev.site
    service: http://localhost:8080
  - service: http_status:404
```
> Nếu mất luôn file credentials `.json` → tunnel không dùng lại được → phải tạo tunnel MỚI: `cloudflared tunnel create planttree2` (cert.pem còn hợp lệ thì khỏi login), rồi cập nhật UUID trong config + record `api` + plist.

**`~/Library/LaunchAgents/com.planttree.cloudflared.plist`** (chạy nền, không cần sudo):
```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>com.planttree.cloudflared</string>
  <key>ProgramArguments</key>
  <array>
    <string>/Users/phamthinh/.local/bin/cloudflared</string>
    <string>tunnel</string>
    <string>--config</string>
    <string>/Users/phamthinh/.cloudflared/planttree.yml</string>
    <string>run</string>
    <string>planttree</string>
  </array>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><true/>
  <key>StandardOutPath</key><string>/tmp/planttree-cloudflared.log</string>
  <key>StandardErrorPath</key><string>/tmp/planttree-cloudflared.err.log</string>
</dict>
</plist>
```
Nạp: `launchctl load ~/Library/LaunchAgents/com.planttree.cloudflared.plist`

---

## Verify nhanh (chạy sau mỗi lần khôi phục)

```bash
dig api.windy-dev.site +short
```
→ Ra **IP Cloudflare** (`104.x` / `172.67.x`). Nếu ra `...cfargotunnel.com` = record đang **mây xám** (fix mục D).

```bash
curl -i --max-time 10 http://localhost:8080/api/devices
```
→ `401` = server local OK.

```bash
curl -i --max-time 10 https://api.windy-dev.site/api/devices
```
→ `401` = **toàn bộ chuỗi domain → tunnel → server chạy OK** ✅.

```bash
pgrep -fl cloudflared
```
→ Đúng 2 dòng: `planttree` (của mình) + `sdl-internal` (project khác, giữ nguyên).

---

## ⚠️ Nguyên tắc an toàn (KHÔNG làm hỏng project khác)
1. plant-tree dùng file **riêng** `planttree.yml`, tunnel **riêng** `planttree`, LaunchAgent label **riêng** `com.planttree.cloudflared`.
2. **KHÔNG** sửa `~/.cloudflared/config.yml` (của sdl-internal).
3. **KHÔNG** xóa tunnel `sdl-internal` (5dbb4bfb).
4. **KHÔNG** chạy `cloudflared service install` (sẽ đè service sdl-internal).
5. Chỉ sửa DNS trong zone **windy-dev.site**, không đụng `learnbestplaybest.com`.

## Lỗi đã gặp + cách tránh (kinh nghiệm)
| Lỗi | Nguyên nhân | Tránh |
|---|---|---|
| `grep: #: No such file` / `curl: option -` | zsh KHÔNG coi `#` là comment | Đừng dán chú thích `# ...` chung dòng lệnh |
| route dns trỏ nhầm `5dbb4bfb` | cloudflared đọc `config.yml` mặc định | Dùng `--config planttree.yml`, và kiểm lại record |
| `Failed to fetch` / `port 443 failed` | record `api` để mây xám (DNS only) | Đặt **Proxied (mây cam)** |
| Zone không thấy trong account | 2 account lẫn lộn / bị xóa | Zone + tunnel phải **CÙNG account** (`learnbestplaybest`) |
| Container crash `JWT secret...` | chạy `docker run` / nút Run, thiếu `.env` | LUÔN `docker compose ... up -d` |
| Quick Tunnel URL đổi mỗi lần | dùng `cloudflared tunnel --url` | Dùng named tunnel + domain (runbook này) |

## Phương án dự phòng (nếu domain hỏng, cần chạy GẤP)
Quick Tunnel cho URL công khai tức thì (URL random, tạm):
```bash
cloudflared tunnel --url http://localhost:8080
```
