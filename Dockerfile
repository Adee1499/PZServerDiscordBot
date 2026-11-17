FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/PZServerDiscordBot.csproj .
RUN dotnet restore
COPY src/ .
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /data
COPY --from=build /app /app
ENTRYPOINT ["dotnet", "/app/PZServerDiscordBot.dll"]
