namespace ProtoTest.Core;

/// <summary>
/// A client that must finish work before attachments are published. Core calls
/// <see cref="CompleteAsync"/> after the test's teardown hooks, in reverse registration order, and lets
/// the client registry dispose it afterwards; a failure fails the test.
/// </summary>
public interface IProtoClientCompletion
{
    ValueTask CompleteAsync();
}
