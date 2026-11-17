FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["global.json", "./"]
COPY ["services/Oficina.Auth/Oficina.Auth.csproj", "services/Oficina.Auth/"]
COPY ["services/Oficina.Auth.Local/Oficina.Auth.Local.csproj", "services/Oficina.Auth.Local/"]
RUN dotnet restore "services/Oficina.Auth.Local/Oficina.Auth.Local.csproj"
COPY services/Oficina.Auth/ services/Oficina.Auth/
COPY services/Oficina.Auth.Local/ services/Oficina.Auth.Local/
RUN dotnet publish "services/Oficina.Auth.Local/Oficina.Auth.Local.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
RUN addgroup -S -g 64198 oficina && adduser -S -D -H -u 64198 -G oficina oficina
COPY --from=build --chown=oficina:oficina /app/publish .
ENV ASPNETCORE_URLS=http://+:8080 DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
EXPOSE 8080
USER 64198
ENTRYPOINT ["dotnet", "Oficina.Auth.Local.dll"]
