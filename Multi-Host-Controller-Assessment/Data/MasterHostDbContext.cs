using Microsoft.EntityFrameworkCore;
using Multi_Host_Controller_Assessment.Data.Entities;

namespace Multi_Host_Controller_Assessment.Data;

/// <summary>
/// Database context for Master Host persistence, mapped to pre-existing SQL Server schema.
/// </summary>
public class MasterHostDbContext : DbContext
{
    public MasterHostDbContext(DbContextOptions<MasterHostDbContext> options) : base(options)
    {
    }

    public virtual DbSet<ClientHost> ClientHosts => Set<ClientHost>();

    public virtual DbSet<ClientHostHeartbeatLog> ClientHostHeartbeatLogs => Set<ClientHostHeartbeatLog>();

    public virtual DbSet<DeploymentInstruction> DeploymentInstructions => Set<DeploymentInstruction>();

    public virtual DbSet<DeploymentJob> DeploymentJobs => Set<DeploymentJob>();

    public virtual DbSet<DeploymentJobLog> DeploymentJobLogs => Set<DeploymentJobLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ClientHost>(entity =>
        {
            entity.ToTable("ClientHosts", "dbo");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("ID")
                .HasColumnType("bigint")
                .ValueGeneratedOnAdd();

            entity.Property(e => e.ClientAgentId)
                .HasColumnName("ClientAgentID")
                .HasColumnType("varchar(64)")
                .HasMaxLength(64)
                .IsUnicode(false)
                .IsRequired();

            entity.Property(e => e.ClientAgentName)
                .HasColumnName("ClientAgentName")
                .HasColumnType("nvarchar(300)")
                .HasMaxLength(300)
                .IsUnicode(true)
                .IsRequired();

            entity.Property(e => e.ClientAgentOS)
                .HasColumnName("ClientAgentOS")
                .HasColumnType("varchar(50)")
                .HasMaxLength(50)
                .IsUnicode(false)
                .IsRequired();

            entity.Property(e => e.ClientAgentVersion)
                .HasColumnName("ClientAgentVersion")
                .HasColumnType("varchar(20)")
                .HasMaxLength(20)
                .IsUnicode(false)
                .IsRequired();

            entity.Property(e => e.ClientRegistrationDateTime)
                .HasColumnName("ClientRegistrationDateTime")
                .HasColumnType("datetime")
                .HasDefaultValueSql("GETDATE()")
                .ValueGeneratedOnAdd();

            entity.Property(e => e.Status)
                .HasColumnName("Status")
                .HasColumnType("bit")
                .HasDefaultValue(true)
                .ValueGeneratedOnAdd();

            entity.Property(e => e.IsDeleted)
                .HasColumnName("isDeleted")
                .HasColumnType("bit")
                .HasDefaultValue(false)
                .IsRequired();

            entity.Property(e => e.DeletedAt)
                .HasColumnName("DeletedAt")
                .HasColumnType("datetime")
                .IsRequired(false);
        });

        modelBuilder.Entity<ClientHostHeartbeatLog>(entity =>
        {
            entity.ToTable("ClientHostHeartbeatLogs", "dbo");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("ID")
                .HasColumnType("bigint")
                .ValueGeneratedOnAdd();

            entity.Property(e => e.ClientAgentId)
                .HasColumnName("ClientAgentID")
                .HasColumnType("varchar(64)")
                .HasMaxLength(64)
                .IsUnicode(false)
                .IsRequired();

            entity.Property(e => e.LoggedDateTime)
                .HasColumnName("LoggedDateTime")
                .HasColumnType("datetime")
                .HasDefaultValueSql("GETDATE()")
                .ValueGeneratedOnAdd();

            entity.Property(e => e.ResourceUsageMetrics)
                .HasColumnName("ResourceUsageMetrics")
                .HasColumnType("nvarchar(max)")
                .IsUnicode(true)
                .IsRequired(false);
        });

        modelBuilder.Entity<DeploymentInstruction>(entity =>
        {
            entity.ToTable("DeploymentInstructions", "dbo");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("ID").HasColumnType("bigint").ValueGeneratedOnAdd();
            entity.Property(e => e.PackageCode).HasColumnName("PackageCode").HasColumnType("varchar(100)").HasMaxLength(100).IsUnicode(false).IsRequired();
            entity.Property(e => e.PackageName).HasColumnName("PackageName").HasColumnType("nvarchar(200)").HasMaxLength(200).IsRequired();
            entity.Property(e => e.Version).HasColumnName("Version").HasColumnType("varchar(30)").HasMaxLength(30).IsUnicode(false).IsRequired();
            entity.Property(e => e.Platform).HasColumnName("Platform").HasColumnType("varchar(30)").HasMaxLength(30).IsUnicode(false).IsRequired();
            entity.Property(e => e.InstallPlaybookYaml).HasColumnName("InstallPlaybookYaml").HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(e => e.VerificationPlaybookYaml).HasColumnName("VerificationPlaybookYaml").HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(e => e.RollbackPlaybookYaml).HasColumnName("RollbackPlaybookYaml").HasColumnType("nvarchar(max)").IsRequired(false);
            entity.Property(e => e.DefaultConfiguration).HasColumnName("DefaultConfiguration").HasColumnType("nvarchar(max)").IsRequired(false);
            entity.Property(e => e.IsActive).HasColumnName("IsActive").HasColumnType("bit").HasDefaultValue(true).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("CreatedAt").HasColumnType("datetime").HasDefaultValueSql("SYSUTCDATETIME()").ValueGeneratedOnAdd();
            entity.HasIndex(e => new { e.PackageCode, e.Version }).IsUnique().HasDatabaseName("UQ_DeploymentInstructions_PackageCode_Version");
        });

        modelBuilder.Entity<DeploymentJob>(entity =>
        {
            entity.ToTable("DeploymentJobs", "dbo");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("ID").HasColumnType("bigint").ValueGeneratedOnAdd();
            entity.Property(e => e.DeploymentInstructionId).HasColumnName("DeploymentInstructionID").HasColumnType("bigint").IsRequired();
            entity.Property(e => e.ClientHostId).HasColumnName("ClientHostID").HasColumnType("bigint").IsRequired();
            entity.Property(e => e.RequestedBy).HasColumnName("RequestedBy").HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired(false);
            entity.Property(e => e.Configuration).HasColumnName("Configuration").HasColumnType("nvarchar(max)").IsRequired(false);
            entity.Property(e => e.Status).HasColumnName("Status").HasColumnType("varchar(30)").HasMaxLength(30).IsUnicode(false).IsRequired();
            entity.Property(e => e.CurrentStep).HasColumnName("CurrentStep").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired(false);
            entity.Property(e => e.RequestedAt).HasColumnName("RequestedAt").HasColumnType("datetime").IsRequired();
            entity.Property(e => e.StartedAt).HasColumnName("StartedAt").HasColumnType("datetime").IsRequired(false);
            entity.Property(e => e.CompletedAt).HasColumnName("CompletedAt").HasColumnType("datetime").IsRequired(false);
            entity.Property(e => e.Success).HasColumnName("Success").HasColumnType("bit").IsRequired(false);
            entity.Property(e => e.ErrorMessage).HasColumnName("ErrorMessage").HasColumnType("nvarchar(max)").IsRequired(false);
        });

        modelBuilder.Entity<DeploymentJobLog>(entity =>
        {
            entity.ToTable("DeploymentJobLogs", "dbo");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("ID").HasColumnType("bigint").ValueGeneratedOnAdd();
            entity.Property(e => e.DeploymentJobId).HasColumnName("DeploymentJobID").HasColumnType("bigint").IsRequired();
            entity.Property(e => e.LoggedAt).HasColumnName("LoggedAt").HasColumnType("datetime").HasDefaultValueSql("GETDATE()").ValueGeneratedOnAdd();
            entity.Property(e => e.EventName).HasColumnName("EventName").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
            entity.Property(e => e.Severity).HasColumnName("Severity").HasColumnType("varchar(20)").HasMaxLength(20).IsUnicode(false).IsRequired();
            entity.Property(e => e.Message).HasColumnName("Message").HasColumnType("nvarchar(max)").IsRequired();
            entity.HasOne<DeploymentJob>().WithMany().HasForeignKey(e => e.DeploymentJobId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

