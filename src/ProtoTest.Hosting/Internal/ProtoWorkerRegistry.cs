namespace ProtoTest.Hosting.Internal;

using Microsoft.Extensions.Hosting;
using ProtoTest.Core;

/// <summary>The worker hosts a run started, so the execution context can reach them by name or program.</summary>
internal sealed class ProtoWorkerRegistry
{
    private readonly ProtoLock _gate = new();
    private readonly Dictionary<string, WorkerHandle> _workers = new(StringComparer.Ordinal);

    public void Add(string name, Type program, IHost host)
    {
        lock (_gate)
        {
            _workers[name] = new WorkerHandle(name, program, host);
        }
    }

    public void Remove(string name)
    {
        lock (_gate)
        {
            _workers.Remove(name);
        }
    }

    public IHost Find<TProgram>(string? name)
        where TProgram : class
    {
        lock (_gate)
        {
            if (name is not null)
            {
                if (!_workers.TryGetValue(name, out var named))
                {
                    throw new InvalidOperationException(
                        $"No worker host named '{name}' is running. Register it with builder.AddWorkerHost<{typeof(TProgram).Name}>(\"{name}\").");
                }

                if (named.Program != typeof(TProgram))
                {
                    throw new InvalidOperationException(
                        $"Worker host '{name}' hosts {named.Program.FullName}, not {typeof(TProgram).FullName}.");
                }

                return named.Host;
            }

            var matches = _workers.Values.Where(worker => worker.Program == typeof(TProgram)).ToArray();
            return matches.Length switch
            {
                1 => matches[0].Host,
                0 => throw new InvalidOperationException(
                    $"No running worker host for {typeof(TProgram).FullName}. Register it with builder.AddWorkerHost<{typeof(TProgram).Name}>()."),
                _ => throw new InvalidOperationException(
                    $"Several worker hosts run {typeof(TProgram).Name} ({string.Join(", ", matches.Select(worker => $"'{worker.Name}'"))}); pass the name.")
            };
        }
    }

    private sealed record WorkerHandle(string Name, Type Program, IHost Host);
}
