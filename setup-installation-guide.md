# Setup and Installation Guide

This guide is to set up the Master Host and one or more Client Agents on Windows. The Master Host and agents are separate .NET 8 applications. SQL Server is an external prerequisite; the applications map to existing tables and do not create or migrate the database schema at startup.

After installation, see [Usage Examples](usage-examples.md) for registering an agent and running a sample deployment. The API contract is described in [API Documentation](api-documentation.md).

## 1. Prerequisites

- Windows 10/11 (the current Client Agent deployment runner invokes `wsl.exe`).
- .NET 8 SDK (the SDK includes the runtime needed for local development).
- SQL Server reachable from the Master Host.
- PowerShell for credential provisioning.
- WSL 2 with Ubuntu, Docker access from Ubuntu, Ansible, and `community.docker` if you intend to run the sample container deployment.
- Git, if cloning the repository.

For WSL and Docker installation steps, see [WSL and Docker Setup for Client Agent](wsl-docker-setup.md).

## 2. Get the source and restore dependencies

From the repository root:

```powershell
git clone <repository-url>
cd Multi-Host-Controller-Assessment
dotnet --info
dotnet restore Multi-Host-Controller-Assessment.slnx
```

If this checkout uses a `.sln` file rather than `.slnx`, restore that solution instead. Individual projects can also be restored:

```powershell
dotnet restore Multi-Host-Controller-Assessment/Master-Host.csproj
dotnet restore Client-Agent/Client-Agent.csproj
```

## 3. Prepare SQL Server

Run the repository's [Multi-Host Controller Application database script](Multi-Host-Controller-Application-Database.sql) on your SQL Server before starting the Master Host. It creates the `MultiHostApplicationDB` database, the tables used by the application, and a sample MinIO deployment instruction. The default Master Host connection string already targets this database name.

**The script creates a new database and its tables; it is not an upgrade or repeatable migration script.** If `MultiHostApplicationDB` or any of these tables already exists, review the script and back up your data before running it. Do not run it blindly against an existing database. If you change the database name in the script, update the Master Host connection string to match.

The application does not create or migrate the schema at startup. After applying the script, confirm the Master Host database identity has read/write access. The connection string key is `ConnectionStrings:LocalDatabase`; for local development it is currently defined in `Multi-Host-Controller-Assessment/appsettings.json`. Prefer an environment variable or secret provider over committing credentials:

```powershell
$env:ConnectionStrings__LocalDatabase = 'Server=<server>;Database=<database>;User Id=<user>;Password=<password>;Encrypt=True;TrustServerCertificate=False;'
```

Use Windows Integrated Security where appropriate. If your SQL Server uses another database name or authentication method, make the connection string and database script consistent. For production or shared environments, have the database owner review the SQL before applying it.

## 4. Configure the Master Host

The Master Host reads `appsettings.json`, environment-specific appsettings files, environment variables, and the normal .NET configuration providers. Important settings:

| Setting | Purpose |
| --- | --- |
| `ConnectionStrings:LocalDatabase` | SQL Server connection |
| `Dashboard:OnlineThresholdSeconds` | How recently a heartbeat must arrive for a host to display as online |
| `Jwt:Issuer` | Expected JWT issuer; defaults to `MasterHost` |
| `Jwt:Audience` | Expected JWT audience; defaults to `ClientAgent` |
| `Jwt:TokenLifetimeSeconds` | JWT lifetime; defaults to 600 seconds |
| `Jwt:KeyFilePath` | Production RSA private-key PEM path; required outside Development |

In Development, if no JWT key path is configured, the Master Host generates and reuses a local key in the current user's application-data directory. For production, create a persistent RSA key file outside the repository and restrict access to the service identity. Example:

```powershell
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out master-host-private.pem
$env:Jwt__KeyFilePath = 'C:\secure\master-host-private.pem'
```

Also set issuer/audience/lifetime as required:

```powershell
$env:Jwt__Issuer = 'MasterHost'
$env:Jwt__Audience = 'ClientAgent'
$env:Jwt__TokenLifetimeSeconds = '600'
```

Keep the same key between restarts. Protect and back it up securely; do not commit it. Use a stable absolute path because a relative path is based on the process working directory. Production API calls for registration and token issuance require HTTPS.

## 5. Start the Master Host

From the repository root, for local Development:

```powershell
dotnet run --project Multi-Host-Controller-Assessment/Master-Host.csproj --launch-profile https
```

The browser launch profile opens `/`, the Razor dashboard. Swagger is available at `/swagger` in Development. The configured launch profile uses `https://localhost:7216` and `http://localhost:5043`; use the actual console output if ports differ. Trust the development certificate if prompted:

```powershell
dotnet dev-certs https --trust
```

If SQL is unreachable or a mapped table/column is missing, requests that access the database will fail. Confirm database connectivity, table names, column names and SQL types before debugging application code.

## 6. Provision a Client Agent credential

Create one unique credential for each agent. From the repository root run:

```powershell
.\tools\Provision-Agent-Credential.ps1
```

The script prints:

1. A random Base64URL secret. Supply this only to the corresponding Client Agent.
2. An `INSERT` statement containing its SHA-256 hash. Run this statement against the Master Host database to create an unbound credential (`ClientHostID = NULL`).

The application's database table stores only the hash. Do not share the plaintext secret with other agents, put it into source control, or paste it into logs.

## 7. Configure and start a Client Agent

Set these agent values in `Client-Agent/appsettings.json`, `appsettings.Development.json`, or environment variables:

| Setting | Example | Notes |
| --- | --- | --- |
| `ClientAgent:Name` | `evaluation-node-01` | Friendly name, max 300 characters |
| `ClientAgent:Version` | `1.0.0` | Agent version, max 20 characters |
| `ClientAgent:MasterHostBaseUrl` | `https://localhost:7216` | Base URL reachable from the agent process |
| `ClientAgent:HeartbeatIntervalSeconds` | `60` | Heartbeat cadence |
| `ClientAgent:WslDistribution` | `Ubuntu` | Must match `wsl --list --verbose` |
| `ClientAgent:AnsiblePlaybookCommand` | `/home/user/ansible-venv/bin/ansible-playbook` | Must exist inside that WSL distribution |
| `ClientAgent:LinuxWorkDirectory` | `~/.multi-host-controller/jobs` | WSL staging directory |
| `ClientAgent:PlaybookTimeoutSeconds` | `300` | Per-playbook execution limit |

For example:

```powershell
$env:ClientAgent__MasterHostBaseUrl = 'https://localhost:7216'
$env:ClientAgent__Name = 'evaluation-node-01'
$env:ClientAgent__Version = '1.0.0'
$env:CLIENT_AGENT_SECRET = '<secret-from-provisioning-script>'
```

On first start, the agent creates a UUID in `client-agent-id` beside the executable (unless `IdFilePath` overrides it) and persists the supplied secret in protected local storage. Windows secret protection is tied to the Windows account running the agent. Keep both files when upgrading/redeploying the same agent; deleting the ID or secret makes it appear as a different agent or prevents authentication. After the secret has been saved, remove `CLIENT_AGENT_SECRET` from the environment. See [WSL setup](wsl-docker-setup.md) for the host-side prerequisites.

Start from the repository root:

```powershell
dotnet run --project Client-Agent/Client-Agent.csproj
```

The agent enrolls, obtains its JWT, sends a heartbeat approximately every minute, and polls for deployment jobs. Run it under the Windows user whose WSL distribution and Docker Desktop integration were configured. To publish a self-contained Windows build:

```powershell
dotnet publish Client-Agent/Client-Agent.csproj -c Release -r win-x64 --self-contained true -o .\publish\client-agent-win-x64
```

Copy the published folder to the agent machine and set its configuration/secrets there. Each simultaneous agent instance needs its own working directory, `client-agent-id`, protected secret store and provisioned database credential; do not run multiple instances from the same folder/state.

## 8. Deploy the sample agent configuration

Make sure WSL, Docker, Ansible, and `community.docker` work from the Client Agent's Windows account. Use the sample playbooks under `fixtures/nginx-docker/`; instructions and the configuration example are in [NGINX Docker Deployment Sample](nginx-docker-deployment-sample.md). The agent executes these playbooks inside its local WSL distribution against the Docker daemon available there.

## 9. Important security and current limitations

- Registration and token calls require HTTPS outside Development. Do not disable TLS certificate validation in the Client Agent.
- The browser dashboard is currently unauthenticated, including its deployment and host deactivation forms. Use only on a trusted development network until human/admin authentication is implemented.
- The API deactivation endpoint requires an Admin-role JWT, but this project currently has no supported admin login or token issuance flow.
- The Client Agent JWT only authorizes agent operations; it does not secure dashboard users.
- Ansible playbooks are powerful instructions executed on the agent's machine. Restrict who can edit deployment instructions and trigger deployments.
- The current WSL runner is intended for a Windows agent host. This setup does not represent a native Linux system service deployment.
