# ProtoTest.Web.Pages

The page identity and inventory shared by ProtoTest's web integrations.

`ProtoTest.Web` (browser sessions and page coverage) and `ProtoTest.AspNetCore` (the in-process
server's page inventory) both depend on this package, so neither has to reference the other for the
one concept they share: what a page *is*.

This is a transitive package. You do not reference it directly unless you are writing a web
integration of your own.

## Includes

- `WebPagePath`: normalizes an address or a route definition to one page identity
  (`/users/{id}`, `/docs/{...}`), and matches a concrete path against an inventory pattern.
- `WebPageInventory`: records page inventory the one way every producer records it, as
  `web.page.available` observations.
- `WebPageConfig`: reads a page-list section (`Include`/`Exclude`) the one way every producer reads it.

## Learn more

- [Coverage](https://prototest.dev/docs/observability/coverage)
- [Web integration](https://prototest.dev/docs/integrations/web)
