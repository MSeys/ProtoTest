---
id: sign-in-as-a-test-user
title: Sign in as a test user
sidebar_label: Sign in as a test user
sidebar_position: 4
description: "Declare the user a test acts as, read the identity in the trace, and learn what the built-in test user can and cannot do."
---

import LearnShell from '@site/src/components/LearnShell';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Sign in as a test user

The first test you wrote carried `[SignedInAs]`, and every journey in the sample carries it. This lesson reads what it declares, who carries it to the application, and what the trace keeps about it.

<LearnShell
  level="Level 2, lesson 4"
  minutes="About 8 minutes"
  outcome={[
    'Declare the identity a test acts as with [SignedInAs].',
    'Read the auth entity and the member role the sample derives from it.',
    'Tell the in-process case from the published one, and what claim values never reach the trace.',
  ]}
  before={[
    <>Add and remove an integration (<Link to="/learn/compose-dont-glue/add-and-remove-an-integration">lesson 3</Link>).</>,
    'The sample cloned. Reading the archive alone also works.',
  ]}
  situation={
    <>
      <p>Every Northstar journey names the user it acts as. <code>[SignedInAs]</code> with no arguments is the tenant's owner; a name and a role make the test a viewer who is refused. The declaration is the test's identity, not a credential.</p>
      <p>The sample's application has its own member store, so it uses the identity to decide which member the journey acts as. An application without its own authentication can opt in to the shipped test user instead. This lesson reads both sides and the trace they leave.</p>
    </>
  }
  checkpoint={{
    question:
      'A test declares [SignedInAs("alice", "admin", Claims = ["tenant=northstar"])]. Which parts reach the trace, and which value does not?',
    verify: (
      <>
        Read the <code>auth:user</code> entity in <a href="pathname:///lessons/l1-first-journey.prototrace">l1-first-journey.prototrace</a>, or run the sample with <code>ProtoTest__TargetUrl=http://127.0.0.1:5099</code> and a test carrying the declaration above. The reason the entity records is in <code>src/ProtoTest.Http/Authentication/SignedInAsAttribute.cs</code>.
      </>
    ),
    reveal: (
      <>
        The name, the role and the claim <em>type</em> <code>tenant</code> are recorded: <code>auth.user: alice</code>, <code>auth.roles: admin</code>, <code>auth.claim_types: tenant</code>. The claim value <code>northstar</code> is nowhere; claim values stay in the header and never reach the trace.
      </>
    ),
  }}
  learned={[
    'A bare [SignedInAs] signs in as the built-in test-user; name, roles and claims describe the identity further.',
    'The identity is per-test state; the trace shows the name, roles and claim types, never claim values.',
    'The shipped transport needs an in-process application; against a published one it is inert and says why.',
  ]}
  next={[
    {
      label: 'When not to compose',
      to: '/learn/compose-dont-glue/when-not-to-compose',
      note: 'Draw the line between what the run owns and what a test owns.',
    },
    {
      label: 'Authentication',
      to: '/docs/integrations/rest/authentication',
      note: 'The full surface: authenticators, precedence and the built-in test user.',
    },
  ]}>

## The declaration

The sample's first journey shows both forms:

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
    {line: 5, title: 'Name the user', note: 'A name and the roles follow; with no arguments the built-in name test-user is used.'},
    {line: 6, title: 'The role the test needs', note: 'The rest of the class runs as the tenant owner; this one test acts as a viewer, so it can be refused.'},
    {line: 14, title: 'The application decides', note: 'The 403 comes from the application, which resolved the viewer and applied its own authorization.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/ProjectsJourney.cs</code>. Claims use the same attribute: <code>Claims = new[] &#123; "tenant=northstar" &#125;</code>.</>}
/>

Everything in the declaration is constant attribute data: names and roles, not secrets. One identity per test; declaring it twice replaces, not merges.

## What the declaration becomes

The attribute's whole before step publishes the identity:

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
    {line: 3, title: 'One call, per test', note: 'SignIn sets the identity the context resolves, and the next test starts with none.'},
    {line: 3, title: 'The trace follows the call', note: 'SignIn records the auth:user entity and an auth.user.sign-in event, and it writes whether the shipped transport can serve the selected application.'},
  ]}
  foot={<>From <code>src/ProtoTest.Http/Authentication/SignedInAsAttribute.cs</code>. REST, GraphQL and gRPC requests carry the identity through the same auth lifecycle.</>}
/>

The sample turns that identity into a member. `[NorthstarMember]` composes a tenant and the sample's authenticator, and the member resolution reads the role:

<AnnotatedCode
  filename="NorthstarMember.cs"
  code={`var user = context.SignedInUser();
var organization = context.Resolve<NorthstarOrganizationContext>();
var role = user.Roles.Count > 0 ? user.Roles[0] : MemberRoles.Owner;
var member = role == MemberRoles.Owner
    ? new NorthstarMemberContext("owner", organization.OwnerEmail, role, organization.OwnerToken)
    : await InviteAsync(context, role);`}
  callouts={[
    {line: 1, title: 'Read the identity', note: 'SignedInUser() throws a message naming both ways to declare one when the test has none.'},
    {line: 3, title: 'The role picks the member', note: 'No declared role means the tenant owner; the first role invites a member with exactly that role.'},
    {line: 4, title: 'The member is the credential', note: 'The sample sends the member token as the credential; its application resolves that, not the shipped test-user header.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/NorthstarMember.cs</code>. The invited member's email carries the role and the test id, so parallel tests never share one.</>}
/>

## The app side

The sample keeps its own authentication. An application that should treat the test user as its own principal opts in to the shipped handler instead:

```csharp
builder.AddApplication("Api", app => app
    .AddAspNetCoreServer<Program>(webHost => webHost.AddTestUserAuthentication())
    .AddRest(rest => rest.AddClient("Api")));
```

The handler decodes the `ProtoTest-User` header into the application's `ClaimsPrincipal`, name, roles and custom claims included, and becomes its default authentication scheme. The application's own `[Authorize]` and role checks then decide exactly as in production. The sample leaves it out on purpose: its subject is the application's own authentication, and the handler replaces the default scheme.

## What the trace records

The first journey's archive is the in-process case:

| Entry | Reading |
| --- | --- |
| `Before · NorthstarTenantAttribute`, 141.3 ms | the tenant is provisioned first, at Order -200 |
| `Before · SignedInAsAttribute`, 1.8 ms | the declaration runs next, at Order -100, and its event reads `Signed in as test-user` |
| `Apply · TestUserAuthenticator`, 1.2 ms | the shipped transport applies for the in-process server |
| `Apply · NorthstarAuthenticator`, 1.0 ms | the sample's own authenticator rides along on the same request |
| auth entity `auth:user`: name `test-user`, roles empty, claim types empty, application `Northstar`, transport `in-process` | the identity the trace keeps |
| Rest and GraphQL auth entities: source `class`, count `2`, types `ProtoTest.Http.SignedInAsAttribute, Northstar.ProtoTest.NorthstarAuthenticator` | the declaration composes with the app's own authenticator instead of replacing it |

Claim values never appear. The entity records `auth.claim_types`, a list of types, and a gRPC call's `prototest-user` metadata is redacted in the trace as well.

## The published case

The shipped transport needs the application the run hosts in-process. Point the sample at an address that hosts nothing, the dead one from the Level 0 environment drill, add a small test with `[SignedInAs("alice", "admin", Claims = ["tenant=northstar"])]`, and run it:

```powershell
$env:ProtoTest__TargetUrl = "http://127.0.0.1:5099"
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~MyFirstJourney"
Remove-Item Env:ProtoTest__TargetUrl
```

The request fails, because nothing answers at the address. The identity entity in the trace is still written, and in that run it reads:

- `auth.user: alice`, `auth.roles: admin`, `auth.claim_types: tenant`;
- `auth.transport: inert`, with the reason: the application is not hosted in-process, so register it with `AddAspNetCoreServer` and add the app-side authentication with `webHost.AddTestUserAuthentication()`;
- an `auth.user.inert` event with outcome `skipped`.

That is the honest limit: a published application holds no test user unless the suite declares its own `[Auth<T>]` authenticator that reads `context.SignedInUser()`. The [authentication reference](/docs/integrations/rest/authentication) shows that shape and the full precedence rules.

</LearnShell>
