<p align="center">
  <img width="1200" alt="ProtoTest" src="assets/brand/prototest-banner.svg" />
</p>

<p align="center">
  <a href="https://github.com/MSeys/ProtoTest/actions/workflows/ci.yml"><img src="https://github.com/MSeys/ProtoTest/actions/workflows/ci.yml/badge.svg" alt="CI" /></a>
  <a href="https://www.nuget.org/packages/ProtoTest.Core"><img src="https://img.shields.io/nuget/v/ProtoTest.Core" alt="NuGet" /></a>
  <a href="https://www.nuget.org/packages/ProtoTest.Core"><img src="https://img.shields.io/nuget/dt/ProtoTest.Core" alt="Downloads" /></a>
  <a href="https://www.nuget.org/profiles/MSeys"><img src="https://img.shields.io/badge/nuget-all%20packages-blue" alt="All NuGet packages" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/MSeys/ProtoTest" alt="License" /></a>

ProtoTest is a foundation for integration testing on .NET 8, 9 and 10. (Big words, but it's hard to explain what it truly is)

## What is ProtoTest?

Hard to describe it. You could call it a foundation, a large test framework or something else. I have a preference for 'foundation' since it describes the goal I personally see in ProtoTest.

It's something you can build upon to do integration testing.

[Documentation](https://prototest.dev/)

## What does it bring me?

The Core gives you the essence of ProtoTest. It gives you a proper lifecycle independent of the test runner of your choice. (This makes swapping test runners as easy as 1-2-3.)

It gives you one unique test context with shareable context states for sharing info between lifecycle steps (hooks, attributes, test), easy client access, integration access point, observations, tracing and much more.

[Learn more about Core](https://prototest.dev/docs/advanced/extending)

ProtoTest currently integrates with REST, GraphQL, gRPC, SQL, Entity Framework Core,
Playwright, Selenium, RabbitMQ, ASP.NET Core, Testcontainers, OpenTelemetry and more.

This definitely is a large list. I picked these because they're commonly used, but if something is missing, you can easily create an integration yourself, extend an existing one or open a discussion and I'll check it out.

Use only what your test suite needs. You're not obligated to use everything.

[Explore the integrations](https://prototest.dev/docs/integrations/overview)
[Extend ProtoTest yourself](https://prototest.dev/docs/advanced/extending)

## What if it breaks?

I admit, ProtoTest hides a lot of setup now for you, or puts it behind abstract layers. 

To solve this, as well as help discover test failures, I have taken inspiration from Playwright traces.

The provided integrations all hook into the tracing provided in core. At the end of the run, a `.prototrace` file will be provided containing the execution trace (including setup and teardown), attachments, additional reports (if configured) and more. 

That brings us to ProtoTrace. The place where you can hopefully find what went wrong with your test.

[ProtoTrace](https://trace.prototest.dev)
[Interactive trace](https://trace.prototest.dev/?demo=1)

# Add screenshot here (TODO)


## Why did I build this?

I love programming, but especially building tools and solving abstract problems.

Integration testing can get rough, especially for bigger applications such as SaaS applications. There's so much infrastructure, setup and so on.

My focus is and always has been clean and readable code. ProtoTest is my response to how messy that setup gets.

It's built from scratch, but on what I learned from a testing framework I wrote by hand years ago.

[Read more about why I built ProtoTest.](https://prototest.dev/docs/project/why-prototest)

## What does that look like?

```csharp
[ProtoTest]
[SignedInAs]
public async Task RestWritesAreVisibleThroughGraphQL()
{
    using var created = await Proto.Context.Rest()
        .Body(new CreateProjectRequest("atlas"))
        .PostAsync("/api/v1/projects");

    created.Should.HaveHttpStatus(HttpStatusCode.Created);

    using var projects = await Proto.Context.GraphQL()
        .Query("projects", new { first = 10 })
        .ExpectAsync(new
        {
            totalCount = 1,
            nodes = new[] { new { name = "atlas", status = ProjectStatuses.Active } }
        });

    projects.ShouldHaveNoErrors();
}
```

This is a simple example combining the REST and GraphQL integrations. The focus lies on a clean test with most of the infrastructure moved out of the test once it has been configured.

The test can focus on the behavior. If something goes wrong, its lifecycle, operations and checks are written to the same `.prototrace` file.

## Is it just wrappers?

In essence, you could call it that. They wrap around proven frameworks that do certain jobs really well.

If they already do their job well, why wrap them?

I'm a huge fan of AAA since discovering that principle. Tests should simply be that readable: Arrange-Act-Assert. Straight to the point.

That's why the wrappers exist. They integrate with the core and they're my opinionated view on how I want to test with them.

Common application setup can stay outside the test. Setup that matters to the scenario should still be visible.

Is it the best for everyone? Probably not. But it might just help someone do integration testing.

## Try it

```bash
dotnet new install ProtoTest.Templates
dotnet new prototest -n Shop
cd Shop
dotnet test
```

[Additional demo suite](https://github.com/MSeys/ProtoTest/tree/main/samples/ProtoTest.Demo)

## AI usage?

Yes, extensively. This is the first personal project where I have used AI this much.

I had already built a similar testing framework by hand before the AI boom. ProtoTest was built from scratch, but the vision for most of it already existed.

AI helped me work through ideas, alternatives and implementations much faster. I know what it can produce without enough direction, so I used different models, compared their suggestions, said no often and kept control over what ProtoTest became.

Do I regret using it? I don't know yet.

**ProtoTest is still what I wanted to build.**

[Read the longer explanation in the documentation.](https://prototest.dev/docs/project/ai-usage)

ProtoTest is available under the [MIT license](https://github.com/MSeys/ProtoTest/blob/main/LICENSE). Issues and contributions are welcome.
