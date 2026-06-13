# ─── Build ─────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# restore (camada cacheável)
COPY AceleraBot.Api/*.csproj AceleraBot.Api/
RUN dotnet restore AceleraBot.Api/AceleraBot.Api.csproj

# publish
COPY AceleraBot.Api/ AceleraBot.Api/
RUN dotnet publish AceleraBot.Api/AceleraBot.Api.csproj -c Release -o /app /p:UseAppHost=false

# ─── Runtime ─────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
COPY --from=build /app .

# O app lê a env PORT (injetada pelo Render) e ouve em 0.0.0.0:$PORT
EXPOSE 3000
ENTRYPOINT ["dotnet", "AceleraBot.Api.dll"]
