# Build depuis la RACINE de la solution :
#   docker build -f src/CleanArchitectureSkeleton.Api/Dockerfile -t orders-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/CleanArchitectureSkeleton.Api -c Release -o /app --no-self-contained

# Image d'exécution minimale. Le runtime ASP.NET écoute sur 8080 et tourne en utilisateur non-root.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
# Dossier de données SQLite (à monter sur un volume pour la persistance).
ENV ConnectionStrings__orders-db="Data Source=/data/orders.db;Default Timeout=30"
RUN mkdir -p /data && chown -R $APP_UID /data
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "CleanArchitectureSkeleton.Api.dll"]
