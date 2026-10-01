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
    'The sample cloned. Reading the archive alone also works',
  ]}
/>

## The problem

A test asks for a REST client, a database connection or a browser, and it works. Somebody decided those would exist before the test started. When one is missing, you need to know where that decision lives.

It lives in one place: the setup class. This lesson finds it, and finds what it produced in the trace.

## Do it

### 1. Open the run in the viewer

Download [l1-first-journey.prototrace](pathname:///lessons/l1-first-journey.prototrace) and open it in the [viewer](https://trace.prototest.dev). Read the run screen. It lists the capabilities this run declared.

A capability is something the run lets a test do, such as calling an API or driving a browser. Each one has a kind and the package that declares it:

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

Open `samples/Northstar.ProtoTest/Setup.cs`. The method `ConfigureApplications` adds the integrations. An integration is a ProtoTest package for one kind of system, and adding it to the host is what creates its capability.

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
    {line: 3, title: 'Name the application', note: 'Api is the target a test selects with [Application]. The clients and the trace use the same name.'},
    {line: 8, title: 'ASP.NET Core capability', note: 'AddAspNetCoreServer hosts the application inside the test process.'},
    {line: 12, title: 'REST and GraphQL', note: 'Each Add call adds a client and the capability that serves it.'},
    {line: 33, title: 'The browser', note: 'AddWeb adds the Playwright capability.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/Setup.cs</code>. The store and the broker follow the same shape. Lesson 3 covers the broker.</>}
/>

Match the table to the code. `AddRest` and `AddGraphQL` give REST and GraphQL. `AddAspNetCoreServer` gives ASP.NET Core. `AddWeb` gives Playwright. SQL, Data and Sheets come from registrations elsewhere in `Setup.cs`.

## What happened

You read the same list twice: once as code, once as a record of what the run did with it. The run screen is a map of the composition.

Two rules keep that list true:

- A capability is declared by the piece that can serve it. A test cannot declare one by hoping it exists.
- A capability the run cannot serve is left out, and the composition can register the reason.

The split of ownership follows lifetime. The **host** is the one object per test process that builds what the run shares: applications, a database, a broker, report sinks. It starts them once and releases them when the run ends. A **test** owns its own context: the data it creates, the state it sets, its checks and attachments. The next lesson shows when the host is built and released.

## Check yourself

<Checkpoint
  question="The first journey's trace lists capabilities. Name three and the call in Setup.cs that each comes from."
  verify={<>Open the run screen in <a href="pathname:///lessons/l1-first-journey.prototrace">l1-first-journey.prototrace</a>, then find each registration in <code>samples/Northstar.ProtoTest/Setup.cs</code>.</>}>

REST and GraphQL come from `AddRest` and `AddGraphQL`. ASP.NET Core comes from `AddAspNetCoreServer`. SQL comes from `AddSql`, Data from `AddData`, Sheets from `AddSheets` and Playwright from `AddWeb`. Each capability names the package that serves it.

</Checkpoint>

## Remember

- A capability exists because something in setup can serve it.
- The composition is written once, and the trace records what it declared.
- The host owns what the run shares. A test owns its own data and checks.

## Go deeper

- [The foundation](/docs/foundation/overview): the host, the test context, attributes, clients and hooks.
- Next: [One host, one lifetime](/learn/good-tests/one-host-one-lifetime).
