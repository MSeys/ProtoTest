namespace ProtoTest.Sql.Internal;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The address keys the <c>AddSql</c> registration declared, recorded when the call ran so an
/// integration registered after it (ProtoTest.Sql.EntityFrameworkCore) can declare its own capability
/// under the same provided-keys rule. A repeated <c>AddSql</c> is a no-op and records nothing.
/// </summary>
internal sealed record SqlAddressKeyDeclaration(IReadOnlyList<string> Keys)
{
    /// <summary>The declaration the first <c>AddSql</c> recorded, or null when it has not run yet.</summary>
    public static SqlAddressKeyDeclaration? Find(IServiceCollection services)
        => services
            .LastOrDefault(descriptor => descriptor.ServiceType == typeof(SqlAddressKeyDeclaration))
            ?.ImplementationInstance as SqlAddressKeyDeclaration;
}
