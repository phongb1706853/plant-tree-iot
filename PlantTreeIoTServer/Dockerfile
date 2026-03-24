# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["PlantTreeIoTServer.csproj", "./"]
RUN dotnet restore "PlantTreeIoTServer.csproj"

# Copy everything else and build
COPY . .
RUN dotnet build "PlantTreeIoTServer.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "PlantTreeIoTServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

EXPOSE 80
EXPOSE 443

ENTRYPOINT ["dotnet", "PlantTreeIoTServer.dll"]