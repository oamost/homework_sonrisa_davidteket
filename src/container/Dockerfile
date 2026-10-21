# Build the Angular client separately; its dist/browser files are served by ASP.NET.
FROM node:24-bookworm-slim AS frontend
WORKDIR /web
COPY src/web/package.json src/web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/web/ ./
RUN npm test -- --watch=false && npm run build

# A failed .NET build or test prevents any runtime image from being produced.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS verified
WORKDIR /work
COPY src/WorldAlerts.sln src/WorldAlerts.sln
COPY src/api/ src/api/
COPY src/tests/WorldAlerts.Api.Tests/ src/tests/WorldAlerts.Api.Tests/
RUN dotnet restore src/WorldAlerts.sln \
    && dotnet build src/WorldAlerts.sln --configuration Release --no-restore \
    && dotnet test src/WorldAlerts.sln --configuration Release --no-build --no-restore \
    && dotnet publish src/api/WorldAlerts.Api.csproj --configuration Release --no-build --no-restore --output /publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=verified /publish/ ./
COPY --from=frontend /web/dist/web/browser/ ./wwwroot/
RUN mkdir -p /app/data /home/app/.aspnet/DataProtection-Keys \
    && chown app:app /app/data /home/app/.aspnet /home/app/.aspnet/DataProtection-Keys \
    && chmod 700 /home/app/.aspnet/DataProtection-Keys
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "WorldAlerts.Api.dll"]
