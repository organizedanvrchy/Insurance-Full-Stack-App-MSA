# One Dockerfile for every .NET service and the gateway. docker-compose.yml passes which one to build:
#   PROJECT  = path of the .csproj to publish
#   ASSEMBLY = its output assembly name
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /src

# Restore first, from the project files only, so Docker caches the packages until a .csproj changes.
COPY Directory.Build.props ./
COPY src/BuildingBlocks/Insurance.BuildingBlocks/*.csproj src/BuildingBlocks/Insurance.BuildingBlocks/
COPY src/Gateway/Insurance.Gateway/*.csproj src/Gateway/Insurance.Gateway/
COPY src/Services/Identity/Insurance.Identity/*.csproj src/Services/Identity/Insurance.Identity/
COPY src/Services/Identity/Insurance.Identity.Contracts/*.csproj src/Services/Identity/Insurance.Identity.Contracts/
COPY src/Services/Catalog/Insurance.Catalog/*.csproj src/Services/Catalog/Insurance.Catalog/
COPY src/Services/Catalog/Insurance.Catalog.Contracts/*.csproj src/Services/Catalog/Insurance.Catalog.Contracts/
COPY src/Services/Quotes/Insurance.Quotes/*.csproj src/Services/Quotes/Insurance.Quotes/
COPY src/Services/Quotes/Insurance.Quotes.Contracts/*.csproj src/Services/Quotes/Insurance.Quotes.Contracts/
COPY src/Services/Enrollments/Insurance.Enrollments/*.csproj src/Services/Enrollments/Insurance.Enrollments/
COPY src/Services/Enrollments/Insurance.Enrollments.Contracts/*.csproj src/Services/Enrollments/Insurance.Enrollments.Contracts/
COPY src/Services/Billing/Insurance.Billing/*.csproj src/Services/Billing/Insurance.Billing/
COPY src/Services/Billing/Insurance.Billing.Contracts/*.csproj src/Services/Billing/Insurance.Billing.Contracts/
COPY src/Services/Claims/Insurance.Claims/*.csproj src/Services/Claims/Insurance.Claims/
COPY src/Services/Claims/Insurance.Claims.Contracts/*.csproj src/Services/Claims/Insurance.Claims.Contracts/
COPY src/Services/Notifications/Insurance.Notifications/*.csproj src/Services/Notifications/Insurance.Notifications/
RUN dotnet restore "$PROJECT"

COPY src/ src/
RUN dotnet publish "$PROJECT" -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG ASSEMBLY
ENV APP_DLL=${ASSEMBLY}.dll \
    ASPNETCORE_URLS=http://+:8080
WORKDIR /app
COPY --from=build /app .
# Claims keeps uploaded documents here (a named volume in docker-compose.yml, which inherits this owner).
RUN mkdir -p /data/claim-documents && chown app /data/claim-documents
USER app
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_DLL\""]
