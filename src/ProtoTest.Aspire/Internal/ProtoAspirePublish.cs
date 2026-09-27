namespace ProtoTest.Aspire;

/// <summary>What one publish mapping fills a key with.</summary>
internal enum ProtoAspirePublishKind
{
    /// <summary>The resource's endpoint, an absolute URL.</summary>
    Endpoint,

    /// <summary>The resource's connection string.</summary>
    ConnectionString
}

/// <summary>
/// One key the started AppHost fills from an Aspire resource: the resource, the target's declared key
/// and what is read from the resource. A configured key is never overwritten.
/// </summary>
internal sealed record ProtoAspirePublish(string Resource, string Key, ProtoAspirePublishKind Kind)
{
    /// <summary>The evidence attribute segment this mapping records.</summary>
    public string KindTag => Kind switch
    {
        ProtoAspirePublishKind.Endpoint => "address",
        _ => "connection_string"
    };
}
