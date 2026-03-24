# Plant Tree IoT - Docker Deployment

## 🚀 Quick Start

### 1. Prerequisites

- Docker & Docker Compose installed
- At least 2GB RAM available
- Ports 8080, 8443, 27017 available

### 2. Deploy Production

```bash
# Clone or navigate to project directory
cd plant-tree

# Start all services
./docker-manage.sh start

# Or use docker-compose directly
docker-compose up -d
```

### 3. Check Status

```bash
# Check container status
./docker-manage.sh status

# View logs
./docker-manage.sh logs

# View specific service logs
./docker-manage.sh logs mongodb
```

## 🌐 Access Points

- **API Server**: http://localhost:8080
- **MongoDB**: localhost:27017
- **Database**: PlantTreeIoT

## 🛠️ Development Mode

For development with hot reload and different ports:

```bash
# Start in development mode
./docker-manage.sh dev

# Access points in dev mode:
# - API Server: http://localhost:5000
# - MongoDB: localhost:27017
```

## 📊 Management Commands

```bash
# Stop containers
./docker-manage.sh stop

# Restart containers
./docker-manage.sh restart

# Rebuild containers
./docker-manage.sh build

# Clean up (removes volumes!)
./docker-manage.sh clean

# Show help
./docker-manage.sh help
```

## 🔧 Configuration

### Environment Variables (.env)

```bash
# Application
HTTP_PORT=8080
HTTPS_PORT=8443
ASPNETCORE_ENVIRONMENT=Production

# Database
MONGO_PORT=27017
MONGO_INITDB_DATABASE=PlantTreeIoT
```

### Custom Configuration

- `appsettings.Production.json` - Production settings
- `appsettings.Development.json` - Development settings
- `mongo-init/init-mongo.js` - Database initialization

## 📈 Monitoring

### Check Container Health

```bash
# Container status
docker ps

# Resource usage
docker stats

# Container logs
docker-compose logs -f
```

### API Health Check

```bash
# Test API endpoint
curl http://localhost:8080/api/devices

# MongoDB connection
docker exec -it planttree-mongodb-1 mongo --eval "db.stats()"
```

## 🔒 Security Notes

- Change default ports in production
- Use proper SSL certificates
- Configure MongoDB authentication
- Use Docker secrets for sensitive data

## 🐛 Troubleshooting

### Common Issues

1. **Port conflicts**

   ```bash
   # Check what's using ports
   netstat -ano | findstr :8080
   # Change ports in .env file
   ```

2. **MongoDB connection failed**

   ```bash
   # Check MongoDB logs
   ./docker-manage.sh logs mongodb
   # Verify network connectivity
   docker exec planttreeiotserver curl mongodb:27017
   ```

3. **Container won't start**
   ```bash
   # Check detailed logs
   docker-compose logs
   # Rebuild without cache
   ./docker-manage.sh build
   ```

### Reset Everything

```bash
# Stop and remove everything
docker-compose down -v
docker system prune -a

# Restart fresh
./docker-manage.sh start
```

## 📚 API Documentation

Once deployed, visit:

- **Swagger UI**: http://localhost:8080/swagger
- **API Docs**: See README.md for detailed endpoints

## 🔄 Updates

To update the application:

```bash
# Pull latest changes
git pull

# Rebuild and restart
./docker-manage.sh build
./docker-manage.sh restart
```
