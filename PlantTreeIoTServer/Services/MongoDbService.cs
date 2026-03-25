using MongoDB.Driver;
using PlantTreeIoTServer.Models;

namespace PlantTreeIoTServer.Services;

public class MongoDbService
{
    private readonly IMongoDatabase _database;

    public MongoDbService(IConfiguration configuration)
    {
        // Railway MongoDB plugin injects MONGO_URL or MONGODB_URL
        var connectionString = Environment.GetEnvironmentVariable("MONGO_URL")
            ?? Environment.GetEnvironmentVariable("MONGODB_URL")
            ?? configuration.GetValue<string>("MongoDbSettings:ConnectionString")
            ?? "mongodb://localhost:27017";
        var databaseName = configuration.GetValue<string>("MongoDbSettings:DatabaseName") ?? "PlantTreeIoT";

        var client = new MongoClient(connectionString);
        _database = client.GetDatabase(databaseName);
    }

    // Collections
    public IMongoCollection<SensorData> SensorData => _database.GetCollection<SensorData>("SensorData");
    public IMongoCollection<Device> Devices => _database.GetCollection<Device>("Devices");
    public IMongoCollection<ControlCommand> ControlCommands => _database.GetCollection<ControlCommand>("ControlCommands");

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

    public async Task UpdateDeviceLastSeenAsync(string deviceId)
    {
        var update = Builders<Device>.Update
            .Set(d => d.LastSeen, DateTime.UtcNow);

        await Devices.UpdateOneAsync(d => d.DeviceId == deviceId, update);
    }

    // Moisture Rule Operations
    public IMongoCollection<MoistureRule> MoistureRules => _database.GetCollection<MoistureRule>("MoistureRules");

    public async Task<List<MoistureRule>> GetMoistureRulesAsync(string deviceId)
    {
        return await MoistureRules
            .Find(r => r.DeviceId == deviceId)
            .ToListAsync();
    }

    public async Task<MoistureRule?> GetMoistureRuleAsync(string ruleId)
    {
        return await MoistureRules.Find(r => r.Id == ruleId).FirstOrDefaultAsync();
    }

    public async Task InsertMoistureRuleAsync(MoistureRule rule)
    {
        await MoistureRules.InsertOneAsync(rule);
    }

    public async Task<bool> UpdateMoistureRuleAsync(string ruleId, MoistureRule updated)
    {
        var update = Builders<MoistureRule>.Update
            .Set(r => r.Name, updated.Name)
            .Set(r => r.MinMoisture, updated.MinMoisture)
            .Set(r => r.MaxMoisture, updated.MaxMoisture)
            .Set(r => r.WaterDurationMs, updated.WaterDurationMs)
            .Set(r => r.IsEnabled, updated.IsEnabled)
            .Set(r => r.CooldownMinutes, updated.CooldownMinutes);

        var result = await MoistureRules.UpdateOneAsync(r => r.Id == ruleId, update);
        return result.ModifiedCount > 0;
    }

    public async Task UpdateRuleLastTriggeredAsync(string ruleId)
    {
        var update = Builders<MoistureRule>.Update
            .Set(r => r.LastTriggeredAt, DateTime.UtcNow);
        await MoistureRules.UpdateOneAsync(r => r.Id == ruleId, update);
    }

    public async Task<bool> DeleteMoistureRuleAsync(string ruleId)
    {
        var result = await MoistureRules.DeleteOneAsync(r => r.Id == ruleId);
        return result.DeletedCount > 0;
    }

    // Light Rule Operations
    public IMongoCollection<LightRule> LightRules => _database.GetCollection<LightRule>("LightRules");

    public async Task<List<LightRule>> GetLightRulesAsync(string deviceId)
        => await LightRules.Find(r => r.DeviceId == deviceId).ToListAsync();

    public async Task InsertLightRuleAsync(LightRule rule)
        => await LightRules.InsertOneAsync(rule);

    public async Task<bool> UpdateLightRuleAsync(string ruleId, LightRule updated)
    {
        var update = Builders<LightRule>.Update
            .Set(r => r.Name, updated.Name)
            .Set(r => r.MinLight, updated.MinLight)
            .Set(r => r.MaxLight, updated.MaxLight)
            .Set(r => r.IsEnabled, updated.IsEnabled)
            .Set(r => r.CooldownMinutes, updated.CooldownMinutes);
        var result = await LightRules.UpdateOneAsync(r => r.Id == ruleId, update);
        return result.ModifiedCount > 0;
    }

    public async Task UpdateLightRuleLastTriggeredAsync(string ruleId)
    {
        var update = Builders<LightRule>.Update.Set(r => r.LastTriggeredAt, DateTime.UtcNow);
        await LightRules.UpdateOneAsync(r => r.Id == ruleId, update);
    }

    public async Task<bool> DeleteLightRuleAsync(string ruleId)
    {
        var result = await LightRules.DeleteOneAsync(r => r.Id == ruleId);
        return result.DeletedCount > 0;
    }

    // Control Command Operations
    public async Task<List<ControlCommand>> GetPendingCommandsAsync(string deviceId)
    {
        return await ControlCommands
            .Find(cmd => cmd.DeviceId == deviceId && !cmd.Executed)
            .SortBy(cmd => cmd.CreatedAt)
            .ToListAsync();
    }

    public async Task InsertControlCommandAsync(ControlCommand command)
    {
        await ControlCommands.InsertOneAsync(command);
    }

    public async Task MarkCommandExecutedAsync(string commandId)
    {
        var update = Builders<ControlCommand>.Update
            .Set(cmd => cmd.Executed, true)
            .Set(cmd => cmd.ExecutedAt, DateTime.UtcNow);

        await ControlCommands.UpdateOneAsync(cmd => cmd.Id == commandId, update);
    }
}