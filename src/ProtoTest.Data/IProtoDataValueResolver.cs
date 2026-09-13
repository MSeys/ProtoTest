namespace ProtoTest.Data;

/// <summary>Extends value resolution for conventions that span more than one exact CLR type.</summary>
public interface IProtoDataValueResolver
{
    /// <summary>Attempts to resolve the requested value. Resolvers run in registration order.</summary>
    bool TryResolve(ProtoDataValueContext context, out ProtoDataResolvedValue value);
}

/// <summary>A value produced by a custom resolver together with its diagnostic source.</summary>
public sealed record ProtoDataResolvedValue(object? Value, string Source);
