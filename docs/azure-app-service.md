# Azure App Service deployment

This repository builds one Linux App Service package containing the ASP.NET Core API and Vite production assets served from the same origin. The intended promotion path is feature branch → `dev` → `qa` → `main`. CI runs on pushes to `feature/**`, `dev`, `qa`, and `main`, and on pull requests targeting `dev`, `qa`, or `main`; it never logs in to Azure. Branch names alone do not select different App Services. Azure deployment is a separate `workflow_dispatch` workflow available from `main` only; it builds the same artifact, waits for the `production` GitHub environment's protection rules, requires typing `letscarpool`, then deploys that artifact without rebuilding it. The CI workflow depends on the integrated .NET 10 `global.json` and strict SQL test markers; this pipeline commit alone is not runnable from the older baseline.

Deployment intentionally fails closed until external setup and QA are complete. Do not set `AZURE_DEPLOYMENT_ENABLED=true` before the checks below pass. No Azure resource, identity, role assignment, federated credential, GitHub environment, secret, or repository setting is created by these files.

## GitHub configuration

An administrator should create a `production` environment before running deployment and configure required reviewers, prevent self-review, and restrict deployments to `main`. Required-reviewer availability depends on repository visibility and GitHub plan. Add these as environment **variables** (not secrets):

| Variable | Value |
| --- | --- |
| `AZURE_DEPLOYMENT_ENABLED` | Leave unset or `false` until operator readiness and QA are approved; then set to `true` to unlock deployment. |
| `AZURE_CLIENT_ID` | Client ID of the deployment identity. |
| `AZURE_TENANT_ID` | Entra tenant ID. |
| `AZURE_SUBSCRIPTION_ID` | Subscription ID containing the existing app. |
| `AZURE_WEBAPP_NAME` | `letscarpool` |
| `AZURE_WEBAPP_HOSTNAME` | `letscarpool-bucxd2amgfc5aadu.westus2-01.azurewebsites.net` |

Configure a federated credential for this repository's GitHub `production` environment subject and the GitHub Actions OIDC issuer. Grant its identity only the deployment permissions needed for this existing App Service; do not grant SQL schema or database permissions to the deployment identity. The workflow requests `id-token: write` only for the gated deploy job. It uses `azure/login` OIDC and does not use a publish profile, password, or stored token.

## App and data readiness

Before enabling the environment variable, an operator must verify the existing `letscarpool` App Service resource and its host, Linux/ASP.NET Core hosting configuration, startup behavior, TLS/HTTPS, logging, health/availability, and rollback access. Confirm that the configured App Service runtime can run the exact .NET 10 target produced by the integrated application; if App Service does not provide a compatible runtime, use a .NET 10 self-contained deployment or an explicitly reviewed container plan before enabling deploy. Do not assume a production or staging slot exists.

If the integrated SQL trip store is selected, set the app configuration `CarpoolStore__Provider=SqlServer` and `ConnectionStrings__DefaultConnection` to an encrypted SQL connection using `Authentication=Active Directory Managed Identity`. Prefer the system-assigned App Service identity. The SQL database must already contain the reviewed schema-2 trip store, and that app identity must already have only the documented runtime access. Configure SQL networking/firewall and database identity separately. Keep `CarpoolStore__Provider` unset or `Json` only for an explicitly reviewed non-production use; local JSON storage is not safe for a multi-instance or durable production deployment.

Schema setup is a separate, operator-reviewed activity. Review and run the versioned SQL scripts only through the approved migration process, against the independently verified intended database. The app startup and deployment workflow do not create or migrate schema, import JSON data, seed trips, or grant permissions. Keep migration credentials separate from the runtime identity. Preserve schema version 2 and the trip API's location provenance contract during upgrades.

## SQL CI verification status

The current local environment could not run SQL acceptance checks: its Docker engine is unavailable and SQL Server LocalDB is not working. This is a local verification blocker, not a passing SQL result. CI starts a disposable SQL Server 2022 service, creates a unique loopback `LetsCarpoolTest_<32 hex GUID>` database, and applies `001_create_trip_store.sql` only to that database. The strict test process receives `LETS_CARPOOL_RUN_SQL_INTEGRATION=true` and `LETS_CARPOOL_TEST_SQL_CONNECTION`; the test harness rejects a missing connection, non-loopback host, or non-unique database. Its ephemeral SQL-auth password is run-scoped and masked. The full connection string is constructed only for the strict test process and is not written to `GITHUB_ENV`, job outputs, logs, or the application artifact. CI requires at least 21 focused checks and exactly one `SQL_INTEGRATION_RESULT=PASS` case with at least 33 scenario assertions. The current scenario covers same-key replay, capability rotation/revocation/expiry, stale plans, concurrent capacity, rollback after mutation, SQL outage fail-closed behavior, token privacy, schema 2, and provenance. It must also report `SQL_PROCESS_RESTART_TESTED=false`; process restart has not been verified. Missing connection, failure, skip, or lower coverage blocks artifact publication. An `always()` cleanup step drops only the unique owned test database; GitHub removes its ephemeral SQL service at job end. Production Entra authentication validation is unchanged. Until a hosted run demonstrates the complete workflow, describe it as configured and locally workflow-validated; do not claim hosted build, SQL integration, or deployment success.

## Mapbox configuration

`VITE_MAPBOX_ACCESS_TOKEN` is a **public browser token** injected at client build time. It is visible to every browser and must never be treated as a secret or used as a server credential. Supply it as a GitHub Actions variable if a map-enabled build is desired; restrict allowed referrers to the production HTTPS origin and the minimum map scopes. A missing token disables the map, so decide and verify the intended UI behavior before deployment. Never put SQL credentials, managed-identity tokens, or other server secrets in any `VITE_*` variable. Server-side configuration belongs in App Service settings or Key Vault references, and SQL auth uses managed identity rather than a stored password.

## Deploy, smoke test, rollback

After environment protection and all external readiness checks are in place, promote the approved build through the feature → dev → qa → main path, then start **Manually deploy LetsCarpool to Azure** from the Actions tab on `main`, enter `letscarpool`, and approve the protected environment. The build and deploy jobs use the same uploaded zip. Pushes and pull requests only build/test and cannot deploy.

After deployment, smoke test `https://letscarpool-bucxd2amgfc5aadu.westus2-01.azurewebsites.net/` for the React shell and static assets, then exercise API health/availability and a complete create/join/organizer flow using synthetic trips only. Verify safe capability handling, schema-2 payload compatibility, location provenance validation, and that SQL failures fail closed. Do not create a trip with private or real travel data. Delete any synthetic trip data only through the approved retention procedure.

For rollback, redeploy the previously approved immutable App Service artifact using the same protected process, or use a previously verified slot only if one is confirmed to exist. Do not roll back schema by deleting tables or running reverse/destructive scripts; coordinate backward-compatible application and schema changes with the migration operator. Preserve logs and identify the artifact/run used for both deployments.

## Workflow validation references

The workflows use the official [Azure Login action](https://github.com/Azure/login), [Azure Web Apps Deploy action](https://github.com/Azure/webapps-deploy), [GitHub service container and artifact behavior](https://docs.github.com/en/actions/tutorials/use-containerized-services/use-docker-service-containers), and [GitHub deployment environment protections](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments). The test service follows Microsoft's [SQL Server Linux container guidance](https://learn.microsoft.com/en-us/sql/linux/containers/deploy?view=sql-server-linux-ver15) and uses the documented [`SQLCMDPASSWORD` environment variable](https://learn.microsoft.com/en-us/sql/tools/sqlcmd/sqlcmd-utility?view=sql-server-linux-ver16) instead of a password command-line argument. Review action major versions and GitHub environment protection availability when updating or configuring the pipeline.
