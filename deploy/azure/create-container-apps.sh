#!/usr/bin/env bash
# Creates the seven services and the gateway as Azure Container Apps, from images already in the container registry.
# Run it once, in Azure Cloud Shell (Bash), from the repository root:
#
#   bash deploy/azure/create-container-apps.sh
#
# It asks for the secrets (SQL password, Service Bus connection string, JWT key) instead of reading them from a file,
# so they never end up on disk or in the shell history. Every name below can be overridden with an environment
# variable, e.g. TAG=sha-1a2b3c bash deploy/azure/create-container-apps.sh
#
# Only the gateway is public. The services get internal ingress and reach each other at http://<app-name>/.
# Apps that already exist are skipped (delete one in the Portal to recreate it); later deployments use
# `az containerapp update --image`, which the CD pipeline does.
set -euo pipefail

RG=${RG:-KVBInsurance-RG}
ENV_NAME=${ENV_NAME:-KVBInsurance-Env}
ACR=${ACR:-KVBInsuranceContainer}
SQL_SERVER=${SQL_SERVER:-kvbinsurance}            # without .database.windows.net
DB_PREFIX=${DB_PREFIX:-KVBInsuranceDB-}           # databases are <prefix>Identity, <prefix>Catalog, ...
TAG=${TAG:-initial}                               # image tag to run
PULL_IDENTITY=${PULL_IDENTITY:-kvbinsurance-acr-pull}
TIME_ZONE=${TIME_ZONE:-America/New_York}

echo "Resource group $RG, environment $ENV_NAME, registry $ACR, SQL server $SQL_SERVER, image tag $TAG"
read -rp  "Static Web App URL (https://...azurestaticapps.net): " SITE_URL
read -rp  "SQL admin username: " SQL_USER
read -rsp "SQL admin password (hidden): " SQL_PASSWORD; echo
read -rsp "Service Bus connection string (hidden): " SB_CONNECTION; echo
read -rsp "JWT key, at least 32 characters (hidden): " JWT_KEY; echo
echo "Email through Azure Communication Services (deploy/azure/README.md, \"Email\"). Leave the username empty"
echo "to only write emails to the Notifications log for now."
read -rp  "SMTP username: " SMTP_USER
if [[ -n $SMTP_USER ]]; then
  read -rsp "SMTP password, the Entra app's client secret (hidden): " SMTP_PASSWORD; echo
  read -rp  "Sender address (DoNotReply@<your domain>.azurecomm.net): " MAIL_FROM
fi
SITE_URL=${SITE_URL%/}

[[ $SITE_URL == https://* ]] || { echo "The site URL should start with https://"; exit 1; }
(( ${#JWT_KEY} >= 32 )) || { echo "The JWT key must be at least 32 characters."; exit 1; }
[[ $SB_CONNECTION == Endpoint=sb://* ]] || { echo "That doesn't look like a Service Bus connection string (Endpoint=sb://...)."; exit 1; }
[[ $SQL_PASSWORD != *";"* ]] || { echo "The SQL password contains ';', which breaks the connection string. Choose another."; exit 1; }

ACR_SERVER=$(az acr show --name "$ACR" --query loginServer -o tsv)
ACR_ID=$(az acr show --name "$ACR" --query id -o tsv)

# One identity, allowed to pull images from the registry, shared by every app: no registry password needed.
echo "Setting up the identity that pulls images..."
az identity create --resource-group "$RG" --name "$PULL_IDENTITY" -o none
IDENTITY_ID=$(az identity show --resource-group "$RG" --name "$PULL_IDENTITY" --query id -o tsv)
IDENTITY_PRINCIPAL=$(az identity show --resource-group "$RG" --name "$PULL_IDENTITY" --query principalId -o tsv)
if [[ -z $(az role assignment list --assignee "$IDENTITY_PRINCIPAL" --scope "$ACR_ID" --role AcrPull --query "[0].id" -o tsv) ]]; then
  az role assignment create --assignee-object-id "$IDENTITY_PRINCIPAL" --assignee-principal-type ServicePrincipal \
    --role AcrPull --scope "$ACR_ID" -o none
  echo "Waiting a minute for the permission to take effect..."
  sleep 60
fi

connection_string() {   # $1 = service name as in the database name (Identity, Catalog, ...)
  echo "Server=tcp:${SQL_SERVER}.database.windows.net,1433;Database=${DB_PREFIX}$1;User ID=${SQL_USER};Password=${SQL_PASSWORD};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;"
}

exists() { az containerapp show --resource-group "$RG" --name "$1" --query name -o tsv >/dev/null 2>&1; }

# $1 = app name, $2 = database (Identity, ...), then extra KEY=VALUE settings.
create_service() {
  local name=$1 database=$2; shift 2
  if exists "$name"; then echo "  $name already exists, skipped"; return; fi
  echo "  creating $name..."
  az containerapp create --resource-group "$RG" --environment "$ENV_NAME" --name "$name" \
    --image "$ACR_SERVER/$name:$TAG" --registry-server "$ACR_SERVER" --registry-identity "$IDENTITY_ID" \
    --user-assigned "$IDENTITY_ID" \
    --ingress internal --target-port 8080 \
    --cpu 0.25 --memory 0.5Gi --min-replicas 1 --max-replicas 1 \
    --secrets "database=$(connection_string "$database")" "jwt-key=$JWT_KEY" "service-bus=$SB_CONNECTION" \
    --env-vars ConnectionStrings__Database=secretref:database Jwt__Key=secretref:jwt-key \
               ServiceBus__ConnectionString=secretref:service-bus BusinessTimeZone="$TIME_ZONE" "$@" \
    -o none
}

echo "Creating the services..."
create_service identity Identity
create_service catalog Catalog
create_service quotes Quotes \
  Services__Catalog=http://catalog/
create_service enrollments Enrollments \
  Services__Identity=http://identity/ Services__Catalog=http://catalog/ Services__Quotes=http://quotes/ \
  Services__Billing=http://billing/
create_service billing Billing \
  Services__Enrollments=http://enrollments/ Services__Identity=http://identity/
create_service claims Claims \
  ClaimDocuments__RootPath=/data/claim-documents \
  Services__Identity=http://identity/ Services__Catalog=http://catalog/ Services__Enrollments=http://enrollments/ \
  Services__Billing=http://billing/
# Without an SMTP username, an empty Email__Host makes the service write each email to its log instead of sending it.
# Addresses at domains reserved for testing (example.com, .example, ...) are skipped: a real mail server refuses them.
if [[ -n $SMTP_USER ]]; then
  mail_settings=(Email__Host=smtp.azurecomm.net Email__Port=587 Email__Security=StartTls Email__Username="$SMTP_USER"
                 Email__Password=secretref:smtp-password Email__FromAddress="$MAIL_FROM")
  mail_secrets=("smtp-password=$SMTP_PASSWORD")
else
  mail_settings=("Email__Host= ")
  mail_secrets=()
fi
if exists notifications; then
  echo "  notifications already exists, skipped"
else
  echo "  creating notifications..."
  az containerapp create --resource-group "$RG" --environment "$ENV_NAME" --name notifications \
    --image "$ACR_SERVER/notifications:$TAG" --registry-server "$ACR_SERVER" --registry-identity "$IDENTITY_ID" \
    --user-assigned "$IDENTITY_ID" \
    --ingress internal --target-port 8080 \
    --cpu 0.25 --memory 0.5Gi --min-replicas 1 --max-replicas 1 \
    --secrets "database=$(connection_string Notifications)" "jwt-key=$JWT_KEY" "service-bus=$SB_CONNECTION" "${mail_secrets[@]}" \
    --env-vars ConnectionStrings__Database=secretref:database Jwt__Key=secretref:jwt-key \
               ServiceBus__ConnectionString=secretref:service-bus BusinessTimeZone="$TIME_ZONE" \
               "${mail_settings[@]}" Email__SkipReservedDomains=true Email__AppBaseUrl="$SITE_URL" \
               Services__Identity=http://identity/ Services__Catalog=http://catalog/ Services__Enrollments=http://enrollments/ \
               Services__Claims=http://claims/ \
    -o none
fi

echo "Creating the gateway (public)..."
if exists gateway; then
  echo "  gateway already exists, skipped"
else
  clusters=()
  for svc in identity catalog quotes enrollments billing claims notifications; do
    clusters+=("ReverseProxy__Clusters__${svc}__Destinations__d1__Address=http://${svc}/")
  done
  az containerapp create --resource-group "$RG" --environment "$ENV_NAME" --name gateway \
    --image "$ACR_SERVER/gateway:$TAG" --registry-server "$ACR_SERVER" --registry-identity "$IDENTITY_ID" \
    --user-assigned "$IDENTITY_ID" \
    --ingress external --target-port 8080 \
    --cpu 0.25 --memory 0.5Gi --min-replicas 1 --max-replicas 1 \
    --secrets "jwt-key=$JWT_KEY" \
    --env-vars Jwt__Key=secretref:jwt-key Cors__AllowedOrigins__0="$SITE_URL" \
               Services__Identity=http://identity/ Services__Enrollments=http://enrollments/ Services__Claims=http://claims/ \
               "${clusters[@]}" \
    -o none
fi

GATEWAY=$(az containerapp show --resource-group "$RG" --name gateway --query properties.configuration.ingress.fqdn -o tsv)
echo
echo "Done. The API is at https://$GATEWAY/api"
echo "Check it with: curl https://$GATEWAY/health"
echo "Still to do: mount the claim-documents share in the claims app (see deploy/azure/README.md)."
