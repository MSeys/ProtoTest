namespace ProtoTest.AdapterContract;

using ProtoTest.Core;

public static class AdapterContract
{
    private static readonly string[] ExpectedBeforeSequence =
    [
        "Hook:Before",
        "Class:Before",
        "Method:Before"
    ];

    internal static readonly string[] ExpectedCompletedSequence =
    [
        .. ExpectedBeforeSequence,
        "Test",
        "Method:After",
        "Class:After",
        "Hook:After"
    ];

    public static void VerifyTestBody<TDeclaringType>(string methodName)
    {
        var context = Proto.Context;
        var state = context.Resolve<AdapterContractState>();

        Ensure(
            state.Events.SequenceEqual(ExpectedBeforeSequence),
            $"Unexpected before lifecycle: {string.Join(", ", state.Events)}");
        Ensure(context.TestMethod.DeclaringType == typeof(TDeclaringType),
            $"Expected declaring type '{typeof(TDeclaringType).FullName}', but found '{context.TestMethod.DeclaringType?.FullName}'.");
        Ensure(context.TestMethod.Name == methodName,
            $"Expected method '{methodName}', but found '{context.TestMethod.Name}'.");
        Ensure(!string.IsNullOrWhiteSpace(context.TestName), "The adapter supplied an empty test name.");
        Ensure(context.TestId.Length is > 0 and <= 18 && context.TestId.All(char.IsAsciiDigit),
            $"The adapter supplied invalid test ID '{context.TestId}'.");
        Ensure(context.TestNumber == long.Parse(context.TestId),
            "The numeric and string test ID representations differ.");

        context.AddAttachment(
            "adapter-contract",
            "ProtoTest adapter attachment publishing succeeded.",
            description: "Shared adapter contract artifact");

        state.Events.Add("Test");
    }

    internal static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"ProtoTest adapter contract failed. {message}");
        }
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class AdapterContractAttribute(string name) : ProtoAttribute
{
    private bool _beforeRan;

    public override Task BeforeTestAsync(ProtoExecutionContext context)
    {
        _beforeRan = true;
        context.Resolve<AdapterContractState>().Events.Add($"{name}:Before");
        return Task.CompletedTask;
    }

    public override Task AfterTestAsync(ProtoExecutionContext context)
    {
        AdapterContract.Ensure(_beforeRan,
            $"The '{name}' attribute instance was recreated between setup and teardown.");
        context.Resolve<AdapterContractState>().Events.Add($"{name}:After");
        return Task.CompletedTask;
    }
}

public sealed class AdapterContractHook : IProtoTestHook
{
    public Task BeforeTestAsync(ProtoExecutionContext context)
    {
        if (!IsContractTest(context))
        {
            return Task.CompletedTask;
        }

        var state = new AdapterContractState();
        state.Events.Add("Hook:Before");
        context.SetContext(state);
        return Task.CompletedTask;
    }

    public Task AfterTestAsync(ProtoExecutionContext context)
    {
        var state = context.TryResolve<AdapterContractState>();
        if (state is null)
        {
            return Task.CompletedTask;
        }

        state.Events.Add("Hook:After");
        AdapterContract.Ensure(
            state.Events.SequenceEqual(AdapterContract.ExpectedCompletedSequence),
            $"Unexpected completed lifecycle: {string.Join(", ", state.Events)}");

        return Task.CompletedTask;
    }

    private static bool IsContractTest(ProtoExecutionContext context)
        => ProtoAttributeResolver.Resolve(context.TestMethod).OfType<AdapterContractAttribute>().Any();
}

internal sealed class AdapterContractState : IProtoContext
{
    public List<string> Events { get; } = [];
}
