FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build-env
WORKDIR /app

# TODO: make this work for faster builds..
# COPY *.csproj ./
# RUN dotnet restore

COPY . ./
RUN dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/core/runtime:3.1
WORKDIR /app
COPY --from=build-env /app/out .
ENV WOOFBOT_AUTO_LIFETIME=true
#ENV WOOFBOT_CONFIG_PATH=
ENTRYPOINT ["dotnet", "WoofBot.dll"]