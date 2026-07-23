FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

COPY Directory.Build.props Directory.Packages.props ./
COPY src/SolidarityGrid.Domain/SolidarityGrid.Domain.csproj src/SolidarityGrid.Domain/
COPY src/SolidarityGrid.Application/SolidarityGrid.Application.csproj src/SolidarityGrid.Application/
COPY src/SolidarityGrid.Contracts/SolidarityGrid.Contracts.csproj src/SolidarityGrid.Contracts/
COPY src/SolidarityGrid.Infrastructure/SolidarityGrid.Infrastructure.csproj src/SolidarityGrid.Infrastructure/
COPY src/SolidarityGrid.Node/SolidarityGrid.Node.csproj src/SolidarityGrid.Node/
RUN dotnet restore src/SolidarityGrid.Node/SolidarityGrid.Node.csproj

COPY src/ src/
RUN dotnet publish src/SolidarityGrid.Node/SolidarityGrid.Node.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && install --directory --owner=app --group=app /data

WORKDIR /app
COPY --from=build /app/publish ./

ENV ASPNETCORE_HTTP_PORTS= \
    ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app

ENTRYPOINT ["dotnet", "SolidarityGrid.Node.dll"]
