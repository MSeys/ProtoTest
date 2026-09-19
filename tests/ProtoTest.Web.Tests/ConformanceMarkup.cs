namespace ProtoTest.Web.Tests;

/// <summary>
/// The markup of the shared web conformance page. Every backend runs its locator semantics against these
/// same documents, so a locator that works on Playwright is expected to resolve on Selenium too.
/// </summary>
internal static class ConformanceMarkup
{
    public const string VueDiscoveryHtml = """
        <!doctype html>
        <html><body>
          <div id="app" data-v-app></div>
          <script>
            const app = document.querySelector('[data-v-app]');
            app.__vue_app__ = {
              config: {
                globalProperties: {
                  $router: {
                    getRoutes: () => [
                      { path: '/orders' },
                      { path: '/orders/:id' }
                    ]
                  }
                }
              }
            };
          </script>
        </body></html>
        """;

    public const string VueTwoChildRoutesHtml = """
        <!doctype html>
        <html><body>
          <div id="app"></div>
          <script>
            const root = document.querySelector('#app');
            root.__vue__ = {
              $router: {
                options: {
                  routes: [
                    {
                      path: '/orders',
                      children: [
                        { path: '', children: [{ path: 'summary' }] },
                        { path: 'new' },
                        { path: ':id' }
                      ]
                    },
                    { path: 'relative-top' }
                  ]
                }
              }
            };
          </script>
        </body></html>
        """;

    public const string NamespacedAttributeHtml = """
        <!doctype html>
        <html><body>
          <p xml:lang="en">Hello</p>
          <p xml:lang="nl">Hallo</p>
        </body></html>
        """;

    public const string EscapeHatchHtml = """
        <!doctype html>
        <html><body>
          <input placeholder="Search invoices">
          <form onsubmit="return false">
            <input type="checkbox" data-field="newsletter" aria-label="Newsletter">
            <button class="primary" onclick="document.getElementById('banner').hidden=false">Go</button>
          </form>
          <p id="banner" hidden>Subscribed to updates</p>
          <a href="#details">Open</a>
          <span data-testid="hidden" style="display:none">secret</span>
          <ul><li class="tag">alpha</li><li class="tag">beta</li><li class="tag">gamma</li></ul>
          <table data-testid="invoices">
            <thead><tr><th>Invoice</th><th>Total</th></tr></thead>
            <tbody><tr><td>INV-1</td><td>€ 10</td></tr><tr><td>INV-2</td><td>€ 20</td></tr></tbody>
          </table>
        </body></html>
        """;

    public const string Html = """
        <!doctype html>
        <html><body>
          <label for="name">Name</label><input id="name">
          <label for="remember">Remember me</label><input id="remember" type="checkbox">
          <label for="language">Language</label><select id="language"><option value="en">English</option><option value="nl">Nederlands</option></select>
          <button disabled onclick="document.querySelector('[role=status]').textContent='saved'">Save</button>
          <div role="status">idle</div>
          <table data-testid="invoices">
            <thead><tr><th>Invoice</th><th>Total</th></tr></thead>
            <tbody><tr><td>INV-1</td><td>€ 10</td></tr></tbody>
          </table>
          <script>setTimeout(() => document.querySelector('button').disabled = false, 100);</script>
        </body></html>
        """;
}
