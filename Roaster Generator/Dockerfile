FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
# Npgsql probes Kerberos support on Linux. Install the small runtime library so
# the probe is clean even when the application uses password authentication.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Roaster Generator/Roaster Generator.csproj", "Roaster Generator/"]
RUN dotnet restore "Roaster Generator/Roaster Generator.csproj"
COPY . .
WORKDIR "/src/Roaster Generator"
RUN dotnet build "Roaster Generator.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Roaster Generator.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Roaster Generator.dll"]
