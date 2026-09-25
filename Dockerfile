FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src
 
COPY ["PickNBook.Api.csproj", "./"]
RUN dotnet restore "PickNBook.Api.csproj"
 
COPY . .
RUN dotnet publish "PickNBook.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false
 
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
 
RUN apk add --no-cache \
    fontconfig \
    ttf-dejavu
 
RUN adduser -D appuser
 
WORKDIR /app
COPY --from=build /app/publish .
 
ENV ASPNETCORE_URLS=http://+:8080
 
EXPOSE 8080
 
USER appuser
ENTRYPOINT ["dotnet", "PickNBook.Api.dll"]
