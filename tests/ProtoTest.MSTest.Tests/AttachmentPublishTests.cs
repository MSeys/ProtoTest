namespace ProtoTest.MSTest.Tests;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProtoTest.AdapterContract;
using ProtoTest.Core;

[TestClass]
public sealed class AttachmentPublishTests
{
    [TestMethod]
    public async Task Attachment_ShouldReachTheMSTestResultAsAResultFile()
    {
        var method = typeof(Subjects).GetMethod(nameof(Subjects.AttachesAResult))!;

        var results = await new ProtoTestAttribute().ExecuteAsync(new FakeTestMethod(method));

        Assert.HasCount(1, results);
        Assert.IsNotNull(results[0].ResultFiles, "the adapter must hand the published file to MSTest");
        var path = results[0].ResultFiles!.Single();
        Assert.IsTrue(File.Exists(path), $"the published attachment file '{path}' must exist");
    }

#pragma warning disable MSTEST0030, MSTEST0032
    private sealed class Subjects
    {
        [ProtoTest]
        public void AttachesAResult()
            => Proto.Context.AddAttachment(AdapterProbes.CreateAttachment());
    }
#pragma warning restore MSTEST0030, MSTEST0032
}
