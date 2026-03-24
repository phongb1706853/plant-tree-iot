# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["PlantTreeIoTServer/PlantTreeIoTServer.csproj", "PlantTreeIoTServer/"]
RUN dotnet restore "PlantTreeIoTServer/PlantTreeIoTServer.csproj"

COPY PlantTreeIoTServer/ PlantTreeIoTServer/
RUN dotnet publish "PlantTreeIoTServer/PlantTreeIoTServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 80

ENTRYPOINT ["dotnet", "PlantTreeIoTServer.dll"]
