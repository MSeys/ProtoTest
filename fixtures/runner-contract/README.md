# Fixture: the runner contract suites

A test fixture, not product code: intentionally failing test applications, one per runner (NUnit, MSTest,
xUnit v2, xUnit v3, TUnit), that `tests/ProtoTest.RunnerContract.Tests` builds once per run and drives in
fresh processes. They are outside `ProtoTest.slnx` and `tests/`, so no ordinary test run discovers them;
do not run them as green suites.

Every project compiles the one `RunnerContract.cs` with its runner's symbol. The environment picks the
scenario: `RUNNER_CONTRACT_BODY` (`pass`, `fail`, `skip`, `cancel`), `RUNNER_CONTRACT_CLEANUP` (`ok`,
`fail`), `RUNNER_CONTRACT_MODE` (`Fail`, `Report`), `RUNNER_CONTRACT_SETUP` (`ok`, `fail`) and
`RUNNER_CONTRACT_OUTPUT`, the folder the trace archive and the release markers go to. A runner filter picks
the test class: `CleanupContract`, `RowsContract` or, on xUnit v2, `MissingFixtureContract`.
