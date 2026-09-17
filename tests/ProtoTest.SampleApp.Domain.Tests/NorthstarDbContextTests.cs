namespace ProtoTest.SampleApp.Domain.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.SampleApp.Contracts;

[TestFixture]
public sealed class NorthstarDbContextTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    private SqliteConnection _connection = null!;
    private DbContextOptions<NorthstarDbContext> _options = null!;

    [SetUp]
    public void SetUp()
    {
        // Pooling is off so the keeper connection alone decides when the in-memory database disappears.
        _connection = new SqliteConnection("Data Source=file:northstar-model;Mode=Memory;Cache=Shared;Pooling=False");
        _connection.Open();
        _options = new DbContextOptionsBuilder<NorthstarDbContext>().UseSqlite(_connection).Options;
        using var context = new NorthstarDbContext(_options);
        context.Database.EnsureCreated();
    }

    [TearDown]
    public void TearDown() => _connection.Dispose();

    [Test]
    public async Task Schema_ShouldRoundTripTheAggregate()
    {
        // Arrange
        var organization = CreateOrganization();

        // Act
        await using (var context = new NorthstarDbContext(_options))
        {
            context.Organizations.Add(organization);
            await context.SaveChangesAsync();
        }

        // Assert
        await using var read = new NorthstarDbContext(_options);
        var loaded = await read.Organizations
            .Include(entity => entity.Members)
            .Include(entity => entity.Tokens)
            .Include(entity => entity.Projects)
                .ThenInclude(project => project.Environments)
                .ThenInclude(environment => environment.Deployments)
            .Include(entity => entity.Invoices)
            .Include(entity => entity.Audit)
            .SingleAsync(entity => entity.Id == "org_1");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(loaded.Slug, Is.EqualTo("acme"));
            Assert.That(loaded.BillableSeats, Is.EqualTo(1));
            Assert.That(loaded.Members.Single().Email, Is.EqualTo("owner@acme.test"));
            Assert.That(loaded.Tokens.Single().Scopes, Is.EqualTo(new[] { TokenScopes.Admin }));
            Assert.That(loaded.Projects.Single().Environments.Single().CurrentVersion, Is.EqualTo("1.0.0"));
            Assert.That(loaded.Projects.Single().Environments.Single().Deployments.Single().Version,
                Is.EqualTo("1.0.0"));
            Assert.That(loaded.Invoices.Single().Lines.Single().Amount, Is.EqualTo(199m));
            Assert.That(loaded.Invoices.Single().Total, Is.EqualTo(238.80m));
            Assert.That(loaded.Audit.Single().Metadata.Single().Key, Is.EqualTo("plan"));
        }
    }

    [Test]
    public async Task Tokens_ShouldBeQueryableBySecret()
    {
        // Arrange
        await using (var context = new NorthstarDbContext(_options))
        {
            context.Organizations.Add(CreateOrganization());
            await context.SaveChangesAsync();
        }

        // Act
        await using var read = new NorthstarDbContext(_options);
        var token = await read.Tokens.SingleOrDefaultAsync(entity => entity.Secret == "nsk_1_secret");

        // Assert
        Assert.That(token, Is.Not.Null);
        Assert.That(token!.MemberId, Is.EqualTo("mem_1"));
    }

    [Test]
    public async Task Store_ShouldHandOverDueWebhookDeliveries()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var provider = new ServiceCollection()
            .AddNorthstarDomain(options => options.UseSqlite(connection))
            .BuildServiceProvider();
        await using (var schema = await provider.GetRequiredService<IDbContextFactory<NorthstarDbContext>>()
                         .CreateDbContextAsync())
        {
            await schema.Database.EnsureCreatedAsync();
        }

        var store = provider.GetRequiredService<NorthstarStore>();
        var tenant = store.ProvisionTenant("webhook-store", PlanIds.Growth, new Uri("http://localhost"));
        var principal = store.Authenticate(tenant.ApiToken);
        store.CreateWebhook(principal, "http://localhost/test-support/webhook-sinks/sink_1", [WebhookEventTypes.ProjectCreated]);

        // Act
        store.CreateProject(principal, "atlas");
        var jobs = store.TakePendingDeliveries(25);

        // Assert
        Assert.That(jobs, Has.Count.EqualTo(1));
        Assert.That(jobs[0].EventType, Is.EqualTo(WebhookEventTypes.ProjectCreated));
    }

    [Test]
    public async Task DomainRegistration_ShouldProvideAContextFactory()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var provider = new ServiceCollection()
            .AddNorthstarDomain(options => options.UseSqlite(connection))
            .BuildServiceProvider();

        // Act
        var factory = provider.GetRequiredService<IDbContextFactory<NorthstarDbContext>>();
        await using (var context = await factory.CreateDbContextAsync())
        {
            await context.Database.EnsureCreatedAsync();
            context.Organizations.Add(CreateOrganization());
            await context.SaveChangesAsync();
        }

        await using var read = await factory.CreateDbContextAsync();
        var organizations = await read.Organizations.CountAsync();

        // Assert
        Assert.That(organizations, Is.EqualTo(1));
    }

    private static Organization CreateOrganization()
    {
        var organization = new Organization
        {
            Id = "org_1",
            Slug = "acme",
            Name = "Acme",
            CreatedAtUtc = Now,
            PlanId = PlanIds.Growth,
            PeriodStartUtc = Now,
            PeriodEndUtc = Now.AddMonths(1)
        };

        organization.Members.Add(new Membership
        {
            Id = "mem_1",
            OrganizationId = organization.Id,
            Email = "owner@acme.test",
            Role = MemberRoles.Owner,
            Status = MemberStatuses.Active,
            InvitedAtUtc = Now
        });

        organization.Tokens.Add(new ApiToken
        {
            Id = "tok_1",
            Name = "owner",
            Prefix = "nsk_1",
            Secret = "nsk_1_secret",
            Scopes = [TokenScopes.Admin],
            MemberId = "mem_1",
            CreatedAtUtc = Now
        });

        var environment = new Environment
        {
            Id = "env_1",
            ProjectId = "prj_1",
            Name = "production",
            Kind = EnvironmentKinds.Production,
            CreatedAtUtc = Now,
            CurrentVersion = "1.0.0"
        };
        environment.Deployments.Add(new Deployment
        {
            Id = "dep_1",
            ProjectId = "prj_1",
            EnvironmentId = "env_1",
            Version = "1.0.0",
            CommitSha = "abc1234",
            Status = DeploymentStatuses.Succeeded,
            RequestedBy = "owner@acme.test",
            DeployMinutes = 2,
            CreatedAtUtc = Now,
            CompletedAtUtc = Now
        });

        var project = new Project
        {
            Id = "prj_1",
            Name = "Atlas",
            Slug = "atlas",
            CreatedAtUtc = Now
        };
        project.Environments.Add(environment);
        organization.Projects.Add(project);

        organization.Invoices.Add(new Invoice
        {
            Id = 1,
            Number = "INV-2026-0001",
            PeriodStartUtc = Now,
            PeriodEndUtc = Now.AddMonths(1),
            IssuedAtUtc = Now,
            DueAtUtc = Now.AddDays(7),
            Lines = [new InvoiceLine("Northstar Growth plan", 1, 199m, 199m)]
        });

        organization.Audit.Add(new AuditEvent
        {
            Sequence = 1,
            Action = "organization.created",
            Resource = "organization:org_1",
            Actor = "owner@acme.test",
            OccurredAtUtc = Now,
            Metadata = [new AuditMetadataEntry("plan", PlanIds.Growth)]
        });

        return organization;
    }
}
