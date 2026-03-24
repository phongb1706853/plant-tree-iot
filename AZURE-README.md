# Azure Deployment Guide for Plant Tree IoT Server

## 🚀 Quick Deploy to Azure

### Prerequisites

- Azure CLI installed (`az` command)
- Azure subscription
- Docker installed locally

### 1. One-Click Setup (Recommended)

```bash
# Setup everything automatically
./setup-azure-complete.sh
```

This script will:

- ✅ Create Azure resource group
- ✅ Setup Azure Cosmos DB (MongoDB API)
- ✅ Create Azure Container Registry
- ✅ Build and push Docker image
- ✅ Deploy to Azure Container Instances
- ✅ Configure all settings automatically

### 2. Manual Setup

```bash
# Step by step setup
./setup-azure-cosmos.sh        # Setup database
./deploy-azure.sh             # Deploy application
```

### 3. Alternative: App Service

```bash
# Deploy to Azure App Service instead
./deploy-azure-appservice.sh
```

### 4. Manual Setup (Advanced)

#### Login to Azure

```bash
az login
az account set --subscription "your-subscription-name"
```

#### Create Resource Group

```bash
az group create --name planttree-rg --location eastus
```

```bash
# Build and push to Azure Container Registry
az acr create --resource-group planttree-rg --name planttreeacr --sku Basic
az acr login --name planttreeacr

# Build and push image
docker build -t planttreeacr.azurecr.io/planttree-iot:latest ./PlantTreeIoTServer
docker push planttreeacr.azurecr.io/planttree-iot:latest

# Deploy to ACI
az container create \
  --resource-group planttree-rg \
  --name planttree-iot-server \
  --image planttreeacr.azurecr.io/planttree-iot:latest \
  --cpu 1 \
  --memory 1.5 \
  --registry-login-server planttreeacr.azurecr.io \
  --registry-username planttreeacr \
  --registry-password $(az acr credential show --name planttreeacr --query passwords[0].value -o tsv) \
  --dns-name-label planttree-iot-server \
  --ports 80 443 \
  --environment-variables ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS="http://+:80;https://+:443"
```

### 4. Get Public URL

```bash
az container show --resource-group planttree-rg --name planttree-iot-server --query ipAddress.fqdn -o tsv
```

## 📊 Alternative: Azure App Service

### Deploy to App Service with Docker

```bash
# Create App Service Plan
az appservice plan create \
  --name planttree-plan \
  --resource-group planttree-rg \
  --sku B1 \
  --is-linux

# Create Web App
az webapp create \
  --resource-group planttree-rg \
  --plan planttree-plan \
  --name planttree-iot-server \
  --deployment-container-image-name planttreeacr.azurecr.io/planttree-iot:latest

# Configure environment variables
az webapp config appsettings set \
  --resource-group planttree-rg \
  --name planttree-iot-server \
  --setting ASPNETCORE_ENVIRONMENT=Production

# Get URL
az webapp show --resource-group planttree-rg --name planttree-iot-server --query defaultHostName -o tsv
```

## 🗄️ Azure Database for MongoDB

### Create Azure Cosmos DB with MongoDB API

```bash
# Create Cosmos DB account
az cosmosdb create \
  --name planttree-cosmos \
  --resource-group planttree-rg \
  --kind MongoDB \
  --server-version 4.0 \
  --default-consistency-level Session \
  --enable-automatic-failover true \
  --locations regionName=eastus failoverPriority=0 isZoneRedundant=false

# Get connection string
az cosmosdb keys list \
  --name planttree-cosmos \
  --resource-group planttree-rg \
  --type connection-strings \
  --query connectionStrings[0].connectionString -o tsv
```

### Update App Configuration

Update `appsettings.Production.json` with Azure Cosmos DB connection string:

```json
{
  "MongoDbSettings": {
    "ConnectionString": "your-azure-cosmos-connection-string",
    "DatabaseName": "PlantTreeIoT"
  }
}
```

## 🔒 Security & Networking

### Add Azure Firewall

```bash
# Create VNet
az network vnet create \
  --resource-group planttree-rg \
  --name planttree-vnet \
  --address-prefix 10.0.0.0/16 \
  --subnet-name planttree-subnet \
  --subnet-prefix 10.0.0.0/24

# Add NSG rules for ESP32 access
az network nsg create --resource-group planttree-rg --name planttree-nsg
az network nsg rule create \
  --resource-group planttree-rg \
  --nsg-name planttree-nsg \
  --name AllowHTTP \
  --priority 100 \
  --destination-port-ranges 80 443 \
  --access Allow \
  --protocol Tcp
```

### SSL Certificate (App Service)

```bash
# Enable HTTPS only
az webapp update \
  --resource-group planttree-rg \
  --name planttree-iot-server \
  --https-only true

# Add custom domain and SSL (optional)
az webapp config hostname set \
  --resource-group planttree-rg \
  --name planttree-iot-server \
  --hostname your-custom-domain.com
```

## 📈 Scaling & Monitoring

### Scale App Service

```bash
# Scale up (more CPU/RAM)
az appservice plan update \
  --name planttree-plan \
  --resource-group planttree-rg \
  --sku S1

# Scale out (more instances)
az webapp scale \
  --resource-group planttree-rg \
  --name planttree-iot-server \
  --instance-count 3
```

### Enable Application Insights

```bash
# Create Application Insights
az monitor app-insights component create \
  --app planttree-insights \
  --location eastus \
  --resource-group planttree-rg \
  --application-type web

# Connect to App Service
az webapp config appsettings set \
  --resource-group planttree-rg \
  --name planttree-iot-server \
  --setting APPINSIGHTS_INSTRUMENTATIONKEY=$(az monitor app-insights component show --app planttree-insights --resource-group planttree-rg --query instrumentationKey -o tsv)
```

## 💰 Cost Estimation

### Basic Setup (ACI)

- Container Instances: ~$0.04/hour (1 CPU, 1.5GB RAM)
- Container Registry: ~$0.5/month
- Cosmos DB: ~$25/month (400 RU/s)

### App Service Setup

- App Service Plan (B1): ~$13/month
- Cosmos DB: ~$25/month
- Application Insights: ~$2/month

## 🛠️ Management Scripts

### Deploy Script (deploy-azure.sh)

```bash
#!/bin/bash
RESOURCE_GROUP="planttree-rg"
ACR_NAME="planttreeacr"
APP_NAME="planttree-iot-server"

# Build and push
az acr login --name $ACR_NAME
docker build -t $ACR_NAME.azurecr.io/planttree-iot:latest ./PlantTreeIoTServer
docker push $ACR_NAME.azurecr.io/planttree-iot:latest

# Deploy
az container create \
  --resource-group $RESOURCE_GROUP \
  --name $APP_NAME \
  --image $ACR_NAME.azurecr.io/planttree-iot:latest \
  --registry-login-server $ACR_NAME.azurecr.io \
  --registry-username $ACR_NAME \
  --registry-password $(az acr credential show --name $ACR_NAME --query passwords[0].value -o tsv) \
  --dns-name-label $APP_NAME \
  --ports 80 443

echo "Deployment completed!"
echo "URL: $(az container show --resource-group $RESOURCE_GROUP --name $APP_NAME --query ipAddress.fqdn -o tsv)"
```

### Update Script (update-azure.sh)

```bash
#!/bin/bash
RESOURCE_GROUP="planttree-rg"
ACR_NAME="planttreeacr"
APP_NAME="planttree-iot-server"

# Build and push new version
az acr login --name $ACR_NAME
docker build -t $ACR_NAME.azurecr.io/planttree-iot:v$(date +%Y%m%d-%H%M%S) ./PlantTreeIoTServer
docker push $ACR_NAME.azurecr.io/planttree-iot:v$(date +%Y%m%d-%H%M%S)

# Update container
az container create \
  --resource-group $RESOURCE_GROUP \
  --name $APP_NAME \
  --image $ACR_NAME.azurecr.io/planttree-iot:v$(date +%Y%m%d-%H%M%S) \
  --registry-login-server $ACR_NAME.azurecr.io \
  --registry-username $ACR_NAME \
  --registry-password $(az acr credential show --name $ACR_NAME --query passwords[0].value -o tsv) \
  --dns-name-label $APP_NAME \
  --ports 80 443 \
  --overwrite
```

## 🔍 Troubleshooting

### Check Logs

```bash
# ACI logs
az container logs --resource-group planttree-rg --name planttree-iot-server

# App Service logs
az webapp log download --resource-group planttree-rg --name planttree-iot-server
```

### Common Issues

1. **Port conflicts**: Change ports in deployment
2. **MongoDB connection**: Verify connection string
3. **SSL issues**: Check certificate configuration
4. **Performance**: Monitor with Application Insights

## 📚 Additional Resources

- [Azure Container Instances Docs](https://docs.microsoft.com/en-us/azure/container-instances/)
- [Azure App Service Docs](https://docs.microsoft.com/en-us/azure/app-service/)
- [Azure Cosmos DB MongoDB API](https://docs.microsoft.com/en-us/azure/cosmos-db/mongodb-introduction)
