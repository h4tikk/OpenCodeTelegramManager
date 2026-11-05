# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props OpenCodeTelegramManager.slnx ./
COPY src/OpenCodeManager.Domain/*.csproj src/OpenCodeManager.Domain/
COPY src/OpenCodeManager.Application/*.csproj src/OpenCodeManager.Application/
COPY src/OpenCodeManager.Infrastructure/*.csproj src/OpenCodeManager.Infrastructure/
COPY src/OpenCodeManager.Telegram/*.csproj src/OpenCodeManager.Telegram/
COPY src/OpenCodeManager.Worker/*.csproj src/OpenCodeManager.Worker/
RUN dotnet restore src/OpenCodeManager.Worker/OpenCodeManager.Worker.csproj
COPY src/ src/
RUN dotnet publish src/OpenCodeManager.Worker -c Release --no-restore -o /out /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
USER root
RUN apt-get update && apt-get install -y --no-install-recommends git poppler-utils ca-certificates curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out/ ./
ENV DOTNET_EnableDiagnostics=0 DOTNET_NOLOGO=1
USER 1654:1654
ENTRYPOINT ["dotnet", "OpenCodeManager.Worker.dll"]
