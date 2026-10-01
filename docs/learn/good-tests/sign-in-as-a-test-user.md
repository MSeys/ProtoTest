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
    'The sample cloned. Reading the archive alone also works',
  ]}
/>

## The problem

Most applications decide what a caller may do by who the caller is. A test that calls the API has to act as someone: an owner who can create projects, or a viewer who is refused.

You already used `[SignedInAs]` in your first test. This lesson shows what it declares, who turns it into a real login, and what the trace keeps.

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

The declaration is plain attribute data: a name, roles and claims. It holds no secret. A test has one identity, and declaring it twice replaces the first.

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
    {line: 6, title: 'Claim values stay out of the trace', note: 'The values travel in the request header. The trace records their types only.'},
  ]}
  foot={<>From <code>src/ProtoTest.Http/Authentication/SignedInAsAttribute.cs</code>. REST, GraphQL and gRPC requests carry the identity through the same auth lifecycle.</>}
/>

The sample then turns the identity into a member. `[NorthstarMember]` reads the role and picks who acts:

<AnnotatedCode
  filename="NorthstarMember.cs"
  code={`var user = context.SignedInUser();
var organization = context.Resolve<NorthstarOrganizationContext>();
var role = user.Roles.Count > 0 ? user.Roles[0] : MemberRoles.Owner;
var member = role == MemberRoles.Owner
    ? new NorthstarMemberContext("owner", organization.OwnerEmail, role, organization.OwnerToken)
    : await InviteAsync(context, role);`}
  callouts={[
    {line: 3, title: 'The role picks the member', note: 'No declared role means the tenant owner. Any other first role invites a member with exactly that role.'},
    {line: 4, title: 'The member is the credential', note: 'The sample sends the member token. Its application resolves that, not the shipped test-user header.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/NorthstarMember.cs</code>. The invited member's email carries the role and the test id, so parallel tests never share one.</>}
/>

### 3. Read the identity in the trace

Open [l1-first-journey.prototrace](pathname:///lessons/l1-first-journey.prototrace) in the [viewer](https://trace.prototest.dev). In setup you will find:

| Entry | Reading |
| --- | --- |
| `Before · NorthstarTenantAttribute`, 139.9 ms | the tenant is provisioned first |
| `Before · SignedInAsAttribute`, 1.7 ms | the declaration runs next, and its event reads `Signed in as test-user` |
| `Apply · TestUserAuthenticator`, 1.2 ms | the shipped transport applies, because the server runs in-process |
| `Apply · NorthstarAuthenticator`, 1.0 ms | the sample's own authenticator rides along on the same request |
| auth entity `auth:user`: name `test-user`, roles empty, claim types empty, transport `in-process` | the identity the trace keeps |

Claim values never appear. The entity records `auth.claim_types`, a list of types. A gRPC call's `prototest-user` metadata is redacted as well.

### 4. Try the limit: a published application

The shipped transport needs an application the run hosts in-process. Put this test in `samples/Northstar.ProtoTest/`. Leave `[NorthstarMember]` off, because it needs a live address before the declaration runs:

<AnnotatedCode
  filename="PublishedAliceProbe.cs"
  code={`[Application(NorthstarTargets.Api)]
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
    {line: 5, title: 'The declaration', note: 'This is what the lesson is about. The request behind it is ordinary.'},
  ]}
/>

Run it against an address that hosts nothing:

```powershell
$env:ProtoTest__TargetUrl = "http://127.0.0.1:5099"
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~PublishedAliceProbe"
Remove-Item Env:ProtoTest__TargetUrl
```

The request fails because nothing answers. The identity entity is still written:

- `auth.user: alice`, `auth.roles: admin`, `auth.claim_types: tenant`.
- `auth.transport: inert`, with the reason: the application is not hosted in-process, so register it with `AddAspNetCoreServer` and add the app-side authentication with `webHost.AddTestUserAuthentication()`.
- An `auth.user.inert` event with outcome `skipped`.

The literal `tenant=northstar` appears nowhere in that trace.

## What happened

`[SignedInAs]` declared who the test acts as and nothing more. Something else has to turn that into a login the application accepts.

- In-process, the shipped transport sends a `ProtoTest-User` header. An application that should treat it as its own principal opts in:

  ```csharp
  builder.AddApplication("Api", app => app
      .AddAspNetCoreServer<Program>(webHost => webHost.AddTestUserAuthentication())
      .AddRest(rest => rest.AddClient("Api")));
  ```

  The handler decodes the header into a `ClaimsPrincipal`, so the application's own `[Authorize]` and role checks decide as in production. The sample leaves it out on purpose, because the handler replaces the default authentication scheme and the sample's subject is its own authentication.
- Published, the shipped transport is inert and says why. The suite needs its own `[Auth<T>]` authenticator that reads `context.SignedInUser()`.

## Check yourself

<Checkpoint
  question='A test declares [SignedInAs("alice", "admin", Claims = ["tenant=northstar"])]. Which parts reach the trace, and which value does not?'
  verify={<>Read the <code>auth:user</code> entity in <a href="pathname:///lessons/l1-first-journey.prototrace">l1-first-journey.prototrace</a>, or run the published case above. The reason is in <code>src/ProtoTest.Http/Authentication/SignedInAsAttribute.cs</code>.</>}>

The name, the role and the claim type are recorded: `auth.user: alice`, `auth.roles: admin`, `auth.claim_types: tenant`. The claim value `northstar` is nowhere. Claim values stay in the header and never reach the trace.

</Checkpoint>

## Remember

- A bare `[SignedInAs]` is the built-in `test-user`. A name, roles and claims describe the identity further.
- The identity is per-test state. The trace shows the name, roles and claim types, never claim values.
- The shipped transport needs an in-process application. Against a published one it is inert and records why.

## Go deeper

- [Authentication](/docs/integrations/rest/authentication): authenticators, precedence and the built-in test user.
- Next: [Give each test its own state](/learn/good-tests/per-test-state-and-cleanup).
