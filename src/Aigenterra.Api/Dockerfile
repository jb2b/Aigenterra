FROM mcr.microsoft.com/dotnet/sdk:10.0.400-noble AS build
WORKDIR /source
COPY Directory.Build.props global.json ./
COPY src/Aigenterra.Api/Aigenterra.Api.csproj src/Aigenterra.Api/
COPY src/Aigenterra.Adapters.Codex/Aigenterra.Adapters.Codex.csproj src/Aigenterra.Adapters.Codex/
COPY src/Aigenterra.Application/Aigenterra.Application.csproj src/Aigenterra.Application/
COPY src/Aigenterra.Domain/Aigenterra.Domain.csproj src/Aigenterra.Domain/
COPY src/Aigenterra.Infrastructure/Aigenterra.Infrastructure.csproj src/Aigenterra.Infrastructure/
COPY src/Aigenterra.ServiceDefaults/Aigenterra.ServiceDefaults.csproj src/Aigenterra.ServiceDefaults/
RUN dotnet restore src/Aigenterra.Api/Aigenterra.Api.csproj
COPY src/Aigenterra.Api/ src/Aigenterra.Api/
COPY src/Aigenterra.Adapters.Codex/ src/Aigenterra.Adapters.Codex/
COPY src/Aigenterra.Application/ src/Aigenterra.Application/
COPY src/Aigenterra.Domain/ src/Aigenterra.Domain/
COPY src/Aigenterra.Infrastructure/ src/Aigenterra.Infrastructure/
COPY src/Aigenterra.ServiceDefaults/ src/Aigenterra.ServiceDefaults/
RUN dotnet publish src/Aigenterra.Api/Aigenterra.Api.csproj --configuration Release --no-restore --no-self-contained -p:UseAppHost=false -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11-noble AS final
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/ ./
ENV ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Aigenterra.Api.dll"]
