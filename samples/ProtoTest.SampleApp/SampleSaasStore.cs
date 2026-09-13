namespace ProtoTest.SampleApp;

using System.Collections.Concurrent;
using ProtoTest.SampleApp.Contracts;

public sealed class SampleSaasStore
{
    private readonly ConcurrentDictionary<string, TenantState> _tenants =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, UserResponse> _usersByToken =
        new(StringComparer.Ordinal);
    private int _orderId = 1000;
    private long _auditSequence;

    public bool TryCreateEnvironment(string tenant, string name)
    {
        var state = new TenantState(
            name,
            [
                new InvoiceResponse(701, tenant, "open", 49.95m),
                new InvoiceResponse(702, tenant, "paid", 129.00m)
            ]);
        return _tenants.TryAdd(tenant, state);
    }

    public bool DeleteEnvironment(string tenant)
    {
        if (!_tenants.TryRemove(tenant, out var state)) return false;
        foreach (var user in state.Users.Values)
        {
            _usersByToken.TryRemove(user.AccessToken, out _);
        }
        return true;
    }

    public UserResponse? CreateUser(string tenant, string email, string role)
    {
        if (!_tenants.TryGetValue(tenant, out var state) || !SampleRoles.IsSupported(role)) return null;

        var id = Guid.NewGuid().ToString("N");
        var user = new UserResponse(id, tenant, email, role, $"sample-{id}");
        state.Users[user.Id] = user;
        _usersByToken[user.AccessToken] = user;
        return user;
    }

    public UserResponse? FindUserByToken(string token)
        => _usersByToken.GetValueOrDefault(token);

    public IReadOnlyCollection<UserResponse>? GetUsers(string tenant)
        => _tenants.TryGetValue(tenant, out var state) ? state.Users.Values.ToArray() : null;

    public OrderResponse? CreateOrder(
        string tenant,
        string product,
        int quantity,
        decimal unitPrice)
    {
        if (!_tenants.TryGetValue(tenant, out var state)) return null;
        var order = new OrderResponse(
            Interlocked.Increment(ref _orderId),
            tenant,
            product,
            quantity,
            quantity * unitPrice,
            "pending");
        state.Orders[order.Id] = order;
        return order;
    }

    public OrderResponse? GetOrder(string tenant, int id)
        => _tenants.TryGetValue(tenant, out var state)
            ? state.Orders.GetValueOrDefault(id)
            : null;

    public IReadOnlyCollection<OrderResponse>? GetOrders(string tenant)
        => _tenants.TryGetValue(tenant, out var state) ? state.Orders.Values.ToArray() : null;

    public IReadOnlyCollection<InvoiceResponse>? GetInvoices(string tenant, string? state)
        => _tenants.TryGetValue(tenant, out var tenantState)
            ? tenantState.Invoices
                .Where(invoice => state is null || string.Equals(invoice.State, state, StringComparison.OrdinalIgnoreCase))
                .ToArray()
            : null;

    public WorkspaceResponse? CreateWorkspace(string tenant, string name, string region, string plan)
    {
        if (!_tenants.TryGetValue(tenant, out var state)) return null;
        var workspace = new WorkspaceResponse(Guid.NewGuid().ToString("N"), tenant, name, region, plan);
        state.Workspaces[workspace.Id] = workspace;
        AddAudit(state, tenant, "workspace.created", $"workspace:{workspace.Id}");
        return workspace;
    }

    public IReadOnlyCollection<WorkspaceResponse>? GetWorkspaces(string tenant)
        => _tenants.TryGetValue(tenant, out var state) ? state.Workspaces.Values.ToArray() : null;

    public ReleaseResponse? CreateRelease(string tenant, string workspaceId, string version, string commitSha)
    {
        if (!_tenants.TryGetValue(tenant, out var state) || !state.Workspaces.ContainsKey(workspaceId)) return null;
        var release = new ReleaseResponse(Guid.NewGuid().ToString("N"), tenant, workspaceId, version, commitSha, "deployed");
        state.Releases[release.Id] = release;
        AddAudit(state, tenant, "release.deployed", $"release:{release.Id}");
        return release;
    }

    public IReadOnlyCollection<ReleaseResponse>? GetReleases(string tenant)
        => _tenants.TryGetValue(tenant, out var state) ? state.Releases.Values.ToArray() : null;

    public IReadOnlyCollection<AuditEventResponse>? GetAuditEvents(string tenant)
        => _tenants.TryGetValue(tenant, out var state)
            ? state.AuditEvents.OrderBy(entry => entry.Sequence).ToArray()
            : null;

    public ControlPlaneResponse? GetControlPlane(string tenant)
    {
        if (!_tenants.TryGetValue(tenant, out var state)) return null;
        return new ControlPlaneResponse(
            tenant,
            state.Users.Count,
            state.Workspaces.Count,
            state.Releases.Count,
            state.Invoices.Count(invoice => invoice.State == "open"),
            state.Workspaces.Values.Sum(workspace => workspace.Plan switch
            {
                "enterprise" => 499m,
                "growth" => 199m,
                _ => 49m
            }));
    }

    private void AddAudit(TenantState state, string tenant, string action, string resource)
        => state.AuditEvents.Enqueue(new AuditEventResponse(
            Interlocked.Increment(ref _auditSequence), tenant, action, resource, DateTimeOffset.UtcNow));

    private sealed class TenantState(string name, IReadOnlyCollection<InvoiceResponse> invoices)
    {
        public string Name { get; } = name;
        public IReadOnlyCollection<InvoiceResponse> Invoices { get; } = invoices;
        public ConcurrentDictionary<string, UserResponse> Users { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentDictionary<int, OrderResponse> Orders { get; } = new();
        public ConcurrentDictionary<string, WorkspaceResponse> Workspaces { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentDictionary<string, ReleaseResponse> Releases { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentQueue<AuditEventResponse> AuditEvents { get; } = new();
    }
}
