FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["global.json", "./"]
COPY ["src/OficinaMvp.Domain/OficinaMvp.Domain.csproj", "src/OficinaMvp.Domain/"]
COPY ["src/OficinaMvp.Application/OficinaMvp.Application.csproj", "src/OficinaMvp.Application/"]
COPY ["src/OficinaMvp.Infrastructure/OficinaMvp.Infrastructure.csproj", "src/OficinaMvp.Infrastructure/"]
COPY ["src/OficinaMvp.Api/OficinaMvp.Api.csproj", "src/OficinaMvp.Api/"]
RUN dotnet restore "src/OficinaMvp.Api/OficinaMvp.Api.csproj"

COPY . .
WORKDIR "/src/src/OficinaMvp.Api"
RUN dotnet publish "OficinaMvp.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app

RUN addgroup -S -g 64198 oficina && \
    adduser -S -D -H -u 64198 -G oficina oficina && \
    mkdir -p /data && \
    chown -R oficina:oficina /app /data

COPY --from=build --chown=oficina:oficina /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

EXPOSE 8080
USER 64198

ENTRYPOINT ["dotnet", "OficinaMvp.Api.dll"]
