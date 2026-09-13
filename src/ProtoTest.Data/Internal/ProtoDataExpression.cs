namespace ProtoTest.Data;

using System.Linq.Expressions;
using System.Reflection;

internal static class ProtoDataExpression
{
    public static PropertyInfo Property<T, TMember>(Expression<Func<T, TMember>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        Expression body = expression.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } conversion)
        {
            body = conversion.Operand;
        }

        if (body is not MemberExpression { Member: PropertyInfo property }
            || body is MemberExpression { Expression: not ParameterExpression })
        {
            throw new ArgumentException("The expression must select a direct property, for example 'x => x.Name'.", nameof(expression));
        }

        return property;
    }
}
