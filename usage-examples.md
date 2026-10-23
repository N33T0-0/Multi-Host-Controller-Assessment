# Usage Examples

This guide walks through common tasks using the Master Host dashboard, the Client Agent, and the sample NGINX deployment. It assumes you followed [Setup and Installation](setup-installation-guide.md) and the Master Host is reachable at `https://localhost:7216`.

For endpoint request/response details, see [API Documentation](api-documentation.md).

## 1. Start the applications

Start the Master Host first:

```powershell
dotnet run --project Multi-Host-Controller-Assessment/Master-Host.csproj --launch-profile https
```

Open the dashboard at `https://localhost:7216/`. In a second terminal, start one Client Agent with its own pre-provisioned secret:

```powershell
$env:ClientAgent__MasterHostBaseUrl = 'https://localhost:7216'
$env:ClientAgent__Name = 'demo-agent-01'
$env:ClientAgent__Version = '1.0.0'
$env:CLIENT_AGENT_SECRET = '<agent-secret-from-provisioning>'
dotnet run --project Client-Agent/Client-Agent.csproj
```

The agent creates/persists its ID and protected secret, registers with the Master Host, acquires a JWT, and begins sending heartbeats. Refresh the dashboard: the host should appear in the registered-host list and show online after a heartbeat. The dashboard uses the configured online threshold (180 seconds by default).

If using a trusted local HTTPS certificate, no special bypass is needed. If the client cannot trust the certificate, install/trust the correct development certificate instead of disabling TLS checks.

## 2. Add a deployment instruction

Open **Manage deployment instructions** from the dashboard. The instruction form has these main fields:

| Field | Example | Description |
| --- | --- | --- |
| Package Code | `nginx-docker` | Stable machine-readable package identifier |
| Package Name | `NGINX Docker` | Human-readable name |
| Version | `1.27-demo-1` | Instruction revision; package code + version must be unique |
| Platform | `Windows` | Must match the registered host OS, or use `Any` |
| Install Playbook YAML | Content of `fixtures/nginx-docker/install.yml` | Starts/installs the application |
| Verification Playbook YAML | Content of `fixtures/nginx-docker/verify.yml` | Fails if the application is not healthy |
| Rollback Playbook YAML | Content of `fixtures/nginx-docker/rollback.yml` | Optional cleanup when install/verification fails |
| Default Configuration | JSON below | Defaults used when starting a job without an override |

The current Windows Client Agent reports an OS string beginning with `Microsoft Windows`; use `Windows` as the instruction platform for it. The platform must match the agent; incompatible instructions are not offered for that host.

Use the example files from `fixtures/nginx-docker/` rather than retyping YAML. The agent expects the `community.docker` collection and Python Docker SDK available in the configured WSL environment. In this design, `hosts: localhost` means the Docker daemon reachable from the agent's WSL instance.

Example default configuration:

```json
{
  "containerName": "nginx-demo-agent-01",
  "image": "nginx:1.27",
  "publishedPort": 8088,
  "containerPort": 80
}
```

The Client Agent wraps this object as the `deployment_config` Ansible extra-vars variable. For example, a YAML expression `{{ deployment_config.containerName }}` resolves to `nginx-demo-agent-01`. The configuration value must be a JSON object. Playbooks are not JSON; enter their YAML contents in the playbook fields.

Click **Create**. If the page reports a duplicate, choose a new version for the same package code.

## 3. Start a deployment for one host

1. On the dashboard, select the registered agent to open its host detail page.
2. Confirm the host is active and that the deployment instruction is listed as compatible.
3. Ensure no existing job for this host is in progress. The app rejects a second concurrent job for the same host.
4. Select the NGINX instruction. Use the default configuration or enter an override JSON object, for example:

   ```json
   {
     "containerName": "nginx-demo-agent-01",
     "image": "nginx:1.27",
     "publishedPort": 8088,
     "containerPort": 80
   }
   ```

5. Submit the deployment once. A job is created as `Pending`; the agent claims it on its next poll (default interval is 5 seconds).
6. Watch the host detail page for progress and logs. The expected sequence is `Pending` -> `Assigned` -> `Running` -> `Verifying` -> `Succeeded`.
7. Browse to `http://localhost:8088/` on the machine running Docker to confirm the NGINX welcome page.

If installation or verification fails and rollback YAML exists, the agent runs rollback. The final state is `RolledBack` when rollback succeeds or `RollbackFailed` when it fails. Without rollback YAML, a failure ends as `Failed`. Check the job's error message and logs, and also check WSL's Ansible/Docker connectivity.

Persistent deployment logs require `dbo.DeploymentJobLogs`. Apply `database/DeploymentJobLogs.sql` if necessary. The service reports a storage error if it cannot write logs; it does not silently claim persistent logging succeeded.

## 4. Change an instruction safely

The package code and version combination is unique. To publish a changed playbook, create a new version (for example `1.27-demo-2`) and deactivate the old instruction if it should no longer be selected. Existing jobs retain their linked instruction record. Use the dashboard's active toggle to control whether an instruction can be selected for future jobs.

## 5. Send a heartbeat manually for API learning

The Client Agent performs this automatically. For learning or diagnosis, first acquire a token using the agent's ID and provisioned secret:

```powershell
$master = 'https://localhost:7216'
$agentId = '<client-agent-id-from-client-agent-id-file>'
$secret = '<provisioned-agent-secret>'

$tokenReply = Invoke-RestMethod `
  -Method Post `
  -Uri "$master/api/master-host/auth/token" `
  -ContentType 'application/json' `
  -Body (@{ clientAgentId = $agentId; clientSecret = $secret } | ConvertTo-Json)

$headers = @{ Authorization = "Bearer $($tokenReply.access_token)" }
Invoke-RestMethod `
  -Method Post `
  -Uri "$master/api/master-host/heartbeat/$agentId" `
  -Headers $headers `
  -ContentType 'application/json' `
  -Body (@{ resourceUsageMetrics = @{ cpuUsagePercent = 14.2; memoryUsedMb = 1536 } } | ConvertTo-Json -Depth 5)
```

Do not save the example secret or access token in a script or source-control file. PowerShell's `$env:CLIENT_AGENT_SECRET` is intended for initial local provisioning; the agent protects the secret for future launches.

## 6. View host details and deactivate a host

The host detail page shows host information, recent heartbeat/resource metrics, deployment instructions compatible with the host, and recent jobs. The dashboard is currently unauthenticated, so use it only in a trusted environment. Deactivating a host is a soft delete: it blocks registration/heartbeats and revokes its agent credential. A deactivated host cannot be reactivated through the current UI; credential re-provisioning/reactivation requires an operator-managed procedure.

The API deactivation endpoint is protected by an Admin-role JWT, but this project does not yet provide admin token issuance. The dashboard action currently calls the service directly and is not protected by human authentication. Do not expose either management surface publicly until admin authentication is implemented.

## 7. Run more than one Client Agent

Give each instance a separate provisioned secret and separate persistent state. For example, run separate published folders or working directories, and configure unique names/IDs. Each agent gets its own `client-agent-id` and protected secret file. Provision a matching unbound hash row in SQL Server for each secret. Never copy one agent's identity/secret files to another machine; doing so makes both machines present as the same registered host.

## 8. Common troubleshooting steps

| Problem | What to check |
| --- | --- |
| Agent does not appear | Master Host URL, SQL connectivity, secret hash row, `CLIENT_AGENT_SECRET`, and agent startup logs |
| Token endpoint returns `401` | Confirm the secret is the Base64URL plaintext for that row's hash, the credential is not expired/revoked, and registration bound it to this host |
| Agent repeatedly reports TLS errors | Use HTTPS with a certificate trusted by the Windows account running the agent; do not disable certificate checks |
| Host appears offline | Confirm the worker is running and heartbeat interval is below dashboard threshold; inspect Master Host database connectivity |
| No deployment is claimed | Agent must be active/enrolled; instruction must be active and platform-compatible; poll worker must be running |
| `ansible-playbook` missing | Verify `ClientAgent:AnsiblePlaybookCommand` in WSL and check the configured distribution name |
| Docker module fails | Run `wsl -d Ubuntu --exec docker info`; verify Docker Desktop WSL Integration, `community.docker`, and Python Docker SDK |
| Deployment logs missing | Apply `database/DeploymentJobLogs.sql` and grant the app database user appropriate permissions |
| Port already allocated | Choose an unused `publishedPort` in the deployment configuration and submit a job with that value |

For exact WSL installation and setup checks see [WSL and Docker Setup](wsl-docker-setup.md). For token, secret, and key setup see [Client Agent JWT Authentication](client-agent-jwt-authentication.md) and [Agent Credential Provisioning](agent-credential-provisioning.md).
