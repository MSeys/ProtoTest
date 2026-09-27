namespace ProtoTest.Messaging.MassTransit.Internal;

using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using global::MassTransit;
using global::MassTransit.Testing;
using ProtoTest.Json;
using ProtoTest.Messaging;

/// <summary>
/// Maps the messaging surface onto MassTransit's message types. A destination names a message contract
/// type - its full name, its short name, or its <c>urn:message:</c> URN - because that is the addressing
/// unit of a MassTransit bus; a message is converted to the framework's broker-agnostic
/// <see cref="ProtoMessage"/> by serializing its contract instance with the shared web JSON defaults.
/// </summary>
internal static class MassTransitMessages
{
    /// <summary>The destination a published message is addressed by: its contract type's full name.</summary>
    public static string DestinationOf(Type messageType) => messageType.FullName ?? messageType.Name;

    /// <summary>Whether <paramref name="destination"/> names <paramref name="messageType"/>.</summary>
    public static bool Matches(string destination, Type messageType)
        => string.Equals(destination, messageType.FullName, StringComparison.Ordinal)
            || string.Equals(destination, messageType.Name, StringComparison.Ordinal)
            || string.Equals(destination, TryUrn(messageType), StringComparison.Ordinal);

    /// <summary>
    /// Resolves a destination to the message contract type it names. The full name and the URN match
    /// exactly; the short name matches only when one loaded contract type carries it, so a typo or an
    /// ambiguous short name fails naming the destination instead of matching nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No loaded type matches, or several do and the destination does not name one unambiguously.
    /// </exception>
    public static Type Resolve(string destination, Assembly applicationAssembly)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        var byFullName = Find(destination, applicationAssembly, type => string.Equals(destination, type.FullName, StringComparison.Ordinal));
        if (byFullName.Count > 0)
        {
            return Single(destination, byFullName);
        }

        var byUrn = destination.StartsWith(MessageUrn.Prefix, StringComparison.Ordinal)
            ? Find(destination, applicationAssembly, type => string.Equals(destination, TryUrn(type), StringComparison.Ordinal))
            : [];
        if (byUrn.Count > 0)
        {
            return Single(destination, byUrn);
        }

        var byShortName = Find(destination, applicationAssembly, type => string.Equals(destination, type.Name, StringComparison.Ordinal));
        if (byShortName.Count > 0)
        {
            return Single(destination, byShortName);
        }

        throw new InvalidOperationException(
            $"The destination '{destination}' names no message contract type loaded in this process. " +
            "Use the contract's full name (Namespace.Type), its short name, or its urn:message: URN.");
    }

    /// <summary>
    /// Converts a harness-observed message to the framework's shape. The payload is the contract
    /// instance serialized with the shared web JSON defaults (camelCase), because the harness keeps the
    /// object, not the wire bytes; the headers are the message's own.
    /// </summary>
    public static ProtoMessage ToProtoMessage(IPublishedMessage published)
    {
        var messageType = published.MessageType;
        var payload = JsonSerializer.Serialize(published.MessageObject, messageType, ProtoJsonDefaults.Web);
        IReadOnlyDictionary<string, string?>? headers = null;
        var all = published.Context.Headers?.GetAll();
        if (all is not null)
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in all)
            {
                values[key] = Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            headers = values.Count > 0 ? values : null;
        }

        // The payload the bridge produces is JSON, whatever envelope MassTransit used on the wire.
        return new ProtoMessage(DestinationOf(messageType), payload, headers, "application/json");
    }

    /// <summary>
    /// Deserializes a JSON payload into the resolved contract type. An empty payload creates the
    /// contract's default instance, so a message that carries no data still exists on the bus; a
    /// contract that cannot produce one fails naming it and the fix.
    /// </summary>
    public static object Deserialize(string destination, Type contractType, string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                return Activator.CreateInstance(contractType)
                    ?? throw NoDefaultInstance(destination, contractType);
            }
            catch (MissingMethodException)
            {
                // A positional record has only its primary constructor, so reflection reports the
                // missing parameterless one instead of the contract's own error.
                throw NoDefaultInstance(destination, contractType);
            }
        }

        try
        {
            return JsonSerializer.Deserialize(payload, contractType, ProtoJsonDefaults.Web)
                ?? throw new InvalidOperationException(
                    $"The payload for '{destination}' is JSON null, which {contractType.Name} cannot be.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"The payload for '{destination}' is not valid JSON for {contractType.Name}: {exception.Message}",
                exception);
        }
    }

    // The named error for a contract that can carry no empty payload: a message contract either has a
    // parameterless shape - so the empty payload's default instance exists - or the test publishes an
    // explicit instance.
    private static InvalidOperationException NoDefaultInstance(string destination, Type contractType)
        => new(
            $"The destination '{destination}' names {contractType.Name}, which has no default instance. " +
            "Give the contract a parameterless shape, or publish an explicit JSON payload for it.");

    // The application's own assembly first, so a contract the suite and the application both reference
    // resolves to the application's copy; every loaded assembly after it. Dynamic and compiler-generated
    // types are never message contracts.
    private static List<Type> Find(string destination, Assembly applicationAssembly, Func<Type, bool> matches)
    {
        var found = new List<Type>();
        Collect(applicationAssembly, matches, found);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || assembly == applicationAssembly)
            {
                continue;
            }

            Collect(assembly, matches, found);
        }

        return found;
    }

    private static void Collect(Assembly assembly, Func<Type, bool> matches, List<Type> found)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            types = [.. exception.Types.OfType<Type>()];
        }
        catch (Exception)
        {
            // A reflection-only or unloadable assembly cannot contribute a contract type.
            return;
        }

        foreach (var type in types)
        {
            if (!type.IsPublic && !type.IsNestedPublic)
            {
                continue;
            }

            if (type.IsGenericTypeDefinition || type.IsSpecialName || type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            {
                continue;
            }

            if (matches(type))
            {
                found.Add(type);
            }
        }
    }

    private static Type Single(string destination, List<Type> candidates)
    {
        var distinct = candidates.Distinct().ToArray();
        if (distinct.Length == 1)
        {
            return distinct[0];
        }

        throw new InvalidOperationException(
            $"The destination '{destination}' is ambiguous: {string.Join(", ", distinct.Select(type => type.FullName))}. " +
            "Use the contract's full name or its urn:message: URN.");
    }

    // MassTransit's URN factory activates a type adapter and refuses some runtime types; a type whose
    // URN cannot be built is not a message contract, so it simply does not match.
    private static string? TryUrn(Type type)
    {
        try
        {
            return MessageUrn.ForTypeString(type);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
