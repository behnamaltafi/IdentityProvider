FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet publish "IdentityProvider.Api/IdentityProvider.Api.csproj" \
    --configuration Release \
    --output /app/publish \
    -p:ContinuousIntegrationBuild=true \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .
USER $APP_UID

ENTRYPOINT ["dotnet", "IdentityProvider.Api.dll"]
