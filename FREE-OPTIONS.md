# Free Tier & Cost Optimization for Plant Tree IoT

## 💰 Azure Free Tier Options

### 1. Azure Free Account (12 months free)

```bash
# Get $200 credit for 30 days + 12 months free services
# Visit: https://azure.microsoft.com/en-us/free/
```

**Free Services for our project:**

- ✅ **Azure Container Instances**: 2 containers free (1 CPU, 1GB RAM each)
- ✅ **Azure Cosmos DB**: 400 RU/s free (enough for our IoT project)
- ✅ **Azure Container Registry**: 1 registry free (100GB storage)
- ✅ **Azure App Service**: 10 web apps free (1GB storage each)

### 2. Azure for Students (Free)

```bash
# $100 credit + free services for 12 months
# Visit: https://azure.microsoft.com/en-us/free/students/
```

### 3. Azure Open Source Credits

```bash
# Credits for open source projects
# Apply at: https://azure.microsoft.com/en-us/resources/open-source/
```

## 🆓 Alternative Free Options

### 1. Railway (Free tier)

```bash
# Modern cloud platform with free tier
# - 512MB RAM, 1GB storage
# - PostgreSQL database included
# - Custom domains
# - Visit: https://railway.app
```

### 2. Render (Free tier)

```bash
# Free web services
# - 750 hours/month
# - PostgreSQL free
# - Docker support
# - Visit: https://render.com
```

### 3. Fly.io (Free tier)

```bash
# Global deployment
# - 3 shared CPU, 256MB RAM
# - 3GB storage
# - PostgreSQL free
# - Visit: https://fly.io
```

### 4. DigitalOcean App Platform (Free tier)

```bash
# $200 credit for new users
# - Docker support
# - Managed databases
# - Visit: https://www.digitalocean.com/
```

## 🏠 Local Development (Completely Free)

### Run everything locally:

```bash
# Use local MongoDB + Docker
docker-compose up -d

# Or use MongoDB in Docker
docker run -d -p 27017:27017 --name mongodb mongo:7.0

# Run .NET app locally
cd PlantTreeIoTServer
dotnet run
```

### Free local alternatives:

- ✅ **MongoDB Community Server** (free)
- ✅ **Docker Desktop** (free for personal use)
- ✅ **Visual Studio Code** (free)
- ✅ **.NET SDK** (free)

## 📊 Cost Comparison

| Service             | Free Tier    | Paid Plan   | Our Usage  |
| ------------------- | ------------ | ----------- | ---------- |
| **Azure ACI**       | 2 containers | $0.04/hour  | ~$30/month |
| **Azure Cosmos DB** | 400 RU/s     | $0.008/hour | ~$25/month |
| **Railway**         | 512MB RAM    | $5/month    | ~$5/month  |
| **Render**          | 750 hours    | $7/month    | ~$7/month  |
| **Local**           | Unlimited    | $0          | $0         |

## 🎯 Recommended Free Setup

### Option 1: Azure Free Tier (Best)

```bash
# 1. Create Azure free account
# 2. Run our Azure setup script
./setup-azure-complete.sh

# Benefits:
# - Professional cloud infrastructure
# - High availability
# - Scalable
# - 12 months free
```

### Option 2: Railway (Easiest)

```bash
# 1. Sign up at railway.app
# 2. Connect GitHub repo
# 3. Deploy automatically
# 4. Get free PostgreSQL

# Benefits:
# - No infrastructure management
# - Automatic deployments
# - Built-in database
```

### Option 3: Local Development (Most Control)

```bash
# Run everything on your machine
docker-compose up -d

# Benefits:
# - Complete control
# - No costs
# - Fast development
# - No internet required
```

## 🚀 Quick Free Deployment

### Azure Free (Recommended):

```bash
# 1. Create free Azure account
# 2. Install Azure CLI
az login

# 3. Deploy
./setup-azure-complete.sh
```

### Railway (Alternative):

```bash
# 1. Create Railway account
# 2. Connect GitHub repository
# 3. Deploy with one click
# 4. Get free database
```

## 💡 Cost Optimization Tips

### For Azure:

```bash
# Use B1 tier for App Service (~$13/month)
# Set up auto-scaling
# Use Azure Front Door for CDN
# Monitor usage with Cost Management
```

### For Railway/Render:

```bash
# Monitor usage to stay within free limits
# Scale down when not needed
# Use their free database offerings
```

## 🔍 Free Tier Limitations

### Azure Free Tier:

- 12 months only
- Limited regions
- Some services have quotas
- Credit expires after 30 days

### Railway Free:

- 512MB RAM limit
- Sleeps after 14 days inactivity
- Limited bandwidth

### Local Development:

- Requires powerful machine
- No high availability
- Manual maintenance

## 🎯 My Recommendation

**For Production:** Azure Free Tier (12 months free, professional)
**For Development:** Local Docker setup (completely free)
**For Quick Demo:** Railway (easiest setup)

Bạn muốn setup theo phương pháp nào? Tôi có thể hướng dẫn chi tiết! 🚀
