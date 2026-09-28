# ProtoTest skills

A minimal, repo-hosted skill that teaches a coding agent the ProtoTest evidence loop and the four MCP
tools: [`prototest-evidence-loop/SKILL.md`](./prototest-evidence-loop/SKILL.md).

## Copy it in

The bundle is copy-in. It is not part of any NuGet package and no installer writes it.

For a client that reads `SKILL.md` folders (Claude Code, for example), copy the skill folder into the
client's skills directory:

```bash
mkdir -p .claude/skills
cp -r skills/prototest-evidence-loop .claude/skills/
```

For a client that reads a rules or instructions file instead, paste the body of `SKILL.md` into that
file.

The MCP server and the `prototest` CLI are the agent-facing surface; the skill only teaches when to
call them. The [Agent workflows](https://prototest.dev/docs/agent-workflows/coding-agents) pages carry
the detail.
