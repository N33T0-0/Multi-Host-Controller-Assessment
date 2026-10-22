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
    }
}

