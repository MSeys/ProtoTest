namespace ProtoTest.Rest.Tests;

using NUnit.Framework;
using ProtoTest.Rest.Internal;
using ProtoTest.Http;

[TestFixture]
public class RestUriBuilderTests
{
    [TestCase("/resource", false)]
    [TestCase("resource", false)]
    [TestCase("https://example.test/resource", true)]
    [TestCase("file:///temporary/resource", true)]
    [TestCase("custom+http://example.test", true)]
    public void ExplicitSchemeDetection_ShouldBePlatformIndependent(string target, bool expected)
        => Assert.That(ProtoHttpUri.HasExplicitScheme(target), Is.EqualTo(expected));

    [Test]
    public void BuildUrl_Should_Return_Original_Template_When_Params_Are_Null()
    {
        var result = RestUriBuilder.BuildTarget("/api/users", null);

        Assert.That(result, Is.EqualTo("/api/users"));
    }

    [Test]
    public void BuildUrl_Should_Substitute_Route_Tokens_Case_Insensitively()
    {
        var template = "/api/v1/tenants/{tenantId}/users/{UserId}";
        var paramsObj = new { tenantId = "tenant-abc", userId = 42 };

        var result = RestUriBuilder.BuildTarget(template, paramsObj);

        Assert.That(result, Is.EqualTo("/api/v1/tenants/tenant-abc/users/42"));
    }

    [Test]
    public void BuildUrl_Should_Reject_Unmatched_Tokens()
    {
        var template = "/api/v1/users/{userId}/posts/{postId}";
        var paramsObj = new { userId = 42 }; // postId ontbreekt

        var exception = Assert.Throws<ArgumentException>(() =>
            RestUriBuilder.BuildTarget(template, paramsObj));

        Assert.That(exception!.Message, Does.Contain("postId"));
    }

    [Test]
    public void BuildUrl_Should_Append_Unused_Properties_As_Query_Parameters()
    {
        var template = "/api/users/{id}";
        var paramsObj = new { id = 10, active = true, sort = "desc" };

        var result = RestUriBuilder.BuildTarget(template, paramsObj);

        Assert.That(result, Is.EqualTo("/api/users/10?active=true&sort=desc"));
    }

    [Test]
    public void BuildUrl_Should_Use_Ampersand_If_Template_Already_Has_Query_String()
    {
        var template = "/api/users?version=2";
        var paramsObj = new { page = 1, limit = 20 };

        var result = RestUriBuilder.BuildTarget(template, paramsObj);

        Assert.That(result, Is.EqualTo("/api/users?version=2&page=1&limit=20"));
    }

    [Test]
    public void BuildUrl_Should_Expand_IEnumerable_Query_Parameters()
    {
        var template = "/api/products";
        var paramsObj = new { category = "tech", tags = new[] { "csharp", "dotnet", "testing" } };

        var result = RestUriBuilder.BuildTarget(template, paramsObj);

        Assert.That(result, Is.EqualTo("/api/products?category=tech&tags=csharp&tags=dotnet&tags=testing"));
    }

    [Test]
    public void BuildUrl_Should_UrlEncode_Values_And_Keys()
    {
        var template = "/search/{query}";
        var paramsObj = new { query = "c# & .net", filter_status = "in progress" };

        var result = RestUriBuilder.BuildTarget(template, paramsObj);

        Assert.That(result, Is.EqualTo("/search/c%23%20%26%20.net?filter_status=in%20progress"));
    }

    [Test]
    public void BuildUrl_Should_Ignore_Null_Values_In_Query()
    {
        var template = "/api/items";
        var paramsObj = new { name = "Keyboard", category = (string?)null };

        var result = RestUriBuilder.BuildTarget(template, paramsObj);

        Assert.That(result, Is.EqualTo("/api/items?name=Keyboard"));
    }

    [Test]
    public void BuildUrl_ShouldIgnoreEmptyCollectionsWithoutDanglingSeparators()
    {
        var result = RestUriBuilder.BuildTarget("/items#results", new
        {
            empty = Array.Empty<string>(),
            page = 2
        });

        Assert.That(result, Is.EqualTo("/items?page=2#results"));
    }

    [Test]
    public void BuildUrl_ShouldFormatValuesUsingInvariantCulture()
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var result = RestUriBuilder.BuildTarget("/prices", new { amount = 12.5m });
            Assert.That(result, Is.EqualTo("/prices?amount=12.5"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Test]
    public void BuildUrl_ShouldAcceptStringKeyedDictionaries()
    {
        var values = new Dictionary<string, object?> { ["id"] = 42, ["view"] = "full" };

        var result = RestUriBuilder.BuildTarget("/items/{id}", values);

        Assert.That(result, Is.EqualTo("/items/42?view=full"));
    }

    [Test]
    public async Task BuildRequestUri_ShouldTreatAColonRouteAsRelativeRatherThanAScheme()
    {
        var result = await RestUriBuilder.BuildRequestUriAsync(
            "orders:search",
            null,
            new Uri("https://example.test/api/"),
            null,
            null!,
            CancellationToken.None);

        Assert.That(result, Is.EqualTo(new Uri("https://example.test/api/orders:search")));
    }
}
