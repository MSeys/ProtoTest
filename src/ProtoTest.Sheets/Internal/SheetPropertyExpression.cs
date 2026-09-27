namespace ProtoTest.Sheets.Internal;

using System.Linq.Expressions;
using System.Reflection;

/// <summary>
/// The one reader that turns a property expression such as <c>row =&gt; row.Amount</c> into the
/// property it names, shared by a table model's <c>Column</c> and a key-value model's <c>Column</c>.
/// </summary>
internal static class SheetPropertyExpression
{
    /// <summary>Reads the property a lambda names; anything else fails with the shape to use.</summary>
    public static PropertyInfo PropertyOf<TRow, TValue>(Expression<Func<TRow, TValue>> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        Expression body = property.Body;
        // A cast, such as row => (long)row.Count, is a Convert node around the property access.
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } conversion)
        {
            body = conversion.Operand;
        }

        return body is MemberExpression { Member: PropertyInfo info }
            ? info
            : throw new ArgumentException("Use a property access like row => row.Amount.", nameof(property));
    }
}
