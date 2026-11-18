# Build context is the repository root. These are the previously verified G01 pins.
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
WORKDIR /source
COPY src/backend/GameLibrary.Core/ src/backend/GameLibrary.Core/
COPY src/backend/GameLibrary.Api/ src/backend/GameLibrary.Api/
COPY tools/cover-native/ tools/cover-native/
RUN bash tools/cover-native/prepare.sh linux-x64
# The API declares its RID; don't impose an application RID on the Core library.
RUN dotnet restore src/backend/GameLibrary.Api/GameLibrary.Api.csproj --locked-mode
RUN dotnet publish src/backend/GameLibrary.Api/GameLibrary.Api.csproj -c Release -r linux-x64 --self-contained false --no-restore -o /published -p:PublishTrimmed=false -p:PublishSingleFile=false

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:10.0-noble@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee
WORKDIR /app
COPY --from=build /published/ ./
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=10000 \
    CoverUploads__Enabled=false \
    CoverUploads__AllowLocalHttp=false \
    E2E__Auth__Enabled=false \
    E2E__CoverUploads__Enabled=false \
    E2E__CoverImageSearch__Enabled=false \
    E2E__SteamIntegration__Enabled=false \
    SteamIntegration__Enabled=false
EXPOSE 10000
USER $APP_UID
# No migrations, test harness, shell wrapper or credential-bearing build arguments.
ENTRYPOINT ["dotnet", "GameLibrary.Api.dll"]
