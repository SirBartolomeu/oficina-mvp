FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/OficinaMvp.Api/OficinaMvp.Api.csproj", "src/OficinaMvp.Api/"]
RUN dotnet restore "src/OficinaMvp.Api/OficinaMvp.Api.csproj"

COPY . .
WORKDIR "/src/src/OficinaMvp.Api"
RUN dotnet publish "OficinaMvp.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN mkdir /data

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "OficinaMvp.Api.dll"]
