# Azure Bicep, Migration Job, and Blue-Green Release — Implementation Plan

**Goal:** Provision a repeatable Azure Server environment with Bicep and deploy immutable Server
images through an explicit migration gate and controlled Container Apps revision traffic.

**Depends on:** `2026-08-04-deployment-profiles-server-artifact.md`.  
**Can ship before:** durable turns, provided the current turn duration is proven below the ingress
limit and the initial rollout uses conservative scaling.

## Constraints

- Bicep owns stable Azure infrastructure; the release workflow owns image revisions and traffic.
- No Terraform state or Terraform code in this phase.
- Keep resource names, parameters and outputs explicit so a later Terraform migration is mechanical.
- Server never runs database migrations during API startup.
- The migration job runs once and must succeed before a new revision receives traffic.
- Schema changes follow expand/contract; application rollback never attempts database rollback.
- GitHub authenticates to Azure with OIDC, not a stored client secret.

## Task 1: Create a dedicated migration executable

**Files:**

- Create: `wretched-whispers-server/WretchedWhispers.Migrations/WretchedWhispers.Migrations.csproj`
- Create: `wretched-whispers-server/WretchedWhispers.Migrations/Program.cs`
- Modify: `wretched-whispers-server/WretchedWhispers.sln`
- Modify: `wretched-whispers-server/WretchedWhispers.Api/Program.cs`
- Test: create `wretched-whispers-server/WretchedWhispers.Tests/Migrations/MigrationRunnerTests.cs`

- [ ] Reference Infrastructure directly; do not boot the API host or register game services.
- [ ] Accept provider and connection string through the same configuration keys as the API.
- [ ] Select `PostgresWwDbContext` for Server and `WretchedWhispersDbContext` for SQLite tooling.
- [ ] Log current, pending, and applied migration IDs; return nonzero on invalid configuration or
      failure.
- [ ] Serialize execution with a PostgreSQL advisory lock so accidental duplicate job starts cannot
      migrate concurrently.
- [ ] Remove `MigrateAsync()` from Server startup. Retain automatic SQLite migration for Desktop and
      StandaloneContainer.
- [ ] Make Server readiness fail when migrations expected by that binary are pending.
- [ ] Test a fresh database, an up-to-date no-op, invalid configuration, and a failed migration exit.

## Task 2: Package API and migrator atomically

**Files:**

- Modify: `Dockerfile`
- Modify: `.dockerignore`

- [ ] Publish the API and migration projects from the same commit into the Server image.
- [ ] Keep the API entrypoint unchanged.
- [ ] Support the migration job command:

```bash
dotnet WretchedWhispers.Migrations.dll
```

- [ ] Give the runtime API a DML-only PostgreSQL connection and the migration job a separate
      DDL-capable connection.
- [ ] Add a container-level migration smoke test against PostgreSQL in CI.

## Task 3: Add production health contracts

**Files:**

- Modify: `wretched-whispers-server/WretchedWhispers.Api/Program.cs`
- Create: `wretched-whispers-server/WretchedWhispers.Api/Health/DatabaseHealthCheck.cs`
- Test: create `wretched-whispers-server/WretchedWhispers.Tests/Health/HealthEndpointTests.cs`

- [ ] `/health/live`: process is responsive; no external dependency checks.
- [ ] `/health/ready`: database reachable and no migrations pending for this binary.
- [ ] Keep probes unauthenticated and return no secret-bearing details.
- [ ] Test healthy, database-unavailable, and pending-migration states.

## Task 4: Provision Azure with Bicep

**Files:**

- Create: `deploy/azure/main.bicep`
- Create: `deploy/azure/production.bicepparam.example`
- Create: `deploy/azure/bootstrap-database.sql`
- Create: `deploy/azure/README.md`
- Modify: `.gitignore` if local parameter files contain secrets

- [ ] Declare the resource group-scoped deployment inputs: location, environment name, image,
      custom domain toggle, PostgreSQL sizing, replica bounds and Azure OpenAI settings.
- [ ] Provision a Container Apps environment with Log Analytics.
- [ ] Provision the Server Container App with external HTTPS ingress on port 8080, multiple revision
      mode, zero traffic to newly created revisions by default, and startup/readiness/liveness probes.
- [ ] Provision a manually triggered Container Apps Job with one replica and the migration command.
- [ ] Provision PostgreSQL Flexible Server, database, and network access appropriate to the selected
      Container Apps environment topology.
- [ ] Add an idempotent bootstrap script that creates a schema-owning migrator role and a DML-only
      application role, grants the minimum permissions, and can safely run again.
- [ ] Provision Key Vault and managed identity; expose database and Azure OpenAI secrets through Key
      Vault references.
- [ ] Persist ASP.NET Data Protection keys in PostgreSQL as the application already supports.
- [ ] Output only resource names, IDs and public/staging FQDNs—never secret values.
- [ ] Start with one API replica; raise the maximum only after durable turns are deployed or the
      current request-bound behavior has been load-tested.

Keep this as one Bicep file initially. Split modules only when a resource group becomes reused or the
file becomes materially difficult to review.

## Task 5: Add infrastructure validation and deployment

**Files:**

- Create: `.github/workflows/azure-infra.yml`

- [ ] Pull requests run `az bicep build` and lint/validate the template.
- [ ] An authenticated manual job runs Azure `what-if` against production.
- [ ] Applying infrastructure requires a protected GitHub Environment approval.
- [ ] Configure GitHub-to-Azure workload identity federation and minimum required roles.
- [ ] Pin action versions and Azure provider API versions.
- [ ] Document the one-time bootstrap: subscription, OIDC identity, resource group and Key Vault
      permissions.
- [ ] After the PostgreSQL resource exists, run the database bootstrap through the protected
      infrastructure workflow and store both generated connection strings in Key Vault; never pass
      them through Bicep outputs.

## Task 6: Implement migration-gated blue-green delivery

**Files:**

- Create: `.github/workflows/deploy-server.yml`
- Create: `scripts/verify-server-revision.sh`

- [ ] Build and push the Server image with an immutable commit SHA tag.
- [ ] Update the migration Job to that exact image, start it, wait for completion, and stop on any
      non-success result.
- [ ] Create the green API revision with 0% production traffic and label it `green`.
- [ ] Smoke its label-specific URL: liveness, readiness, UI, register/login, authenticated request,
      and one streamed turn.
- [ ] Shift traffic in explicit stages (initially 10%, 50%, 100%) with a health check between each.
- [ ] Keep the previous `blue` revision active for rapid application rollback.
- [ ] On rollback, restore traffic only; never attempt to reverse the database migration.
- [ ] Deactivate obsolete revisions only after an observation window.

## Task 7: Enforce expand/contract migration policy

**Files:**

- Create: `docs/database-migrations.md`
- Modify: pull-request template if one exists

- [ ] Document expand, code rollout, and later contract phases with examples.
- [ ] Require every destructive migration to state which deployed version stopped using the old
      schema and in which later release it may be removed.
- [ ] Add a CI compatibility test that starts the previous Server image against the newly migrated
      schema for migrations that change existing structures.

## Acceptance gate

- [ ] Bicep validation and production `what-if` contain no destructive surprise.
- [ ] Fresh environment provisions without portal edits.
- [ ] Migration failure prevents green revision rollout.
- [ ] Green receives no production traffic until all smoke checks pass.
- [ ] Traffic can move back to blue without changing the database.
- [ ] API identity cannot perform DDL; migration identity is available only to the Job.
- [ ] Secrets are Key Vault references and GitHub uses OIDC.

## Terraform migration boundary

Bicep is deliberately confined to `deploy/azure`. Application code consumes only environment
variables, secret references, resource names and URLs. A later Terraform conversion replaces the
resource declarations and imports existing resources; it does not change build profiles, runtime
configuration, migration commands, or the release protocol.

## Deliberately deferred

- Multi-region deployment and database failover.
- WAF/Application Gateway, private endpoints and enterprise network topology.
- Automatic contract migrations.
- Terraform remote-state bootstrap.
