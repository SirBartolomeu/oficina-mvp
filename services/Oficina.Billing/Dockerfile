FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["services/Oficina.Billing/Oficina.Billing.csproj", "services/Oficina.Billing/"]
RUN dotnet restore "services/Oficina.Billing/Oficina.Billing.csproj"
COPY services/Oficina.Billing/ services/Oficina.Billing/
RUN dotnet publish "services/Oficina.Billing/Oficina.Billing.csproj" -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
RUN addgroup -S -g 64198 oficina && adduser -S -D -H -u 64198 -G oficina oficina
COPY --from=build --chown=64198:64198 /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER 64198
ENTRYPOINT ["dotnet", "Oficina.Billing.dll"]
