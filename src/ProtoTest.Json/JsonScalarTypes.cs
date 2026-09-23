namespace ProtoTest.Json;

/// <summary>
/// One definition of what counts as a scalar (non-structured) value, shared by the shape matcher, the
/// GraphQL selection and literal writers so "is this a leaf" cannot drift between them.
/// </summary>
public static class JsonScalarTypes
{
    /// <summary>Whether the type is one of the numeric types JSON numbers surface as.</summary>
    public static bool IsNumeric(Type type)
        => Type.GetTypeCode(type) is TypeCode.Byte or TypeCode.SByte
            or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64
            or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64
            or TypeCode.Decimal or TypeCode.Double or TypeCode.Single;

    /// <summary>Whether the type is a scalar: a primitive, enum, string, decimal, date/time, GUID or URI.</summary>
    public static bool IsScalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(DateOnly)
            || type == typeof(TimeOnly)
            || type == typeof(Guid)
            || type == typeof(Uri);
    }
}
