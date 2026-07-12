# Chạy server bằng Docker trên máy Mac (pull → run)

Thay cho `git pull` + `dotnet run`. Trên Mac **không cần source code**, chỉ cần Docker Desktop + 1 file compose.

## Cách hoạt động

Mỗi lần push lên `main`, GitHub Actions ([`.github/workflows/docker-publish.yml`](.github/workflows/docker-publish.yml)) tự build image **multi-arch (Intel + Apple Silicon)** và push lên GHCR:

```
ghcr.io/phongb1706853/plant-tree-iot:latest
```

Trên Mac chỉ việc **pull image mới nhất rồi run**. MongoDB chạy kèm trong stack (dữ liệu lưu ở volume `mongodb_data`).

## Chuẩn bị 1 lần

1. **Cho phép pull image không cần login** — mở https://github.com/phongb1706853/plant-tree-iot → tab **Packages** → chọn package `plant-tree-iot` → **Package settings** → **Change visibility → Public**.
   - (Hoặc nếu muốn để private: trên Mac chạy `docker login ghcr.io` với GitHub username + Personal Access Token có scope `read:packages`.)
2. Cài **Docker Desktop** trên Mac.
3. Copy file [`docker-compose.deploy.yml`](docker-compose.deploy.yml) về Mac (đặt ở đâu cũng được, ví dụ `~/planttree/`).
4. Tạo file `.env` cùng thư mục với compose, đặt `JWT_SECRET` (**BẮT BUỘC**, ≥ 32 ký tự):
   ```bash
   echo "JWT_SECRET=$(openssl rand -base64 48)" > .env
   ```

## Chạy

```bash
cd ~/planttree
docker compose -f docker-compose.deploy.yml up -d
```

- Lần đầu tự pull image server + `mongo:7.0`.
- API: **http://localhost:8080**
- Xem log: `docker compose -f docker-compose.deploy.yml logs -f server`
  → thấy `MQTT Publisher connected...` và `Connected to MQTT broker...` là OK.

### Trong Docker Desktop (giao diện)

Sau lần `up` đầu, stack **planttree-iot** hiện trong tab **Containers** — bấm ▶/⏹ để chạy/dừng. Muốn lấy bản mới: bấm **pull** ở image `ghcr.io/phongb1706853/plant-tree-iot` trong tab **Images** rồi restart stack.

## Cập nhật lên bản mới nhất

```bash
docker compose -f docker-compose.deploy.yml pull
docker compose -f docker-compose.deploy.yml up -d
```

## Cấu hình

`JWT_SECRET` là **BẮT BUỘC** — auth đã có sẵn trong `main` và compose fail-closed: thiếu `JWT_SECRET` thì `up` báo lỗi ngay, server không khởi động. Config MQTT (HiveMQ) đã nhúng default trong compose, chỉ ghi đè khi cần. File `.env` cùng thư mục với compose:

```dotenv
# BẮT BUỘC — chuỗi ngẫu nhiên >= 32 ký tự:  openssl rand -base64 48
JWT_SECRET=...

# Tùy chọn — ghi đè MQTT (HiveMQ) nếu cần
MQTT_BROKER=...
MQTT_USERNAME=...
MQTT_PASSWORD=...
MQTT_ALLOW_INVALID_CERT=true
```
