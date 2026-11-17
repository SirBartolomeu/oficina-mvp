FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["services/Oficina.Execution/Oficina.Execution.csproj", "services/Oficina.Execution/"]
RUN dotnet restore "services/Oficina.Execution/Oficina.Execution.csproj"
COPY services/Oficina.Execution/ services/Oficina.Execution/
RUN dotnet publish "services/Oficina.Execution/Oficina.Execution.csproj" -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
RUN addgroup -S -g 64198 oficina && adduser -S -D -H -u 64198 -G oficina oficina
COPY --from=build --chown=64198:64198 /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER 64198
ENTRYPOINT ["dotnet", "Oficina.Execution.dll"]
