namespace ProtoTest.Web.Tests;

using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.XPath;
using ProtoTest.Web.Selenium;

/// <summary>
/// The Selenium translation runs against the same conformance markup the Playwright conformance tests
/// drive through a real browser. The markup is parsed as XML and the translated XPath is evaluated
/// directly, which is the closest test project equivalent of a Selenium driver evaluating it in the
/// browser.
/// </summary>
[TestFixture]
public sealed class SeleniumConformanceTests
{
    [Test]
    public void Text_ShouldResolveTheDeepestMatchOnTheSharedConformancePage()
    {
        var document = Parse(ConformanceMarkup.EscapeHatchHtml);

        Assert.Multiple(() =>
        {
            Assert.That(
                Evaluate(document, By.Text("Subscribed", exact: false)).Select(element => element.Name.LocalName),
                Is.EqualTo(new[] { "p" }),
                "only the banner carries the text; its body and html ancestors do not match");
            Assert.That(
                Evaluate(document, By.Text("SUBSCRIBED TO UPDATES", exact: true, ignoreCase: true))
                    .Select(element => element.Value),
                Is.EqualTo(new[] { "Subscribed to updates" }),
                "the case-insensitive exact match still resolves the deepest element");
        });
    }

    [Test]
    public void And_ShouldKeepRowSemanticsWhenTheTextIsInADescendant()
    {
        var document = Parse(ConformanceMarkup.EscapeHatchHtml);

        // The row itself matches the left locator, and the text lives in a cell: the deepest-element
        // guard must not strip the row.
        var rows = Evaluate(document, By.Role(WebRole.Row).And(By.HasText("INV-1"))).ToArray();

        Assert.That(rows.Select(row => row.Name.LocalName), Is.EqualTo(new[] { "tr" }));
    }

    [Test]
    public void NamespacedAttribute_ShouldResolveAsALiteralQualifiedName()
    {
        var document = Parse(ConformanceMarkup.NamespacedAttributeHtml);

        var matched = Evaluate(document, By.Attribute("xml:lang", "en")).ToArray();

        Assert.That(matched.Select(element => element.Value), Is.EqualTo(new[] { "Hello" }),
            "the xml: prefix is matched without a namespace resolver");
    }

    [Test]
    public void TableCellAt_ShouldBeDocumentScopedFromTheDriverAndChildScopedFromAnElement()
    {
        var document = Parse(ConformanceMarkup.EscapeHatchHtml);
        const string prefix = "By.XPath: ";

        var driverSelector = SeleniumLocatorTranslator.DiagnosticSelector(By.TableCellAt(1), documentScoped: true);
        var elementSelector = SeleniumLocatorTranslator.DiagnosticSelector(By.TableCellAt(1));

        Assert.Multiple(() =>
        {
            Assert.That(driverSelector, Does.Not.Contain("./*[self::th"), "the driver-rooted lookup must not use ./*");
            Assert.That(elementSelector, Does.Contain("./*[self::th"));
        });

        var driverScoped = document.XPathSelectElements(driverSelector[prefix.Length..]).ToArray();
        var row = document.Descendants("tr").First();
        var elementScoped = row.XPathSelectElements(elementSelector[prefix.Length..]).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(driverScoped.Select(cell => cell.Value), Is.EqualTo(new[] { "Total" }),
                "index 1 is the second cell in document order");
            Assert.That(elementScoped.Select(cell => cell.Value), Is.EqualTo(new[] { "Total" }),
                "index 1 is the second direct cell of the row scope");
        });
    }

    [Test]
    public void And_WithACssLeft_ShouldRejectWithTheDocumentedLimitation()
    {
        var exception = Assert.Throws<WebBackendCapabilityException>(() =>
            SeleniumLocatorTranslator.DiagnosticSelector(By.Css("tr").And(By.HasText("INV-1"))));

        Assert.That(exception!.Message, Does.Contain("CSS escape-hatch"),
            "the unsupported combination is rejected with a message that names the limitation");
    }

    private static IEnumerable<XElement> Evaluate(XDocument document, WebLocator locator)
    {
        const string prefix = "By.XPath: ";
        var selector = SeleniumLocatorTranslator.DiagnosticSelector(locator);
        Assert.That(selector, Does.StartWith(prefix));
        return document.XPathSelectElements(selector[prefix.Length..]);
    }

    /// <summary>
    /// Parses the conformance markup as XML: the HTML doctype is invalid XML and the void elements are
    /// not self-closed, so both are normalized before parsing.
    /// </summary>
    private static XDocument Parse(string markup)
    {
        var xml = markup.Replace("<!doctype html>", string.Empty, StringComparison.OrdinalIgnoreCase);
        xml = Regex.Replace(xml, @"<(input|br|img|hr|meta|link)([^>]*?)(?<!/)>", "<$1$2/>", RegexOptions.IgnoreCase);
        xml = Regex.Replace(
            xml,
            @"\s(hidden|disabled|checked|selected|readonly|required|multiple|autofocus)(?=[\s>])",
            " $1=\"\"",
            RegexOptions.IgnoreCase);
        return XDocument.Parse(xml);
    }
}
