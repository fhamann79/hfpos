FROM mcr.microsoft.com/dotnet/sdk:10.0.403-noble@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
WORKDIR /src
COPY backend/Pos.Backend.Api/Pos.Backend.Api/Pos.Backend.Api.csproj ./
RUN dotnet restore Pos.Backend.Api.csproj
COPY backend/Pos.Backend.Api/Pos.Backend.Api/ ./
RUN dotnet publish Pos.Backend.Api.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14-noble@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee
WORKDIR /app
COPY --from=build /out ./
COPY scripts/ops/health-probe.sh /app/health-probe.sh
RUN mkdir /keys && chown 1654:1654 /keys
ENV ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://+:8080
USER 1654:1654
EXPOSE 8080
ENTRYPOINT ["dotnet", "Pos.Backend.Api.dll"]
