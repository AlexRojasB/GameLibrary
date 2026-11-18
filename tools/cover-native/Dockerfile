FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
WORKDIR /source
COPY src/backend/GameLibrary.Core/ src/backend/GameLibrary.Core/
COPY src/backend/GameLibrary.Api/ src/backend/GameLibrary.Api/
COPY tests/backend/ tests/backend/
COPY tools/cover-native/ tools/cover-native/
RUN bash tools/cover-native/prepare.sh linux-x64
RUN dotnet publish tools/cover-native/Smoke.csproj -c Release -r linux-x64 --self-contained false -o /published -p:PublishTrimmed=false -p:PublishSingleFile=false
RUN dotnet publish src/backend/GameLibrary.Api/GameLibrary.Api.csproj -c Release -r linux-x64 --self-contained false --no-restore -o /api-published -p:PublishTrimmed=false -p:PublishSingleFile=false && cmp /api-published/libgamelibrary-turbojpeg.so /published/libgamelibrary-turbojpeg.so && cmp /api-published/cover-native-notices.txt /published/cover-native-notices.txt
RUN sha256sum /published/libSkiaSharp.so /published/libgamelibrary-turbojpeg.so

FROM build AS inspection
RUN apt-get update && apt-get install -y --no-install-recommends file binutils
RUN file /published/libSkiaSharp.so /published/libgamelibrary-turbojpeg.so && readelf -h -d --version-info /published/libgamelibrary-turbojpeg.so && readelf -h -d --version-info /published/libSkiaSharp.so

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:10.0-noble@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee AS final
WORKDIR /app
COPY --from=build /published/ ./
USER $APP_UID
CMD ["sh", "-c", "id && uname -m && dotnet --info && sha256sum libSkiaSharp.so libgamelibrary-turbojpeg.so && ldd -r libSkiaSharp.so && ldd -r libgamelibrary-turbojpeg.so && dotnet Smoke.dll"]

FROM final AS missing-native
USER root
RUN rm /app/libgamelibrary-turbojpeg.so
USER $APP_UID
CMD ["dotnet", "Smoke.dll", "--expect-unavailable"]

FROM final AS tampered-native
COPY --from=build /source/tools/cover-native/invalid-native.txt /app/libgamelibrary-turbojpeg.so
CMD ["dotnet", "Smoke.dll", "--expect-unavailable"]
