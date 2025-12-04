FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
RUN apk add --upgrade --no-cache build-base clang zlib-dev
WORKDIR /src
COPY . .
WORKDIR "/src/src"
RUN dotnet publish "Dantherm2Mqtt.csproj" -r linux-musl-x64 -c Release -o /out
RUN rm -f /out/*.dbg /out/*.Development.json && chmod +x /out/Dantherm2Mqtt

FROM alpine AS final
WORKDIR /app
COPY --from=build /out/* .
ENTRYPOINT ["/app/Dantherm2Mqtt"]