---
id: capabilities-and-the-host
title: Capabilities and the host
sidebar_label: Capabilities and the host
sidebar_position: 1
description: "What a capability is, who declares it, and how to read a run's composition from its trace."
---

import LearnShell from '@site/src/components/LearnShell';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Capabilities and the host

Level 0 was about what a test answers. This level is about the other side: what the run provides, and where that decision lives.

<LearnShell
  level="Level 2, lesson 1"
  minutes="About 8 minutes"
  outcome={[
    'Say what a capability is and which piece serves it.',
    'Read a run\'s composition from its trace.',
    'Tell what belongs to the host from what belongs to a test.',
  ]}
  before={[
    <>Level 1 (<Link to="/learn/one-test-one-journey/read-the-trace">read the trace</Link>).</>,
    'The sample cloned. Reading the archives alone also works.',
  ]}
  situation={
    <>
      <p>The host is built once for the run. Every journey then asks the context for what it needs: a REST client, a database connection, a signed-in member. That is why a failure in setup is one line in the trace instead of a hunt through test code.</p>
      <p>Time and state had their answers in the test. Environment and visibility had theirs in the composition. This lesson reads the composition.</p>
    </>
  }
  checkpoint={{
    question:
      'The first journey\'s trace lists the capabilities the run declared. Name three of them and where each one comes from in Setup.cs.',
    verify: (
      <>
        Download <a href="pathname:///lessons/l1-first-journey.prototrace">l1-first-journey.prototrace</a>, open it in the{' '}
        <a href="https://trace.prototest.dev">viewer</a>, and read the run screen. Then find each registration in <code>samples/Northstar.ProtoTest/Setup.cs</code>.
      </>
    ),
    reveal: (
      <>
        REST and GraphQL come from <code>AddRest</code> and <code>AddGraphQL</code>; ASP.NET Core from <code>AddAspNetCoreServer</code>; SQL from <code>AddSql</code>; Data from <code>AddData</code>; Sheets from <code>AddSheets</code>; Playwright from the <code>AddWeb</code> registration. Each capability in the list names the package that serves it, so the run screen is also a map of the composition.
      </>
    ),
  }}
  learned={[
    'A capability is declared only by something that can serve it.',
    'The composition is written once, in one Configure method, and the run records what it declared.',
    'The host owns what the run shares; a test owns its own data and assertions.',
  ]}
  next={[
    {
      label: 'Add and remove an integration',
      to: '/learn/compose-dont-glue/add-and-remove-an-integration',
      note: 'Watch a capability go missing, add it back, and take it away again.',
    },
    {
      label: 'The foundation',
      to: '/docs/foundation/overview',
      note: 'The host, the execution context, attributes, clients and hooks.',
    },
  ]}>

## Capabilities

A capability is a named thing the run can do, such as calling an API, running the application in-process, or serving a broker. Each one has a kind and the package that declares it. The run records all of them as items, and the trace's run layer prints them.

From the first journey's archive:

| Capability | Kind | Declared by |
| --- | --- | --- |
| REST | protocol | ProtoTest.Rest |
| GraphQL | protocol | ProtoTest.GraphQL |
| ASP.NET Core | server | ProtoTest.AspNetCore |
| SQL | store | ProtoTest.Sql |
| Data | data | ProtoTest.Data |
| Sheets | document | ProtoTest.Sheets |
| Playwright | browser | ProtoTest.Web.Playwright |

Two rules keep the list honest. A capability is declared by the piece that can serve it, never by a test that hopes it exists. And a capability the run cannot serve is absent, with a reason the composition can register.

## One Configure method

The sample composes everything in `samples/Northstar.ProtoTest/Setup.cs`. This is `ConfigureApplications`:

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
    {line: 1, title: 'One place composes the run', note: 'Setup runs once before any test and builds the host every journey shares.'},
    {line: 3, title: 'Name the application', note: 'Api is the target tests select with [Application]; the same name is used by the clients and by the trace.'},
    {line: 8, title: 'Host the application in-process', note: 'The in-process server carries the test clock into the application, which is what makes the time answer work.'},
    {line: 12, title: 'REST and GraphQL from one registration', note: 'Each Add... adds a client and the capability that serves it.'},
    {line: 25, title: 'A real listener for the browser', note: 'Playwright needs an address, so the run starts a loopback instance and waits for /health before any test uses it.'},
    {line: 29, title: 'Compose the browser client', note: 'AddWeb adds the Playwright capability; the web application gets its own REST client for the page journey.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/Setup.cs</code>. The store and the broker follow the same shape through <code>AddInfrastructure</code>, which the next lesson covers.</>}
/>

## What the host owns, and what a test owns

The split is about lifetime:

- The **host** owns what the whole run shares: applications, infrastructure such as a database or a broker, report sinks, and run gates. It starts them once and releases them when the run ends.
- A **test** owns its execution context: the data it creates, the state it sets, the checks it makes and the attachments it publishes. All of it is recorded and released per test.

The sample shows both. The store, the loopback instance and the broker are run pieces. The tenant, the project and the sign-in are test pieces, created by attributes and removed at teardown. The next lesson watches one of those run pieces come and go.

</LearnShell>
