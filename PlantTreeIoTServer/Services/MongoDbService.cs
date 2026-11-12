using MongoDB.Driver;
using PlantTreeIoTServer.Models;

namespace PlantTreeIoTServer.Services;

public class MongoDbService
{
    private readonly IMongoDatabase _database;

    public MongoDbService(IConfiguration configuration)
    {
        // MONGO_URL / MONGODB_URL (đặt trong docker-compose.deploy.yml) ghi đè connection string trong config
        var connectionString = Environment.GetEnvironmentVariable("MONGO_URL")
            ?? Environment.GetEnvironmentVariable("MONGODB_URL")
            ?? configuration.GetValue<string>("MongoDbSettings:ConnectionString")
            ?? "mongodb://localhost:27017";
        var databaseName = configuration.GetValue<string>("MongoDbSettings:DatabaseName") ?? "PlantTreeIoT";

        var client = new MongoClient(connectionString);
        _database = client.GetDatabase(databaseName);

        // Unique index cho email (best-effort — không crash startup nếu Mongo tạm chưa sẵn sàng;
        // tầng ứng dụng vẫn kiểm tra trùng email khi đăng ký)
        try
        {
            Users.Indexes.CreateOne(new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(u => u.Email),
                new CreateIndexOptions { Unique = true }));
        }
        catch { /* ignore */ }

        // Mỗi thiết bị chỉ giữ 1 bản cấu hình ngưỡng auto (upsert theo deviceId).
        try
        {
            DeviceConfigs.Indexes.CreateOne(new CreateIndexModel<DeviceConfig>(
                Builders<DeviceConfig>.IndexKeys.Ascending(c => c.DeviceId),
                new CreateIndexOptions { Unique = true }));
        }
        catch { /* ignore */ }

        // Mỗi thiết bị chỉ giữ 1 bản trạng thái xmini/status gần nhất (upsert theo deviceId).
        try
        {
            DeviceStatuses.Indexes.CreateOne(new CreateIndexModel<DeviceStatus>(
                Builders<DeviceStatus>.IndexKeys.Ascending(s => s.DeviceId),
                new CreateIndexOptions { Unique = true }));
        }
        catch { /* ignore */ }
    }

    // Collections
    public IMongoCollection<SensorData> SensorData => _database.GetCollection<SensorData>("SensorData");
    public IMongoCollection<Device> Devices => _database.GetCollection<Device>("Devices");
    public IMongoCollection<ControlCommand> ControlCommands => _database.GetCollection<ControlCommand>("ControlCommands");
    public IMongoCollection<DeviceConfig> DeviceConfigs => _database.GetCollection<DeviceConfig>("DeviceConfigs");
    public IMongoCollection<DeviceStatus> DeviceStatuses => _database.GetCollection<DeviceStatus>("DeviceStatuses");
    public IMongoCollection<User> Users => _database.GetCollection<User>("Users");

    // User Operations
    public async Task<User?> GetUserByEmailAsync(string email)
        => await Users.Find(u => u.Email == email.ToLowerInvariant()).FirstOrDefaultAsync();

    public async Task<User?> GetUserByIdAsync(string id)
        => await Users.Find(u => u.Id == id).FirstOrDefaultAsync();

    public async Task CreateUserAsync(User user)
        => await Users.InsertOneAsync(user);

    // Sensor Data Operations
    public async Task<List<SensorData>> GetSensorDataAsync(string deviceId, int limit = 100)
    {
        return await SensorData
            .Find(data => data.DeviceId == deviceId)
            .SortByDescending(data => data.Timestamp)
            .Limit(limit)
            .ToListAsync();
    }

    public async Task<SensorData?> GetLatestSensorDataAsync(string deviceId)
    {
        return await SensorData
            .Find(data => data.DeviceId == deviceId)
            .SortByDescending(data => data.Timestamp)
            .FirstOrDefaultAsync();
    }

    public async Task InsertSensorDataAsync(SensorData data)
    {
        await SensorData.InsertOneAsync(data);
    }

    // Device Operations
    public async Task<List<Device>> GetAllDevicesAsync()
    {
        return await Devices.Find(_ => true).ToListAsync();
    }

    public async Task<Device?> GetDeviceAsync(string deviceId)
    {
        return await Devices.Find(d => d.DeviceId == deviceId).FirstOrDefaultAsync();
    }

    public async Task CreateDeviceAsync(Device device)
    {
        await Devices.InsertOneAsync(device);
    }

    // Device ownership + sharing
    /// <summary>Device user có quyền: là owner HOẶC nằm trong Members (được chia sẻ).</summary>
    public async Task<List<Device>> GetDevicesForUserAsync(string userId)
        => await Devices.Find(d => d.OwnerId == userId || d.Members.Contains(userId)).ToListAsync();

    /// <summary>Chỉ trả về khi user là CHỦ SỞ HỮU (dùng cho xoá / chia sẻ / claim).</summary>
    public async Task<Device?> GetOwnedDeviceAsync(string deviceId, string ownerId)
        => await Devices.Find(d => d.DeviceId == deviceId && d.OwnerId == ownerId).FirstOrDefaultAsync();

    /// <summary>Trả về khi user là owner HOẶC member (dùng cho xem / điều khiển).</summary>
    public async Task<Device?> GetAccessibleDeviceAsync(string deviceId, string userId)
        => await Devices.Find(d => d.DeviceId == deviceId && (d.OwnerId == userId || d.Members.Contains(userId))).FirstOrDefaultAsync();

    public async Task AddDeviceMemberAsync(string deviceId, string userId)
        => await Devices.UpdateOneAsync(d => d.DeviceId == deviceId,
            Builders<Device>.Update.AddToSet(d => d.Members, userId));

    public async Task RemoveDeviceMemberAsync(string deviceId, string userId)
        => await Devices.UpdateOneAsync(d => d.DeviceId == deviceId,
            Builders<Device>.Update.Pull(d => d.Members, userId));

    public async Task SetDeviceOwnerAsync(string deviceId, string ownerId)
    {
        var update = Builders<Device>.Update.Set(d => d.OwnerId, ownerId);
        await Devices.UpdateOneAsync(d => d.DeviceId == deviceId, update);
    }

    // Xoá device kèm toàn bộ dữ liệu liên quan; trả về số bản ghi đã xoá mỗi loại.
    public async Task<(long SensorData, long DeviceConfigs, long Commands)> DeleteDeviceAndDataAsync(string deviceId)
    {
        await Devices.DeleteOneAsync(d => d.DeviceId == deviceId);
        var s = await SensorData.DeleteManyAsync(x => x.DeviceId == deviceId);
        var cfg = await DeviceConfigs.DeleteManyAsync(x => x.DeviceId == deviceId);
        var c = await ControlCommands.DeleteManyAsync(x => x.DeviceId == deviceId);
        await DeviceStatuses.DeleteManyAsync(x => x.DeviceId == deviceId);
        return (s.DeletedCount, cfg.DeletedCount, c.DeletedCount);
    }

    public async Task UpdateDeviceLastSeenAsync(string deviceId)
    {
        var update = Builders<Device>.Update
            .Set(d => d.LastSeen, DateTime.UtcNow);

        await Devices.UpdateOneAsync(d => d.DeviceId == deviceId, update);
    }

    // Cờ chế độ auto/manual do server làm chủ, lưu trên Device doc để bền qua redeploy.
    /// <summary>Đọc controlMode của thiết bị (null nếu chưa đặt hoặc thiết bị chưa đăng ký).</summary>
    public async Task<string?> GetDeviceControlModeAsync(string deviceId)
        => (await GetDeviceAsync(deviceId))?.ControlMode;

    /// <summary>Ghi controlMode (chỉ khi thiết bị đã đăng ký — không upsert; caller đã kiểm ownership).</summary>
    public async Task SetDeviceControlModeAsync(string deviceId, string mode)
        => await Devices.UpdateOneAsync(d => d.DeviceId == deviceId,
            Builders<Device>.Update.Set(d => d.ControlMode, mode));

    /// <summary>Thiết bị ảo: bật/tắt gửi CẢ sang Notify thật (xem Device.SimNotifyForward).</summary>
    public async Task SetSimNotifyForwardAsync(string deviceId, bool forward)
        => await Devices.UpdateOneAsync(d => d.DeviceId == deviceId,
            Builders<Device>.Update.Set(d => d.SimNotifyForward, forward));

    // Device Config (ngưỡng auto) Operations
    public async Task<DeviceConfig?> GetDeviceConfigAsync(string deviceId)
        => await DeviceConfigs.Find(c => c.DeviceId == deviceId).FirstOrDefaultAsync();

    /// <summary>
    /// Ghi/hợp nhất cấu hình của thiết bị. Chỉ set các trường KHÁC null (echo đầy đủ từ xmini/config ghi cả 25
    /// trường; thiết bị ảo chỉ gửi 15 khoá cũ). <paramref name="updateRejected"/> = bản tin LIVE: ghi đè thông tin
    /// từ chối (null = lần này không có khoá bị từ chối -> xoá). Bản retained thì giữ nguyên thông tin từ chối cũ.
    /// </summary>
    public async Task UpsertDeviceConfigAsync(DeviceConfig config, bool updateRejected = false)
    {
        var b = Builders<DeviceConfig>.Update;
        var updates = new List<UpdateDefinition<DeviceConfig>>
        {
            b.SetOnInsert(c => c.DeviceId, config.DeviceId),
            b.Set(c => c.UpdatedAt, config.UpdatedAt),
        };

        if (config.SoilOnPct.HasValue) updates.Add(b.Set(c => c.SoilOnPct, config.SoilOnPct));
        if (config.SoilOffPct.HasValue) updates.Add(b.Set(c => c.SoilOffPct, config.SoilOffPct));
        if (config.PumpDoseMs.HasValue) updates.Add(b.Set(c => c.PumpDoseMs, config.PumpDoseMs));
        if (config.SoakS.HasValue) updates.Add(b.Set(c => c.SoakS, config.SoakS));
        if (config.MaxDosesPerCycle.HasValue) updates.Add(b.Set(c => c.MaxDosesPerCycle, config.MaxDosesPerCycle));
        if (config.MaxDosesPerDay.HasValue) updates.Add(b.Set(c => c.MaxDosesPerDay, config.MaxDosesPerDay));
        if (config.SoilDry.HasValue) updates.Add(b.Set(c => c.SoilDry, config.SoilDry));
        if (config.SoilWet.HasValue) updates.Add(b.Set(c => c.SoilWet, config.SoilWet));
        if (config.LightAuto.HasValue) updates.Add(b.Set(c => c.LightAuto, config.LightAuto));
        if (config.LuxOn.HasValue) updates.Add(b.Set(c => c.LuxOn, config.LuxOn));
        if (config.LuxOff.HasValue) updates.Add(b.Set(c => c.LuxOff, config.LuxOff));
        if (config.LightAutoPwm.HasValue) updates.Add(b.Set(c => c.LightAutoPwm, config.LightAutoPwm));
        if (config.LightMinBattPct.HasValue) updates.Add(b.Set(c => c.LightMinBattPct, config.LightMinBattPct));
        if (config.LightMaxMinPerDay.HasValue) updates.Add(b.Set(c => c.LightMaxMinPerDay, config.LightMaxMinPerDay));
        if (config.BattWarnPct.HasValue) updates.Add(b.Set(c => c.BattWarnPct, config.BattWarnPct));
        if (config.BattRecoverPct.HasValue) updates.Add(b.Set(c => c.BattRecoverPct, config.BattRecoverPct));
        if (config.BattFullOnV.HasValue) updates.Add(b.Set(c => c.BattFullOnV, config.BattFullOnV));
        if (config.BattFullOffV.HasValue) updates.Add(b.Set(c => c.BattFullOffV, config.BattFullOffV));
        if (config.BattCritV.HasValue) updates.Add(b.Set(c => c.BattCritV, config.BattCritV));
        if (config.BattCritRecoverV.HasValue) updates.Add(b.Set(c => c.BattCritRecoverV, config.BattCritRecoverV));
        if (config.PowerMode != null) updates.Add(b.Set(c => c.PowerMode, config.PowerMode));
        if (config.WakeIntervalS.HasValue) updates.Add(b.Set(c => c.WakeIntervalS, config.WakeIntervalS));
        if (config.PluggedOnV.HasValue) updates.Add(b.Set(c => c.PluggedOnV, config.PluggedOnV));
        if (config.PluggedOffV.HasValue) updates.Add(b.Set(c => c.PluggedOffV, config.PluggedOffV));
        if (config.ScreenTimeoutS.HasValue) updates.Add(b.Set(c => c.ScreenTimeoutS, config.ScreenTimeoutS));
        if (config.PumpMaxRunS.HasValue) updates.Add(b.Set(c => c.PumpMaxRunS, config.PumpMaxRunS));
        if (config.PumpCooldownS.HasValue) updates.Add(b.Set(c => c.PumpCooldownS, config.PumpCooldownS));

        if (updateRejected)
        {
            updates.Add(b.Set(c => c.Rejected, config.Rejected));
            updates.Add(b.Set(c => c.RejectedCount, config.RejectedCount));
            updates.Add(b.Set(c => c.RejectedAt, config.RejectedAt));
        }

        await DeviceConfigs.UpdateOneAsync(
            c => c.DeviceId == config.DeviceId,
            b.Combine(updates),
            new UpdateOptions { IsUpsert = true });
    }

    // Device Status (xmini/status) Operations
    public async Task<DeviceStatus?> GetDeviceStatusAsync(string deviceId)
        => await DeviceStatuses.Find(s => s.DeviceId == deviceId).FirstOrDefaultAsync();

    /// <summary>
    /// Ghi trạng thái xmini/status gần nhất. LWT "offline" chỉ có state + device_id nên chỉ cập nhật state/giờ nhận
    /// (giữ fw, power_*... của bản tin trước). online/sleeping thì ghi đủ mọi trường (kể cả null: next_wake_s chỉ có
    /// khi sleeping, plugged_guess chỉ có khi power_mode = auto — không được giữ giá trị cũ).
    /// </summary>
    public async Task UpsertDeviceStatusAsync(DeviceStatus status)
    {
        var b = Builders<DeviceStatus>.Update;
        var updates = new List<UpdateDefinition<DeviceStatus>>
        {
            b.SetOnInsert(s => s.DeviceId, status.DeviceId),
            b.Set(s => s.State, status.State),
            b.Set(s => s.NextWakeS, status.NextWakeS),
            b.Set(s => s.Retained, status.Retained),
            b.Set(s => s.ReceivedAt, status.ReceivedAt),
        };

        if (status.State != ConnectionStateResolver.Offline)
        {
            updates.Add(b.Set(s => s.Fw, status.Fw));
            updates.Add(b.Set(s => s.PowerSource, status.PowerSource));
            updates.Add(b.Set(s => s.PowerMode, status.PowerMode));
            updates.Add(b.Set(s => s.PowerState, status.PowerState));
            updates.Add(b.Set(s => s.PluggedGuess, status.PluggedGuess));
            updates.Add(b.Set(s => s.UptimeS, status.UptimeS));
            updates.Add(b.Set(s => s.BootCount, status.BootCount));
            updates.Add(b.Set(s => s.ResetReason, status.ResetReason));
            updates.Add(b.Set(s => s.FreeHeap, status.FreeHeap));
            updates.Add(b.Set(s => s.MinFreeHeap, status.MinFreeHeap));
            updates.Add(b.Set(s => s.MaxAlloc, status.MaxAlloc));
            updates.Add(b.Set(s => s.Rssi, status.Rssi));
            updates.Add(b.Set(s => s.WifiFailCount, status.WifiFailCount));
            updates.Add(b.Set(s => s.WakeMs, status.WakeMs));
            updates.Add(b.Set(s => s.SafeMode, status.SafeMode));
        }

        await DeviceStatuses.UpdateOneAsync(
            s => s.DeviceId == status.DeviceId,
            b.Combine(updates),
            new UpdateOptions { IsUpsert = true });
    }

    // Control Command (nhật ký lệnh đã publish) Operations
    public async Task InsertControlCommandAsync(ControlCommand command)
    {
        await ControlCommands.InsertOneAsync(command);
    }

    /// <summary>Nhật ký các lệnh gần nhất BE đã publish xuống xmini/control (mới nhất trước).</summary>
    public async Task<List<ControlCommand>> GetRecentControlCommandsAsync(string deviceId, int limit = 50)
    {
        return await ControlCommands
            .Find(cmd => cmd.DeviceId == deviceId)
            .SortByDescending(cmd => cmd.CreatedAt)
            .Limit(limit)
            .ToListAsync();
    }
}
