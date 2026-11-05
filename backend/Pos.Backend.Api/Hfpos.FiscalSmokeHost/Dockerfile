# SYNTHETIC PROOF ONLY. Not a release image and never published by the release workflow.
FROM mcr.microsoft.com/dotnet/sdk:10.0.403-noble@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
WORKDIR /src
COPY backend/Pos.Backend.Api/ ./backend/Pos.Backend.Api/
RUN dotnet publish backend/Pos.Backend.Api/Hfpos.FiscalSmokeHost -c Release -o /out /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:10.0.14-noble@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee
WORKDIR /app
COPY --from=build /out ./
USER 1654:1654
ENTRYPOINT ["dotnet", "Hfpos.FiscalSmokeHost.dll", "--release-pilot"]
