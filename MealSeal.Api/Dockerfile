FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
# Npgsql's connection negotiation (triggered by `channel_binding=require` in the Neon connection
# string) needs libgssapi, which this base image doesn't ship. Without it, opening a *new* pooled
# connection throws, while a reused one works fine — that's what caused the intermittent
# "every other request" 500s on /auth/login (and anything else that hits the database).
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["MealSeal.Api/MealSeal.Api.csproj", "MealSeal.Api/"]
RUN dotnet restore "MealSeal.Api/MealSeal.Api.csproj"
COPY . .
WORKDIR "/src/MealSeal.Api"
RUN dotnet build "./MealSeal.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./MealSeal.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MealSeal.Api.dll"]
