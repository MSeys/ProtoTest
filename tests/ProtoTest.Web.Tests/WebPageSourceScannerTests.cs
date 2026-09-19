namespace ProtoTest.Web.Tests;

using ProtoTest.Web.Internal;

[TestFixture]
public sealed class WebPageSourceScannerTests
{
    private string _folder = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = Path.Combine(Path.GetTempPath(), "prototest-web-pages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Test]
    public void NextPages_ShouldMapIndexNestedDynamicAndSkipSpecials()
    {
        Write("pages/index.tsx");
        Write("pages/about.tsx");
        Write("pages/users/[id].tsx");
        Write("pages/docs/[...slug].tsx");
        Write("pages/shop/[[...slug]].tsx");
        Write("src/pages/settings.tsx");
        Write("pages/_app.tsx");
        Write("pages/_document.tsx");
        Write("pages/_error.tsx");
        Write("pages/404.tsx");
        Write("pages/notes.md");

        var routes = WebPageSourceScanner.Discover(_folder, "next");

        Assert.That(routes, Is.EqualTo(new[]
        {
            "/", "/about", "/docs/{...}", "/settings", "/shop/{...}", "/users/{id}"
        }), "catch-alls map to the rest pattern");
    }

    [Test]
    public void NextPages_ShouldSkipApiRoutesTestsSpecsDeclarationsAndFrameworkSpecials()
    {
        Write("pages/index.tsx");
        Write("pages/api/users.ts");
        Write("pages/api/orders/[id].ts");
        Write("pages/user-list.test.tsx");
        Write("pages/user-list.spec.tsx");
        Write("pages/types.d.ts");
        Write("pages/500.tsx");
        Write("pages/_middleware.ts");
        Write("src/pages/api/health.ts");

        var routes = WebPageSourceScanner.Discover(_folder, "next");

        Assert.That(routes, Is.EqualTo(new[] { "/" }),
            "API handlers, test/spec files, declarations and framework specials are not pages");
    }

    [Test]
    public void NextAppRouter_ShouldMapPagesAndSkipGroupsAndSpecials()
    {
        Write("app/page.tsx");
        Write("app/(marketing)/about/page.tsx");
        Write("app/users/[id]/page.tsx");
        Write("app/users/[id]/layout.tsx");
        Write("app/error.tsx");
        Write("app/not-found.tsx");
        Write("app/loading.tsx");
        Write("app/template.tsx");
        Write("src/app/reports/page.tsx");

        var routes = WebPageSourceScanner.Discover(_folder, "next");

        Assert.That(routes, Is.EqualTo(new[] { "/", "/about", "/reports", "/users/{id}" }));
    }

    [Test]
    public void Remix_ShouldMapFlatRouteNames()
    {
        Write("app/routes/_index.tsx");
        Write("app/routes/_auth.login.tsx");
        Write("app/routes/users.$id.tsx");
        Write("app/routes/users._index.tsx");
        Write("app/routes/users.$id.edit.tsx");
        Write("app/routes/files.$.tsx");

        var routes = WebPageSourceScanner.Discover(_folder, "remix");

        Assert.That(routes, Is.EqualTo(new[]
        {
            "/", "/files/{...}", "/login", "/users", "/users/{id}", "/users/{id}/edit"
        }), "a bare $ is a splat and maps to the rest pattern");
    }

    [TestCase("vue")]
    [TestCase("react")]
    [TestCase("auto")]
    public void RouteLiterals_ShouldCollectAbsoluteVueAndReactPatterns(string framework)
    {
        Write("src/router/index.ts", """
            const routes = [
              { path: '/login', component: Login },
              { path: '/users/:id', component: UserDetail },
              { path: '/legacy/*', component: Legacy },
              { path: '/users/:id?', component: Optional },
              { path: 'relative-child', component: Child },
            ];
            let path = '/settings';
            """);
        Write("src/App.tsx", """
            export const App = () => (
              <Routes>
                <Route path="/dashboard" element={<Dashboard />} />
                <Route path="/orders/:orderId" element={<Order />} />
                <Route path={dynamic} element={<Dynamic />} />
              </Routes>
            );
            """);
        Write("node_modules/pkg/routes.ts", "const routes = [{ path: '/ignored-package' }];");
        Write("dist/routes.js", "const routes = [{ path: '/ignored-build' }];");

        var routes = WebPageSourceScanner.Discover(_folder, framework);

        Assert.That(routes, Is.EqualTo(new[]
        {
            "/dashboard", "/legacy/{...}", "/login", "/orders/{orderId}", "/settings", "/users/{id}"
        }), "relative children and build folders are out of scope");
    }

    [Test]
    public void Auto_ShouldDetectNextFromPackageJson()
    {
        Write("package.json", """{ "dependencies": { "next": "14.0.0", "react": "18.0.0" } }""");
        Write("pages/index.tsx");
        Write("pages/orders.tsx");
        Write("src/ignored.ts", "const routes = [{ path: '/literal-only' }];");

        var routes = WebPageSourceScanner.Discover(_folder, "auto");

        Assert.That(routes, Is.EqualTo(new[] { "/", "/orders" }));
    }

    [Test]
    public void Auto_ShouldDetectVueFromPackageJson()
    {
        Write("package.json", """{ "dependencies": { "vue": "^3.4.0" } }""");
        Write("pages/index.tsx");
        Write("src/router.ts", "export const routes = [{ path: '/login' }];");

        var routes = WebPageSourceScanner.Discover(_folder, "auto");

        Assert.That(routes, Is.EqualTo(new[] { "/login" }), "package.json wins over the pages layout");
    }

    [Test]
    public void Auto_ShouldDetectReactFromPackageJson()
    {
        Write("package.json", """{ "devDependencies": { "react": "^18.0.0" } }""");
        Write("src/App.tsx", """<Route path="/dashboard" />""");

        var routes = WebPageSourceScanner.Discover(_folder, "auto");

        Assert.That(routes, Is.EqualTo(new[] { "/dashboard" }));
    }

    [Test]
    public void Auto_ShouldDetectRemixFromLayoutWithoutPackageJson()
    {
        Write("app/routes/users.$id.tsx");

        var routes = WebPageSourceScanner.Discover(_folder, "auto");

        Assert.That(routes, Is.EqualTo(new[] { "/users/{id}" }));
    }

    [Test]
    public void Auto_ShouldPreferNextOverRemixWhenTheAppRouterOwnsAnAppRoutesFolder()
    {
        // A Next app-router project can have an app/routes folder; page.* files anywhere under app/
        // identify it as Next, not Remix.
        Write("app/routes/dashboard/page.tsx");

        Assert.Multiple(() =>
        {
            Assert.That(WebPageSourceScanner.DetectFromLayout(_folder),
                Is.EqualTo(WebPageSourceScanner.Framework.Next));
            Assert.That(WebPageSourceScanner.Discover(_folder, "auto"),
                Is.EqualTo(new[] { "/routes/dashboard" }));
        });
    }

    [Test]
    public void Discover_ShouldReturnEmptyForMissingOrEmptySource()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WebPageSourceScanner.Discover(null, "next"), Is.Empty);
            Assert.That(WebPageSourceScanner.Discover("  ", "next"), Is.Empty);
            Assert.That(WebPageSourceScanner.Discover(Path.Combine(_folder, "missing"), "next"), Is.Empty);
        });
    }

    [Test]
    public void Discover_ShouldResolveRelativeSourceAgainstBaseDirectory()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "prototest-web-relative-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "pages"));
        File.WriteAllText(Path.Combine(folder, "pages", "index.tsx"), string.Empty);
        try
        {
            var relative = Path.GetRelativePath(AppContext.BaseDirectory, folder);
            var routes = WebPageSourceScanner.Discover(relative, "next");

            Assert.Multiple(() =>
            {
                Assert.That(routes, Is.EqualTo(new[] { "/" }));
                Assert.That(
                    WebPageSourceScanner.ResolveFolder(relative),
                    Is.EqualTo(Path.GetFullPath(folder)),
                    "the resolved folder is the canonical source folder");
            });
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Test]
    public void Discover_ShouldRejectARelativeSourceThatEscapesTheBaseDirectory()
    {
        var escaping = Path.Combine("..", "prototest-web-outside-" + Guid.NewGuid().ToString("N"));

        Assert.Multiple(() =>
        {
            Assert.That(WebPageSourceScanner.ResolveFolder(escaping), Is.Null,
                "a relative folder may not escape the test assembly's base directory");
            Assert.That(WebPageSourceScanner.Discover(escaping, "next"), Is.Empty);
        });
    }

    [Test]
    public void Auto_ShouldNotReadAPackageJsonAboveTheBoundedWalk()
    {
        // The only package.json sits four folders above the source, beyond the boundary. If the walk
        // passed it, the vue dependency would suppress the file-based pages inventory.
        Write("package.json", """{ "dependencies": { "vue": "^3.4.0" } }""");
        Write("a/b/c/d/pages/about.tsx");

        var routes = WebPageSourceScanner.Discover(Path.Combine(_folder, "a", "b", "c", "d"), "auto");

        Assert.That(routes, Is.EqualTo(new[] { "/about" }),
            "a package.json above the bounded walk does not decide the framework");
    }

    [Test]
    public void Auto_ShouldStopAtTheNearestPackageJson()
    {
        // The nearest package.json is authoritative even when it says nothing: the walk must not skip
        // past it to an outer vue dependency, which would suppress the file-based pages inventory.
        Write("package.json", """{ "dependencies": { "vue": "^3.4.0" } }""");
        Write("app/package.json", """{ "name": "inner" }""");
        Write("app/pages/index.tsx");

        var routes = WebPageSourceScanner.Discover(Path.Combine(_folder, "app"), "auto");

        Assert.That(routes, Is.EqualTo(new[] { "/" }),
            "the inner package.json is the project boundary, so the pages layout decides");
    }

    [Test]
    public void Discover_ShouldTreatUnknownFrameworkAsAuto()
    {
        Write("pages/about.tsx");

        var routes = WebPageSourceScanner.Discover(_folder, "svelte");

        Assert.That(routes, Is.EqualTo(new[] { "/about" }), "the pages layout is detected from the folder shape");
    }

    [Test]
    public void Detect_ShouldNotTreatVuexAsVue()
    {
        Write("vuex-project/pages/index.vuex");
        Write("vue-project/pages/index.vue");

        Assert.Multiple(() =>
        {
            Assert.That(WebPageSourceScanner.DetectFromLayout(Path.Combine(_folder, "vuex-project")),
                Is.EqualTo(WebPageSourceScanner.Framework.Next),
                "a Windows wildcard search for *.vue must not match .vuex");
            Assert.That(WebPageSourceScanner.DetectFromLayout(Path.Combine(_folder, "vue-project")),
                Is.EqualTo(WebPageSourceScanner.Framework.Nuxt));
        });
    }

    [Test]
    public void Discover_ShouldNotRecurseThroughReparsePointsOrLoops()
    {
        Write("pages/index.tsx");
        Write("pages/about.tsx");
        var external = Path.Combine(Path.GetTempPath(), "prototest-web-external-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(external, "pages"));
        File.WriteAllText(Path.Combine(external, "pages", "linked.tsx"), string.Empty);
        try
        {
            if (!TryCreateDirectoryLink(Path.Combine(_folder, "pages", "loop"), _folder)
                || !TryCreateDirectoryLink(Path.Combine(_folder, "pages", "external"), external))
            {
                Assert.Ignore(
                    "This environment does not allow creating directory links, so the reparse-point guard " +
                    "cannot be exercised here.");
            }

            var routes = WebPageSourceScanner.Discover(_folder, "next");

            Assert.That(routes, Is.EqualTo(new[] { "/", "/about" }),
                "a self-referencing link cannot loop and an external link is not followed");
        }
        finally
        {
            TryDeleteDirectoryLink(Path.Combine(_folder, "pages", "loop"));
            TryDeleteDirectoryLink(Path.Combine(_folder, "pages", "external"));
            try
            {
                Directory.Delete(external, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static void TryDeleteDirectoryLink(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or PlatformNotSupportedException)
        {
        }

        if (!OperatingSystem.IsWindows()) return false;
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "cmd.exe",
            $"/c mklink /J \"{linkPath}\" \"{targetPath}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        });
        if (process is null) return false;
        process.WaitForExit();
        return process.ExitCode == 0 && Directory.Exists(linkPath);
    }

    private void Write(string relativePath, string? content = null)
    {
        var path = Path.Combine(_folder, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content ?? string.Empty);
    }
}
