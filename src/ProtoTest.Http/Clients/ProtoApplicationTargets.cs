namespace ProtoTest.Http;

/// <summary>Resolves the application a registered target belongs to.</summary>
public static class ProtoApplicationTargets
{
    /// <summary>Returns the application registered for <paramref name="targetName"/>, or the target name itself.</summary>
    public static string ResolveApplication(string targetName, IEnumerable<ProtoApplicationTarget>? targets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        foreach (var target in targets ?? [])
        {
            if (string.Equals(target.TargetName, targetName, StringComparison.OrdinalIgnoreCase))
            {
                return target.Application;
            }
        }

        return targetName;
    }
}
