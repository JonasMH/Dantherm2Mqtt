#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER app
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["src/Dantherm2Mqtt.csproj", "src/"]
RUN dotnet restore "src/Dantherm2Mqtt.csproj"
COPY . .
WORKDIR "/src/src"
RUN dotnet publish "Dantherm2Mqtt.csproj" --no-restore -c Release -o /app
RUN rm /app/*.dbg /app/*.Development.json

FROM mcr.microsoft.com/dotnet/nightly/runtime-deps:8.0-alpine-aot AS final
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Dantherm2Mqtt.dll"]