#!/usr/bin/env node
/*
 * Measures the built viewer against a large trace in a real browser.
 *
 * Cold load, the run list, the summarising interactions (filter, search), a test detail with a large
 * span list, the state view and the inspector are timed with performance marks; long tasks, heap use
 * and optional CPU profiles come from the same page. A local static server serves viewer/dist, so no
 * dev server is involved.
 *
 * Requirements: an installed Chromium-family browser (Microsoft Edge by default; override with
 * PROTOTEST_BROWSER or --browser) and puppeteer-core (npm install --no-save puppeteer-core).
 *
 * Usage:
 *   npm run build
 *   node scripts/measure-viewer.mjs --trace .perf/scale-1200.prototrace --tests 1200 --out .perf/measure.json
 *   node scripts/measure-viewer.mjs ... --profile --dist .perf/dist-profile
 */

import { createReadStream, existsSync, mkdirSync, readdirSync, readFileSync, statSync, writeFileSync } from "node:fs";
import { createServer } from "node:http";
import { cpus, release, totalmem } from "node:os";
import { dirname, extname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import puppeteer from "puppeteer-core";

const here = dirname(fileURLToPath(import.meta.url));
const viewer = resolve(here, "..");
const options = parse(process.argv.slice(2));
const dist = resolve(viewer, options.dist ?? "dist");
const trace = resolve(viewer, options.trace ?? ".perf/scale-1200.prototrace");
const tests = Number(options.tests ?? 1200);
const runs = Number(options.runs ?? 3);
const withProfile = Boolean(options.profile);
if (!existsSync(join(dist, "index.html"))) throw new Error(`No built viewer at ${dist}; run npm run build first.`);
if (!existsSync(trace)) throw new Error(`No trace at ${trace}; run scripts/generate-scale-trace.mjs first.`);

const browserPath = options.browser ?? process.env.PROTOTEST_BROWSER ?? edgePath();
const types = {
  ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".css": "text/css; charset=utf-8",
  ".svg": "image/svg+xml", ".json": "application/json", ".png": "image/png", ".woff2": "font/woff2", ".ico": "image/x-icon"
};

const server = createServer((request, response) => {
  let path = resolve(dist, `.${decodeURIComponent(new URL(request.url, "http://127.0.0.1").pathname)}`);
  if (existsSync(path) && statSync(path).isDirectory()) path = join(path, "index.html");
  if (!path.startsWith(dist) || !existsSync(path) || statSync(path).isDirectory()) {
    response.writeHead(404).end();
    return;
  }
  response.writeHead(200, { "Content-Type": types[extname(path)] ?? "application/octet-stream", "Cache-Control": "no-store" });
  createReadStream(path).pipe(response);
});
await new Promise(done => server.listen(0, "127.0.0.1", done));
const base = `http://127.0.0.1:${server.address().port}/`;

const browser = await puppeteer.launch({
  executablePath: browserPath,
  headless: true,
  args: ["--no-sandbox", "--disable-dev-shm-usage", "--window-size=1440,900"]
});

const results = { method: await method(), runs: [], profile: {} };
try {
  for (let iteration = 1; iteration <= runs; iteration++) {
    results.runs.push(await once(iteration));
    process.stdout.write(`run ${iteration}/${runs} done\n`);
  }
} finally {
  await browser.close();
  server.close();
}

results.median = {};
for (const key of Object.keys(results.runs[0].timings)) {
  const values = results.runs.map(run => run.timings[key]).filter(value => typeof value === "number");
  results.median[key] = values.sort((left, right) => left - right)[Math.floor(values.length / 2)];
}
if (options.out) {
  mkdirSync(dirname(resolve(viewer, options.out)), { recursive: true });
  writeFileSync(resolve(viewer, options.out), JSON.stringify(results, null, 1));
}
console.log(JSON.stringify({ method: results.method, median: results.median, runs: results.runs.map(run => run.timings) }, null, 1));

async function once(iteration) {
  const page = await browser.newPage();
  await page.setViewport({ width: 1440, height: 900, deviceScaleFactor: 1 });
  await page.goto(base, { waitUntil: "load" });
  await page.evaluate(() => {
    window.__longTasks = [];
    try {
      new PerformanceObserver(list => { for (const entry of list.getEntries()) window.__longTasks.push(entry.duration); })
        .observe({ type: "longtask", buffered: true });
    } catch { /* longtask is Chromium-only */ }
  });
  await page.evaluate(() => { window.__longTasks.length = 0; });

  const timings = {};
  const heap = {};
  const performance = {};
  const client = withProfile && iteration === 1 ? await page.createCDPSession() : null;
  if (client) {
    await client.send("Profiler.enable");
    await client.send("Performance.enable");
    await client.send("Profiler.start");
  }

  const input = await page.$("input[type=file]");
  performance.open = await deltas(client, timings, "open", () => measure(page, "open", () => input.uploadFile(trace),
    `document.querySelector(".workspace") !== null`, 240_000));
  heap.afterOpen = await heapUsed(page);
  timings.runRows = await measure(page, "runRows", async () => { /* the workspace renders the run list with it */ },
    `document.querySelectorAll(".tests .test-row").length === ${tests}`, 60_000);
  heap.afterRunRows = await heapUsed(page);
  if (client) {
    await client.send("Profiler.stop").then(({ profile }) => saveProfile("open", profile));
  }

  timings.filterAttention = await measure(page, "filterAttention", () => clickChip(page, "Needs attention"),
    `document.querySelectorAll(".tests .test-row").length > 0 && document.querySelectorAll(".tests .test-row").length < ${tests}`, 60_000);
  performance.clearFilter = await deltas(client, timings, "clearFilter", () => measure(page, "clearFilter", () => clickChip(page, "All"),
    `document.querySelectorAll(".tests .test-row").length === ${tests}`, 60_000));

  const runSearch = await page.$('.run input[type="search"]');
  performance.runSearch = await deltas(client, timings, "runSearch", () => measure(page, "runSearch", () => runSearch.type("GraphQL", { delay: 0 }),
    `document.querySelectorAll(".tests .test-row").length > 0 && document.querySelectorAll(".tests .test-row").length < ${tests}`, 60_000));
  timings.clearSearch = await measure(page, "clearSearch", () => clearInput(page, '.run input[type="search"]'),
    `document.querySelectorAll(".tests .test-row").length === ${tests}`, 60_000);

  if (client) {
    await client.send("Profiler.enable");
    await client.send("Profiler.start");
  }
  timings.openTest = await measure(page, "openTest", () => clickTest(page, "ExceedingTheTokenRateLimitReturnsTooManyRequests"),
    `document.querySelector(".test") !== null && document.querySelectorAll(".story .line").length > 0`, 120_000);
  heap.afterOpenTest = await heapUsed(page);
  if (client) {
    await client.send("Profiler.stop").then(({ profile }) => saveProfile("test", profile));
  }

  timings.spansTab = await measure(page, "spansTab", () => clickTab(page, "Spans"),
    `document.querySelectorAll(".spans .list .row").length > 0`, 60_000);
  const spanSearch = await page.$('.spans input[type="search"]');
  timings.spansSearch = await measure(page, "spansSearch", () => spanSearch.type("http", { delay: 0 }),
    `document.querySelectorAll(".spans .list .row").length > 0 && document.querySelectorAll(".spans .list .row").length < 100`, 60_000);
  timings.spanInspect = await measure(page, "spanInspect", () => page.evaluate(() => document.querySelector(".spans .list .row .pick")?.click()),
    `document.querySelector(".inspector .body")?.childElementCount > 0`, 60_000);
  heap.afterInspect = await heapUsed(page);

  timings.stateTab = await measure(page, "stateTab", () => clickTab(page, "State"),
    `document.querySelectorAll(".state .item").length > 0`, 60_000);
  performance.backToRun = await deltas(client, timings, "backToRun", () => measure(page, "backToRun", () => clickTab(page, "Run"),
    `document.querySelectorAll(".tests .test-row").length === ${tests}`, 60_000));

  const longTasks = await page.evaluate(() => window.__longTasks);
  const paints = await page.evaluate(() => performance.getEntriesByType("paint").map(entry => ({ name: entry.name, startTime: entry.startTime })));
  await page.close();
  return {
    iteration,
    timings,
    heap,
    performance,
    longTasks: { count: longTasks.length, totalMs: round(longTasks.reduce((sum, value) => sum + value, 0)), longestMs: round(Math.max(0, ...longTasks)) },
    paints: paints.map(entry => ({ ...entry, startTime: round(entry.startTime) })),
    trace: { path: trace, bytes: statSync(trace).size }
  };
}

/** Measures one step and, in profile mode, the renderer time the step cost (script, layout, style). */
async function deltas(client, timings, name, action) {
  if (!client) { timings[name] = await action(); return undefined; }
  const before = await client.send("Performance.getMetrics");
  timings[name] = await action();
  const after = await client.send("Performance.getMetrics");
  const value = (metrics, key) => metrics.metrics.find(entry => entry.name === key)?.value ?? 0;
  return {
    scriptMs: round((value(after, "ScriptDuration") - value(before, "ScriptDuration")) * 1000),
    layoutMs: round((value(after, "LayoutDuration") - value(before, "LayoutDuration")) * 1000),
    styleMs: round((value(after, "RecalcStyleDuration") - value(before, "RecalcStyleDuration")) * 1000),
    taskMs: round((value(after, "TaskDuration") - value(before, "TaskDuration")) * 1000),
    layoutCount: value(after, "LayoutCount") - value(before, "LayoutCount"),
    styleCount: value(after, "RecalcStyleCount") - value(before, "RecalcStyleCount")
  };
}

function saveProfile(name, profile) {
  const file = resolve(viewer, options.out ? `${options.out}.${name}.cpuprofile` : `.perf/${name}.cpuprofile`);
  writeFileSync(file, JSON.stringify(profile));
  results.profile[name] = topFunctions(profile);
}

async function method() {
  return {
    measuredAt: new Date().toISOString(),
    machine: `${cpus()[0].model} (${cpus().length} logical), ${(totalmem() / 1024 ** 3).toFixed(0)} GB, ${release()}`,
    browser: browserPath,
    browserVersion: await browser.version(),
    puppeteer: JSON.parse(readFileSync(join(viewer, "node_modules/puppeteer-core/package.json"), "utf8")).version,
    node: process.version,
    build: { dist, files: describe(dist) },
    viewport: "1440x900",
    throttling: "none",
    trace: { path: trace, bytes: statSync(trace).size, tests }
  };
}

function describe(directory) {
  const files = [];
  for (const name of readdirSync(directory)) {
    const path = join(directory, name);
    if (statSync(path).isFile()) files.push({ name, bytes: statSync(path).size });
  }
  const assets = join(directory, "assets");
  if (existsSync(assets)) {
    for (const name of readdirSync(assets)) {
      const path = join(assets, name);
      if (statSync(path).isFile()) files.push({ name: `assets/${name}`, bytes: statSync(path).size });
    }
  }
  return files;
}

function edgePath() {
  const candidates = [
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
    "C:/Program Files/Microsoft/Edge/Application/msedge.exe"
  ];
  const found = candidates.find(existsSync);
  if (!found) throw new Error("Microsoft Edge was not found; pass --browser <path> or set PROTOTEST_BROWSER.");
  return found;
}

/** Runs an action and returns the wall time from a performance mark to the ready DOM plus two painted frames. */
async function measure(page, mark, action, ready, timeout) {
  const name = `__measure_${mark}`;
  await page.evaluate(value => { performance.clearMarks(value); performance.mark(value); }, name);
  await action();
  await page.waitForFunction(ready, { timeout });
  await page.evaluate(() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done))));
  return page.evaluate(value => Math.round((performance.now() - performance.getEntriesByName(value).at(-1).startTime) * 10) / 10, name);
}

function heapUsed(page) {
  return page.metrics().then(metrics => metrics.JSHeapUsedSize);
}

async function clickChip(page, label) {
  await page.evaluate(text => {
    const chip = [...document.querySelectorAll("button.chip")].find(button => button.textContent.trim().startsWith(text));
    if (!chip) throw new Error(`No chip named ${text}`);
    chip.click();
  }, label);
}

async function clickTab(page, label) {
  await page.evaluate(text => {
    const tab = [...document.querySelectorAll('nav.tabs a[role="tab"]')].find(anchor => anchor.textContent.trim() === text);
    if (!tab) throw new Error(`No tab named ${text}`);
    tab.click();
  }, label);
}

async function clickTest(page, title) {
  await page.evaluate(text => {
    const row = [...document.querySelectorAll("button.test-row")].find(button => button.title.includes(text));
    if (!row) throw new Error(`No test row matching ${text}`);
    row.click();
  }, title);
}

async function clearInput(page, selector) {
  await page.evaluate(value => {
    const input = document.querySelector(value);
    if (!input) throw new Error(`No input at ${value}`);
    input.value = "";
    input.dispatchEvent(new Event("input", { bubbles: true }));
  }, selector);
}

/** Self time per function from a Chrome CPU profile; native functions like JSON.parse have no URL. */
function topFunctions(cpu) {
  const byId = new Map(cpu.nodes.map(node => [node.id, node]));
  const totals = new Map();
  for (let index = 0; index < cpu.samples.length; index++) {
    const node = byId.get(cpu.samples[index]);
    if (!node) continue;
    const frame = node.callFrame;
    const url = frame.url.split("/").pop() ?? "";
    const key = `${frame.functionName || "(anonymous)"} ${url}:${(frame.lineNumber ?? 0) + 1}`;
    totals.set(key, (totals.get(key) ?? 0) + (cpu.timeDeltas[index] ?? 0));
  }
  return [...totals.entries()]
    .sort((left, right) => right[1] - left[1])
    .slice(0, 15)
    .map(([key, micros]) => ({ function: key, ms: round(micros / 1000) }));
}

function round(value) {
  return Math.round(value * 10) / 10;
}

function parse(args) {
  const parsed = {};
  for (let index = 0; index < args.length; index++) {
    const arg = args[index];
    if (!arg.startsWith("--")) continue;
    const key = arg.slice(2);
    const next = args[index + 1];
    if (next !== undefined && !next.startsWith("--")) { parsed[key] = next; index++; }
    else parsed[key] = true;
  }
  return parsed;
}
