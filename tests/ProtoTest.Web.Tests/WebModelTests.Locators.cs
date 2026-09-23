namespace ProtoTest.Web.Tests;

using System.Reflection;
using System.Text;
using System.Xml.Linq;
using System.Xml.XPath;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Web.Internal;
using ProtoTest.Web.Playwright;
using ProtoTest.Web.Selenium;

public sealed partial class WebModelTests
{
    [Test]
    public void Locators_ShouldDescribeEveryFactory()
    {
        Assert.Multiple(() =>
        {
            Assert.That(By.Css("form > button.primary").Describe(), Is.EqualTo("Css(\"form > button.primary\")"));
            Assert.That(By.Text("Saved", exact: true, ignoreCase: true).Describe(),
                Is.EqualTo("Text(\"Saved\", exact: true, ignoreCase: true)"));
            Assert.That(By.Placeholder("Search", exact: false).Describe(),
                Is.EqualTo("Placeholder(\"Search\", exact: false)"));
            Assert.That(By.Attribute("data-state", "open").Describe(),
                Is.EqualTo("Attribute(\"data-state\", \"open\")"));
            Assert.That(By.TableCellAt(0).Describe(), Is.EqualTo("TableCellAt(0)"));
        });
    }

    [Test]
    public void Locators_ShouldRejectInvalidArguments()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => By.Attribute("data state", "open"));
            Assert.Throws<ArgumentException>(() => By.Attribute("onclick=\"x\"", "open"));
            Assert.Throws<ArgumentException>(() => By.Css(" "));
            Assert.Throws<ArgumentException>(() => By.Text(""));
            Assert.Throws<ArgumentException>(() => By.Placeholder(""));
        });
    }

    [Test]
    public void SeleniumTranslator_ShouldTranslateEscapeHatchAndTextLocators()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SeleniumLocatorTranslator.DiagnosticSelector(By.Css("form > button.primary")),
                Is.EqualTo("By.CssSelector: form > button.primary"));
            Assert.That(SeleniumLocatorTranslator.DiagnosticSelector(By.Attribute("data-state", "open")),
                Does.Contain("@data-state='open'"));
            Assert.That(SeleniumLocatorTranslator.DiagnosticSelector(By.Placeholder("Search")),
                Does.Contain("@placeholder='Search'"));
            Assert.That(SeleniumLocatorTranslator.DiagnosticSelector(By.Placeholder("Sea", exact: false)),
                Does.Contain("contains(@placeholder,'Sea')"));
            Assert.That(SeleniumLocatorTranslator.DiagnosticSelector(By.Text("Saved", exact: true)),
                Does.Contain("normalize-space(.)='Saved'"));
            Assert.That(SeleniumLocatorTranslator.DiagnosticSelector(By.TableCellAt(0)),
                Does.Contain("position()=1"));
        });
    }

    [Test]
    public void SeleniumTranslator_ShouldExplainUnsupportedCombinations()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<WebBackendCapabilityException>(() =>
                SeleniumLocatorTranslator.DiagnosticSelector(By.HasText("INV-123")));
            Assert.Throws<WebBackendCapabilityException>(() =>
                SeleniumLocatorTranslator.DiagnosticSelector(By.Css("tr").And(By.HasText("INV-123"))));
            Assert.Throws<WebBackendCapabilityException>(() =>
                SeleniumLocatorTranslator.DiagnosticSelector(By.Role(WebRole.Row).And(By.TestId("invoice"))));
        });
    }
}
