import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { parseTraceParam } from "./share";

/* The deployed site's response headers, as public/_headers declares them for every path. */
function contentSecurityPolicy(): Map<string, string[]> {
  const headers = readFileSync(resolve(__dirname, "../public/_headers"), "utf8");
  const line = headers.split(/\r?\n/).find(entry => entry.trim().startsWith("Content-Security-Policy:"));
  if (!line) throw new Error("public/_headers declares no Content-Security-Policy.");
  const directives = line.slice(line.indexOf(":") + 1).split(";").map(part => part.trim()).filter(Boolean);
  return new Map(directives.map(directive => {
    const [name, ...sources] = directive.split(/\s+/);
    return [name, sources];
  }));
}

describe("the site's Content-Security-Policy", () => {
  it("lets ?trace= fetch a trace hosted on another https origin", () => {
    // ?trace=<absolute-url> is the documented way to open a hosted trace, for example from
    // raw.githubusercontent.com; a connect-src that only allows the site itself blocks every one.
    const url = "https://raw.githubusercontent.com/owner/repo/main/run.prototrace";
    expect(parseTraceParam(`${url}`)).toEqual({ state: "valid", url });

    const connect = contentSecurityPolicy().get("connect-src") ?? [];
    expect(connect).toContain("'self'");
    expect(connect).toContain("https:");
  });

  it("keeps everything else on the site's own origin", () => {
    const policy = contentSecurityPolicy();
    expect(policy.get("default-src")).toEqual(["'self'"]);
    expect(policy.get("object-src")).toEqual(["'none'"]);
    expect(policy.get("frame-ancestors")).toEqual(["'none'"]);
  });
});
