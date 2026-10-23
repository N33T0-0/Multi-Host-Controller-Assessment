using System.Text;
using System.Text.Json;
using Client_Agent.Models;

namespace Client_Agent.Services;

/// <summary>
/// Implements validation, JSON wrapping, and staging of playbooks into Windows temp and WSL directories.
/// </summary>
public class DeploymentStager : IDeploymentStager
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IWslProcessRunner _runner;
    private readonly ILogger<DeploymentStager> _logger;

    public DeploymentStager(IWslProcessRunner runner, ILogger<DeploymentStager> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<StagedJobContext> StageJobAsync(
        DeploymentAssignment job,
        string distribution,
        string linuxWorkDirectoryRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (string.IsNullOrWhiteSpace(job.InstallPlaybookYaml))
        {
            throw new ArgumentException("Install playbook YAML cannot be empty or whitespace.", nameof(job));
        }

        if (string.IsNullOrWhiteSpace(job.VerificationPlaybookYaml))
        {
            throw new ArgumentException("Verification playbook YAML cannot be empty or whitespace.", nameof(job));
        }

        var extraVarsJson = BuildExtraVarsJson(job.Configuration);

        var stagingFolderName = $"mhc-job-{job.JobId}-{Guid.NewGuid():N}";
        var windowsStagingPath = Path.Combine(Path.GetTempPath(), stagingFolderName);
        string? linuxJobDir = null;

        try
        {
            Directory.CreateDirectory(windowsStagingPath);

            const string installFileName = "install.yml";
            const string verifyFileName = "verify.yml";
            const string extraVarsFileName = "extra-vars.json";
            string? rollbackFileName = null;

            await File.WriteAllTextAsync(Path.Combine(windowsStagingPath, installFileName), job.InstallPlaybookYaml, Utf8NoBom, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(windowsStagingPath, verifyFileName), job.VerificationPlaybookYaml, Utf8NoBom, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(windowsStagingPath, extraVarsFileName), extraVarsJson, Utf8NoBom, cancellationToken);

            if (!string.IsNullOrWhiteSpace(job.RollbackPlaybookYaml))
            {
                rollbackFileName = "rollback.yml";
                await File.WriteAllTextAsync(Path.Combine(windowsStagingPath, rollbackFileName), job.RollbackPlaybookYaml, Utf8NoBom, cancellationToken);
            }

            // Convert Windows staging path to WSL mount path
            var wslWindowsStagingPath = await _runner.ConvertWindowsPathToWslAsync(distribution, windowsStagingPath, cancellationToken);

            // Resolve target Linux work directory
            var resolvedLinuxRoot = await ResolveLinuxWorkDirectoryRootAsync(distribution, linuxWorkDirectoryRoot, cancellationToken);
            linuxJobDir = $"{resolvedLinuxRoot.TrimEnd('/')}/{stagingFolderName}";

            // Create target directory in Linux
            var mkdirResult = await _runner.ExecuteWslAsync(
                distribution,
                ["mkdir", "-p", linuxJobDir],
                timeout: TimeSpan.FromSeconds(15),
                cancellationToken: cancellationToken);

            if (mkdirResult.ExitCode != 0)
            {
                throw new InvalidOperationException($"Failed to create Linux work directory '{linuxJobDir}': {mkdirResult.StandardError}");
            }

            // Copy staged files from Windows mount to Linux work directory
            var cpResult = await _runner.ExecuteWslAsync(
                distribution,
                ["cp", "-r", $"{wslWindowsStagingPath}/.", linuxJobDir],
                timeout: TimeSpan.FromSeconds(30),
                cancellationToken: cancellationToken);

            if (cpResult.ExitCode != 0)
            {
                throw new InvalidOperationException($"Failed to copy staged files to Linux directory '{linuxJobDir}': {cpResult.StandardError}");
            }

            var context = new StagedJobContext(
                windowsStagingPath,
                linuxJobDir,
                installFileName,
                verifyFileName,
                rollbackFileName,
                extraVarsFileName);

            context.CleanupAction = async () =>
            {
                try
                {
                    if (Directory.Exists(windowsStagingPath))
                    {
                        Directory.Delete(windowsStagingPath, recursive: true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete Windows staging directory '{Path}'.", windowsStagingPath);
                }

                try
                {
                    if (!string.IsNullOrWhiteSpace(linuxJobDir))
                    {
                        var rmResult = await _runner.ExecuteWslAsync(
                            distribution,
                            ["rm", "-rf", linuxJobDir],
                            timeout: TimeSpan.FromSeconds(15),
                            cancellationToken: CancellationToken.None);

                        if (rmResult.ExitCode != 0)
                        {
                            _logger.LogWarning("Failed to remove Linux directory '{Path}': {Error}", linuxJobDir, rmResult.StandardError);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up Linux directory '{Path}'.", linuxJobDir);
                }
            };

            return context;
        }
        catch
        {
            // Clean up immediately on staging failure
            try
            {
                if (Directory.Exists(windowsStagingPath))
                {
                    Directory.Delete(windowsStagingPath, recursive: true);
                }
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(linuxJobDir))
            {
                try
                {
                    await _runner.ExecuteWslAsync(
                        distribution,
                        ["rm", "-rf", linuxJobDir],
                        timeout: TimeSpan.FromSeconds(10),
                        cancellationToken: CancellationToken.None);
                }
                catch { }
            }

            throw;
        }
    }

    private static string BuildExtraVarsJson(string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
        {
            return "{\n  \"deployment_config\": {}\n}";
        }

        try
        {
            using var doc = JsonDocument.Parse(configuration);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(
                    $"Configuration must be a JSON object with an object root (got {doc.RootElement.ValueKind}).", nameof(configuration));
            }

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("deployment_config");
                doc.RootElement.WriteTo(writer);
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Configuration contains malformed JSON: {ex.Message}", nameof(configuration), ex);
        }
    }

    private async Task<string> ResolveLinuxWorkDirectoryRootAsync(
        string distribution,
        string configuredRoot,
        CancellationToken cancellationToken)
    {
        var trimmed = configuredRoot.Trim();
        if (trimmed.StartsWith("~"))
        {
            var home = await _runner.ResolveLinuxHomeDirectoryAsync(distribution, cancellationToken);
            var subPath = trimmed.Length > 1 ? trimmed.Substring(1).TrimStart('/') : string.Empty;
            return string.IsNullOrEmpty(subPath) ? home : $"{home.TrimEnd('/')}/{subPath}";
        }
        return trimmed;
    }
}

