namespace ProtoTest.GraphQL.Demo;

using ProtoTest.Core;
using ProtoTest.GraphQL;
using ProtoTest.Json;
using ProtoTest.NUnit;

[GraphQLClient(Setup.Api)]
public sealed class GraphQLExamples
{
    [ProtoTest]
    public async Task AQueryReadsLikeTheShapeItSelects()
    {
        using var response = await Proto.Context.GraphQL()
            .Query("About", query => query
                .Field("apiInfo", info => info.Fields("name", "version")))
            .ExecuteAsync();

        response.ShouldHaveNoErrors().ShouldMatchData(new
        {
            apiInfo = new { name = JsonValue.StringContaining("ProtoTest"), version = JsonValue.NotNull() }
        });
    }

    [ProtoTest]
    public async Task GraphQLErrorsRemainFirstClassTestResults()
    {
        using var response = await Proto.Context.GraphQL()
            .Query("CurrentUser", query => query.Field("me", me => me.Fields("id", "email")))
            .ExecuteAsync();

        response.ShouldHaveErrors().ShouldHaveError("UNAUTHORIZED");
    }
}
