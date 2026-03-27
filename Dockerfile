FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/FactChecker.Web/FactChecker.Web.csproj", "src/FactChecker.Web/"]
COPY ["src/FactChecker.Core/FactChecker.Core.csproj", "src/FactChecker.Core/"]
COPY ["src/FactChecker.Infrastructure/FactChecker.Infrastructure.csproj", "src/FactChecker.Infrastructure/"]
RUN dotnet restore "src/FactChecker.Web/FactChecker.Web.csproj"
COPY . .
WORKDIR "/src/src/FactChecker.Web"
RUN dotnet build "FactChecker.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "FactChecker.Web.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FactChecker.Web.dll"]
