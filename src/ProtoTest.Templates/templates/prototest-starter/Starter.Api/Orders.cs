using System.Collections.Concurrent;

namespace Starter.Api;

public sealed record NewOrder(string Product, int Quantity);

public sealed record Order(int Id, string Product, int Quantity, string Status);

/// <summary>Keeps orders in memory; swap it for a database when the application grows one.</summary>
public sealed class OrderStore
{
    private readonly ConcurrentDictionary<int, Order> _orders = new();
    private int _lastId;

    public Order Add(NewOrder order)
    {
        var created = new Order(Interlocked.Increment(ref _lastId), order.Product, order.Quantity, "pending");
        _orders[created.Id] = created;
        return created;
    }

    public Order? Find(int id) => _orders.GetValueOrDefault(id);
}
