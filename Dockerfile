FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build-env
WORKDIR /app

# TODO: make this work for faster builds..
# COPY *.csproj ./
# RUN dotnet restore

COPY . ./
RUN dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/runtime:5.0
WORKDIR /app
COPY --from=build-env /app/out .
ENV DOTNET_EnableDiagnostics=0
ENV WOOFBOT_AUTO_LIFETIME=true
#ENV WOOFBOT_CONFIG_PATH=
ENTRYPOINT ["dotnet", "WoofBot.dll"]