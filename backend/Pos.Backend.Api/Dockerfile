FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim@sha256:78235e09001f52b6592c458ac010775ebac6725422e80cd0c1650590f67b2743 AS build
WORKDIR /src
COPY backend/Pos.Backend.Api/Pos.Backend.Api/Pos.Backend.Api.csproj ./
RUN dotnet restore Pos.Backend.Api.csproj
COPY backend/Pos.Backend.Api/Pos.Backend.Api/ ./
RUN dotnet publish Pos.Backend.Api.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim@sha256:2f202e1169ec507bdc07007cf68c14d0ff3a098110b17c460a60185e1f36a9d1
WORKDIR /app
COPY --from=build /out ./
COPY scripts/ops/health-probe.sh /app/health-probe.sh
RUN mkdir /keys && chown 1654:1654 /keys
ENV ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://+:8080
USER 1654:1654
EXPOSE 8080
ENTRYPOINT ["dotnet", "Pos.Backend.Api.dll"]
