import { existsSync, readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { describe, expect, it } from "vitest";
import { openTraceArchive } from "./archive";
import { buildRun } from "./model";
import type { WireSpans, WireState } from "./wire";

/** Walks up from the test's working directory so the file is found from the viewer or the repository root. */
function repositoryFile(relative: string): string {
  let directory = process.cwd();
  for (;;) {
    const candidate = join(directory, relative);
    if (existsSync(candidate)) return candidate;
    const parent = dirname(directory);
    if (parent === directory) throw new Error(`Could not find ${relative} from ${process.cwd()}.`);
    directory = parent;
  }
}

/** The wire facts both the C# writer and this reader assert against; see design/prototrace-wire.contract.json. */
const contract = JSON.parse(readFileSync(repositoryFile("design/prototrace-wire.contract.json"), "utf8")) as {
  archiveFormatVersion: string;
  spanFormatVersion: string;
  stateFormatVersion: string;
  valueSourceTokens: string[];
};

describe("wire contract", () => {
  it("recognizes exactly the change-source tokens the writer emits", () => {
    const changes = contract.valueSourceTokens.map((token, index) => ({
      atUtc: "2026-01-01T00:00:00Z",
      operationId: null,
      change: `change ${index}`,
      state: {},
      source: token,
      inferred: false
    }));
    const state: WireState = {
      formatVersion: contract.stateFormatVersion,
      run: {
        items: [{
          kind: "value",
          id: "value:1",
          name: "value",
          scope: null,
          firstSeenUtc: "2026-01-01T00:00:00Z",
          lastSeenUtc: "2026-01-01T00:00:00Z",
          state: {},
          changes
        }]
      },
      tests: []
    };
    const spans: WireSpans = { formatVersion: contract.spanFormatVersion, resourceSpans: [] };

    const run = buildRun(spans, state);

    expect(run.visibility.sources).toEqual(contract.valueSourceTokens);
  });

  it("opens the committed golden archive with the contract's versions", async () => {
    const bytes = readFileSync(repositoryFile("viewer/public/demos/prototest-demo.prototrace"));
    const buffer = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer;

    const archive = await openTraceArchive(buffer);

    expect(archive.spans.formatVersion).toBe(contract.spanFormatVersion);
    expect(archive.state.formatVersion).toBe(contract.stateFormatVersion);
    expect(archive.spans.resourceSpans.length).toBeGreaterThan(0);
  });
});
