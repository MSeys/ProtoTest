namespace ProtoTest.OpenApi.Tests;

using Microsoft.OpenApi.Models;
using NUnit.Framework;
using ProtoTest.OpenApi.Internal;

[TestFixture]
public class OpenApiSchemaExtractorTests
{
    [Test]
    public void ExtractResponseProperties_ShouldExtractNestedJsonPaths()
    {
        // Arrange
        var doc = OpenApiSpecLoader.LoadFromPath(OpenApiTestHelper.SampleJsonSpec);
        var operation = doc.Paths["/users/{id}"].Operations[OperationType.Get];

        // Act
        var properties = OpenApiSchemaExtractor.ExtractResponseProperties(operation);

        // Assert
        Assert.That(properties, Is.EquivalentTo(
        [
            "$.id",
            "$.name",
            "$.address",
            "$.address.city"
        ]));
    }
}