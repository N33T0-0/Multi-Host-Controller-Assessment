# Master Host API Documentation

This guide describes the HTTP API implemented by the Master Host. The service targets .NET 8 and exposes the API under `/api/master-host`. Examples use `https://localhost:7216`; use the URL and port printed by your Master Host if they differ.

For installation and database preparation, see [Setup and Installation](setup-installation-guide.md). For end-to-end workflows, see [Usage Examples](usage-examples.md).

## Authentication overview

Client Agents use a pre-provisioned per-agent secret to enroll and obtain short-lived JWT access tokens. They send the bearer token to heartbeat and deployment endpoints. The token is an RS256-signed JWT and is not a substitute for the original secret when obtaining a replacement token.

| Endpoint | Authentication |
| --- | --- |
| `POST /api/master-host/register` | No bearer token; requires a valid provisioned `clientSecret` in the body |
| `POST /api/master-host/auth/token` | No bearer token; validates the same agent ID and secret |
| Heartbeat and deployment endpoints | Client Agent bearer token; route/body agent ID must match the token identity |

Registration and token requests require HTTPS outside the Development environment.

## Common response behavior

Validation and application errors are returned as JSON, commonly using the ASP.NET Core `ProblemDetails` shape. Common HTTP codes are:

| Code | Meaning |
| --- | --- |
| `200 OK` | Request succeeded, including idempotent registration |
| `201 Created` | A new host was registered |
| `204 No Content` | Request succeeded without a response body, or there is no deployment to claim |
| `400 Bad Request` | Invalid request, invalid enrollment credential, or HTTP used for enrollment/token outside Development |
| `401 Unauthorized` | Missing, invalid, expired, or rejected bearer token/agent secret |
| `403 Forbidden` | Token is valid but not permitted, or token agent ID does not match the requested agent |
| `404 Not Found` | Host or deployment job was not found |
| `409 Conflict` | Invalid deployment status transition or already-completed job |
| `410 Gone` | Host is deactivated; it cannot register or heartbeat |
| `429 Too Many Requests` | Rate limit reached for registration/token authentication attempts |
| `503 Service Unavailable` | Persistent deployment log storage could not be used |

## 1. Register a Client Agent

`POST /api/master-host/register`

Registering requires a pre-provisioned secret. The first successful registration creates the host record and binds the secret hash in `ClientAgentCredentials`. A repeat using the same agent ID and secret is safe and returns the existing host without changing its stored metadata. The endpoint does not update an existing host's name, OS, or version.

### Request

```json
{
  "clientAgentId": "4bbab8f6-9a8e-48ec-8b01-a19c37c41a22",
  "clientAgentName": "build-node-01",
  "clientAgentOS": "Microsoft Windows 10.0.22631",
  "clientAgentVersion": "1.2.0",
  "clientSecret": "<provisioned-base64url-secret>"
}
```

Fields are required. Maximum lengths: ID 64, name 300, OS 50, version 20, secret 128 characters.

### Responses

- `201 Created`: new host; JSON contains `clientAgentId`, name, OS, version, `status`, `clientRegistrationDateTime`, `isNewRegistration: true`, and `message`.
- `200 OK`: same ID and secret already enrolled; `isNewRegistration: false`.
- `400 Bad Request`: validation error or secret invalid/already bound elsewhere.
- `410 Gone`: the host was deactivated.
- `429 Too Many Requests`: authentication rate limit reached.

The Client Agent sends this automatically at startup. Operators normally should not send it manually.

## 2. Acquire a Client Agent JWT

`POST /api/master-host/auth/token`

### Request

```json
{
  "clientAgentId": "4bbab8f6-9a8e-48ec-8b01-a19c37c41a22",
  "clientSecret": "<provisioned-base64url-secret>"
}
```

### Success response: `200 OK`

```json
{
  "access_token": "<signed-jwt>",
  "token_type": "Bearer",
  "expires_in": 600
}
```

The default lifetime is 600 seconds (10 minutes). The agent obtains a replacement using its secret shortly before expiry. Keep the token secret and do not log it. Invalid credentials return a generic `401 Unauthorized`; requests are rate-limited.

## 3. Submit a heartbeat

`POST /api/master-host/heartbeat/{id}`

Requires `Authorization: Bearer <access_token>`. The `{id}` value must match the Client Agent ID in the token. `resourceUsageMetrics` is optional and is stored as JSON text in the heartbeat log.

### Request example

```http
POST /api/master-host/heartbeat/4bbab8f6-9a8e-48ec-8b01-a19c37c41a22
Authorization: Bearer <access_token>
Content-Type: application/json
```

```json
{
  "resourceUsageMetrics": {
    "cpuUsagePercent": 17.4,
    "memoryUsedMb": 2048,
    "memoryTotalMb": 8192,
    "diskUsedPercent": 62.1
  }
}
```

An empty body or `{}` is allowed. Success returns `200 OK` with `clientAgentId`, `loggedDateTime`, `status`, and a message. A mismatched token identity returns `403`; an unknown ID returns `404`; a deactivated host returns `410` and is not logged.

## 4. Deployment agent endpoints

All endpoints below require a Client Agent bearer token. The token's agent identity must match the ID in the request. Normal users do not call these endpoints directly; the Client Agent polls and reports automatically.

### Claim the next pending job

`POST /api/master-host/deployments/claim`

Request:

```json
{ "clientAgentId": "4bbab8f6-9a8e-48ec-8b01-a19c37c41a22" }
```

- `200 OK`: returns one job with `jobId`, package metadata, install/verification/optional rollback YAML, and optional `configuration` JSON string.
- `204 No Content`: no pending job exists.
- `401`/`403`: missing/invalid token or ID mismatch.
- `404`: agent not registered; `410`: agent deactivated.

The Client Agent stages the returned YAML and configuration, runs install, then verification. If either fails and rollback YAML exists, it attempts rollback. A job is executed sequentially by the agent.

### Report job progress

`POST /api/master-host/deployments/{jobId}/progress`

Request:

```json
{
  "clientAgentId": "4bbab8f6-9a8e-48ec-8b01-a19c37c41a22",
  "status": "Running",
  "currentStep": "Installing",
  "message": "Starting install playbook"
}
```

Progress `status` must be `Running`, `Verifying`, or `RollingBack`. A valid update returns `204 No Content`. Invalid status/transitions return `400` or `409`; a missing job returns `404`.

### Complete a job

`POST /api/master-host/deployments/{jobId}/complete`

Request examples:

```json
{
  "clientAgentId": "4bbab8f6-9a8e-48ec-8b01-a19c37c41a22",
  "status": "Succeeded",
  "errorMessage": null
}
```

```json
{
  "clientAgentId": "4bbab8f6-9a8e-48ec-8b01-a19c37c41a22",
  "status": "RolledBack",
  "errorMessage": "Verification failed; rollback completed."
}
```

Terminal status must be `Succeeded`, `Failed`, `RolledBack`, or `RollbackFailed`. Success returns `204 No Content`; invalid status/transitions return `400` or `409`; an unknown job returns `404`.

### Deployment statuses

| Status | Meaning |
| --- | --- |
| `Pending` | Requested in the dashboard; waiting for an agent to claim it |
| `Assigned` | Claimed by the agent |
| `Running` | Install playbook is running |
| `Verifying` | Verification playbook is running |
| `RollingBack` | Rollback playbook is running after an install/verification failure |
| `Succeeded` | Install and verification passed |
| `Failed` | Deployment failed without a successful rollback |
| `RolledBack` | Deployment failed, rollback succeeded |
| `RollbackFailed` | Deployment and its rollback failed |
| `Cancelled` | Pending/assigned job cancelled when its host was deactivated |

## 5. Deactivate a Client Agent

`POST /api/master-host/deactivate/{id}`

Requires a JWT. Success is `204 No Content`; an unknown ID is `404`. Deactivation soft-deletes the host and revokes its credential, which blocks future token use. The host detail page has a deactivation form, but the Razor dashboard is currently unauthenticated; do not expose it on an untrusted network.

## Example: call an authenticated API with PowerShell

```powershell
$token = '<access-token-from-token-endpoint>'
$agentId = '<client-agent-id>'
$headers = @{ Authorization = "Bearer $token" }

Invoke-RestMethod `
  -Method Post `
  -Uri "https://localhost:7216/api/master-host/heartbeat/$agentId" `
  -Headers $headers `
  -ContentType 'application/json' `
  -Body '{"resourceUsageMetrics":{"cpuUsagePercent":12.5}}'
```

