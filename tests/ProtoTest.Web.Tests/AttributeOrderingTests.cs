namespace ProtoTest.Web.Tests;

using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Web;

/// <summary>
/// Pins the setup order the session path relies on: the application selection runs before a web session
/// is declared and before a login. A session created without the selection targets its own name, which
/// is never the application.
/// </summary>
public sealed class AttributeOrderingTests
{
    [Test]
    public void ApplicationSelection_ShouldRunBeforeSessionDeclarationsAndLogins()
    {
        var selection = new ApplicationAttribute("Api").Order;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selection, Is.EqualTo(ProtoAttributeOrder.Application));
            Assert.That(
                selection,
                Is.LessThan(new WebSessionAttribute("Default").Order),
                "a session declaration creates the session during its own setup step");
            Assert.That(selection, Is.LessThan(ProtoAttributeOrder.Default), "logins use the default order");
        }
    }
}
