using System.ComponentModel.DataAnnotations;

namespace Multi_Host_Controller_Assessment.Models.Requests;

/// <summary>
/// Request payload for client agent registration.
/// </summary>
public class RegisterClientAgentRequest : IValidatableObject
{
    /// <summary>
    /// Stable external identifier of the client agent (maps to VARCHAR(64)).
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "ClientAgentId is required.")]
    [StringLength(64, MinimumLength = 1, ErrorMessage = "ClientAgentId must not exceed 64 characters.")]
    public string ClientAgentId { get; set; } = string.Empty;

    /// <summary>
    /// Friendly name or hostname of the client agent (maps to NVARCHAR(300)).
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "ClientAgentName is required.")]
    [StringLength(300, MinimumLength = 1, ErrorMessage = "ClientAgentName must not exceed 300 characters.")]
    public string ClientAgentName { get; set; } = string.Empty;

    /// <summary>
    /// Operating system description of the client agent (maps to VARCHAR(50)).
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "ClientAgentOS is required.")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "ClientAgentOS must not exceed 50 characters.")]
    public string ClientAgentOS { get; set; } = string.Empty;

    /// <summary>
    /// Running version of the client agent software (maps to VARCHAR(20)).
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "ClientAgentVersion is required.")]
    [StringLength(20, MinimumLength = 1, ErrorMessage = "ClientAgentVersion must not exceed 20 characters.")]
    public string ClientAgentVersion { get; set; } = string.Empty;

    /// <summary>
    /// Pre-provisioned secret for agent authentication.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "ClientSecret is required.")]
    [StringLength(128, MinimumLength = 1, ErrorMessage = "ClientSecret must not exceed 128 characters.")]
    public string ClientSecret { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(ClientAgentId))
        {
            yield return new ValidationResult(
                "ClientAgentId cannot be empty or whitespace.",
                new[] { nameof(ClientAgentId) });
        }

        if (string.IsNullOrWhiteSpace(ClientAgentName))
        {
            yield return new ValidationResult(
                "ClientAgentName cannot be empty or whitespace.",
                new[] { nameof(ClientAgentName) });
        }

        if (string.IsNullOrWhiteSpace(ClientAgentOS))
        {
            yield return new ValidationResult(
                "ClientAgentOS cannot be empty or whitespace.",
                new[] { nameof(ClientAgentOS) });
        }

        if (string.IsNullOrWhiteSpace(ClientAgentVersion))
        {
            yield return new ValidationResult(
                "ClientAgentVersion cannot be empty or whitespace.",
                new[] { nameof(ClientAgentVersion) });
        }

        if (string.IsNullOrWhiteSpace(ClientSecret))
        {
            yield return new ValidationResult(
                "ClientSecret cannot be empty or whitespace.",
                new[] { nameof(ClientSecret) });
        }
    }
}

