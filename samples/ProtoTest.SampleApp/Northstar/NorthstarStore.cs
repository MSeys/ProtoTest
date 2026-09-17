namespace ProtoTest.SampleApp.Northstar;

using System.Text;
using System.Text.Json;
using ProtoTest.SampleApp.Contracts;

internal sealed record DispatchJob(
    string OrganizationSlug,
    string DeliveryId,
    string EndpointUrl,
    string Secret,
    string EventType,
    string Payload,
    int Attempts);

/// <summary>
/// The Northstar platform state. Everything is in memory, but the behaviour - plans, entitlements,
/// metering, billing periods, payments, webhook outbox, audit and rate limits - is real.
/// </summary>
internal sealed class NorthstarStore(TimeProvider time, NorthstarEventBus events)
{
    public const int RateLimitPerMinute = 60;
    public const int MaxWebhookAttempts = 3;
    private static readonly TimeSpan RateWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan WebhookBackoff = TimeSpan.FromMilliseconds(150);

    private readonly object _gate = new();
    private readonly Dictionary<string, Organization> _bySlug = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Organization> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _tokenIndex = new(StringComparer.Ordinal);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public DateTimeOffset Now(Organization organization) => time.GetUtcNow() + organization.ClockOffset;

    // ---------------------------------------------------------------- provisioning (test support)

    public TenantResponse ProvisionTenant(string name, string planId, Uri apiBaseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var plan = NorthstarPlans.Get(planId);
        var slug = Slugify(name);
        lock (_gate)
        {
            if (_bySlug.ContainsKey(slug))
            {
                throw NorthstarException.Conflict($"The tenant '{slug}' already exists.");
            }

            var now = time.GetUtcNow();
            var organization = new Organization
            {
                Id = $"org_{Guid.NewGuid():N}",
                Slug = slug,
                Name = name.Trim(),
                CreatedAtUtc = now,
                PlanId = plan.Id,
                PeriodStartUtc = now,
                PeriodEndUtc = now.AddMonths(1)
            };
            _bySlug[slug] = organization;
            _byId[organization.Id] = organization;

            var owner = AddMember(organization, $"owner@{slug}.example.test", MemberRoles.Owner, MemberStatuses.Active, now);
            var token = IssueToken(organization, owner, "owner", AllScopes(), now);
            Record(organization, "organization.created", $"organization:{organization.Id}", owner.Email);
            return new TenantResponse(slug, organization.Id, owner.Email, owner.Token!, token.Secret, apiBaseUrl);
        }
    }

    public void DeleteTenant(string slug)
    {
        lock (_gate)
        {
            if (!_bySlug.Remove(slug, out var organization))
            {
                throw NorthstarException.NotFound("tenant");
            }

            _byId.Remove(organization.Id);
            foreach (var token in organization.Tokens.Values)
            {
                _tokenIndex.Remove(token.Secret);
            }
        }
    }

    public (MembershipResponse Membership, string Token) CreateMemberForRole(string slug, string email, string role)
    {
        var organization = RequireOrganization(slug);
        lock (_gate)
        {
            if (!MemberRoles.IsSupported(role))
            {
                throw NorthstarException.Validation($"Unsupported role '{role}'.");
            }

            var now = Now(organization);
            var member = AddMember(organization, email, role, MemberStatuses.Active, now);
            var token = IssueToken(organization, member, role, ScopesForRole(role), now);
            return (ToResponse(member), token.Secret);
        }
    }

    public ClockResponse AdvanceClock(string slug, TimeSpan delta)
    {
        var organization = RequireOrganization(slug);
        lock (_gate)
        {
            organization.ClockOffset += delta;
            Advance(organization);
            return new ClockResponse(slug, Now(organization));
        }
    }

    // ---------------------------------------------------------------- authentication

    public NorthstarPrincipal Authenticate(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw NorthstarException.Unauthorized();
        }

        lock (_gate)
        {
            if (!_tokenIndex.TryGetValue(secret, out var organizationId)
                || !_byId.TryGetValue(organizationId, out var organization)
                || !organization.Tokens.TryGetValue(secret, out var token))
            {
                throw NorthstarException.Unauthorized("The API token is not valid.");
            }

            var now = Now(organization);
            EnforceRateLimit(token, now);
            token.LastUsedAtUtc = now;
            Advance(organization);

            if (string.Equals(organization.Status, OrganizationStatuses.Suspended, StringComparison.Ordinal))
            {
                throw NorthstarException.Forbidden("The organization is suspended.");
            }

            var member = organization.Members[token.MemberId];
            return new NorthstarPrincipal(organization, member, token);
        }
    }

    private void EnforceRateLimit(ApiToken token, DateTimeOffset now)
    {
        token.RequestTimestamps.RemoveAll(stamp => now - stamp > RateWindow);
        if (token.RequestTimestamps.Count >= RateLimitPerMinute)
        {
            var oldest = token.RequestTimestamps[0];
            throw NorthstarException.RateLimited(RateWindow - (now - oldest));
        }

        token.RequestTimestamps.Add(now);
    }

    // ---------------------------------------------------------------- organization & subscription

    public OrganizationResponse GetOrganization(NorthstarPrincipal principal)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            return ToResponse(organization);
        }
    }

    public OrganizationResponse UpdateOrganization(NorthstarPrincipal principal, string? name)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            RequireScope(principal, TokenScopes.Admin);
            Require(organization);
            if (!string.IsNullOrWhiteSpace(name))
            {
                organization.Name = name.Trim();
            }

            Record(organization, "organization.updated", $"organization:{organization.Id}", principal.Actor);
            return ToResponse(organization);
        }
    }

    public IReadOnlyList<PlanResponse> GetPlans()
        => NorthstarPlans.All.Select(NorthstarPlans.ToResponse).ToArray();

    public SubscriptionResponse GetSubscription(NorthstarPrincipal principal)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            Advance(organization);
            return ToSubscriptionResponse(organization);
        }
    }

    public SubscriptionResponse ChangePlan(NorthstarPrincipal principal, string planId, int? seats)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            RequireScope(principal, TokenScopes.Admin);
            RequireRole(principal, MemberRoles.CanManageBilling, "Only owners, administrators and billing contacts can change the plan.");
            Require(organization);

            var current = organization.Plan;
            var next = NorthstarPlans.Get(planId);
            var requestedSeats = seats ?? Math.Max(organization.BillableSeats, next.IncludedSeats);
            if (requestedSeats < organization.BillableSeats)
            {
                throw NorthstarException.Validation("Seats cannot be lower than the number of billable members.");
            }

            if (next.MaxSeats is { } max && requestedSeats > max)
            {
                throw NorthstarException.PlanLimit(
                    $"The {next.Name} plan supports at most {max} seats.",
                    new Dictionary<string, string> { ["limit"] = max.ToString(), ["requested"] = requestedSeats.ToString() });
            }

            var now = Now(organization);
            if (next.MonthlyBasePrice > current.MonthlyBasePrice)
            {
                organization.CreditBalance += ProrationCredit(organization, current, now);
            }

            organization.PlanId = next.Id;
            if (organization.Status == SubscriptionStatuses.Canceled)
            {
                organization.Status = SubscriptionStatuses.Active;
                organization.CancelAtPeriodEnd = false;
            }

            var payload = new
            {
                organization = organization.Slug,
                previousPlan = current.Id,
                plan = next.Id,
                seats = requestedSeats
            };
            Record(organization, "subscription.plan_changed", $"organization:{organization.Id}", principal.Actor,
                new Dictionary<string, string> { ["plan"] = next.Id, ["previousPlan"] = current.Id });
            Enqueue(organization, WebhookEventTypes.PlanChanged, payload, now);
            return ToSubscriptionResponse(organization);
        }
    }

    public SubscriptionResponse CancelSubscription(NorthstarPrincipal principal)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            RequireScope(principal, TokenScopes.Billing);
            RequireRole(principal, MemberRoles.CanManageBilling, "Only owners, administrators and billing contacts can cancel the subscription.");
            Require(organization);
            organization.CancelAtPeriodEnd = true;
            Record(organization, "subscription.cancel_scheduled", $"organization:{organization.Id}", principal.Actor);
            return ToSubscriptionResponse(organization);
        }
    }

    public SubscriptionResponse ResumeSubscription(NorthstarPrincipal principal)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            RequireScope(principal, TokenScopes.Billing);
            RequireRole(principal, MemberRoles.CanManageBilling, "Only owners, administrators and billing contacts can resume the subscription.");
            organization.CancelAtPeriodEnd = false;
            Record(organization, "subscription.resumed", $"organization:{organization.Id}", principal.Actor);
            return ToSubscriptionResponse(organization);
        }
    }

    // ---------------------------------------------------------------- members & tokens

    public CursorPage<MembershipResponse> ListMembers(NorthstarPrincipal principal, string? cursor, int limit)
    {
        lock (_gate)
        {
            var members = principal.Organization.Members.Values
                .OrderBy(member => member.InvitedAtUtc)
                .Select(ToResponse);
            return Page(members, cursor, limit);
        }
    }

    public MembershipResponse InviteMember(NorthstarPrincipal principal, string email, string role)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            RequireScope(principal, TokenScopes.Admin);
            RequireRole(principal, MemberRoles.CanManageMembers, "Only owners and administrators can invite members.");
            ValidateEmail(email);
            if (!MemberRoles.IsSupported(role))
            {
                throw NorthstarException.Validation($"Unsupported role '{role}'.");
            }

            if (organization.Members.Values.Any(member =>
                    member.Status != MemberStatuses.Removed
                    && string.Equals(member.Email, email, StringComparison.OrdinalIgnoreCase)))
            {
                throw NorthstarException.Conflict($"'{email}' is already a member of this organization.");
            }

            RequireSeatAvailable(organization);
            var now = Now(organization);
            var member = AddMember(organization, email, role, MemberStatuses.Invited, now);
            Record(organization, "member.invited", $"member:{member.Id}", principal.Actor,
                new Dictionary<string, string> { ["email"] = email, ["role"] = role });
            Enqueue(organization, WebhookEventTypes.MemberInvited, new { member = ToResponse(member) }, now);
            return ToResponse(member);
        }
    }

    public MembershipResponse UpdateMember(NorthstarPrincipal principal, string memberId, string role)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            RequireScope(principal, TokenScopes.Admin);
            RequireRole(principal, MemberRoles.CanManageMembers, "Only owners and administrators can change member roles.");
            if (!MemberRoles.IsSupported(role))
            {
                throw NorthstarException.Validation($"Unsupported role '{role}'.");
            }

            var member = RequireMember(organization, memberId);
            if (member.Role == MemberRoles.Owner && role != MemberRoles.Owner && CountOwners(organization) <= 1)
            {
                throw NorthstarException.Conflict("The last owner cannot be demoted.");
            }

            member.Role = role;
            Record(organization, "member.role_changed", $"member:{member.Id}", principal.Actor,
                new Dictionary<string, string> { ["role"] = role });
            return ToResponse(member);
        }
    }

    public void RemoveMember(NorthstarPrincipal principal, string memberId)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            RequireScope(principal, TokenScopes.Admin);
            RequireRole(principal, MemberRoles.CanManageMembers, "Only owners and administrators can remove members.");
            var member = RequireMember(organization, memberId);
            if (member.Role == MemberRoles.Owner && CountOwners(organization) <= 1)
            {
                throw NorthstarException.Conflict("The last owner cannot be removed.");
            }

            member.Status = MemberStatuses.Removed;
            foreach (var token in organization.Tokens.Values.Where(token => token.MemberId == member.Id).ToArray())
            {
                Revoke(organization, token);
            }

            Record(organization, "member.removed", $"member:{member.Id}", principal.Actor);
        }
    }

    public CursorPage<ApiTokenResponse> ListTokens(NorthstarPrincipal principal, string? cursor, int limit)
    {
        lock (_gate)
        {
            var tokens = principal.Organization.Tokens.Values
                .OrderBy(token => token.CreatedAtUtc)
                .Select(ToResponse);
            return Page(tokens, cursor, limit);
        }
    }

    public ApiTokenSecretResponse CreateToken(NorthstarPrincipal principal, string name, IReadOnlyList<string>? scopes)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            RequireScope(principal, TokenScopes.Admin);
            RequireRole(principal, MemberRoles.CanManageMembers, "Only owners and administrators can create API tokens.");
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            var requested = scopes is { Count: > 0 } ? scopes.ToArray() : [TokenScopes.Read];
            foreach (var scope in requested)
            {
                if (!TokenScopes.IsSupported(scope))
                {
                    throw NorthstarException.Validation($"Unsupported scope '{scope}'.");
                }
            }

            var token = IssueToken(organization, principal.Member, name.Trim(), requested, Now(organization));
            Record(organization, "token.created", $"token:{token.Id}", principal.Actor);
            return new ApiTokenSecretResponse(ToResponse(token), token.Secret);
        }
    }

    public void RevokeToken(NorthstarPrincipal principal, string tokenId)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            RequireScope(principal, TokenScopes.Admin);
            RequireRole(principal, MemberRoles.CanManageMembers, "Only owners and administrators can revoke API tokens.");
            var token = organization.Tokens.Values.FirstOrDefault(candidate => candidate.Id == tokenId)
                ?? throw NorthstarException.NotFound("token");
            Revoke(organization, token);
            Record(organization, "token.revoked", $"token:{token.Id}", principal.Actor);
        }
    }

    // ---------------------------------------------------------------- projects & environments

    public CursorPage<ProjectResponse> ListProjects(NorthstarPrincipal principal, string? cursor, int limit)
    {
        lock (_gate)
        {
            var projects = principal.Organization.Projects.Values
                .OrderBy(project => project.CreatedAtUtc)
                .Select(ToResponse);
            return Page(projects, cursor, limit);
        }
    }

    public ProjectResponse CreateProject(NorthstarPrincipal principal, string name)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            RequireScope(principal, TokenScopes.Admin);
            RequireRole(principal, MemberRoles.CanManageProjects, "Only owners and administrators can create projects.");
            Require(organization);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            var active = organization.Projects.Values.Count(project => project.Status == ProjectStatuses.Active);
            if (organization.Plan.MaxProjects is { } max && active >= max)
            {
                throw NorthstarException.PlanLimit(
                    $"The {organization.Plan.Name} plan includes at most {max} active projects.",
                    new Dictionary<string, string> { ["limit"] = max.ToString(), ["active"] = active.ToString() });
            }

            var now = Now(organization);
            var project = new Project
            {
                Id = $"prj_{Guid.NewGuid():N}",
                Name = name.Trim(),
                Slug = Slugify(name),
                CreatedAtUtc = now
            };
            organization.Projects[project.Id] = project;
            Record(organization, "project.created", $"project:{project.Id}", principal.Actor,
                new Dictionary<string, string> { ["name"] = project.Name });
            Enqueue(organization, WebhookEventTypes.ProjectCreated, new { project = ToResponse(project) }, now);
            return ToResponse(project);
        }
    }

    public ProjectResponse GetProject(NorthstarPrincipal principal, string projectId)
    {
        lock (_gate)
        {
            return ToResponse(RequireProject(principal.Organization, projectId));
        }
    }

    public ProjectResponse ArchiveProject(NorthstarPrincipal principal, string projectId)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            RequireScope(principal, TokenScopes.Admin);
            RequireRole(principal, MemberRoles.CanManageProjects, "Only owners and administrators can archive projects.");
            var project = RequireProject(organization, projectId);
            if (project.Status == ProjectStatuses.Archived)
            {
                throw NorthstarException.Conflict("The project is already archived.");
            }

            project.Status = ProjectStatuses.Archived;
            Record(organization, "project.archived", $"project:{project.Id}", principal.Actor);
            return ToResponse(project);
        }
    }

    public CursorPage<EnvironmentResponse> ListEnvironments(NorthstarPrincipal principal, string projectId, string? cursor, int limit)
    {
        lock (_gate)
        {
            var project = RequireProject(principal.Organization, projectId);
            var environments = project.Environments.Values
                .OrderBy(environment => environment.CreatedAtUtc)
                .Select(ToResponse);
            return Page(environments, cursor, limit);
        }
    }

    public EnvironmentResponse CreateEnvironment(NorthstarPrincipal principal, string projectId, string name, string kind)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            RequireScope(principal, TokenScopes.Admin);
            RequireRole(principal, MemberRoles.CanManageProjects, "Only owners and administrators can create environments.");
            Require(organization);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (!EnvironmentKinds.IsSupported(kind))
            {
                throw NorthstarException.Validation($"Unsupported environment kind '{kind}'.");
            }

            var project = RequireProject(organization, projectId);
            if (project.Status == ProjectStatuses.Archived)
            {
                throw NorthstarException.Conflict("The project is archived.");
            }

            RequireFeature(organization, kind, EnvironmentKinds.Preview, FeatureKeys.PreviewEnvironments);
            if (project.Environments.Values.Any(environment =>
                    string.Equals(environment.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                throw NorthstarException.Conflict($"The project already has an environment named '{name}'.");
            }

            var environment = new Environment
            {
                Id = $"env_{Guid.NewGuid():N}",
                ProjectId = project.Id,
                Name = name.Trim(),
                Kind = kind,
                CreatedAtUtc = Now(organization)
            };
            project.Environments[environment.Id] = environment;
            Record(organization, "environment.created", $"environment:{environment.Id}", principal.Actor,
                new Dictionary<string, string> { ["kind"] = kind, ["project"] = project.Id });
            return ToResponse(environment);
        }
    }

    // ---------------------------------------------------------------- deployments & usage

    public DeploymentResponse Deploy(NorthstarPrincipal principal, string environmentId, string version, string commitSha)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            RequireScope(principal, TokenScopes.Deploy);
            Require(organization);
            ArgumentException.ThrowIfNullOrWhiteSpace(version);
            ArgumentException.ThrowIfNullOrWhiteSpace(commitSha);
            var (project, environment) = RequireEnvironment(organization, environmentId);
            if (project.Status == ProjectStatuses.Archived)
            {
                throw NorthstarException.Conflict("The project is archived.");
            }

            var production = environment.Kind == EnvironmentKinds.Production;
            var allowed = production
                ? MemberRoles.CanDeployToProduction(principal.Member.Role)
                : MemberRoles.CanDeployToPreview(principal.Member.Role);
            if (!allowed)
            {
                throw NorthstarException.Forbidden(
                    production
                        ? "Only owners and administrators can deploy to production."
                        : "This role cannot deploy to preview environments.");
            }

            var now = Now(organization);
            var failed = commitSha.StartsWith("bad", StringComparison.OrdinalIgnoreCase);
            var minutes = 1.0 + StableSum(commitSha) % 4;
            var deployment = new Deployment
            {
                Id = $"dep_{Guid.NewGuid():N}",
                ProjectId = project.Id,
                EnvironmentId = environment.Id,
                Version = version.Trim(),
                CommitSha = commitSha.Trim(),
                Status = failed ? DeploymentStatuses.Failed : DeploymentStatuses.Succeeded,
                RequestedBy = principal.Actor,
                DeployMinutes = minutes,
                CreatedAtUtc = now,
                CompletedAtUtc = now
            };
            environment.Deployments.Add(deployment);
            if (!failed)
            {
                environment.CurrentVersion = deployment.Version;
            }

            RecordUsage(organization, UsageMetrics.DeployMinutes, minutes, now);
            Record(organization, failed ? "deployment.failed" : "deployment.succeeded", $"deployment:{deployment.Id}", principal.Actor,
                new Dictionary<string, string> { ["environment"] = environment.Id, ["version"] = deployment.Version });
            Enqueue(
                organization,
                failed ? WebhookEventTypes.DeploymentFailed : WebhookEventTypes.DeploymentSucceeded,
                new { deployment = ToResponse(deployment) },
                now);
            events.PublishDeployment(organization.Slug, ToResponse(deployment));
            return ToResponse(deployment);
        }
    }

    public CursorPage<DeploymentResponse> ListDeployments(
        NorthstarPrincipal principal,
        string? environmentId,
        string? projectId,
        string? status,
        string? cursor,
        int limit)
    {
        lock (_gate)
        {
            var deployments = principal.Organization.Projects.Values
                .SelectMany(project => project.Environments.Values)
                .Where(environment => environmentId is null || environment.Id == environmentId)
                .SelectMany(environment => environment.Deployments)
                .Where(deployment => projectId is null || deployment.ProjectId == projectId)
                .Where(deployment => status is null || deployment.Status == status)
                .OrderByDescending(deployment => deployment.CompletedAtUtc)
                .Select(ToResponse);
            return Page(deployments, cursor, limit);
        }
    }

    public DeploymentResponse GetDeployment(NorthstarPrincipal principal, string deploymentId)
    {
        lock (_gate)
        {
            return ToResponse(RequireDeployment(principal.Organization, deploymentId));
        }
    }

    public DeploymentResponse RollbackDeployment(NorthstarPrincipal principal, string deploymentId)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            RequireScope(principal, TokenScopes.Deploy);
            var deployment = RequireDeployment(organization, deploymentId);
            var (project, environment) = RequireEnvironment(organization, deployment.EnvironmentId);
            if (deployment.Status != DeploymentStatuses.Succeeded)
            {
                throw NorthstarException.Conflict("Only a successful deployment can be rolled back.");
            }

            if (!string.Equals(environment.CurrentVersion, deployment.Version, StringComparison.Ordinal))
            {
                throw NorthstarException.Conflict("Only the current deployment can be rolled back.");
            }

            var previous = environment.Deployments
                .Where(candidate => candidate.Status == DeploymentStatuses.Succeeded && candidate.Id != deployment.Id)
                .OrderByDescending(candidate => candidate.CompletedAtUtc)
                .FirstOrDefault()
                ?? throw NorthstarException.Conflict("There is no previous successful deployment to roll back to.");

            var production = environment.Kind == EnvironmentKinds.Production;
            var allowed = production
                ? MemberRoles.CanDeployToProduction(principal.Member.Role)
                : MemberRoles.CanDeployToPreview(principal.Member.Role);
            if (!allowed)
            {
                throw NorthstarException.Forbidden("This role cannot roll back deployments.");
            }

            _ = project;
            var now = Now(organization);
            deployment.Status = DeploymentStatuses.RolledBack;
            environment.CurrentVersion = previous.Version;
            Record(organization, "deployment.rolled_back", $"deployment:{deployment.Id}", principal.Actor,
                new Dictionary<string, string> { ["environment"] = environment.Id, ["to"] = previous.Version });
            Enqueue(
                organization,
                WebhookEventTypes.DeploymentRolledBack,
                new { deployment = ToResponse(deployment), restoredVersion = previous.Version },
                now);
            events.PublishDeployment(organization.Slug, ToResponse(deployment));
            return ToResponse(deployment);
        }
    }

    public CursorPage<UsageRecordResponse> ListUsage(NorthstarPrincipal principal, string? metric, string? cursor, int limit)
    {
        lock (_gate)
        {
            var usage = principal.Organization.Usage
                .Where(record => metric is null || record.Metric == metric)
                .OrderBy(record => record.OccurredAtUtc)
                .Select(record => new UsageRecordResponse(record.Id, record.Metric, record.Quantity, record.OccurredAtUtc));
            return Page(usage, cursor, limit);
        }
    }

    public UsageSummaryResponse GetUsageSummary(NorthstarPrincipal principal, string metric)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            Advance(organization);
            if (!IsSupportedMetric(metric))
            {
                throw NorthstarException.Validation($"Unsupported metric '{metric}'.");
            }

            var total = UsageInPeriod(organization, metric, organization.PeriodStartUtc, organization.PeriodEndUtc);
            var included = metric == UsageMetrics.DeployMinutes ? organization.Plan.IncludedDeployMinutes : 0;
            var overage = Math.Max(0, total - included);
            return new UsageSummaryResponse(metric, total, included, overage, organization.PeriodStartUtc, organization.PeriodEndUtc);
        }
    }

    public UsageRecordResponse RecordUsage(NorthstarPrincipal principal, string metric, double quantity)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            RequireScope(principal, TokenScopes.Deploy);
            Require(organization);
            if (!IsSupportedMetric(metric))
            {
                throw NorthstarException.Validation($"Unsupported metric '{metric}'.");
            }

            if (quantity <= 0)
            {
                throw NorthstarException.Validation("The usage quantity must be positive.");
            }

            return ToResponse(RecordUsage(organization, metric, quantity, Now(organization)));
        }
    }

    // ---------------------------------------------------------------- billing

    public CursorPage<InvoiceResponse> ListInvoices(NorthstarPrincipal principal, string? status, string? cursor, int limit)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            Advance(organization);
            var invoices = organization.Invoices
                .Where(invoice => status is null || invoice.Status == status)
                .OrderByDescending(invoice => invoice.Id)
                .Select(ToResponse);
            return Page(invoices, cursor, limit);
        }
    }

    public InvoiceResponse GetInvoice(NorthstarPrincipal principal, long invoiceId)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            Advance(organization);
            return ToResponse(RequireInvoice(organization, invoiceId));
        }
    }

    public InvoiceResponse PayInvoice(NorthstarPrincipal principal, long invoiceId, string method)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            RequireScope(principal, TokenScopes.Billing);
            RequireRole(principal, MemberRoles.CanManageBilling, "Only owners, administrators and billing contacts can pay invoices.");
            Advance(organization);
            var invoice = RequireInvoice(organization, invoiceId);
            if (invoice.Status is InvoiceStatuses.Paid or InvoiceStatuses.Void)
            {
                throw NorthstarException.InvoiceNotPayable($"The invoice cannot be paid while it is '{invoice.Status}'.");
            }

            var now = Now(organization);
            var declined = string.Equals(method, PaymentMethods.Declined, StringComparison.OrdinalIgnoreCase);
            var payment = new Payment
            {
                Id = ++organization.PaymentSequence,
                Amount = invoice.Total,
                Status = declined ? PaymentStatuses.Failed : PaymentStatuses.Succeeded,
                Method = method,
                FailureReason = declined ? "card_declined" : null,
                AttemptedAtUtc = now
            };
            invoice.Payments.Add(payment);
            if (declined)
            {
                organization.Status = SubscriptionStatuses.PastDue;
                Record(organization, "invoice.payment_failed", $"invoice:{invoice.Id}", principal.Actor,
                    new Dictionary<string, string> { ["method"] = method });
                Enqueue(organization, WebhookEventTypes.InvoicePaymentFailed, new { invoiceNumber = invoice.Number, payment = ToResponse(payment) }, now);
            }
            else
            {
                invoice.Status = InvoiceStatuses.Paid;
                invoice.PaidAtUtc = now;
                organization.Status = SubscriptionStatuses.Active;
                Record(organization, "invoice.paid", $"invoice:{invoice.Id}", principal.Actor,
                    new Dictionary<string, string> { ["method"] = method });
                Enqueue(organization, WebhookEventTypes.InvoicePaid, new { invoiceNumber = invoice.Number, payment = ToResponse(payment) }, now);
                events.PublishInvoice(organization.Slug, ToResponse(invoice));
            }

            return ToResponse(invoice);
        }
    }

    public InvoiceResponse VoidInvoice(NorthstarPrincipal principal, long invoiceId)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            RequireScope(principal, TokenScopes.Billing);
            RequireRole(principal, MemberRoles.CanManageBilling, "Only owners, administrators and billing contacts can void invoices.");
            var invoice = RequireInvoice(organization, invoiceId);
            if (invoice.Status is InvoiceStatuses.Paid or InvoiceStatuses.Void)
            {
                throw NorthstarException.InvoiceNotPayable($"The invoice cannot be voided while it is '{invoice.Status}'.");
            }

            invoice.Status = InvoiceStatuses.Void;
            Record(organization, "invoice.voided", $"invoice:{invoice.Id}", principal.Actor);
            RefreshStatus(organization);
            return ToResponse(invoice);
        }
    }

    // ---------------------------------------------------------------- webhooks

    public CursorPage<WebhookEndpointResponse> ListWebhooks(NorthstarPrincipal principal, string? cursor, int limit)
    {
        lock (_gate)
        {
            var webhooks = principal.Organization.Webhooks
                .OrderBy(webhook => webhook.CreatedAtUtc)
                .Select(ToResponse);
            return Page(webhooks, cursor, limit);
        }
    }

    public WebhookEndpointResponse CreateWebhook(NorthstarPrincipal principal, string url, IReadOnlyList<string>? events)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            RequireScope(principal, TokenScopes.Admin);
            RequireRole(principal, MemberRoles.CanManageProjects, "Only owners and administrators can manage webhooks.");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                throw NorthstarException.Validation("The webhook URL must be an absolute http(s) URL.");
            }

            var requested = events is { Count: > 0 } ? events.ToArray() : WebhookEventTypes.All;
            foreach (var type in requested)
            {
                if (!WebhookEventTypes.IsSupported(type))
                {
                    throw NorthstarException.Validation($"Unsupported webhook event '{type}'.");
                }
            }

            var webhook = new WebhookEndpoint
            {
                Id = $"whk_{Guid.NewGuid():N}",
                Url = uri.ToString(),
                Events = requested,
                Secret = $"whsec_{Guid.NewGuid():N}",
                CreatedAtUtc = Now(organization)
            };
            organization.Webhooks.Add(webhook);
            Record(organization, "webhook.created", $"webhook:{webhook.Id}", principal.Actor);
            return ToResponse(webhook);
        }
    }

    public void DeleteWebhook(NorthstarPrincipal principal, string webhookId)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            RequireScope(principal, TokenScopes.Admin);
            RequireRole(principal, MemberRoles.CanManageProjects, "Only owners and administrators can manage webhooks.");
            var webhook = organization.Webhooks.FirstOrDefault(candidate => candidate.Id == webhookId)
                ?? throw NorthstarException.NotFound("webhook");
            organization.Webhooks.Remove(webhook);
            Record(organization, "webhook.removed", $"webhook:{webhook.Id}", principal.Actor);
        }
    }

    public CursorPage<WebhookDeliveryResponse> ListWebhookDeliveries(NorthstarPrincipal principal, string? status, string? cursor, int limit)
    {
        lock (_gate)
        {
            var deliveries = principal.Organization.Deliveries
                .Where(delivery => status is null || delivery.Status == status)
                .OrderByDescending(delivery => delivery.CreatedAtUtc)
                .Select(ToResponse);
            return Page(deliveries, cursor, limit);
        }
    }

    public WebhookDeliveryResponse RedeliverWebhook(NorthstarPrincipal principal, string deliveryId)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            RequireScope(principal, TokenScopes.Admin);
            RequireRole(principal, MemberRoles.CanManageProjects, "Only owners and administrators can redeliver webhooks.");
            var delivery = organization.Deliveries.FirstOrDefault(candidate => candidate.Id == deliveryId)
                ?? throw NorthstarException.NotFound("webhook delivery");
            var now = Now(organization);
            delivery.Status = WebhookDeliveryStatuses.Pending;
            delivery.Attempts = 0;
            delivery.LastError = null;
            delivery.NextAttemptUtc = now;
            Record(organization, "webhook.redelivered", $"webhook-delivery:{delivery.Id}", principal.Actor);
            return ToResponse(delivery);
        }
    }

    public IReadOnlyList<DispatchJob> TakePendingDeliveries(int maximum)
    {
        lock (_gate)
        {
            var jobs = new List<DispatchJob>();
            foreach (var organization in _byId.Values)
            {
                var now = Now(organization);
                foreach (var delivery in organization.Deliveries
                             .Where(candidate =>
                                 candidate.Status == WebhookDeliveryStatuses.Pending
                                 && candidate.NextAttemptUtc <= now)
                             .OrderBy(candidate => candidate.CreatedAtUtc)
                             .Take(maximum))
                {
                    var endpoint = organization.Webhooks.FirstOrDefault(webhook => webhook.Id == delivery.EndpointId);
                    if (endpoint is null)
                    {
                        delivery.Status = WebhookDeliveryStatuses.Failed;
                        delivery.LastError = "endpoint_removed";
                        continue;
                    }

                    jobs.Add(new DispatchJob(
                        organization.Slug,
                        delivery.Id,
                        endpoint.Url,
                        endpoint.Secret,
                        delivery.EventType,
                        delivery.Payload,
                        delivery.Attempts));
                    if (jobs.Count >= maximum)
                    {
                        return jobs;
                    }
                }
            }

            return jobs;
        }
    }

    public void ReportDelivery(string organizationSlug, string deliveryId, bool success, string? error)
    {
        lock (_gate)
        {
            if (!_bySlug.TryGetValue(organizationSlug, out var organization))
            {
                return;
            }

            var delivery = organization.Deliveries.FirstOrDefault(candidate => candidate.Id == deliveryId);
            if (delivery is null)
            {
                return;
            }

            var now = Now(organization);
            delivery.Attempts++;
            if (success)
            {
                delivery.Status = WebhookDeliveryStatuses.Delivered;
                delivery.DeliveredAtUtc = now;
                delivery.LastError = null;
                return;
            }

            delivery.LastError = error ?? "delivery_failed";
            if (delivery.Attempts >= MaxWebhookAttempts)
            {
                delivery.Status = WebhookDeliveryStatuses.Failed;
            }
            else
            {
                delivery.NextAttemptUtc = now + WebhookBackoff;
            }
        }
    }

    // ---------------------------------------------------------------- audit

    public CursorPage<AuditEventResponse> ListAudit(NorthstarPrincipal principal, string? cursor, int limit)
    {
        lock (_gate)
        {
            var organization = principal.Organization;
            RequireRole(principal, MemberRoles.CanViewAudit, "This role cannot read the audit log.");
            var events = organization.Audit
                .OrderBy(entry => entry.Sequence)
                .Select(entry => new AuditEventResponse(
                    entry.Sequence,
                    entry.Action,
                    entry.Resource,
                    entry.Actor,
                    entry.OccurredAtUtc,
                    entry.Metadata.Select(pair => new AuditMetadataEntry(pair.Key, pair.Value)).ToArray()));
            return Page(events, cursor, limit);
        }
    }

    // ---------------------------------------------------------------- internals

    private void Advance(Organization organization)
    {
        var now = Now(organization);
        while (now >= organization.PeriodEndUtc)
        {
            IssueInvoice(organization);
            organization.PeriodStartUtc = organization.PeriodEndUtc;
            organization.PeriodEndUtc = organization.PeriodStartUtc.AddMonths(1);
            if (organization.CancelAtPeriodEnd)
            {
                organization.CancelAtPeriodEnd = false;
                organization.Status = SubscriptionStatuses.Canceled;
                organization.CanceledAtUtc = organization.PeriodStartUtc;
                Record(organization, "subscription.canceled", $"organization:{organization.Id}", "northstar");
            }
        }

        RefreshStatus(organization);
    }

    private void IssueInvoice(Organization organization)
    {
        var plan = organization.Plan;
        var periodStart = organization.PeriodStartUtc;
        var periodEnd = organization.PeriodEndUtc;
        var lines = new List<InvoiceLine>();
        if (plan.MonthlyBasePrice > 0)
        {
            lines.Add(new InvoiceLine($"Northstar {plan.Name} plan", 1, plan.MonthlyBasePrice, plan.MonthlyBasePrice));
        }

        var extraSeats = Math.Max(0, organization.BillableSeats - plan.IncludedSeats);
        if (extraSeats > 0 && plan.ExtraSeatPrice > 0)
        {
            lines.Add(new InvoiceLine($"Additional seats ({extraSeats})", extraSeats, plan.ExtraSeatPrice, extraSeats * plan.ExtraSeatPrice));
        }

        if (plan.OveragePricePerDeployMinute > 0)
        {
            var minutes = UsageInPeriod(organization, UsageMetrics.DeployMinutes, periodStart, periodEnd);
            var overage = Math.Max(0, minutes - plan.IncludedDeployMinutes);
            if (overage > 0)
            {
                var amount = Math.Round((decimal)overage * plan.OveragePricePerDeployMinute, 2, MidpointRounding.AwayFromZero);
                lines.Add(new InvoiceLine($"Deploy-minute overage ({overage:0.#} min)", overage, plan.OveragePricePerDeployMinute, amount));
            }
        }

        if (organization.CreditBalance > 0)
        {
            lines.Add(new InvoiceLine("Account credit", 1, -organization.CreditBalance, -organization.CreditBalance));
            organization.CreditBalance = 0;
        }

        var subtotal = lines.Sum(line => line.Amount);
        if (lines.Count == 0 || subtotal <= 0)
        {
            return;
        }

        var issued = periodEnd;
        var invoice = new Invoice
        {
            Id = ++organization.InvoiceSequence,
            Number = $"INV-{issued:yyyyMM}-{organization.InvoiceSequence:D4}",
            Status = InvoiceStatuses.Open,
            PeriodStartUtc = periodStart,
            PeriodEndUtc = periodEnd,
            IssuedAtUtc = issued,
            DueAtUtc = issued.AddDays(7),
            Lines = lines
        };
        organization.Invoices.Add(invoice);
        Record(organization, "invoice.issued", $"invoice:{invoice.Id}", "northstar",
            new Dictionary<string, string> { ["number"] = invoice.Number, ["total"] = invoice.Total.ToString("0.00") });
        Enqueue(organization, WebhookEventTypes.InvoiceIssued, new { invoice = ToResponse(invoice) }, issued);
    }

    private void RefreshStatus(Organization organization)
    {
        if (organization.Status == SubscriptionStatuses.Canceled)
        {
            return;
        }

        var now = Now(organization);
        var pastDue = organization.Invoices.Any(invoice =>
            invoice.Status == InvoiceStatuses.Open
            && (invoice.DueAtUtc < now
                || invoice.Payments.Any(payment => payment.Status == PaymentStatuses.Failed)));
        organization.Status = pastDue ? SubscriptionStatuses.PastDue : SubscriptionStatuses.Active;
    }

    private void Require(Organization organization)
    {
        if (organization.Status == SubscriptionStatuses.Canceled)
        {
            throw NorthstarException.PaymentRequired("The subscription is canceled.");
        }

        if (organization.Status == SubscriptionStatuses.PastDue)
        {
            throw NorthstarException.PaymentRequired("The subscription is past due.");
        }

        if (organization.Status == OrganizationStatuses.Suspended)
        {
            throw NorthstarException.Forbidden("The organization is suspended.");
        }
    }

    private static void RequireFeature(Organization organization, string actual, string expected, string feature)
    {
        if (actual == expected && !organization.Plan.HasFeature(feature))
        {
            throw NorthstarException.FeatureUnavailable(feature);
        }
    }

    private static void RequireScope(NorthstarPrincipal principal, string scope)
    {
        if (!principal.HasScope(scope))
        {
            throw NorthstarException.Forbidden($"The token is missing the '{scope}' scope.");
        }
    }

    private static void RequireRole(NorthstarPrincipal principal, Func<string, bool> predicate, string message)
    {
        if (!predicate(principal.Member.Role))
        {
            throw NorthstarException.Forbidden(message);
        }
    }

    private void RequireSeatAvailable(Organization organization)
    {
        var next = organization.BillableSeats + 1;
        if (organization.Plan.MaxSeats is { } max && next > max)
        {
            throw NorthstarException.PlanLimit(
                $"The {organization.Plan.Name} plan includes at most {max} seats.",
                new Dictionary<string, string> { ["limit"] = max.ToString(), ["current"] = organization.BillableSeats.ToString() });
        }
    }

    private static void ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal) || email.Contains(' ', StringComparison.Ordinal))
        {
            throw NorthstarException.Validation($"'{email}' is not a valid email address.");
        }
    }

    private static bool IsSupportedMetric(string metric)
        => metric is UsageMetrics.DeployMinutes or UsageMetrics.StorageGb or UsageMetrics.BandwidthGb;

    private double UsageInPeriod(Organization organization, string metric, DateTimeOffset from, DateTimeOffset to)
        => organization.Usage
            .Where(record => record.Metric == metric && record.OccurredAtUtc >= from && record.OccurredAtUtc < to)
            .Sum(record => record.Quantity);

    private static decimal ProrationCredit(Organization organization, PlanDefinition plan, DateTimeOffset now)
    {
        var total = organization.PeriodEndUtc - organization.PeriodStartUtc;
        var remaining = organization.PeriodEndUtc - now;
        if (total <= TimeSpan.Zero || remaining <= TimeSpan.Zero)
        {
            return 0m;
        }

        var fraction = Math.Clamp((decimal)(remaining.TotalSeconds / total.TotalSeconds), 0m, 1m);
        var extraSeats = Math.Max(0, organization.BillableSeats - plan.IncludedSeats);
        var monthly = plan.MonthlyBasePrice + (extraSeats * plan.ExtraSeatPrice);
        return Math.Round(monthly * fraction, 2, MidpointRounding.AwayFromZero);
    }

    private UsageRecord RecordUsage(Organization organization, string metric, double quantity, DateTimeOffset occurredAt)
    {
        var record = new UsageRecord
        {
            Id = ++organization.UsageSequence,
            Metric = metric,
            Quantity = quantity,
            OccurredAtUtc = occurredAt
        };
        organization.Usage.Add(record);
        return record;
    }

    private Membership AddMember(Organization organization, string email, string role, string status, DateTimeOffset now)
    {
        var member = new Membership
        {
            Id = $"mem_{Guid.NewGuid():N}",
            OrganizationId = organization.Id,
            Email = email,
            Role = role,
            Status = status,
            InvitedAtUtc = now,
            JoinedAtUtc = status == MemberStatuses.Active ? now : null
        };
        organization.Members[member.Id] = member;
        return member;
    }

    private ApiToken IssueToken(Organization organization, Membership member, string name, IReadOnlyList<string> scopes, DateTimeOffset now)
    {
        var prefix = $"nsk_{Guid.NewGuid():N}"[..12];
        var token = new ApiToken
        {
            Id = $"tok_{Guid.NewGuid():N}",
            Name = name,
            Prefix = prefix,
            Secret = $"{prefix}_{Guid.NewGuid():N}",
            Scopes = scopes,
            MemberId = member.Id,
            CreatedAtUtc = now
        };
        organization.Tokens[token.Secret] = token;
        _tokenIndex[token.Secret] = organization.Id;
        member.Token ??= token.Secret;
        return token;
    }

    private void Revoke(Organization organization, ApiToken token)
    {
        organization.Tokens.Remove(token.Secret);
        _tokenIndex.Remove(token.Secret);
    }

    private void Record(
        Organization organization,
        string action,
        string resource,
        string actor,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        organization.Audit.Add(new AuditEvent
        {
            Sequence = ++organization.AuditSequence,
            Action = action,
            Resource = resource,
            Actor = actor,
            OccurredAtUtc = Now(organization),
            Metadata = metadata ?? new Dictionary<string, string>()
        });
    }

    private void Enqueue(Organization organization, string eventType, object payload, DateTimeOffset now)
    {
        var subscribed = organization.Webhooks.Where(webhook => webhook.Active && webhook.Events.Contains(eventType)).ToArray();
        if (subscribed.Length == 0)
        {
            return;
        }

        var json = JsonSerializer.Serialize(payload, _json);
        foreach (var webhook in subscribed)
        {
            organization.Deliveries.Add(new WebhookDelivery
            {
                Id = $"whd_{Guid.NewGuid():N}",
                EndpointId = webhook.Id,
                EventType = eventType,
                Payload = json,
                CreatedAtUtc = now,
                NextAttemptUtc = now
            });
        }
    }

    private Organization RequireOrganization(string slug)
        => _bySlug.TryGetValue(slug, out var organization) ? organization : throw NorthstarException.NotFound("tenant");

    private static Membership RequireMember(Organization organization, string memberId)
        => organization.Members.TryGetValue(memberId, out var member) && member.Status != MemberStatuses.Removed
            ? member
            : throw NorthstarException.NotFound("member");

    private static int CountOwners(Organization organization)
        => organization.Members.Values.Count(member =>
            member.Role == MemberRoles.Owner && member.Status != MemberStatuses.Removed);

    private static Project RequireProject(Organization organization, string projectId)
        => organization.Projects.TryGetValue(projectId, out var project)
            ? project
            : throw NorthstarException.NotFound("project");

    private static (Project Project, Environment Environment) RequireEnvironment(Organization organization, string environmentId)
    {
        foreach (var project in organization.Projects.Values)
        {
            if (project.Environments.TryGetValue(environmentId, out var environment))
            {
                return (project, environment);
            }
        }

        throw NorthstarException.NotFound("environment");
    }

    private static Deployment RequireDeployment(Organization organization, string deploymentId)
        => organization.Projects.Values
            .SelectMany(project => project.Environments.Values)
            .SelectMany(environment => environment.Deployments)
            .FirstOrDefault(deployment => deployment.Id == deploymentId)
            ?? throw NorthstarException.NotFound("deployment");

    private static Invoice RequireInvoice(Organization organization, long invoiceId)
        => organization.Invoices.FirstOrDefault(invoice => invoice.Id == invoiceId)
            ?? throw NorthstarException.NotFound("invoice");

    private static CursorPage<T> Page<T>(IEnumerable<T> source, string? cursor, int limit)
    {
        var all = source.ToList();
        var start = int.TryParse(cursor, out var parsed) && parsed > 0 ? Math.Min(parsed, all.Count) : 0;
        var size = Math.Clamp(limit <= 0 ? 25 : limit, 1, 100);
        var items = all.Skip(start).Take(size).ToArray();
        var next = start + items.Length;
        var hasMore = next < all.Count;
        return new CursorPage<T>(items, hasMore ? next.ToString() : null, hasMore, all.Count);
    }

    private static IReadOnlyList<string> AllScopes() =>
        [TokenScopes.Read, TokenScopes.Deploy, TokenScopes.Billing, TokenScopes.Admin];

    private static IReadOnlyList<string> ScopesForRole(string role) => role switch
    {
        MemberRoles.Owner or MemberRoles.Administrator => AllScopes(),
        MemberRoles.Billing => [TokenScopes.Read, TokenScopes.Billing],
        MemberRoles.Developer => [TokenScopes.Read, TokenScopes.Deploy],
        _ => [TokenScopes.Read]
    };

    private static int StableSum(string value)
    {
        var sum = 0;
        foreach (var character in value)
        {
            sum = (sum + character) % 10_000;
        }

        return sum;
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        var slug = builder.ToString().Trim('-');
        return string.IsNullOrEmpty(slug) ? $"tenant-{Guid.NewGuid():N}"[..16] : slug;
    }

    // ---------------------------------------------------------------- projections

    private static OrganizationResponse ToResponse(Organization organization) => new(
        organization.Id,
        organization.Slug,
        organization.Name,
        organization.PlanId,
        organization.Plan.Name,
        organization.Status,
        organization.BillableSeats,
        organization.Plan.MaxSeats,
        organization.Projects.Values.Count(project => project.Status == ProjectStatuses.Active),
        organization.Plan.MaxProjects,
        organization.CancelAtPeriodEnd,
        organization.CreatedAtUtc,
        organization.PeriodStartUtc,
        organization.PeriodEndUtc);

    private static MembershipResponse ToResponse(Membership member) => new(
        member.Id,
        member.OrganizationId,
        member.Email,
        member.Role,
        member.Status,
        member.InvitedAtUtc,
        member.JoinedAtUtc);

    private static SubscriptionResponse ToSubscriptionResponse(Organization organization)
    {
        var plan = organization.Plan;
        return new SubscriptionResponse(
            plan.Id,
            plan.Name,
            organization.Status,
            organization.BillableSeats,
            plan.IncludedSeats,
            plan.MonthlyBasePrice,
            plan.ExtraSeatPrice,
            plan.IncludedDeployMinutes,
            organization.CancelAtPeriodEnd,
            organization.PeriodStartUtc,
            organization.PeriodEndUtc);
    }

    private static ApiTokenResponse ToResponse(ApiToken token) => new(
        token.Id,
        token.Name,
        token.Prefix,
        token.Scopes,
        token.CreatedAtUtc,
        token.LastUsedAtUtc);

    private static ProjectResponse ToResponse(Project project) => new(
        project.Id,
        project.Name,
        project.Slug,
        project.Status,
        project.Environments.Values.Count,
        project.CreatedAtUtc);

    private static EnvironmentResponse ToResponse(Environment environment) => new(
        environment.Id,
        environment.ProjectId,
        environment.Name,
        environment.Kind,
        environment.Status,
        environment.CurrentVersion,
        environment.CreatedAtUtc);

    private static DeploymentResponse ToResponse(Deployment deployment) => new(
        deployment.Id,
        deployment.ProjectId,
        deployment.EnvironmentId,
        deployment.Version,
        deployment.CommitSha,
        deployment.Status,
        deployment.RequestedBy,
        deployment.DeployMinutes,
        deployment.CreatedAtUtc,
        deployment.CompletedAtUtc);

    private static UsageRecordResponse ToResponse(UsageRecord record) => new(
        record.Id,
        record.Metric,
        record.Quantity,
        record.OccurredAtUtc);

    private static InvoiceResponse ToResponse(Invoice invoice) => new(
        invoice.Id,
        invoice.Number,
        invoice.Status,
        invoice.PeriodStartUtc,
        invoice.PeriodEndUtc,
        invoice.IssuedAtUtc,
        invoice.DueAtUtc,
        invoice.PaidAtUtc,
        invoice.Subtotal,
        invoice.Tax,
        invoice.Total,
        invoice.Lines.Select(line => new InvoiceLineResponse(line.Description, line.Quantity, line.UnitPrice, line.Amount)).ToArray(),
        invoice.Payments.Select(ToResponse).ToArray());

    private static PaymentResponse ToResponse(Payment payment) => new(
        payment.Id,
        payment.Amount,
        payment.Status,
        payment.Method,
        payment.FailureReason,
        payment.AttemptedAtUtc);

    private static WebhookEndpointResponse ToResponse(WebhookEndpoint webhook) => new(
        webhook.Id,
        webhook.Url,
        webhook.Events,
        webhook.Active,
        webhook.Secret,
        webhook.CreatedAtUtc);

    private static WebhookDeliveryResponse ToResponse(WebhookDelivery delivery) => new(
        delivery.Id,
        delivery.EndpointId,
        delivery.EventType,
        delivery.Status,
        delivery.Attempts,
        delivery.LastError,
        delivery.CreatedAtUtc,
        delivery.DeliveredAtUtc);
}
