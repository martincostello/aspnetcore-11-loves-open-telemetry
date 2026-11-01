# Build context: the repository root (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/GrotsPizzeria.Shared/GrotsPizzeria.Shared.csproj src/GrotsPizzeria.Shared/
COPY src/GrotsPizzeria.Client/GrotsPizzeria.Client.csproj src/GrotsPizzeria.Client/
COPY src/GrotsPizzeria.Server/GrotsPizzeria.Server.csproj src/GrotsPizzeria.Server/
RUN dotnet restore src/GrotsPizzeria.Server/GrotsPizzeria.Server.csproj

COPY src/ src/
RUN dotnet publish src/GrotsPizzeria.Server/GrotsPizzeria.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:11.0
WORKDIR /app

RUN mkdir /data && chown app:app /data
VOLUME /data

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__Users="Data Source=/data/grots.db" \
    DataProtection__KeysPath=/data/keys \
    OTEL_SERVICE_NAME=grots-web

USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "GrotsPizzeria.Server.dll"]
