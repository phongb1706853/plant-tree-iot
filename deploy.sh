#!/bin/bash

# Plant Tree IoT Server Deployment Script
# Usage: ./deploy.sh [environment]

ENVIRONMENT=${1:-"Production"}
PROJECT_DIR="/var/www/planttree-iot"
BACKUP_DIR="/var/backups/planttree-iot"

echo "🚀 Deploying Plant Tree IoT Server..."
echo "Environment: $ENVIRONMENT"

# Create directories
sudo mkdir -p $PROJECT_DIR
sudo mkdir -p $BACKUP_DIR

# Backup current deployment
if [ -d "$PROJECT_DIR" ] && [ "$(ls -A $PROJECT_DIR)" ]; then
    echo "📦 Backing up current deployment..."
    sudo tar -czf $BACKUP_DIR/backup-$(date +%Y%m%d-%H%M%S).tar.gz -C $PROJECT_DIR .
fi

# Stop service
echo "🛑 Stopping service..."
sudo systemctl stop planttree-iot.service || true

# Copy files
echo "📋 Copying files..."
sudo cp -r * $PROJECT_DIR/
sudo chown -R www-data:www-data $PROJECT_DIR

# Install dependencies
echo "📦 Installing dependencies..."
cd $PROJECT_DIR
sudo dotnet restore
sudo dotnet publish -c Release -o ./publish

# Update database connection if needed
if [ "$ENVIRONMENT" = "Production" ]; then
    echo "🔧 Updating production configuration..."
    # Update MongoDB connection string here
    # sudo sed -i 's/localhost/your-production-mongo-server/g' appsettings.Production.json
fi

# Start service
echo "▶️  Starting service..."
sudo systemctl daemon-reload
sudo systemctl enable planttree-iot.service
sudo systemctl start planttree-iot.service

# Check status
echo "🔍 Checking service status..."
sudo systemctl status planttree-iot.service --no-pager

echo "✅ Deployment completed!"
echo "🌐 Server should be running on http://your-server:5000"
echo "📊 Check logs with: sudo journalctl -u planttree-iot.service -f"