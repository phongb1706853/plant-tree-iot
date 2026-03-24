#!/bin/bash

# Setup GitHub Actions for Azure deployment
# This creates a CI/CD pipeline that deploys to Azure on git push

set -e

echo "🔄 Setup GitHub Actions for Azure Auto-Deployment"
echo "================================================="

# Create .github/workflows directory if it doesn't exist
mkdir -p .github/workflows

# Create Azure deployment workflow
cat > .github/workflows/azure-deploy.yml << 'EOF'
name: Deploy to Azure

on:
  push:
    branches: [ main, master ]
  pull_request:
    branches: [ main, master ]

env:
  AZURE_WEBAPP_NAME: planttree-iot-server
  AZURE_WEBAPP_PACKAGE_PATH: './PlantTreeIoTServer'
  DOTNET_VERSION: '10.0.x'

jobs:
  build-and-deploy:
    runs-on: ubuntu-latest

    steps:
    - name: Checkout code
      uses: actions/checkout@v4

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: ${{ env.DOTNET_VERSION }}

    - name: Restore dependencies
      run: dotnet restore ./PlantTreeIoTServer

    - name: Build
      run: dotnet build ./PlantTreeIoTServer --configuration Release --no-restore

    - name: Test
      run: dotnet test ./PlantTreeIoTServer --no-restore --verbosity normal

    - name: Publish
      run: dotnet publish ./PlantTreeIoTServer --configuration Release --output ./publish

    - name: Login to Azure
      uses: azure/login@v2
      with:
        creds: ${{ secrets.AZURE_CREDENTIALS }}

    - name: Deploy to Azure Web App
      uses: azure/webapps-deploy@v3
      with:
        app-name: ${{ env.AZURE_WEBAPP_NAME }}
        package: ./publish

    - name: Logout from Azure
      run: az logout
EOF

echo "✅ Created GitHub Actions workflow: .github/workflows/azure-deploy.yml"
echo ""

echo "📋 Next steps:"
echo "1. Go to GitHub repository Settings -> Secrets and variables -> Actions"
echo "2. Add secret: AZURE_CREDENTIALS"
echo "   Get credentials: az ad sp create-for-rbac --name 'planttree-github-actions' --role contributor --scopes /subscriptions/<subscription-id>/resourceGroups/<resource-group> --sdk-auth"
echo "3. Push this workflow to GitHub"
echo "4. Every push to main/master will auto-deploy to Azure!"
echo ""

echo "🔧 Alternative: For Azure Container Instances:"
echo "   Modify the workflow to use Docker build and Azure CLI commands"
echo ""

# Make the script executable
chmod +x "$0"