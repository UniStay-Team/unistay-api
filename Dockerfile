FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore UniStay.Api/UniStay.Api.csproj --locked-mode && dotnet publish UniStay.Api/UniStay.Api.csproj --no-restore -c Release -o /out
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
RUN mkdir -p /app/media /app/mail && chown -R $APP_UID:$APP_UID /app/media /app/mail
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080 MediaDirectory=/app/media LocalMailDirectory=/app/mail
ENTRYPOINT ["dotnet", "UniStay.Api.dll"]
