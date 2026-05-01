using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Audit;
using AgentPlatform.Domain.Inbox;
using AgentPlatform.Domain.Modules;
using AgentPlatform.Domain.Policies;
using AgentPlatform.Domain.Runs;
using AgentPlatform.Domain.Secrets;
using AgentPlatform.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Postgres;

public sealed class AgentPlatformDbContext(
    DbContextOptions<AgentPlatformDbContext> options,
    IRequestTenantContext? tenantContext = null)
    : DbContext(options)
{
    private readonly IRequestTenantContext? _tenantContext = tenantContext;

    public Guid CurrentTenantId =>
        _tenantContext is not null && _tenantContext.TryGetTenantId(out var t) ? t : Guid.Empty;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
#pragma warning disable CA1716
    public DbSet<Module> Modules => Set<Module>();
#pragma warning restore CA1716
    public DbSet<ModuleVersion> ModuleVersions => Set<ModuleVersion>();
    public DbSet<WorkflowDef> WorkflowDefs => Set<WorkflowDef>();
    public DbSet<PhaseDef> PhaseDefs => Set<PhaseDef>();
    public DbSet<WorkflowRun> WorkflowRuns => Set<WorkflowRun>();
    public DbSet<PhaseRun> PhaseRuns => Set<PhaseRun>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<InboxItem> InboxItems => Set<InboxItem>();
    public DbSet<ConfirmationRequest> ConfirmationRequests => Set<ConfirmationRequest>();
    public DbSet<DangerousAction> DangerousActions => Set<DangerousAction>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<UsageLedgerEntry> UsageLedger => Set<UsageLedgerEntry>();
    public DbSet<VaultSecretRef> VaultSecretRefs => Set<VaultSecretRef>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("agency");

        modelBuilder.Entity<Tenant>(b =>
        {
            b.ToTable("tenants");
            b.HasKey(x => x.Id);
            b.Property(x => x.Slug).HasMaxLength(64).IsRequired();
            b.HasIndex(x => x.Slug).IsUnique();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Plan).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users");
            b.HasKey(x => x.Id);
            b.Property(x => x.Email).HasMaxLength(320).IsRequired();
            b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
            b.HasQueryFilter(x => CurrentTenantId == Guid.Empty || x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<UserRole>(b =>
        {
            b.ToTable("user_roles");
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.UserId, x.Role }).IsUnique();
            b.HasQueryFilter(x => CurrentTenantId == Guid.Empty || x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<Module>(b =>
        {
            b.ToTable("modules");
            b.HasKey(x => x.Id);
            b.Property(x => x.ModuleId).HasMaxLength(128).IsRequired();
            b.HasIndex(x => x.ModuleId).IsUnique();
            b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            b.Property(x => x.SourcePath).HasMaxLength(1024).IsRequired();
            b.Property(x => x.Status).HasConversion<int>();
            b.Property(x => x.UnavailableReason).HasMaxLength(500);
            b.HasMany(x => x.Versions).WithOne().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ModuleVersion>(b =>
        {
            b.ToTable("module_versions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Version).HasMaxLength(64).IsRequired();
            b.Property(x => x.ManifestJson).HasColumnType("jsonb").IsRequired();
            b.HasIndex(x => new { x.ModuleId, x.Version }).IsUnique();
        });

        modelBuilder.Entity<WorkflowDef>(b =>
        {
            b.ToTable("workflow_defs");
            b.HasKey(x => x.Id);
            b.Property(x => x.WorkflowId).HasMaxLength(128).IsRequired();
            b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            b.Property(x => x.Description).HasMaxLength(2000);
            b.Property(x => x.InputsSchemaJson).HasColumnType("jsonb").IsRequired();
            b.HasIndex(x => new { x.ModuleVersionId, x.WorkflowId }).IsUnique();
            b.HasMany(x => x.Phases).WithOne().HasForeignKey(x => x.WorkflowDefId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PhaseDef>(b =>
        {
            b.ToTable("phase_defs");
            b.HasKey(x => x.Id);
            b.Property(x => x.PhaseId).HasMaxLength(128).IsRequired();
            b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            b.Property(x => x.Skill).HasMaxLength(128).IsRequired();
            b.Property(x => x.Role).HasConversion<int>();
            b.Property(x => x.Kind).HasConversion<int>();
            b.HasIndex(x => new { x.WorkflowDefId, x.Order }).IsUnique();
        });

        modelBuilder.Entity<WorkflowRun>(b =>
        {
            b.ToTable("workflow_runs");
            b.HasKey(x => x.Id);
            b.Property(x => x.ModuleId).HasMaxLength(128).IsRequired();
            b.Property(x => x.WorkflowId).HasMaxLength(128).IsRequired();
            b.Property(x => x.Status).HasConversion<int>();
            b.Property(x => x.InputsJson).HasColumnType("jsonb").IsRequired();
            b.Property(x => x.WorkingDirPath).HasMaxLength(1024).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.StartedAt });
            b.HasMany(x => x.Phases).WithOne().HasForeignKey(x => x.WorkflowRunId).OnDelete(DeleteBehavior.Cascade);
            b.HasQueryFilter(x => CurrentTenantId == Guid.Empty || x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<PhaseRun>(b =>
        {
            b.ToTable("phase_runs");
            b.HasKey(x => x.Id);
            b.Property(x => x.PhaseId).HasMaxLength(128).IsRequired();
            b.Property(x => x.Status).HasConversion<int>();
            b.Property(x => x.ContainerId).HasMaxLength(128);
            b.HasIndex(x => new { x.WorkflowRunId, x.Order }).IsUnique();
            b.HasQueryFilter(x => CurrentTenantId == Guid.Empty || x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<Assignment>(b =>
        {
            b.ToTable("assignments");
            b.HasKey(x => x.Id);
            b.Property(x => x.RequiredRole).HasConversion<int>();
            b.Property(x => x.State).HasConversion<int>();
            b.HasIndex(x => x.PhaseRunId).IsUnique();
            b.HasQueryFilter(x => CurrentTenantId == Guid.Empty || x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<InboxItem>(b =>
        {
            b.ToTable("inbox_items");
            b.HasKey(x => x.Id);
            b.Property(x => x.Title).HasMaxLength(200).IsRequired();
            b.Property(x => x.Subtitle).HasMaxLength(500);
            b.Property(x => x.Kind).HasConversion<int>();
            b.HasIndex(x => new { x.TenantId, x.UserId, x.CreatedAt });
            b.HasQueryFilter(x => CurrentTenantId == Guid.Empty || x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<ConfirmationRequest>(b =>
        {
            b.ToTable("confirmation_requests");
            b.HasKey(x => x.Id);
            b.Property(x => x.Classification).HasConversion<int>();
            b.Property(x => x.ActionSummary).HasMaxLength(500).IsRequired();
            b.Property(x => x.TargetPath).HasMaxLength(1024);
            b.Property(x => x.CommandLine).HasMaxLength(2048);
            b.OwnsOne(x => x.Decision, d =>
            {
                d.Property(p => p.Confirmed).HasColumnName("decision_confirmed");
                d.Property(p => p.DecidedByUserId).HasColumnName("decision_user_id");
                d.Property(p => p.DecidedAt).HasColumnName("decision_at");
                d.Property(p => p.Note).HasColumnName("decision_note").HasMaxLength(1000);
            });
            b.HasQueryFilter(x => CurrentTenantId == Guid.Empty || x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<DangerousAction>(b =>
        {
            b.ToTable("dangerous_actions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Classification).HasConversion<int>();
            b.Property(x => x.Summary).HasMaxLength(500).IsRequired();
            b.Property(x => x.TargetPath).HasMaxLength(1024);
            b.Property(x => x.CommandLine).HasMaxLength(2048);
            b.HasQueryFilter(x => CurrentTenantId == Guid.Empty || x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<AuditEntry>(b =>
        {
            b.ToTable("audit_entries");
            b.HasKey(x => x.Id);
            b.Property(x => x.Category).HasMaxLength(64).IsRequired();
            b.Property(x => x.Action).HasMaxLength(128).IsRequired();
            b.Property(x => x.SubjectType).HasMaxLength(128);
            b.Property(x => x.SubjectId).HasMaxLength(128);
            b.Property(x => x.PayloadJson).HasColumnType("jsonb");
            b.HasIndex(x => new { x.TenantId, x.OccurredAt });
            b.HasQueryFilter(x => CurrentTenantId == Guid.Empty || x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<UsageLedgerEntry>(b =>
        {
            b.ToTable("usage_ledger");
            b.HasKey(x => x.Id);
            b.Property(x => x.Model).HasMaxLength(64).IsRequired();
            b.Property(x => x.CostUsd).HasColumnType("numeric(18,6)");
            b.HasIndex(x => new { x.TenantId, x.RecordedAt });
            b.HasIndex(x => x.WorkflowRunId);
            b.HasQueryFilter(x => CurrentTenantId == Guid.Empty || x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<VaultSecretRef>(b =>
        {
            b.ToTable("vault_secret_refs");
            b.HasKey(x => x.Id);
            b.Property(x => x.Key).HasMaxLength(128).IsRequired();
            b.Property(x => x.CiphertextRef).HasMaxLength(1024).IsRequired();
            b.Property(x => x.Description).HasMaxLength(500);
            b.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
            b.HasQueryFilter(x => CurrentTenantId == Guid.Empty || x.TenantId == CurrentTenantId);
        });
    }
}
