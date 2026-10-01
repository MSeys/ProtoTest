---
id: capabilities-and-the-host
title: Read what your run provides
sidebar_label: Capabilities and the host
sidebar_position: 1
description: "Read a run's capability list in the trace, find the line in Setup.cs that declared each one, and tell what the host owns from what a test owns."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Read what your run provides

<Lesson
  track="Write good integration tests"
  step="Lesson 1 of 7"
  minutes={8}
  outcomes={[
    'Say what a capability is and which line of setup declares it',
    'Read a run\'s capability list from its trace',
    'Tell what the host owns from what a test owns',
  ]}
  needs={[
    <>The Start track, especially <Link to="/learn/start/read-the-trace">read the trace</Link></>,
    'An optional sample checkout for inspecting Setup.cs. The archive and excerpt below are enough to follow along',
  ]}
/>

## The problem

A test asks for a REST client, a database connection or a browser, and it works. Somebody decided those would exist before the test started. When one is missing, you need to know where that decision lives.

Start with the setup class, which configures what the suite provides. This lesson matches its registrations to the capabilities listed in a recorded run.

## Do it

### 1. Open the run in the viewer

Download [l1-first-journey.prototrace](pathname:///lessons/l1-first-journey.prototrace) and open it in the [viewer](https://trace.prototest.dev). Read the run screen. It lists the capabilities this run declared.

A capability describes support registered for the run, such as making REST calls or driving a browser. The recorded list includes each capability's kind and declaring package:

| Capability | Kind | Declared by |
| --- | --- | --- |
| REST | protocol | ProtoTest.Rest |
| GraphQL | protocol | ProtoTest.GraphQL |
| ASP.NET Core | server | ProtoTest.AspNetCore |
| SQL | store | ProtoTest.Sql |
| Data | data | ProtoTest.Data |
| Sheets | document | ProtoTest.Sheets |
| Playwright | browser | ProtoTest.Web.Playwright |

### 2. Find where each one comes from

If you have the checkout, open `samples/Northstar.ProtoTest/Setup.cs`. Otherwise, read the excerpt below. Its `ConfigureApplications` method registers the API and browser integrations.

An integration is a ProtoTest package for one kind of system. Its registration can declare a capability and configure clients or services that tests will use.

<AnnotatedCode
  filename="Setup.cs"
  code={`private static void ConfigureApplications(IProtoHostBuilder builder, NorthstarRun run)
{
    builder.AddApplication(NorthstarTargets.Api, app =>
    {
        if (run.RunsLocalApplications)
        {
            // The in-process server is what carries the test clock into the application.
            app.AddAspNetCoreServer<NorthstarProgram>(configureWebHost: webHost =>
                ConfigureHostedApplication(webHost, run));
        }

        app.AddRest(rest => rest
                .CaptureAttachments()
                .AddClient(NorthstarTargets.Api)
                .AddCollector<RestCoverageCollector>()
                .AddCollector<RestTrafficCoverageCollector>())
            .AddGraphQL(graphQL => graphQL
                .CaptureAttachments()
                .AddClient("GraphQL", endpoint: "GraphQL"));
    });

    if (run.RunsLocalApplications)
    {
        // The browser needs a real listener; the page journey follows this instance's address.
        builder.AddLoopbackApplication(NorthstarTargets.Web, NorthstarProgram.CreateApp);
        builder.AddHttpReadiness(NorthstarTargets.Web, "/health");
    }

    builder.AddApplication(NorthstarTargets.Web, app => app
        .AddRest(rest => rest
            .CaptureAttachments()
            .AddClient(NorthstarTargets.Web))
        .AddWeb(options => options.Headless = true));
}`}
  callouts={[
    {line: 3, title: 'Name the application', note: 'NorthstarTargets.Api identifies the application a test selects with [Application]. Its value is Northstar, the name used in the trace.'},
    {line: 8, title: 'ASP.NET Core capability', note: 'AddAspNetCoreServer registers an in-process application server when this run hosts the application locally.'},
    {line: 12, title: 'REST and GraphQL', note: 'AddRest and AddGraphQL declare protocol capabilities. Their AddClient calls register the clients for this application.'},
    {line: 33, title: 'The browser', note: 'AddWeb adds the Playwright capability.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/Setup.cs</code>. The store and the broker follow the same shape. Lesson 3 covers the broker.</>}
/>

Match the table to the code. `AddRest` and `AddGraphQL` declare REST and GraphQL. `AddAspNetCoreServer` declares ASP.NET Core. In this sample, `AddWeb` selects Playwright.

The remaining rows come from three more calls in `samples/Northstar.ProtoTest/Setup.cs`: `AddSql` inside the `ConfigureDomain` method, `AddSheets` inside `Configure`, and `AddNorthstarData`, which calls `AddData` in `NorthstarTestHost.cs`.

## What happened

The setup code describes what to register. The trace shows which capabilities this recorded run declared. Configuration can change that list, so compare it with the run you are investigating.

Use the list to identify registered support, then check what the test still needs:

- Integration registration supplies capability metadata. A capability does not prove that a remote service is healthy or that a request will succeed.
- Some declarations depend on configuration. For example, the sample leaves out its broker capability when no broker is configured.

Playwright illustrates the distinction. `AddWeb` declares browser support. `[RequiresPlaywrightBrowser]` separately checks availability, or allows the test when automatic browser installation is enabled.

The **host** is the suite's shared object for registrations, services and resources that belong to the run. It manages the resources the framework owns, such as a container the run starts.
An existing database or broker supplied through configuration remains externally owned. Individual integrations can also create resources for each test.

Each test has a **test context** for its clients, state, checks and attachments. External data needs an explicit cleanup strategy.
For example, Northstar's tenant provisioners register cleanup that deletes the test's tenant. Creating a context alone does not delete arbitrary application data.
The next lesson follows the host's lifetime.

## Check yourself

<Checkpoint
  question="The first journey's trace lists capabilities. Name three and the call in Setup.cs that each comes from."
  verify={<>Open the run screen in <a href="pathname:///lessons/l1-first-journey.prototrace">l1-first-journey.prototrace</a>, then find each registration in <code>samples/Northstar.ProtoTest/Setup.cs</code>.</>}>

REST and GraphQL come from `AddRest` and `AddGraphQL`. ASP.NET Core comes from `AddAspNetCoreServer`. SQL comes from `AddSql`, Sheets from `AddSheets` and Playwright from this sample's `AddWeb` registration.

Data comes from `AddNorthstarData` in `Setup.cs`, through its call to `AddData` in `NorthstarTestHost.cs`. Each capability names its declaring package.

</Checkpoint>

## Remember

- A capability describes registered support, not a successful connection or request.
- The trace records the capabilities declared for that run's configuration.
- The host manages shared resources it owns. Each test has its own context, and external data needs explicit cleanup.

## Go deeper

- [The foundation](/docs/foundation/overview): the host, the test context, attributes, clients and hooks.
- Next: [One host, one lifetime](/learn/good-tests/one-host-one-lifetime).
