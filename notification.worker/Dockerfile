FROM mcr.microsoft.com/dotnet/sdk:8.0@sha256:306301580fcaa5b445180e759db59309979002d1000669cb4cf58a567d0014bc AS build
WORKDIR /src

COPY notification.worker/NSA.Worker.csproj notification.worker/
RUN dotnet restore "notification.worker/NSA.Worker.csproj"

COPY notification.worker/ notification.worker/
RUN dotnet publish "notification.worker/NSA.Worker.csproj" \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0@sha256:9d94ecf60a21c6e7a784cf0761fbd4a8391646617a0ff2f39621443d580cc2c3 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER app
ENTRYPOINT ["dotnet", "NSA.Worker.dll"]

