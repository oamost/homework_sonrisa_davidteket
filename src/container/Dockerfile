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
RUN mkdir -p /app/data && chown app:app /app/data
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "WorldAlerts.Api.dll"]
