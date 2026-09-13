namespace ProtoTest.SampleApp;

using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using HotChocolate.Types;
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

        var filtered = orders.ToArray();
        var start = Math.Clamp(DecodeCursor(after) + 1, 0, filtered.Length);
        var end = before is null
            ? filtered.Length
            : Math.Clamp(DecodeCursor(before), start, filtered.Length);
        if (first is not null) end = Math.Min(end, start + Math.Max(0, first.Value));
        if (last is not null) start = Math.Max(start, end - Math.Max(0, last.Value));
        var materialized = filtered[start..end];
        return new OrderConnection(
            materialized,
            new PageInfo(end < filtered.Length, start > 0, materialized.Length == 0 ? null : EncodeCursor(start),
                materialized.Length == 0 ? null : EncodeCursor(end - 1)),
            filtered.Length);
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
    public async Task<OrderResponse> CreateOrder(
        CreateOrderInput input,
        [Service] IHttpContextAccessor accessor,
        [Service] SampleSaasStore store,
        [Service] ITopicEventSender eventSender,
        CancellationToken cancellationToken)
    {
        var user = SampleGraphQLSecurity.RequireTenantUser(accessor.HttpContext, store);
        if (input.Quantity <= 0 || input.UnitPrice <= 0 || string.IsNullOrWhiteSpace(input.Product))
            throw SampleGraphQLSecurity.Error("The order is invalid.", "INVALID_ORDER");
        var order = store.CreateOrder(user.Tenant, input.Product, input.Quantity, input.UnitPrice)!;
        await eventSender.SendAsync($"OrderCreated:{user.Tenant}", order, cancellationToken);
        return order;
    }

    public async Task<UploadReceipt> UploadDocument(
        IFile file,
        [Service] IHttpContextAccessor accessor,
        [Service] SampleSaasStore store,
        CancellationToken cancellationToken)
    {
        _ = SampleGraphQLSecurity.RequireTenantUser(accessor.HttpContext, store);
        await using var content = new MemoryStream();
        await file.CopyToAsync(content, cancellationToken);
        return new UploadReceipt(file.Name, file.ContentType ?? "application/octet-stream", content.Length);
    }
}

public sealed class SampleSubscription
{
    [Subscribe(With = nameof(SubscribeToOrderCreated))]
    public OrderResponse OrderCreated([EventMessage] OrderResponse order) => order;

    public async ValueTask<ISourceStream<OrderResponse>> SubscribeToOrderCreated(
        [Service] IHttpContextAccessor accessor,
        [Service] SampleSaasStore store,
        [Service] ITopicEventReceiver eventReceiver,
        CancellationToken cancellationToken)
    {
        var user = SampleGraphQLSecurity.RequireTenantUser(accessor.HttpContext, store);
        return await eventReceiver.SubscribeAsync<OrderResponse>(
            $"OrderCreated:{user.Tenant}",
            cancellationToken);
    }
}

public sealed record ApiInfo(string Name, string Version);
public sealed record CreateOrderInput(string Product, int Quantity, decimal UnitPrice);
public sealed record UploadReceipt(string FileName, string ContentType, long Length);
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
