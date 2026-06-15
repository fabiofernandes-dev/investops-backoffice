FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend/ ./backend/
COPY contracts/proto/ ./contracts/proto/
RUN dotnet publish backend/src/InvestOps.Api/InvestOps.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "InvestOps.Api.dll"]
