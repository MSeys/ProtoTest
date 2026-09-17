namespace ProtoTest.Core;

/// <summary>
/// Selects the application under test for a test or class and, optionally, which registered client each
/// protocol uses. Bindings use <c>Protocol:Client</c> (for example <c>Rest:Billing</c> or
/// <c>Web:Admin</c>); unlisted protocols use the application's first registered client of that protocol.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ApplicationAttribute : ProtoAttribute
{
    public ApplicationAttribute(string name, params string[] clients)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("An application name is required.", nameof(name))
            : name;
        Bindings = Parse(clients ?? []);
    }

    /// <summary>Gets the application name.</summary>
    public string Name { get; }

    /// <summary>Gets the protocol-to-client bindings, keyed by protocol name.</summary>
    public IReadOnlyDictionary<string, string> Bindings { get; }

    public override Task BeforeTestAsync(ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.SetContext(new ProtoApplicationState(Name, Bindings));
        return Task.CompletedTask;
    }

    private static IReadOnlyDictionary<string, string> Parse(IReadOnlyList<string> clients)
    {
        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in clients)
        {
            var separator = entry.IndexOf(':');
            if (separator <= 0 || separator == entry.Length - 1)
            {
                throw new ArgumentException(
                    $"Application client binding '{entry}' is invalid. Use 'Protocol:Client', for example 'Rest:Billing'.",
                    nameof(clients));
            }

            bindings[entry[..separator].Trim()] = entry[(separator + 1)..].Trim();
        }

        return bindings;
    }
}
