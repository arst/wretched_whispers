# Azure Server deployment

This directory owns stable Azure infrastructure. The release workflow owns immutable images,
migration executions, revisions, labels, and traffic.

## One-time setup

1. Register `Microsoft.App`, `Microsoft.OperationalInsights`, `Microsoft.ContainerRegistry`,
   `Microsoft.DBforPostgreSQL`, `Microsoft.KeyVault`, and `Microsoft.ManagedIdentity` in the Azure
   subscription, then create the target resource group.
2. Create a Microsoft Entra application/service principal with a GitHub federated credential for
   the repository and `production` GitHub Environment. Assign it `Contributor` and
   `Role Based Access Control Administrator` on only the target resource group. Bicep uses the
   latter to grant ACR pull and Key Vault access to the workload identity.
3. Protect the GitHub `production` Environment with required reviewers.
4. Configure repository/environment variables:
   `AZURE_CLIENT_ID`, `AZURE_PRINCIPAL_OBJECT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`,
   `AZURE_RESOURCE_GROUP`, `AZURE_ENVIRONMENT_NAME`, `AZURE_LOCATION`, `AZURE_OPENAI_ENDPOINT`, and
   `AZURE_OPENAI_DEPLOYMENT`.
5. Configure Environment secrets `POSTGRES_ADMIN_PASSWORD` and `AZURE_OPENAI_KEY`.

The client ID identifies the OIDC application; the principal object ID is the corresponding
service principal object used in Azure role assignments.

## Provision and release

1. Run **Azure infrastructure / what-if** and review the result.
2. Run **Azure infrastructure / apply** after approval.
3. Run **Azure infrastructure / bootstrap** once. It temporarily permits the GitHub runner IP,
   creates/rotates `ww_migrator` and DML-only `ww_app`, stores their connection strings and the
   Azure OpenAI key in Key Vault, removes the temporary firewall rule, and provisions the manual
   migration job.
4. Run **Deploy Server**. It builds the commit-SHA image in ACR, runs the migration job, verifies a
   zero-production-traffic green revision, then moves traffic through 10%, 50%, and 100%.

`production.bicepparam.example` documents every template input. Copy it without the `.example`
suffix for local use; real parameter files are ignored because the administrator password is a
secure deployment input.

## Operational contract

- The API receives only the `ww_app` connection; the job alone receives `ww_migrator`.
- Both connections and the Azure OpenAI key are versionless Key Vault references, so rotation does
  not require editing Bicep.
- The previous `blue` revision stays active. Rollback is traffic-only:

  ```bash
  az containerapp ingress traffic set -g RESOURCE_GROUP -n APP \
    --label-weight blue=100 green=0
  ```

- The default public PostgreSQL rule permits connections originating from Azure services because a
  consumption Container Apps environment has dynamic egress addresses. Move both services into a
  managed VNet before tightening this to private access.
- Keep `maxReplicas=1` until request-bound SSE turns have been load-tested or durable turns ship.
- A custom domain requires an existing Container Apps environment certificate ID plus DNS. Pass
  both `customDomainName` and `customDomainCertificateId`; leave both empty otherwise.

The workflows use [GitHub OIDC for Azure](https://learn.microsoft.com/azure/developer/github/connect-from-azure-identity),
[Container Apps multiple revisions](https://learn.microsoft.com/azure/container-apps/revisions),
and [Key Vault-backed Container Apps secrets](https://learn.microsoft.com/azure/container-apps/manage-secrets).
