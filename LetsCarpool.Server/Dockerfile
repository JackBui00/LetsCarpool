# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Build the client into the server's static web root for production images.
FROM node:22-alpine AS client-build
WORKDIR /src/letscarpool.client
COPY ["letscarpool.client/package.json", "letscarpool.client/package-lock.json", "./"]
RUN npm ci
COPY ["letscarpool.client/", "./"]
RUN npm run build

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["global.json", "./"]
COPY ["LetsCarpool.Server/LetsCarpool.Server.csproj", "LetsCarpool.Server/"]
COPY ["letscarpool.client/letscarpool.client.esproj", "letscarpool.client/"]
RUN dotnet restore "./LetsCarpool.Server/LetsCarpool.Server.csproj"
COPY . .
COPY --from=client-build /src/letscarpool.client/dist ./LetsCarpool.Server/wwwroot
WORKDIR "/src/LetsCarpool.Server"
RUN dotnet publish "./LetsCarpool.Server.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:ShouldRunNpmInstall=false "/p:BuildCommand=echo Client assets are prebuilt"

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "LetsCarpool.Server.dll"]
