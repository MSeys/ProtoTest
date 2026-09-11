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

    private sealed class TenantState(string name, IReadOnlyCollection<InvoiceResponse> invoices)
    {
        public string Name { get; } = name;
        public IReadOnlyCollection<InvoiceResponse> Invoices { get; } = invoices;
        public ConcurrentDictionary<string, UserResponse> Users { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentDictionary<int, OrderResponse> Orders { get; } = new();
    }
}
