---
name: prototest-write-test
description: Use when writing a new ProtoTest integration test, covering an untested endpoint, page, operation or device message, or extending a ProtoTest suite. Teaches reusing what the suite already composes, through the get_suite_map and get_coverage MCP tools, and proving the test with a run, review_tests and prototest verify.
---

# Writing a ProtoTest test

A ProtoTest suite composes its applications, clients, test data and page objects once. A good new test
reuses all of that and adds only the scenario: the calls, the checks, and the data the scenario is about.
This skill writes that test and proves it.

## 1. Read the suite before writing

Call `get_suite_map` (the ProtoTest MCP server). It describes the newest run:

| Field | Use it to |
| --- | --- |
| `clients` | call the API through `Proto.Context.Rest()`, `.GraphQL()`, `.Grpc()`; never a new `HttpClient` |
| `provisioners` | create test data with `Proto.Context.Data().For<TInput>()...CreateAsync<TResult>()`; never raw inserts |
| `attributes` | put the suite's own attributes (`suite: true`) on the test, such as a tenant or a signed-in user |
| `pages` | drive the browser through the page objects and component paths listed; never a CSS selector in the test |
| `devices` | talk to a device through its client and device class; build data strings as `DeviceMessage` records |
| `examples` | open the example test for the kind of work, and copy its shape |
| `coverageGaps` | pick what to cover when the request names no unit; `get_coverage` pages through more |

`get_coverage` gives one suggestion per uncovered endpoint: **extend** the named test when it already
calls the endpoint, or write a **new** test shaped like the named one. The `cover_change` prompt runs
this whole skill and ends with its checks.

The map lists what the run recorded. When the request needs something it does not list, search the
test project for the setup class (the `ProtoTestAssembly` with `Configure(IProtoHostBuilder)`, `AddApplication`,
`AddDataProvisioner`, `AddData`) before adding setup of your own.

## 2. Write the scenario

- **One behavior per test.** Name it after the behavior: `CreatingAProjectReturnsIt`.
- **Arrange through the suite.** A provisioner or attribute creates what the test needs, in the test's own
  tenant or scope, and owns its cleanup. Build values with `Proto.Context.Data().For<T>().With(...)`, and
  set only what the test is about.
- **No fixed ids.** Use the identity a provisioner returned, `Proto.Context.Data().Ref<T>()`, or
  `Proto.Context.UniqueName("...")`.
- **Assert shapes, not bodies.** `Should.HaveHttpStatus(...)` then `Should.MatchShape(new { ... })` with only
  the fields the behavior defines. Constrain values the application chooses (`JsonValue.GreaterThan(0)`).
- **No sleeps.** Move time with `Proto.Context.Clock.Advance`. Wait for background work with
  `ProtoPolling.PollAsync` and a deadline. A service's start is the host's readiness, not the test's.
- **Browser checks retry on their own.** Use `Should.HaveTextAsync` and friends with the page objects;
  never read once and compare.

## 3. Prove it

1. Run only the new test: `dotnet test --filter "FullyQualifiedName~<Class>.<Method>"`, or the runner's own
   filter on Microsoft Testing Platform (TUnit: `--treenode-filter`).
2. On red, follow the `prototest-evidence-loop` skill: `get_failure`, then `get_diagnosis` with
   `detail=context`. Fix from the evidence.
3. Run it a second time. A test that passes once and fails once is not done.
4. Call `review_tests` for the new test (or `prototest review <trace> --test <name>`). It must be clean:
   the body checks what it calls and records its waits.
5. Break the expectation once on purpose and run it: it must fail. A test that cannot fail proves nothing.
6. Run the whole suite and compare with the previous report:
   `prototest verify <baseline-report.json> <current-report.json>`. The unit you covered moves from
   uncovered to covered, and no other test changed outcome.

## Done when

- the test passes twice, alone and in the full suite, and fails when its expectation is broken;
- `review_tests` reads it clean;
- the requested unit is covered in the new run's report;
- the diff adds a test, and setup only where the suite had none, in the setup class, not in the test;
- the test contains no `HttpClient`, `Task.Delay`, `Thread.Sleep`, raw SQL insert or fixed id.
