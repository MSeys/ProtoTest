namespace ProtoTest.Xunit3.Tests;

using ProtoTest.Core;
using Xunit;

public class ProtoTestTheoryTests
{
    [ProtoTestTheory]
    [InlineData("Alpha", 100)]
    [InlineData("Beta", 200)]
    public void ProtoTestTheory_ShouldExecutePerDataRow(string label, int value)
    {
        // Act & Assert
        var service = Proto.Context.Service<ITestService>();
        Assert.NotNull(service);
        Assert.True(value > 0);
        Assert.False(string.IsNullOrEmpty(label));
    }
}