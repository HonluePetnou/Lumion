# Image du serveur Lumion. Contexte de construction : la racine du dépôt.
#   docker build -f Lumion.Api/Dockerfile -t lumion-api .

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS construction
WORKDIR /src
# Seuls le cœur métier et l'API sont nécessaires (pas l'application MAUI).
COPY Lumion.Core/ Lumion.Core/
COPY Lumion.Api/ Lumion.Api/
RUN dotnet publish Lumion.Api/Lumion.Api.csproj -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=construction /app .
# Base SQLite dans un volume : elle survit aux mises à jour de l'image.
ENV Lumion__Donnees=/donnees \
    ASPNETCORE_URLS=http://0.0.0.0:5080
VOLUME /donnees
EXPOSE 5080
ENTRYPOINT ["dotnet", "Lumion.Api.dll"]
