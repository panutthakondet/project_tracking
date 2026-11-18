FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY SecurityWorker/ SecurityWorker/
COPY Services/Security/SecurityContracts.cs Services/Security/SecurityContracts.cs
RUN dotnet publish SecurityWorker/SecurityWorker.csproj -c Release -o /out
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
ENTRYPOINT ["dotnet", "SecurityWorker.dll"]
