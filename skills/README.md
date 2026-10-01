# ProtoTest skills

Repo-hosted skills that teach a coding agent to work with a ProtoTest suite:

- [`prototest-evidence-loop`](./prototest-evidence-loop/SKILL.md): run the suite, read the trace, fix from evidence.
- [`prototest-write-test`](./prototest-write-test/SKILL.md): write a new test that reuses what the suite composes, and prove it.

## Copy them in

The skills are copy-in. They are not part of any NuGet package. `dotnet new prototest` places both in the new project's `.claude/skills/`.

For a client that reads `SKILL.md` folders (Claude Code, for example), copy the skill folders into the client's skills directory:

```bash
mkdir -p .claude/skills
cp -r skills/prototest-evidence-loop skills/prototest-write-test .claude/skills/
```

For a client that reads a rules or instructions file instead, paste the body of a `SKILL.md` into that file.

The MCP server and the `prototest` CLI are the agent-facing surface. The skills only teach when to call them. The [Agent workflows](https://prototest.dev/docs/agent-workflows/coding-agents) pages carry the detail.
