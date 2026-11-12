FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend/ ./backend/
RUN dotnet restore backend/src/SchoolErp.Api/SchoolErp.Api.csproj
RUN dotnet publish backend/src/SchoolErp.Api/SchoolErp.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
RUN mkdir -p /app/App_Data/uploads && chown -R app:app /app/App_Data
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "SchoolErp.Api.dll"]
