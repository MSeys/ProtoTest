namespace ProtoTest.OpenApi.Tests;

public static class OpenApiTestHelper
{
    public const string SampleJsonSpec = """
    {
      "openapi": "3.0.1",
      "info": {
        "title": "Test API",
        "version": "1.0.0"
      },
      "paths": {
        "/users/{id}": {
          "get": {
            "summary": "Get User by ID",
            "responses": {
              "200": {
                "description": "User found",
                "content": {
                  "application/json": {
                    "schema": {
                      "type": "object",
                      "properties": {
                        "id": { "type": "string" },
                        "name": { "type": "string" },
                        "address": {
                          "type": "object",
                          "properties": {
                            "city": { "type": "string" }
                          }
                        }
                      }
                    }
                  }
                }
              },
              "404": {
                "description": "User not found"
              }
            }
          }
        }
      }
    }
    """;
}