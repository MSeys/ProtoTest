namespace ProtoTest.Web.Tests;

using ProtoTest.Web.Internal;

[TestFixture]
public sealed class WebBackendErrorsTests
{
    private static WebElementReference Reference()
        => new([], "LoginPage", "Submit", By.TestId("submit"));

    [Test]
    public void NotActionable_ShouldNameTheElementLocatorAndObservation()
    {
        var withoutObservation = WebBackendErrors.NotActionable(Reference(), TimeSpan.FromSeconds(5));
        var withObservation = WebBackendErrors.NotActionable(Reference(), TimeSpan.FromSeconds(5), "not found");

        Assert.Multiple(() =>
        {
            Assert.That(withoutObservation.Message, Does.Contain("did not become actionable within 00:00:05"));
            Assert.That(withoutObservation.Message, Does.Contain("TestId(\"submit\")"));
            Assert.That(withObservation.Message, Does.EndWith("Last observed: not found."));
        });
    }

    [Test]
    public void NotPresent_ShouldNameTheElementAndLocator()
        => Assert.That(
            WebBackendErrors.NotPresent(Reference(), TimeSpan.FromSeconds(1)).Message,
            Is.EqualTo("Element 'LoginPage.Submit' was not present within 00:00:01. Locator: TestId(\"submit\")."));

    [Test]
    public void MultipleMatch_ShouldReportTheCountWhenKnown()
        => Assert.Multiple(() =>
        {
            Assert.That(
                WebBackendErrors.MultipleMatch(By.TestId("row"), "Table", 3).Message,
                Is.EqualTo("Expected at most one element for TestId(\"row\") in Table, but found 3."));
            Assert.That(
                WebBackendErrors.MultipleMatch(By.TestId("row"), "Table", null).Message,
                Does.EndWith("but more than one element matched."));
        });
}
