# Client Agent JWT authentication setup

## Master Host signing key

The Master Host signs 10-minute RS256 tokens. The RSA private key stays on the Master Host; agents receive only the short-lived access token. In Development, the Master Host generates a persistent 3072-bit key on first start under the current user's local application-data folder (`MultiHostController/Development/master-host-private.pem`). For production, configure a persistent PEM private-key file outside the repository and restrict its file permissions to the Master Host service account. For example, generate a key pair with OpenSSL:

```powershell
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out master-host-private.pem
openssl pkey -in master-host-private.pem -pubout -out master-host-public.pem
```

Set these Master Host environment variables (or equivalent secret-provider settings):

```text
Jwt__Issuer=MasterHost
Jwt__Audience=ClientAgent
Jwt__TokenLifetimeSeconds=600
Jwt__KeyFilePath=<protected path to master-host-private.pem>
```

Do not check the private key into source control or store it in appsettings. In production, the application refuses to start if `Jwt__KeyFilePath` is unset or the file is missing/invalid. Keep the same key between restarts so already-issued tokens remain verifiable until expiry.

## Provision one agent credential

Run `tools/Provision-Agent-Credential.ps1` once for each agent. It prints two values:

1. The Base64URL secret: provide it to only that agent, for example through the `CLIENT_AGENT_SECRET` environment variable on first startup.
2. The SQL insert: execute it on the Master Host database to create a credential with `ClientHostID = NULL`.

The agent generates and persists its own `client-agent-id`. On first successful registration, the Master Host atomically binds the matching unbound secret hash to that host. The secret file is protected with Windows DPAPI for the current service account on Windows, or owner-only mode `600` on Unix-like systems. A plaintext secret file created by the interrupted implementation is upgraded to protected storage on first read. Preserve the secret file and `client-agent-id` across application upgrades. To provision a new secret for an existing agent, use a separate credential rotation procedure; registration will not replace a bound credential.

The SQL table is a prerequisite and is not created by application startup. Apply the user-owned `ClientAgentCredentials` DDL with nullable `ClientHostID` before starting the Master Host. The app does not run migrations or SQL scripts.

## Agent configuration and transport

Set `ClientAgent:MasterHostBaseUrl` to the Master Host URL. Set `CLIENT_AGENT_SECRET` only for initial secret provisioning; after it is saved, remove the environment variable. On first startup the agent enrolls, then obtains a token, then sends heartbeats and polls deployment jobs. Tokens are kept in memory, refreshed before expiry, and reacquired with the same agent secret. A `401` causes one refresh and one retry. Invalid enrollment credentials stop the agent and require operator correction.

Use HTTPS for Master Host communications outside Development. Do not disable certificate validation; install/trust the correct server certificate in the client OS. HTTP is permitted only in Development.
