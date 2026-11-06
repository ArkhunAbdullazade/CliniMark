FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-env
WORKDIR /app

COPY CliniMark.sln ./
COPY Directory.Build.props ./

COPY src/CliniMark.Core/*.csproj ./src/CliniMark.Core/
COPY src/CliniMark.Infrastructure/*.csproj ./src/CliniMark.Infrastructure/
COPY src/CliniMark.Presentation/*.csproj ./src/CliniMark.Presentation/
RUN dotnet restore src/CliniMark.Presentation/CliniMark.Presentation.csproj

COPY src/ ./src/
RUN dotnet publish src/CliniMark.Presentation/CliniMark.Presentation.csproj -c Release -o /app/out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14
WORKDIR /app
COPY --from=build-env /app/out .
RUN mkdir -p /app/keys && chown app:app /app/keys

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app

ENTRYPOINT ["dotnet", "CliniMark.Presentation.dll"]
