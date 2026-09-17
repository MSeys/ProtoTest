namespace ProtoTest.SampleApp.Domain;

using Microsoft.EntityFrameworkCore;
using ProtoTest.SampleApp.Contracts;

/// <summary>
/// Persists the Northstar domain. Entities become tables; value collections that are always written and
/// read with their owner - invoice lines, audit metadata, token scopes, webhook events - become JSON
/// columns rather than child tables.
/// </summary>
internal sealed class NorthstarDbContext(DbContextOptions<NorthstarDbContext> options) : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<ApiToken> Tokens => Set<ApiToken>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Environment> Environments => Set<Environment>();

    public DbSet<Deployment> Deployments => Set<Deployment>();

    public DbSet<UsageRecord> UsageRecords => Set<UsageRecord>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();

    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Organization>(organization =>
        {
            organization.ToTable("Organizations");
            organization.HasKey(entity => entity.Id);
            organization.Property(entity => entity.Id).ValueGeneratedNever();
            organization.HasIndex(entity => entity.Slug).IsUnique();
            organization.Property(entity => entity.Slug).HasMaxLength(128);
            organization.Property(entity => entity.Name).HasMaxLength(256);
            organization.Property(entity => entity.PlanId).HasMaxLength(32);
            organization.Property(entity => entity.Status).HasMaxLength(32);
            organization.Property(entity => entity.CreditBalance).HasPrecision(18, 2);
            organization.Ignore(entity => entity.BillableSeats);
            organization.Ignore(entity => entity.Plan);

            organization.HasMany(entity => entity.Members).WithOne()
                .HasForeignKey(member => member.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            organization.HasMany(entity => entity.Tokens).WithOne().OnDelete(DeleteBehavior.Cascade);
            organization.HasMany(entity => entity.Projects).WithOne().OnDelete(DeleteBehavior.Cascade);
            organization.HasMany(entity => entity.Usage).WithOne().OnDelete(DeleteBehavior.Cascade);
            organization.HasMany(entity => entity.Invoices).WithOne().OnDelete(DeleteBehavior.Cascade);
            organization.HasMany(entity => entity.Webhooks).WithOne().OnDelete(DeleteBehavior.Cascade);
            organization.HasMany(entity => entity.Deliveries).WithOne().OnDelete(DeleteBehavior.Cascade);
            organization.HasMany(entity => entity.Audit).WithOne().OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Membership>(member =>
        {
            member.ToTable("Memberships");
            member.HasKey(entity => entity.Id);
            member.Property(entity => entity.Id).ValueGeneratedNever();
            member.Property(entity => entity.Email).HasMaxLength(320);
            member.Property(entity => entity.Role).HasMaxLength(32);
            member.Property(entity => entity.Status).HasMaxLength(32);
            member.HasIndex(entity => new { entity.OrganizationId, entity.Email }).IsUnique();
        });

        modelBuilder.Entity<ApiToken>(token =>
        {
            token.ToTable("Tokens");
            token.HasKey(entity => entity.Id);
            token.Property(entity => entity.Id).ValueGeneratedNever();
            token.Property(entity => entity.Name).HasMaxLength(128);
            token.Property(entity => entity.Prefix).HasMaxLength(64);
            token.Property(entity => entity.Secret).HasMaxLength(128);
            token.Property(entity => entity.MemberId).HasMaxLength(64);
            token.HasIndex(entity => entity.Secret).IsUnique();
        });

        modelBuilder.Entity<Project>(project =>
        {
            project.ToTable("Projects");
            project.HasKey(entity => entity.Id);
            project.Property(entity => entity.Id).ValueGeneratedNever();
            project.Property(entity => entity.Name).HasMaxLength(256);
            project.Property(entity => entity.Slug).HasMaxLength(128);
            project.Property(entity => entity.Status).HasMaxLength(32);
            project.HasMany(entity => entity.Environments).WithOne()
                .HasForeignKey(environment => environment.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Environment>(environment =>
        {
            environment.ToTable("Environments");
            environment.HasKey(entity => entity.Id);
            environment.Property(entity => entity.Id).ValueGeneratedNever();
            environment.Property(entity => entity.ProjectId).HasMaxLength(64);
            environment.Property(entity => entity.Name).HasMaxLength(128);
            environment.Property(entity => entity.Kind).HasMaxLength(32);
            environment.Property(entity => entity.Status).HasMaxLength(32);
            environment.Property(entity => entity.CurrentVersion).HasMaxLength(64);
            environment.HasIndex(entity => new { entity.ProjectId, entity.Name }).IsUnique();
            environment.HasMany(entity => entity.Deployments).WithOne()
                .HasForeignKey(deployment => deployment.EnvironmentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Deployment>(deployment =>
        {
            deployment.ToTable("Deployments");
            deployment.HasKey(entity => entity.Id);
            deployment.Property(entity => entity.Id).ValueGeneratedNever();
            deployment.Property(entity => entity.ProjectId).HasMaxLength(64);
            deployment.Property(entity => entity.EnvironmentId).HasMaxLength(64);
            deployment.Property(entity => entity.Version).HasMaxLength(64);
            deployment.Property(entity => entity.CommitSha).HasMaxLength(64);
            deployment.Property(entity => entity.Status).HasMaxLength(32);
            deployment.Property(entity => entity.RequestedBy).HasMaxLength(320);
            deployment.HasIndex(entity => entity.EnvironmentId);
        });

        modelBuilder.Entity<UsageRecord>(usage =>
        {
            usage.ToTable("UsageRecords");
            usage.HasKey(entity => entity.Id);
            usage.Property(entity => entity.Id).ValueGeneratedOnAdd();
            usage.Property(entity => entity.Metric).HasMaxLength(64);
            usage.HasIndex(entity => new { entity.Metric, entity.OccurredAtUtc });
        });

        modelBuilder.Entity<Invoice>(invoice =>
        {
            invoice.ToTable("Invoices");
            invoice.HasKey(entity => entity.Id);
            invoice.Property(entity => entity.Id).ValueGeneratedOnAdd();
            invoice.Property(entity => entity.Number).HasMaxLength(64);
            invoice.Property(entity => entity.Status).HasMaxLength(32);
            invoice.Ignore(entity => entity.Subtotal);
            invoice.Ignore(entity => entity.Tax);
            invoice.Ignore(entity => entity.Total);

            invoice.OwnsMany(entity => entity.Lines, lines => lines.ToJson());
            invoice.HasMany(entity => entity.Payments).WithOne().OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Payment>(payment =>
        {
            payment.ToTable("Payments");
            payment.HasKey(entity => entity.Id);
            payment.Property(entity => entity.Id).ValueGeneratedOnAdd();
            payment.Property(entity => entity.Amount).HasPrecision(18, 2);
            payment.Property(entity => entity.Status).HasMaxLength(32);
            payment.Property(entity => entity.Method).HasMaxLength(64);
            payment.Property(entity => entity.FailureReason).HasMaxLength(128);
        });

        modelBuilder.Entity<WebhookEndpoint>(webhook =>
        {
            webhook.ToTable("WebhookEndpoints");
            webhook.HasKey(entity => entity.Id);
            webhook.Property(entity => entity.Id).ValueGeneratedNever();
            webhook.Property(entity => entity.Url).HasMaxLength(2048);
            webhook.Property(entity => entity.Secret).HasMaxLength(128);
        });

        modelBuilder.Entity<WebhookDelivery>(delivery =>
        {
            delivery.ToTable("WebhookDeliveries");
            delivery.HasKey(entity => entity.Id);
            delivery.Property(entity => entity.Id).ValueGeneratedNever();
            delivery.Property(entity => entity.EndpointId).HasMaxLength(64);
            delivery.Property(entity => entity.EventType).HasMaxLength(64);
            delivery.Property(entity => entity.Status).HasMaxLength(32);
            delivery.Property(entity => entity.LastError).HasMaxLength(512);
            delivery.HasIndex(entity => new { entity.Status, entity.NextAttemptUtc });
        });

        modelBuilder.Entity<AuditEvent>(audit =>
        {
            audit.ToTable("AuditEvents");
            audit.HasKey(entity => entity.Sequence);
            audit.Property(entity => entity.Sequence).ValueGeneratedOnAdd();
            audit.Property(entity => entity.Action).HasMaxLength(128);
            audit.Property(entity => entity.Resource).HasMaxLength(256);
            audit.Property(entity => entity.Actor).HasMaxLength(320);
            audit.OwnsMany(entity => entity.Metadata, metadata => metadata.ToJson());
        });
    }
}
