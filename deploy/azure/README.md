# Deploying to Azure

The first deployment, by hand. After this, the CD pipeline builds the images and updates the apps.

| Piece | Azure resource |
|---|---|
| Seven services + gateway | Container Apps in one environment (only the gateway is public) |
| Images | Azure Container Registry |
| One database per service | Azure SQL (serverless, free offer) |
| Event bus | Azure Service Bus, **Standard** tier (topics) |
| Claim documents | Azure Files share, mounted into the Claims app |
| Angular app | Azure Static Web Apps (Free) |

Keep the secrets (SQL password, Service Bus connection string, JWT key) out of the repository and out of chat: the
script below asks for them and stores them as Container App secrets.

## 1. Build and push the images

`az acr build` doesn't work on every subscription (`TasksOperationsNotAllowed`), so build with Docker Desktop. In
PowerShell, from the repository root, after `docker login <registry>.azurecr.io` (registry → Access keys → Admin user):

```powershell
$acr = "kvbinsurancecontainer.azurecr.io"; foreach ($svc in "Identity","Catalog","Quotes","Enrollments","Billing","Claims","Notifications") { docker build -f deploy/Dockerfile --build-arg PROJECT=src/Services/$svc/Insurance.$svc/Insurance.$svc.csproj --build-arg ASSEMBLY=Insurance.$svc -t "$acr/$($svc.ToLower()):initial" . ; if ($LASTEXITCODE -ne 0) { break }; docker push "$acr/$($svc.ToLower()):initial" }
docker build -f deploy/Dockerfile --build-arg PROJECT=src/Gateway/Insurance.Gateway/Insurance.Gateway.csproj --build-arg ASSEMBLY=Insurance.Gateway -t kvbinsurancecontainer.azurecr.io/gateway:initial . ; docker push kvbinsurancecontainer.azurecr.io/gateway:initial
```

## 2. Create the container apps

In Azure Cloud Shell (Bash), from a clone of the repository:

```bash
bash deploy/azure/create-container-apps.sh
```

It creates a managed identity that may pull from the registry (no registry password in the apps), then the seven
services (internal ingress, reachable as `http://<name>/` inside the environment) and the gateway (external
ingress), each with its own connection string, the shared JWT key and the Service Bus connection string as secrets.
Names default to this project's resources; override them with environment variables (see the top of the script).

## 3. Mount the claim documents share

The Container Apps environment needs the share registered first (environment → Settings → **Azure Files** →
`claimdocs`, pointing at the `claim-documents` share). Then, in the **claims** app:

1. **Application → Volumes → + Add**: type **Azure file volume**, name `claimdocs`, file share `claimdocs`,
   mount options `uid=1654,gid=1654,dir_mode=0750,file_mode=0640` (1654 is the `app` user the container runs as).
2. **Application → Containers → Edit and deploy**, select the container → **Volume mounts** → add `claimdocs` at
   `/data/claim-documents` → **Save**, then **Create** to deploy the new revision.

## 4. Email (Azure Communication Services)

The Notifications service sends over SMTP, so Azure Communication Services needs no code, only settings.

1. Create an **Email Communication Service** and, in it, an **Azure managed domain** (`<id>.azurecomm.net`, sender
   `DoNotReply@<id>.azurecomm.net`). Create a **Communication Service** and connect the domain to it
   (Communication Service → Email → Domains).
2. Create the SMTP login: an Entra app whose client secret is the password, allowed to send through the
   Communication Service:

   ```bash
   SMTP_APP=$(az ad app create --display-name kvbinsurance-smtp --query appId -o tsv)
   az ad sp create --id "$SMTP_APP" -o none
   az ad app credential reset --id "$SMTP_APP" --display-name smtp --years 1 --query password -o tsv   # the password, shown once
   az role assignment create --assignee "$SMTP_APP" --role "Communication and Email Service Owner" \
     --scope "$(az resource show --resource-group KVBInsurance-RG --name KVBInsurance-Communication --resource-type Microsoft.Communication/communicationServices --query id -o tsv)"
   ```

   Then Communication Service → Settings → **SMTP Usernames** → add one for `kvbinsurance-smtp`.
3. Give the settings to the Notifications app (the script asks for them on a first deployment):

   ```bash
   read -rsp "SMTP password (hidden): " SMTP_PASSWORD; echo
   az containerapp secret set --resource-group KVBInsurance-RG --name notifications --secrets smtp-password="$SMTP_PASSWORD" -o none; unset SMTP_PASSWORD
   az containerapp update --resource-group KVBInsurance-RG --name notifications -o none --set-env-vars \
     Email__Host=smtp.azurecomm.net Email__Port=587 Email__Security=StartTls Email__Username="<SMTP username>" \
     Email__Password=secretref:smtp-password Email__FromAddress="DoNotReply@<domain>.azurecomm.net" Email__SkipReservedDomains=true
   ```

`Email__SkipReservedDomains=true` skips addresses at domains reserved for testing (`example.com`, `*.example`,
`*.test`, ...), which Azure refuses with "Recipient address reserved by RFC 2606"; those emails are recorded as
Skipped. The client secret expires after a year: run `credential reset` again and repeat the `secret set`.
Azure managed domains have low sending limits (Email Communication Service → Quotas); a custom domain raises them.

## 5. Check it

```bash
curl https://<gateway fqdn>/health
az containerapp logs show --resource-group KVBInsurance-RG --name identity --tail 50
```

Each service creates its tables on first start. A serverless database that was paused can take up to a minute to
wake up: the first start may fail and restart once.

## 6. Point the Angular app at the gateway

Set `apiUrl` in `insurance-web/src/environments/environment.ts` to `https://<gateway fqdn>/api`. The Static Web App
gets the build (`insurance-web/dist/insurance-web/browser`); `public/staticwebapp.config.json` sends every app
route to `index.html`.

## Continuous deployment

`.github/workflows/cd.yml` runs after CI passes on `main`. Once the setup below is done it also pushes every image to
the Azure container registry (tags `latest` and `sha-<commit>`), switches each container app to the new `sha-` tag,
then deploys the Angular build to the Static Web App. GitHub signs in to Azure with OpenID Connect: there is no
Azure password stored in GitHub, only the ids below.

### One-time setup

In Cloud Shell, create an identity for GitHub that Azure trusts for runs on `main` (and on `azure-deployment`, to try
the workflow by hand before merging), allowed to change this resource group only:

```bash
# How GitHub names this repository in its sign-in tokens. Some accounts add id numbers
# (owner@123/repo@456): if a run fails with AADSTS700213, copy the part before ":ref:" from that error.
REPO=organizedanvrchy@154699780/Insurance-Full-Stack-App-MSA@1437338850
APP_ID=$(az ad app create --display-name kvbinsurance-github-deploy --query appId -o tsv)
az ad sp create --id "$APP_ID" -o none
for branch in main azure-deployment; do
  az ad app federated-credential create --id "$APP_ID" --parameters "{\"name\":\"github-${branch}\",\"issuer\":\"https://token.actions.githubusercontent.com\",\"subject\":\"repo:${REPO}:ref:refs/heads/${branch}\",\"audiences\":[\"api://AzureADTokenExchange\"]}" -o none
done
az role assignment create --assignee "$APP_ID" --role Contributor --scope "$(az group show --name KVBInsurance-RG --query id -o tsv)" -o none
echo "AZURE_CLIENT_ID=$APP_ID"; echo "AZURE_TENANT_ID=$(az account show --query tenantId -o tsv)"; echo "AZURE_SUBSCRIPTION_ID=$(az account show --query id -o tsv)"
```

Then in GitHub, **Settings → Secrets and variables → Actions** (needs admin rights on the repository):

| Kind | Name | Value |
|---|---|---|
| Secret | `AZURE_CLIENT_ID` | printed above |
| Secret | `AZURE_TENANT_ID` | printed above |
| Secret | `AZURE_SUBSCRIPTION_ID` | printed above |
| Secret | `AZURE_STATIC_WEB_APPS_API_TOKEN` | Static Web App → Overview → Manage deployment token |
| Variable | `AZURE_CONTAINER_REGISTRY` | `KVBInsuranceContainer` |
| Variable | `AZURE_RESOURCE_GROUP` | `KVBInsurance-RG` |

The Azure steps run only while `AZURE_CONTAINER_REGISTRY` is set. To try it before merging: **Actions → CD → Run
workflow**, branch `azure-deployment`.

To go back to an earlier version, point the apps at its tag:
`az containerapp update --resource-group KVBInsurance-RG --name <app> --image <registry>.azurecr.io/<app>:sha-<commit>`.
