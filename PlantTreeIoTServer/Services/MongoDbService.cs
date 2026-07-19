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
    }

    // Collections
    public IMongoCollection<SensorData> SensorData => _database.GetCollection<SensorData>("SensorData");
    public IMongoCollection<Device> Devices => _database.GetCollection<Device>("Devices");
    public IMongoCollection<ControlCommand> ControlCommands => _database.GetCollection<ControlCommand>("ControlCommands");
    public IMongoCollection<DeviceConfig> DeviceConfigs => _database.GetCollection<DeviceConfig>("DeviceConfigs");
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
        return (s.DeletedCount, cfg.DeletedCount, c.DeletedCount);
    }

    public async Task UpdateDeviceLastSeenAsync(string deviceId)
    {
        var update = Builders<Device>.Update
            .Set(d => d.LastSeen, DateTime.UtcNow);

        await Devices.UpdateOneAsync(d => d.DeviceId == deviceId, update);
    }

    // Device Config (ngưỡng auto) Operations
    public async Task<DeviceConfig?> GetDeviceConfigAsync(string deviceId)
        => await DeviceConfigs.Find(c => c.DeviceId == deviceId).FirstOrDefaultAsync();

    /// <summary>
    /// Ghi/hợp nhất cấu hình ngưỡng auto của thiết bị. Chỉ set các trường KHÁC null
    /// (echo đầy đủ từ topic xmini/config sẽ ghi cả 15 trường; PUT một phần sẽ chỉ merge trường được gửi).
    /// </summary>
    public async Task UpsertDeviceConfigAsync(DeviceConfig config)
    {
        var b = Builders<DeviceConfig>.Update;
        var updates = new List<UpdateDefinition<DeviceConfig>>
        {
            b.SetOnInsert(c => c.DeviceId, config.DeviceId),
            b.Set(c => c.UpdatedAt, config.UpdatedAt),
        };

        if (config.SoilOnPct.HasValue) updates.Add(b.Set(c => c.SoilOnPct, config.SoilOnPct));
        if (config.SoilOffPct.HasValue) updates.Add(b.Set(c => c.SoilOffPct, config.SoilOffPct));
        if (config.PumpMaxRunS.HasValue) updates.Add(b.Set(c => c.PumpMaxRunS, config.PumpMaxRunS));
        if (config.PumpCooldownS.HasValue) updates.Add(b.Set(c => c.PumpCooldownS, config.PumpCooldownS));
        if (config.LuxOn.HasValue) updates.Add(b.Set(c => c.LuxOn, config.LuxOn));
        if (config.LuxOff.HasValue) updates.Add(b.Set(c => c.LuxOff, config.LuxOff));
        if (config.LightAutoPwm.HasValue) updates.Add(b.Set(c => c.LightAutoPwm, config.LightAutoPwm));
        if (config.BattWarnPct.HasValue) updates.Add(b.Set(c => c.BattWarnPct, config.BattWarnPct));
        if (config.BattRecoverPct.HasValue) updates.Add(b.Set(c => c.BattRecoverPct, config.BattRecoverPct));
        if (config.SoilDry.HasValue) updates.Add(b.Set(c => c.SoilDry, config.SoilDry));
        if (config.SoilWet.HasValue) updates.Add(b.Set(c => c.SoilWet, config.SoilWet));
        if (config.BattFullOnV.HasValue) updates.Add(b.Set(c => c.BattFullOnV, config.BattFullOnV));
        if (config.BattFullOffV.HasValue) updates.Add(b.Set(c => c.BattFullOffV, config.BattFullOffV));
        if (config.BattCritV.HasValue) updates.Add(b.Set(c => c.BattCritV, config.BattCritV));
        if (config.BattCritRecoverV.HasValue) updates.Add(b.Set(c => c.BattCritRecoverV, config.BattCritRecoverV));

        await DeviceConfigs.UpdateOneAsync(
            c => c.DeviceId == config.DeviceId,
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
