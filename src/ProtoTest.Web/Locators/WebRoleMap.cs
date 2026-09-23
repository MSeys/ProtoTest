namespace ProtoTest.Web;

/// <summary>
/// The role vocabulary every backend resolves: the canonical lowercase ARIA name. Playwright resolves
/// roles natively through its ARIA engine; Selenium translates the name to XPath. Public so additional
/// backend packages translate identically, next to <see cref="WebXPath"/> for the same reason.
/// </summary>
public static class WebRoleMap
{
    /// <summary>The lowercase ARIA name a role is matched by: <c>img</c> for Image, <c>listitem</c> for ListItem.</summary>
    public static string AriaName(WebRole role)
        => role switch
        {
            WebRole.ListItem => "listitem",
            WebRole.MenuItem => "menuitem",
            WebRole.ProgressBar => "progressbar",
            WebRole.RowGroup => "rowgroup",
            WebRole.SpinButton => "spinbutton",
            WebRole.TabList => "tablist",
            WebRole.TabPanel => "tabpanel",
            WebRole.TreeItem => "treeitem",
            WebRole.Image => "img",
            _ => role.ToString().ToLowerInvariant()
        };
}
