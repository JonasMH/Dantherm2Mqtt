#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0-preview-jammy AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0-preview-jammy AS build
WORKDIR /src
COPY ["src/Dantherm2Mqtt.csproj", "src/"]
RUN dotnet restore --use-current-runtime "src/Dantherm2Mqtt.csproj"
COPY . .
WORKDIR "/src/src"
RUN dotnet publish "Dantherm2Mqtt.csproj" -c Release -o /app/publish --use-current-runtime --self-contained false --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0-preview-jammy-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Dantherm2Mqtt.dll"]