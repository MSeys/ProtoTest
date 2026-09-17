namespace ProtoTest.Http;

public class ProtoHttpResponseOptions
{
    /// <summary>Maximum response body size buffered by a convenience API. Defaults to 10 MiB.</summary>
    public int MaxResponseBodyBytes { get; set; } = 10 * 1024 * 1024;
}
