FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY *.sln ./
COPY Directory.Build.props ./
COPY _nuget.config nuget.config

COPY src/Kroki.Mcp.Contracts/Kroki.Mcp.Contracts.csproj src/Kroki.Mcp.Contracts/
COPY src/Kroki.Mcp.Core/Kroki.Mcp.Core.csproj src/Kroki.Mcp.Core/
COPY src/Kroki.Mcp.Server/Kroki.Mcp.Server.csproj src/Kroki.Mcp.Server/

RUN dotnet restore src/Kroki.Mcp.Server/Kroki.Mcp.Server.csproj

COPY src/ src/

RUN dotnet publish src/Kroki.Mcp.Server/Kroki.Mcp.Server.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV DOTNET_ROLL_FORWARD=LatestMajor \
    ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Kroki.Mcp.Server.dll"]
