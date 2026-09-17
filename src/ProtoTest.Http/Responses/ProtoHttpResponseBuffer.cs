namespace ProtoTest.Http;

public static class ProtoHttpResponseBuffer
{
    public static async Task<byte[]> BufferAsync(
        HttpResponseMessage response,
        int maximumBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (maximumBytes < 0) throw new InvalidOperationException("MaxResponseBodyBytes cannot be negative.");

        var original = response.Content;
        if (original.Headers.ContentLength is > 0 && original.Headers.ContentLength > maximumBytes)
            throw new ProtoResponseTooLargeException(maximumBytes, original.Headers.ContentLength.Value);

        await using var source = await original.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
        var buffer = new byte[16 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > maximumBytes) throw new ProtoResponseTooLargeException(maximumBytes, total);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        var bytes = destination.ToArray();
        var buffered = new ByteArrayContent(bytes);
        foreach (var header in original.Headers) buffered.Headers.TryAddWithoutValidation(header.Key, header.Value);
        response.Content = buffered;
        original.Dispose();
        return bytes;
    }
}
