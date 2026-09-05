namespace ProtoTest.Rest.Tests;

using NUnit.Framework;
using ProtoTest.Rest.Internal;

[TestFixture]
public class RouteAndQueryParserTests
{
    [Test]
    public void BuildUrl_Should_Return_Original_Template_When_Params_Are_Null()
    {
        var result = RouteAndQueryParser.BuildUrl("/api/users", null);

        Assert.That(result, Is.EqualTo("/api/users"));
    }

    [Test]
    public void BuildUrl_Should_Substitute_Route_Tokens_Case_Insensitively()
    {
        var template = "/api/v1/tenants/{tenantId}/users/{UserId}";
        var paramsObj = new { tenantId = "tenant-abc", userId = 42 };

        var result = RouteAndQueryParser.BuildUrl(template, paramsObj);

        Assert.That(result, Is.EqualTo("/api/v1/tenants/tenant-abc/users/42"));
    }

    [Test]
    public void BuildUrl_Should_Leave_Unmatched_Tokens_Intact()
    {
        var template = "/api/v1/users/{userId}/posts/{postId}";
        var paramsObj = new { userId = 42 }; // postId ontbreekt

        var result = RouteAndQueryParser.BuildUrl(template, paramsObj);

        Assert.That(result, Is.EqualTo("/api/v1/users/42/posts/{postId}"));
    }

    [Test]
    public void BuildUrl_Should_Append_Unused_Properties_As_Query_Parameters()
    {
        var template = "/api/users/{id}";
        var paramsObj = new { id = 10, active = true, sort = "desc" };

        var result = RouteAndQueryParser.BuildUrl(template, paramsObj);

        Assert.That(result, Is.EqualTo("/api/users/10?active=True&sort=desc"));
    }

    [Test]
    public void BuildUrl_Should_Use_Ampersand_If_Template_Already_Has_Query_String()
    {
        var template = "/api/users?version=2";
        var paramsObj = new { page = 1, limit = 20 };

        var result = RouteAndQueryParser.BuildUrl(template, paramsObj);

        Assert.That(result, Is.EqualTo("/api/users?version=2&page=1&limit=20"));
    }

    [Test]
    public void BuildUrl_Should_Expand_IEnumerable_Query_Parameters()
    {
        var template = "/api/products";
        var paramsObj = new { category = "tech", tags = new[] { "csharp", "dotnet", "testing" } };

        var result = RouteAndQueryParser.BuildUrl(template, paramsObj);

        Assert.That(result, Is.EqualTo("/api/products?category=tech&tags=csharp&tags=dotnet&tags=testing"));
    }

    [Test]
    public void BuildUrl_Should_UrlEncode_Values_And_Keys()
    {
        var template = "/search/{query}";
        var paramsObj = new { query = "c# & .net", filter_status = "in progress" };

        var result = RouteAndQueryParser.BuildUrl(template, paramsObj);

        Assert.That(result, Is.EqualTo("/search/c%23+%26+.net?filter_status=in+progress"));
    }

    [Test]
    public void BuildUrl_Should_Ignore_Null_Values_In_Query()
    {
        var template = "/api/items";
        var paramsObj = new { name = "Keyboard", category = (string?)null };

        var result = RouteAndQueryParser.BuildUrl(template, paramsObj);

        Assert.That(result, Is.EqualTo("/api/items?name=Keyboard"));
    }
}