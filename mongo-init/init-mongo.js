// MongoDB initialization script
// This script runs when the MongoDB container starts for the first time

// Switch to the PlantTreeIoT database
db = db.getSiblingDB("PlantTreeIoT");

// Create a collection for devices
db.createCollection("Devices");

// Create a collection for sensor data
db.createCollection("SensorData");

// Create a collection for control commands
db.createCollection("ControlCommands");

// Create indexes for better performance
db.SensorData.createIndex({ deviceId: 1, timestamp: -1 });
db.Devices.createIndex({ deviceId: 1 }, { unique: true });
db.ControlCommands.createIndex({ deviceId: 1, executed: 1 });

// Insert a sample device (optional)
db.Devices.insertOne({
  deviceId: "ESP32_SAMPLE",
  name: "Sample Device",
  location: "Test Location",
  plantType: "Pine Tree",
  isActive: true,
  lastSeen: new Date(),
  createdAt: new Date(),
});

print("PlantTreeIoT database initialized successfully!");
