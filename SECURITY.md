# Security policy

ProtoTest is maintained by one person in personal time. Triage and fixes are best-effort with no response-time guarantee.

## Supported versions

Security fixes are made against the latest stable release. Reproduce an issue on the newest ProtoTest packages before reporting it.

## Scope

In scope are the ProtoTest NuGet packages, the CLI, the MCP server and the trace viewer. Sample apps and test-only helpers are out of scope unless they ship in a package.

## Reporting a vulnerability

Do not open a public issue containing exploit details, credentials, private trace data or other sensitive material. Use GitHub's **Report a vulnerability** flow on the repository's Security tab to start a private report. If that control is unavailable, use the contact route on the [project owner's GitHub profile](https://github.com/MSeys).

Include the affected package and version, impact, reproduction steps and any suggested mitigation. Share only the minimum artifact needed to demonstrate the issue. A `.prototrace` can contain application data and attachments.

ProtoTest redacts its own capture by name, but findings and attachments you record yourself are redacted only where you mark them sensitive. Treat a trace like test output and strip secrets before sharing it. The [ProtoTrace page](https://prototest.dev/docs/observability/prototrace) describes what is redacted and how to mark more values sensitive.

You can expect acknowledgement when the report is seen, followed by an assessment of scope and next steps. Public disclosure should wait until a fix or safe mitigation is available.
