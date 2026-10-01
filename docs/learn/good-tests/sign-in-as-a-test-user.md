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

You already used `[SignedInAs]` in your first test. This lesson follows that identity into a request and into the trace.

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
    {line: 6, title: 'Name, then roles', note: 'Without arguments it is the built-in test-user, with no roles.'},
    {line: 13, title: 'The application decides', note: 'The 403 comes from the application\'s own authorization.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/ProjectsJourney.cs</code>. Claims use the same attribute: <code>Claims = new[] &#123; "tenant=northstar" &#125;</code>.</>}
/>

The declaration only describes the identity. It does not get credentials from the application.

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
    {line: 3, title: 'One call publishes the identity', note: 'SignIn sets this test\'s user and records an auth:user entity.'},
    {line: 6, title: 'Claim values stay out of auth:user', note: 'It records only the claim types. Embedded source can still show the values.'},
  ]}
  foot={<>From <code>src/ProtoTest.Http/Authentication/SignedInAsAttribute.cs</code>. REST, GraphQL and gRPC requests carry the identity through the same auth lifecycle.</>}
/>

`[NorthstarMember]` also adds the sample's authenticator. When a request needs credentials, the authenticator turns the declared identity into a member of the test's tenant:

<AnnotatedCode
  filename="NorthstarMember.cs"
  code={`var user = context.SignedInUser();
var organization = context.Resolve<NorthstarOrganizationContext>();
var role = user.Roles.Count > 0 ? user.Roles[0] : MemberRoles.Owner;
var member = role == MemberRoles.Owner
    ? new NorthstarMemberContext("owner", organization.OwnerEmail, role, organization.OwnerToken)
    : await InviteAsync(context, role);`}
  callouts={[
    {line: 3, title: 'The first role picks the member', note: 'No role, or owner: the tenant owner. Any other role: an invited member.'},
    {line: 4, title: 'The member is the credential', note: 'The application resolves the member token.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/NorthstarMember.cs</code>.</>}
/>

### 3. Read the identity in the trace

Open [l1-first-journey.prototrace](pathname:///lessons/l1-first-journey.prototrace) in the [viewer](https://trace.prototest.dev). The recorded test contains these setup and request operations, plus its identity entity:

| Entry | Reading |
| --- | --- |
| `Before · NorthstarTenantAttribute`, 139.9 ms | the tenant comes first |
| `Before · SignedInAsAttribute`, 1.7 ms | its event reads `Signed in as test-user` |
| `Apply · TestUserAuthenticator`, 1.2 ms | the shipped transport: ProtoTest's built-in way to pass the identity, as a request header. It applies because the server runs in-process |
| `Apply · NorthstarAuthenticator`, 1.0 ms | the sample's own authenticator on the same request |
| auth entity `auth:user`: `test-user`, no roles, no claim types, transport `in-process` | the identity the trace keeps |



### 4. Try the limit: a published application

The shipped transport needs an application the run hosts in-process. To try the limit, create `PublishedAliceProbe.cs` in `samples/Northstar.ProtoTest/` with this content.
It leaves `[NorthstarMember]` off, because tenant setup needs a live application.

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

Run it against an unused local port, such as 5099. The script restores your previous target setting:

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
- `auth.transport: inert`, meaning the header is switched off, with a reason that names `AddAspNetCoreServer` and `webHost.AddTestUserAuthentication()`.
- An `auth.user.inert` event with outcome `skipped`.

Delete `PublishedAliceProbe.cs` afterwards.

## What happened

`[SignedInAs]` declared who the test acts as and nothing more. Something else has to turn that into a login the application accepts.

- In-process, the shipped transport sends a `ProtoTest-User` header. An application that should treat it as its own principal opts in:

  ```csharp
  builder.AddApplication("Api", app => app
      .AddAspNetCoreServer<Program>(webHost => webHost.AddTestUserAuthentication())
      .AddRest(rest => rest.AddClient("Api")));
  ```

  The handler turns the header into a `ClaimsPrincipal`, so the application's own `[Authorize]` checks decide. The sample leaves it out, because it tests its own authentication.
- Published, the shipped transport is inert and says why. A custom `[Auth<T>]` authenticator can map `context.SignedInUser()` to credentials the application accepts, as the sample does.

## Check yourself

<Checkpoint
  question='A test declares [SignedInAs("alice", "admin", Claims = ["tenant=northstar"])]. What does auth:user record, and where could the claim value still appear?'
  verify={<>Run the published case above and read its <code>auth:user</code> entity. The saved <a href="pathname:///lessons/l1-first-journey.prototrace">first journey</a> shows the same fields for <code>test-user</code>, without roles or claims.</>}>

The entity records `auth.user: alice`, `auth.roles: admin` and `auth.claim_types: tenant`. It does not record the claim value `northstar`.
Embedded source or captured content can still contain it.

</Checkpoint>

## Remember

- `[SignedInAs]` declares a per-test identity: a name, roles and claims.
- Its auth entity records the name, roles and claim types, without claim values.
- The shipped transport needs an in-process application. Against a published one it is inert and records why.

## Go deeper

- [Authentication](/docs/integrations/rest/authentication): authenticators, precedence and the built-in test user.
- Next: [Give each test its own state](/learn/good-tests/per-test-state-and-cleanup).
