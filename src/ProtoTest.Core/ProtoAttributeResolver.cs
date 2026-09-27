namespace ProtoTest.Core;

using System.Collections;
using System.Reflection;

/// <summary>
/// Resolves the ProtoTest lifecycle attributes that apply to a test method, expanding the composite
/// attributes among them.
/// </summary>
public static class ProtoAttributeResolver
{
    /// <summary>
    /// Returns inherited class-level attributes followed by inherited method-level attributes, with
    /// every <see cref="ProtoCompositeAttribute"/> expanded into the attributes it declares.
    /// Lifecycle ordering is subsequently determined by <see cref="ProtoAttribute.Order"/>.
    /// </summary>
    public static IReadOnlyList<ProtoAttribute> Resolve(MethodInfo testMethod)
    {
        ArgumentNullException.ThrowIfNull(testMethod);

        var attributes = new List<ProtoAttribute>();

        if (testMethod.DeclaringType is not null)
        {
            attributes.AddRange(
                testMethod.DeclaringType.GetCustomAttributes<ProtoAttribute>(inherit: true));
        }

        attributes.AddRange(testMethod.GetCustomAttributes<ProtoAttribute>(inherit: true));
        return [.. Expand(attributes).OfType<ProtoAttribute>()];
    }

    /// <summary>
    /// Expands the composite attributes in <paramref name="attributes"/> into the attributes they
    /// declare, recursively. Every attribute handed in is an explicit declaration: it is kept, and a
    /// composed attribute identical to one of them is dropped, so an attribute that appears both
    /// explicitly and through a composite runs once. Two attributes are identical when they have the
    /// same type and the same public property values (collections compare element-wise, so
    /// <see cref="ProtoAttribute.Order"/> participates). A composite whose chain reaches itself throws
    /// naming the chain.
    /// </summary>
    public static IReadOnlyList<Attribute> Expand(IEnumerable<Attribute> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        var declarations = attributes as IReadOnlyList<Attribute> ?? [.. attributes];
        var expanded = new List<Attribute>(declarations.Count);
        var chain = new List<Type>();
        foreach (var attribute in declarations)
        {
            Include(attribute, declarations, expanded, chain, isComposed: false);
        }

        return expanded;
    }

    private static void Include(
        Attribute attribute,
        IReadOnlyList<Attribute> declarations,
        List<Attribute> expanded,
        List<Type> chain,
        bool isComposed)
    {
        if (attribute is ProtoCompositeAttribute composite)
        {
            var type = attribute.GetType();
            if (chain.Contains(type))
            {
                var cycle = string.Join(
                    " → ",
                    chain.Append(type).Select(step => step.FullName ?? step.Name));
                throw new InvalidOperationException(
                    $"Composite attribute cycle detected: {cycle}. A composite must not compose itself, " +
                    "directly or through another composite.");
            }

            // A composed composite already declared (explicitly or by an earlier composite) brings
            // nothing new: its declarations are already in the expansion.
            if (isComposed && IsKnown(attribute, declarations, expanded))
            {
                return;
            }

            chain.Add(type);
            try
            {
                // The composed attributes are inserted before the composite, so a tie on Order runs
                // what the composite needs before the composite itself.
                foreach (var composed in composite.ComposedAttributes)
                {
                    Include(composed, declarations, expanded, chain, isComposed: true);
                }
            }
            finally
            {
                chain.RemoveAt(chain.Count - 1);
            }

            expanded.Add(attribute);
            return;
        }

        if (isComposed && IsKnown(attribute, declarations, expanded))
        {
            return;
        }

        expanded.Add(attribute);
    }

    private static bool IsKnown(Attribute attribute, IReadOnlyList<Attribute> declarations, List<Attribute> expanded)
        => declarations.Any(declaration => SameIdentity(declaration, attribute))
           || expanded.Any(declaration => SameIdentity(declaration, attribute));

    private static bool SameIdentity(Attribute left, Attribute right)
    {
        if (left.GetType() != right.GetType())
        {
            return false;
        }

        foreach (var property in left.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (!SameValue(property.GetValue(left), property.GetValue(right)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameValue(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        // A string is an IEnumerable of chars; compare it as a value.
        if (left is string || right is string)
        {
            return left.Equals(right);
        }

        if (left is IEnumerable leftItems && right is IEnumerable rightItems)
        {
            return SameItems(leftItems, rightItems);
        }

        return left.Equals(right);
    }

    private static bool SameItems(IEnumerable left, IEnumerable right)
    {
        var leftItems = left.GetEnumerator();
        var rightItems = right.GetEnumerator();
        while (true)
        {
            var leftHasNext = leftItems.MoveNext();
            if (leftHasNext != rightItems.MoveNext())
            {
                return false;
            }

            if (!leftHasNext)
            {
                return true;
            }

            if (!SameValue(leftItems.Current, rightItems.Current))
            {
                return false;
            }
        }
    }
}
