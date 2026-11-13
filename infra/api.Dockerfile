# API image. Used by Render (render.yaml) and by infra/docker-compose.yml.
# Build context is the repository root.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first so the package layer is cached between code changes.
COPY backend/src/SchoolErp.Domain/SchoolErp.Domain.csproj backend/src/SchoolErp.Domain/
COPY backend/src/SchoolErp.Application/SchoolErp.Application.csproj backend/src/SchoolErp.Application/
COPY backend/src/SchoolErp.Infrastructure/SchoolErp.Infrastructure.csproj backend/src/SchoolErp.Infrastructure/
COPY backend/src/SchoolErp.Api/SchoolErp.Api.csproj backend/src/SchoolErp.Api/
RUN dotnet restore backend/src/SchoolErp.Api/SchoolErp.Api.csproj

COPY backend/src/ backend/src/
RUN dotnet publish backend/src/SchoolErp.Api/SchoolErp.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Fonts for PDF receipts and statements (QuestPDF).
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# Uploads are written here. Mount a persistent disk at this path on a paid plan.
RUN mkdir -p /app/App_Data/uploads && chown -R app:app /app/App_Data
USER app

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    PORT=8080
EXPOSE 8080

# Render injects PORT; fall back to 8080 elsewhere.
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} exec dotnet SchoolErp.Api.dll"]
