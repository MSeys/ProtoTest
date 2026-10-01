---
id: sign-in-as-a-test-user
title: Sign in as a test user
sidebar_label: Sign in as a test user
sidebar_position: 4
description: "Declare the user a test acts as with [SignedInAs], read the identity in the trace, and learn what the built-in test user cannot do against a published application."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Sign in as a test user

<Lesson
  track="Write good integration tests"
  step="Lesson 4 of 7"
  minutes={10}
  outcomes={[
    'Declare the identity a test acts as with [SignedInAs]',
    'Read the identity the trace keeps, and what it leaves out',
    'Tell the in-process case from the published one',
  ]}
  needs={[
    <>Lesson 3, <Link to="/learn/good-tests/add-and-remove-an-integration">Add and remove an integration</Link></>,
    'A sample checkout to run the optional probe. The excerpts and archive are enough to read along',
  ]}
/>

## The problem

Most applications decide what a caller may do by who the caller is. A test that calls the API has to act as someone: an owner who can create projects, or a viewer who is refused.

You already used `[SignedInAs]` in your first test. This lesson follows that identity into an authenticated request and reads the identity metadata in the trace.

## Do it

### 1. Declare who the test acts as

`ProjectsJourney` has a test that acts as a viewer:

<AnnotatedCode
  filename="ProjectsJourney.cs"
  code={`[Application(NorthstarTargets.Api)]
[NorthstarMember(PlanIds.Growth)]
public sealed class ProjectsJourney
{
    [ProtoTest]
    [SignedInAs("viewer", MemberRoles.Viewer)]
    public async Task AViewerCannotCreateProjects()
    {
        using var response = await Proto.Context.Rest()
            .Body(new CreateProjectRequest("viewer-atlas"))
            .PostAsync("/api/v1/projects");

        response.Should.HaveHttpStatus(HttpStatusCode.Forbidden);
    }
}`}
  callouts={[
    {line: 5, title: 'One context per test', note: '[ProtoTest] wraps this test in its own context. The identity belongs to that context alone.'},
    {line: 6, title: 'Name, then roles', note: 'With no arguments the built-in name test-user is used, with no roles. Here the viewer role is what the application refuses.'},
    {line: 13, title: 'The application decides', note: 'The 403 comes from the application, which resolved the viewer and applied its own authorization.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/ProjectsJourney.cs</code>. Claims use the same attribute: <code>Claims = new[] &#123; "tenant=northstar" &#125;</code>.</>}
/>

The declaration contains a name, roles and claims. It does not obtain credentials from the application. The context stores one current identity, which a later `SignIn` call replaces.

### 2. See what the attribute does

Before the test body runs, the attribute publishes the identity to the context:

<AnnotatedCode
  filename="SignedInAsAttribute.cs"
  code={`public override Task BeforeTestAsync(ProtoExecutionContext context)
{
    context.SignIn(new ProtoTestUser(
        Name,
        Roles,
        [.. Claims.Select(ParseClaim)]));
    return Task.CompletedTask;
}`}
  callouts={[
    {line: 3, title: 'One call publishes the identity', note: 'SignIn sets the user the context resolves and records an auth:user entity. The next test starts with none.'},
    {line: 6, title: 'The auth entity omits claim values', note: 'The identity includes claim values, but auth:user records only their types. This does not control custom attachments or embedded source.'},
  ]}
  foot={<>From <code>src/ProtoTest.Http/Authentication/SignedInAsAttribute.cs</code>. REST, GraphQL and gRPC requests carry the identity through the same auth lifecycle.</>}
/>

`[NorthstarMember]` adds tenant setup and the sample's authenticator. When a request needs credentials, that authenticator calls `NorthstarMember.EnsureAsync` to choose or provision a member:

<AnnotatedCode
  filename="NorthstarMember.cs"
  code={`var user = context.SignedInUser();
var organization = context.Resolve<NorthstarOrganizationContext>();
var role = user.Roles.Count > 0 ? user.Roles[0] : MemberRoles.Owner;
var member = role == MemberRoles.Owner
    ? new NorthstarMemberContext("owner", organization.OwnerEmail, role, organization.OwnerToken)
    : await InviteAsync(context, role);`}
  callouts={[
    {line: 3, title: 'The first role picks the member', note: 'No role, or owner as the first role, selects the tenant owner. Another first role invites a member with that role.'},
    {line: 4, title: 'The member is the credential', note: 'The sample sends the member token. Its application resolves that, not the shipped test-user header.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/NorthstarMember.cs</code>. The invited member's email includes the role and test id. The member is cached in that test's context.</>}
/>

### 3. Read the identity in the trace

Open [l1-first-journey.prototrace](pathname:///lessons/l1-first-journey.prototrace) in the [viewer](https://trace.prototest.dev). The recorded test contains these setup and request operations, plus its identity entity:

| Entry | Reading |
| --- | --- |
| `Before · NorthstarTenantAttribute`, 139.9 ms | the tenant is provisioned first |
| `Before · SignedInAsAttribute`, 1.7 ms | the declaration runs next, and its event reads `Signed in as test-user` |
| `Apply · TestUserAuthenticator`, 1.2 ms | the shipped transport applies, because the server runs in-process |
| `Apply · NorthstarAuthenticator`, 1.0 ms | the sample's own authenticator rides along on the same request |
| auth entity `auth:user`: name `test-user`, roles empty, claim types empty, transport `in-process` | the identity the trace keeps |

The auth entity records `auth.claim_types`, without their values. The default gRPC metadata capture also redacts `prototest-user`.
These rules do not remove values you write into custom attachments, response bodies or embedded source files.

### 4. Try the limit: a published application

The shipped transport needs an application the run hosts in-process. To try the limit, create `PublishedAliceProbe.cs` in `samples/Northstar.ProtoTest/` with this content.
Leave `[NorthstarMember]` off: its tenant setup would need a live application before the identity declaration runs.

<AnnotatedCode
  filename="PublishedAliceProbe.cs"
  code={`namespace Northstar.ProtoTest;

using System.Net;
using global::ProtoTest.Core;
using global::ProtoTest.Http;
using global::ProtoTest.NUnit;
using global::ProtoTest.Rest;
using global::ProtoTest.SampleApp.Contracts;

[Application(NorthstarTargets.Api)]
public sealed class PublishedAliceProbe
{
    [ProtoTest]
    [SignedInAs("alice", "admin", Claims = ["tenant=northstar"])]
    public async Task TheIdentityIsStillRecorded()
    {
        using var response = await Proto.Context.Rest()
            .Body(new CreateProjectRequest("alice-atlas"))
            .PostAsync("/api/v1/projects");

        response.Should.HaveHttpStatus(HttpStatusCode.Created);
    }
}`}
  callouts={[
    {line: 14, title: 'The declaration', note: 'The identity is recorded before the request attempts to reach the application.'},
  ]}
/>

From the repository root, run it against an unused local port. The example uses 5099. Choose another port if something already listens there.
The script restores any previous target setting after the run:

```powershell
$previousTargetUrl = $env:ProtoTest__TargetUrl
try {
    $env:ProtoTest__TargetUrl = "http://127.0.0.1:5099"
    dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~PublishedAliceProbe"
}
finally {
    if ($null -eq $previousTargetUrl) {
        Remove-Item Env:ProtoTest__TargetUrl -ErrorAction SilentlyContinue
    }
    else {
        $env:ProtoTest__TargetUrl = $previousTargetUrl
    }
}
```

With no listener on that port, the request fails. The test's trace still records the identity:

- `auth.user: alice`, `auth.roles: admin`, `auth.claim_types: tenant`.
- `auth.transport: inert`, with the reason: the application is not hosted in-process, so register it with `AddAspNetCoreServer` and add the app-side authentication with `webHost.AddTestUserAuthentication()`.
- An `auth.user.inert` event with outcome `skipped`.

The auth entity omits the claim value `northstar`. The trace may still contain it in the embedded test source.
Delete `PublishedAliceProbe.cs` after inspecting the result so this deliberate failure does not remain in your suite.

## What happened

`[SignedInAs]` declared who the test acts as and nothing more. Something else has to turn that into a login the application accepts.

- In-process, the shipped transport sends a `ProtoTest-User` header. An application that should treat it as its own principal opts in:

  ```csharp
  builder.AddApplication("Api", app => app
      .AddAspNetCoreServer<Program>(webHost => webHost.AddTestUserAuthentication())
      .AddRest(rest => rest.AddClient("Api")));
  ```

  The handler decodes the header into a `ClaimsPrincipal`, so the application's own `[Authorize]` and role checks decide as in production. The sample leaves it out on purpose, because the handler replaces the default authentication scheme and the sample's subject is its own authentication.
- Published, the shipped transport is inert and says why. A custom `[Auth<T>]` authenticator can map `context.SignedInUser()` to credentials the application accepts, as the sample does.

## Check yourself

<Checkpoint
  question='A test declares [SignedInAs("alice", "admin", Claims = ["tenant=northstar"])]. What does auth:user record, and where could the claim value still appear?'
  verify={<>Run the published case above and read its <code>auth:user</code> entity. The saved <a href="pathname:///lessons/l1-first-journey.prototrace">first journey</a> shows the same fields for <code>test-user</code>, without roles or claims.</>}>

The entity records `auth.user: alice`, `auth.roles: admin` and `auth.claim_types: tenant`. It does not record the claim value `northstar`.
Embedded source or custom captured content can still contain that value. The auth entity's omission is not a trace-wide redaction guarantee.

</Checkpoint>

## Remember

- A bare `[SignedInAs]` is the built-in `test-user`. A name, roles and claims describe the identity further.
- The identity is per-test state. Its auth entity records name, roles and claim types, without claim values.
- The shipped transport needs an in-process application. Against a published one it is inert and records why.

## Go deeper

- [Authentication](/docs/integrations/rest/authentication): authenticators, precedence and the built-in test user.
- Next: [Give each test its own state](/learn/good-tests/per-test-state-and-cleanup).
