namespace ProtoTest.SampleApp;

using HotChocolate;
using ProtoTest.SampleApp.Contracts;

public sealed class SampleQuery
{
    public ApiInfo ApiInfo() => new("ProtoTest Sample SaaS", "1.0");

    public UserResponse Me(
        [Service] IHttpContextAccessor accessor,
        [Service] SampleSaasStore store)
        => SampleGraphQLSecurity.RequireUser(accessor.HttpContext, store);

    public OrderConnection Orders(
        int? first,
        string? after,
        int? last,
        string? before,
        OrderFilterInput? where,
        IReadOnlyList<OrderSortInput>? order,
        [Service] IHttpContextAccessor accessor,
        [Service] SampleSaasStore store)
    {
        var user = SampleGraphQLSecurity.RequireTenantUser(accessor.HttpContext, store);
        IEnumerable<OrderResponse> orders = store.GetOrders(user.Tenant) ?? [];
        if (where?.Product?.Contains is { Length: > 0 } contains)
            orders = orders.Where(item => item.Product.Contains(contains, StringComparison.OrdinalIgnoreCase));
        if (where?.Total?.Gt is { } minimum) orders = orders.Where(item => item.Total > minimum);
        foreach (var sort in order ?? [])
            orders = sort.Total == SortDirection.Desc ? orders.OrderByDescending(item => item.Total)
                : sort.Total == SortDirection.Asc ? orders.OrderBy(item => item.Total) : orders;

        var materialized = orders.ToArray();
        var offset = DecodeCursor(after) + 1;
        if (before is not null) materialized = materialized.Take(Math.Max(0, DecodeCursor(before))).ToArray();
        if (offset > 0) materialized = materialized.Skip(offset).ToArray();
        if (first is not null) materialized = materialized.Take(first.Value).ToArray();
        if (last is not null) materialized = materialized.TakeLast(last.Value).ToArray();
        return new OrderConnection(
            materialized,
            new PageInfo(false, offset > 0, materialized.Length == 0 ? null : EncodeCursor(offset),
                materialized.Length == 0 ? null : EncodeCursor(offset + materialized.Length - 1)),
            store.GetOrders(user.Tenant)?.Count ?? 0);
    }

    public IReadOnlyCollection<WorkspaceResponse> Workspaces(
        [Service] IHttpContextAccessor accessor,
        [Service] SampleSaasStore store)
    {
        var user = SampleGraphQLSecurity.RequireTenantUser(accessor.HttpContext, store);
        return store.GetWorkspaces(user.Tenant) ?? [];
    }

    public ControlPlaneResponse ControlPlane(
        [Service] IHttpContextAccessor accessor,
        [Service] SampleSaasStore store)
    {
        var user = SampleGraphQLSecurity.RequireTenantUser(accessor.HttpContext, store);
        return store.GetControlPlane(user.Tenant)!;
    }

    private static int DecodeCursor(string? cursor)
        => cursor is null ? -1 : int.TryParse(cursor, out var value) ? value : -1;
    private static string EncodeCursor(int value) => value.ToString();
}

public sealed class SampleMutation
{
    public OrderResponse CreateOrder(
        CreateOrderInput input,
        [Service] IHttpContextAccessor accessor,
        [Service] SampleSaasStore store)
    {
        var user = SampleGraphQLSecurity.RequireTenantUser(accessor.HttpContext, store);
        if (input.Quantity <= 0 || input.UnitPrice <= 0 || string.IsNullOrWhiteSpace(input.Product))
            throw SampleGraphQLSecurity.Error("The order is invalid.", "INVALID_ORDER");
        return store.CreateOrder(user.Tenant, input.Product, input.Quantity, input.UnitPrice)!;
    }
}

public sealed record ApiInfo(string Name, string Version);
public sealed record CreateOrderInput(string Product, int Quantity, decimal UnitPrice);
public sealed record OrderConnection(IReadOnlyList<OrderResponse> Nodes, PageInfo PageInfo, int TotalCount);
public sealed record PageInfo(bool HasNextPage, bool HasPreviousPage, string? StartCursor, string? EndCursor);
public sealed record OrderFilterInput(StringFilterInput? Product, DecimalFilterInput? Total);
public sealed record StringFilterInput(string? Contains);
public sealed record DecimalFilterInput(decimal? Gt);
public sealed record OrderSortInput(SortDirection? Total);
public enum SortDirection { Asc, Desc }

internal static class SampleGraphQLSecurity
{
    public static UserResponse RequireUser(HttpContext? context, SampleSaasStore store)
    {
        var authorization = context?.Request.Headers.Authorization.ToString() ?? string.Empty;
        const string prefix = "Bearer ";
        var user = authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? store.FindUserByToken(authorization[prefix.Length..].Trim()) : null;
        return user ?? throw Error("Authentication is required.", "UNAUTHORIZED");
    }

    public static UserResponse RequireTenantUser(HttpContext? context, SampleSaasStore store)
    {
        var user = RequireUser(context, store);
        if (!context!.Request.Headers.TryGetValue("X-Tenant", out var tenant)
            || !string.Equals(tenant, user.Tenant, StringComparison.OrdinalIgnoreCase))
            throw Error("Tenant access is denied.", "TENANT_ACCESS_DENIED");
        return user;
    }

    public static GraphQLException Error(string message, string code)
        => new(ErrorBuilder.New().SetMessage(message).SetCode(code).Build());
}
