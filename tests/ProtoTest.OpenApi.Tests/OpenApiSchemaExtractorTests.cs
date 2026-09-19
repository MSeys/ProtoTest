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
        var doc = OpenApiSpecLoader.Load(OpenApiTestHelper.SampleJsonSpec);
        var operation = doc.Paths["/users/{id}"].Operations[OperationType.Get];
        var response = operation.Responses["200"];

        // Act
        var properties = OpenApiSchemaExtractor.ExtractResponseProperties(doc, response);

        // Assert
        Assert.That(properties, Is.EquivalentTo(
        [
            "$",
            "$.id",
            "$.name",
            "$.address",
            "$.address.city"
        ]));
    }

    [Test]
    public void ExtractResponseProperties_ShouldResolveReferencesCompositionsAndArrays()
    {
        const string specification = """
        {
          "openapi": "3.0.1",
          "info": { "title": "Composed", "version": "1" },
          "components": {
            "schemas": {
              "User": {
                "allOf": [
                  { "type": "object", "properties": { "id": { "type": "string" } } },
                  { "type": "object", "properties": {
                    "roles": { "type": "array", "items": {
                      "type": "object", "properties": { "name": { "type": "string" } }
                    } }
                  } }
                ]
              }
            }
          },
          "paths": {
            "/users": {
              "get": {
                "responses": {
                  "200": {
                    "description": "ok",
                    "content": { "application/json": { "schema": { "$ref": "#/components/schemas/User" } } }
                  }
                }
              }
            }
          }
        }
        """;
        var document = OpenApiSpecLoader.Load(specification);
        var response = document.Paths["/users"].Operations[OperationType.Get].Responses["200"];

        var properties = OpenApiSchemaExtractor.ExtractResponseProperties(document, response);

        Assert.That(properties, Is.EquivalentTo(["$", "$.id", "$.roles", "$.roles[]", "$.roles[].name"]));
    }

    [Test]
    public void ExtractResponseProperties_ShouldNotRepeatSharedSubSchemas()
    {
        // A chain of diamonds: each level references the level below twice. Without memoizing the
        // schema/path pairs the traversal is exponential (2^40 calls) and would not finish.
        const int depth = 40;
        var schemas = new System.Text.StringBuilder();
        schemas.Append("\"Level0\": { \"type\": \"object\", \"properties\": { \"leaf\": { \"type\": \"string\" } } }");
        for (var level = 1; level <= depth; level++)
        {
            schemas.Append(
                $", \"Level{level}\": {{ \"allOf\": [" +
                $"{{ \"$ref\": \"#/components/schemas/Level{level - 1}\" }}," +
                $"{{ \"$ref\": \"#/components/schemas/Level{level - 1}\" }} ] }}");
        }

        var specification = $$"""
        {
          "openapi": "3.0.1",
          "info": { "title": "Diamond", "version": "1" },
          "components": {
            "schemas": {
              {{schemas}}
            }
          },
          "paths": {
            "/diamond": {
              "get": {
                "responses": {
                  "200": {
                    "description": "ok",
                    "content": {
                      "application/json": {
                        "schema": { "$ref": "#/components/schemas/Level{{depth}}" }
                      }
                    }
                  }
                }
              }
            }
          }
        }
        """;
        var document = OpenApiSpecLoader.Load(specification);
        var response = document.Paths["/diamond"].Operations[OperationType.Get].Responses["200"];

        var properties = OpenApiSchemaExtractor.ExtractResponseProperties(document, response);

        Assert.That(properties, Is.EquivalentTo(["$", "$.leaf"]));
    }
}
