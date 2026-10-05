# SYNTHETIC PROOF ONLY. Not a release image and never published by the release workflow.
FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim@sha256:78235e09001f52b6592c458ac010775ebac6725422e80cd0c1650590f67b2743 AS build
WORKDIR /src
COPY backend/Pos.Backend.Api/ ./backend/Pos.Backend.Api/
RUN dotnet publish backend/Pos.Backend.Api/Hfpos.FiscalSmokeHost -c Release -o /out /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim@sha256:2f202e1169ec507bdc07007cf68c14d0ff3a098110b17c460a60185e1f36a9d1
WORKDIR /app
COPY --from=build /out ./
USER 1654:1654
ENTRYPOINT ["dotnet", "Hfpos.FiscalSmokeHost.dll", "--release-pilot"]
