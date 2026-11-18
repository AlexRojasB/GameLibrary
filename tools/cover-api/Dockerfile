FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
WORKDIR /source
COPY src/backend/ src/backend/
COPY tests/backend/ tests/backend/
COPY tools/cover-native/ tools/cover-native/
COPY tools/cover-api/ tools/cover-api/
RUN bash tools/cover-native/prepare.sh linux-x64
RUN --mount=type=cache,target=/root/.nuget/packages dotnet publish tools/cover-api/Verify.csproj -c Release -r linux-x64 --self-contained false -o /verified -p:PublishTrimmed=false -p:PublishSingleFile=false
RUN --mount=type=cache,target=/root/.nuget/packages dotnet publish src/backend/GameLibrary.Api/GameLibrary.Api.csproj -c Release -r linux-x64 --self-contained false --no-restore -o /api-published -p:PublishTrimmed=false -p:PublishSingleFile=false
RUN cmp /api-published/libSkiaSharp.so /verified/libSkiaSharp.so && cmp /api-published/libgamelibrary-turbojpeg.so /verified/libgamelibrary-turbojpeg.so

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:10.0-noble@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee
WORKDIR /app/harness
ENV ASPNETCORE_HTTP_PORTS=""
ENV Logging__LogLevel__Default="None"
COPY --from=build /verified/ ./
COPY --from=build /api-published/ /app/GameLibrary.Api/
COPY src/backend/GameLibrary.sln /app/GameLibrary.sln
USER $APP_UID
ENTRYPOINT ["dotnet", "Verify.dll"]
