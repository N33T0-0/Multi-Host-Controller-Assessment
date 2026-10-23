# WSL and Docker Setup for Client Agent

This guide prepares a Windows machine to run the Client Agent's Ansible deployments through WSL 2 and Docker. The agent starts `wsl.exe`, stages playbook files into the configured WSL distribution, then runs `ansible-playbook` there. The sample Docker playbooks target `localhost`, so they manage containers in the Docker daemon accessible from that WSL distribution.

## Requirements

- A supported Windows 10/11 machine with virtualization enabled.
- Administrator access to install WSL and Docker Desktop.
- Docker Desktop with its WSL 2 backend, or a Docker Engine installed directly in the WSL distribution.
- The Client Agent built for and running on Windows. This guide does not configure a Linux service deployment.

## 1. Install WSL 2 and Ubuntu

Open PowerShell as Administrator and install Ubuntu:

```powershell
wsl --install -d Ubuntu
```

Restart Windows if prompted. Launch Ubuntu from the Start menu and create its initial Linux username and password. Then confirm the distribution is running as WSL 2:

```powershell
wsl --list --verbose
wsl -d Ubuntu --exec /bin/true
```

The Ubuntu row should show version `2`, and `/bin/true` should exit without an error. If Ubuntu is version 1, convert it:

```powershell
wsl --set-version Ubuntu 2
```

The agent configuration uses the distribution name `Ubuntu` by default. If yours differs, set `ClientAgent:WslDistribution` to the exact name shown by `wsl --list --verbose`.

## 2. Connect Docker Desktop to Ubuntu

Install and start Docker Desktop for Windows. In Docker Desktop:

1. Open **Settings > General** and enable **Use the WSL 2 based engine**.
2. Open **Settings > Resources > WSL Integration**.
3. Enable integration for the Ubuntu distribution used by the agent, then select **Apply & restart**.

In PowerShell, verify the Docker CLI can reach the daemon through Ubuntu:

```powershell
wsl -d Ubuntu --exec docker version
wsl -d Ubuntu --exec docker info
```

Both commands should complete successfully. `docker info` should show server information. Docker Desktop must be running when deployments execute. Docker Desktop supplies the daemon; installing only the Docker CLI in Ubuntu is not enough.

### Alternative: Docker Engine inside Ubuntu

Instead of Docker Desktop integration, an operator can install and maintain Docker Engine inside Ubuntu. Ensure the Linux account used by WSL can access the daemon without interactive `sudo`. For example, after installing Docker Engine:

```bash
sudo usermod -aG docker "$USER"
```

Close all WSL sessions and reopen Ubuntu for the group change to take effect. Verify `docker info` from that account. Do not configure both approaches unless you intentionally understand which daemon the Docker CLI will use.

## 3. Install Ansible and its Docker requirements

In Ubuntu, install Python, pip, and Ansible. A virtual environment is recommended so the executable path is clear and isolated:

```bash
sudo apt-get update
sudo apt-get install -y python3 python3-venv python3-pip
python3 -m venv "$HOME/ansible-venv"
"$HOME/ansible-venv/bin/python" -m pip install --upgrade pip
"$HOME/ansible-venv/bin/python" -m pip install ansible-core docker
"$HOME/ansible-venv/bin/ansible-galaxy" collection install community.docker
```

The `docker` Python package is used by the `community.docker` modules. Install it into the same Python environment used by Ansible. The sample configuration sets `ansible_python_interpreter` to `/usr/bin/python3.12`; remove or change that override if the selected Python environment does not have the Docker package. One straightforward option is to omit the override and use the virtual environment's `ansible-playbook` executable, which selects its associated Python environment.

Verify the tools from PowerShell, replacing `pang` if the Linux username differs:

```powershell
wsl -d Ubuntu --exec /home/pang/ansible-venv/bin/ansible-playbook --version
wsl -d Ubuntu --exec /home/pang/ansible-venv/bin/ansible-galaxy collection list community.docker
wsl -d Ubuntu --exec docker info
```

The Ansible version should print, the collection list should include `community.docker`, and Docker info should show a reachable server.

## 4. Configure and start the Client Agent

Set `ClientAgent:AnsiblePlaybookCommand` to an executable available inside the configured WSL distribution. For the virtual environment above, update `Client-Agent/appsettings.json` (or the environment-specific configuration) to the actual Linux home path:

```json
{
  "ClientAgent": {
    "WslDistribution": "Ubuntu",
    "AnsiblePlaybookCommand": "/home/<linux-user>/ansible-venv/bin/ansible-playbook",
    "LinuxWorkDirectory": "~/.multi-host-controller/jobs"
  }
}
```

Replace `<linux-user>` with the Ubuntu username. The checked-in sample may contain a developer-specific executable path; evaluators must replace it with their own valid path or use `ansible-playbook` if it is on the WSL user's `PATH`.

Also configure the Master Host URL and the agent's provisioned credential according to the [Client Agent JWT authentication guide](client-agent-jwt-authentication.md). Start the Master Host first, then start the Client Agent under the same Windows account that initialized Ubuntu and can run `wsl -d Ubuntu`. A Windows service running under a different account may not see that user's WSL distribution or Docker Desktop session; validate WSL access under the service identity before installing the agent as a service.

## 5. Smoke-check a deployment

Before submitting a deployment from the dashboard, check all of these from PowerShell:

```powershell
wsl -d Ubuntu --exec /bin/true
wsl -d Ubuntu --exec /home/<linux-user>/ansible-venv/bin/ansible-playbook --version
wsl -d Ubuntu --exec docker info
```

Then create/use an active deployment instruction with a compatible playbook and configuration. For example, the repository's [NGINX Docker sample](nginx-docker-deployment-sample.md) uses `community.docker` and expects Docker access from Ubuntu. A successful run should install, verify, and report status through the Master Host; if verification fails and a rollback playbook is configured, the agent runs rollback.

## Troubleshooting

| Symptom | Checks |
| --- | --- |
| `wsl.exe` or distribution not found | Run `wsl --list --verbose`; ensure WSL and the configured distribution are installed for the Windows account running the agent. |
| `ansible-playbook` not found | Run the configured command with `wsl -d Ubuntu --exec ...`; update `AnsiblePlaybookCommand` to the actual Linux executable path. |
| Docker module cannot connect to daemon | Start Docker Desktop, enable Ubuntu under WSL Integration, then verify `wsl -d Ubuntu --exec docker info`. |
| `community.docker` module not found | Install the collection for the same Linux user/environment Ansible runs as, then check `ansible-galaxy collection list`. |
| Python Docker SDK missing | Install Python package `docker` into the interpreter used by the playbook, and ensure `ansible_python_interpreter` points to that interpreter or is unset. |
| Works interactively but fails as a Windows service | The service may run under a different Windows identity without the user's WSL distribution or Docker Desktop access. Run the service under the intended account or install/configure WSL and Docker for its identity. |
| Container image pull fails | Check registry image name/tag, network connectivity, and whether the registry requires authentication. Configure credentials securely in Docker/Ansible; do not put them in checked-in playbooks or deployment logs. |

## Operational notes

- The Client Agent does not install WSL, Docker, Ansible, or Ansible collections automatically. Prepare and maintain these prerequisites separately.
- The sample playbooks use `hosts: localhost` and `connection: local`: they deploy to the Docker daemon reachable from the agent's WSL environment. They do not provision a separate Linux server.
- Treat playbooks and deployment configuration as privileged instructions. Anyone who can submit deployment instructions may be able to cause commands or containers to run on the agent's machine.
- WSL distribution files are staged under the configured Linux work directory and removed after a job by the current implementation. Ensure that directory is writable by the WSL user running Ansible.
