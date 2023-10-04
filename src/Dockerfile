#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER app
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
RUN apk add --upgrade --no-cache build-base clang zlib-dev
WORKDIR /src
COPY ["src/Dantherm2Mqtt.csproj", "src/"]
RUN dotnet restore -r linux-musl-x64 "src/Dantherm2Mqtt.csproj"
COPY . .
WORKDIR "/src/src"
RUN dotnet publish "Dantherm2Mqtt.csproj" --no-restore -c Release -o /app
RUN rm /app/*.dbg /app/*.Development.json

FROM mcr.microsoft.com/dotnet/nightly/runtime-deps:8.0-alpine-aot AS final
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["./Dantherm2Mqtt"]