namespace Multi_Host_Controller_Assessment.Data.Entities;

public class DeploymentJobLog
{
    public long Id { get; set; }
    public long DeploymentJobId { get; set; }
    public DateTime LoggedAt { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string Severity { get; set; } = "Information";
    public string Message { get; set; } = string.Empty;
}
