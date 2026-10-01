/*
 * The values the docs show of the demo run, prototest-demo.prototrace, as the viewer draws them: names,
 * counts, summaries and the positions the viewer computes. Every viewer picture on the site reads from here,
 * so a change to the archive or the viewer is made once.
 */

export type Outcome = 'passed' | 'failed' | 'partial';

/** Where a mark sits on a clock, as the viewer places it: left and width in percent. */
export type At = readonly [left: number, width: number];

export const run = {
  verdict: [
    {text: '4 failed', tone: 'failed'},
    {text: '1 partial', tone: 'partial'},
    {text: '14 passed', tone: 'passed'},
  ] as const,
  meta: ['19 tests in 4.06 s', '29 Sep 2026, 18:37:26 UTC', '.NET 8.0.31 on Microsoft Windows 10.0.26200'],
  file: 'ProtoTest demo trace',
  duration: 4059.3055,
  outcomes: Array.from({length: 19}, (_, index): Outcome => {
    const number = index + 1;
    if (number === 8 || number === 10 || number === 12 || number === 15) return 'failed';
    if (number === 11) return 'partial';
    return 'passed';
  }),
  tabs: ['Overview', 'Timeline', 'Operations', 'Details', 'Files 64'],
  id: 'b8f1c1c58d984319a2c90b05aa3f2d3e',
  environment: [
    ['environment.os', 'Microsoft Windows 10.0.26200'],
    ['environment.osArchitecture', 'X64'],
    ['environment.processArchitecture', 'X64'],
    ['environment.runtime', '.NET 8.0.31'],
  ] as const,
};

export interface Attention {
  number: string;
  title: string;
  rule: string;
  reason: string;
  detail?: string;
  outcome: Outcome;
}

export const attention: Attention[] = [
  {number: '08', title: 'A bare status hides what the application said', rule: 'Assertion', reason: 'Assert status · 201 Created', detail: 'Expected HTTP status 201 (Created), but received 400 (BadRequest).', outcome: 'failed'},
  {number: '10', title: 'An unknown project ID is treated as mine', rule: 'Assertion', reason: 'Assert status · 200 OK', detail: 'Expected HTTP status 200 (OK), but received 404 (NotFound).', outcome: 'failed'},
  {number: '12', title: 'A real wait does not close the due window', rule: 'Assertion', reason: 'Assert response shape', detail: '$.status: expected "past_due", got "active"', outcome: 'failed'},
  {number: '15', title: 'The address was hardcoded for one machine', rule: 'Runner failure', reason: 'Test execution', detail: 'ConnectionError reaching http://127.0.0.1:5099: connection refused.', outcome: 'failed'},
  {number: '11', title: 'A passing journey can still carry a warning', rule: 'Finding', reason: 'The create response carried 4 fields no assertion mentioned: createdAtUtc, environmentCount, id, slug.', detail: 'Warning, Coverage', outcome: 'partial'},
  {number: 'Gate', title: 'no error findings', rule: 'Passed', reason: 'No error findings were recorded.', outcome: 'passed'},
];

export const visibility = {
  application: 'In-process',
  capabilities: ['Playwright', 'Data', 'Sheets', 'GraphQL', 'REST', 'ASP.NET Core', 'SQL'],
  sources: [
    {label: 'Test side', seen: true},
    {label: 'Observed', seen: false},
    {label: 'Application', seen: true},
  ],
};

/** Four test rows of the run clock: phases and untraced time, on the run's own axis in milliseconds. */
export const runClock = [
  {number: '08', title: 'A bare status hides what the application said', outcome: 'failed' as Outcome, phases: [['setup', 1741.8, 44.1041], ['execution', 1785.929, 35.6183], ['teardown', 1821.6, 58.9866]] as const},
  {number: '10', title: 'An unknown project ID is treated as mine', outcome: 'failed' as Outcome, phases: [['setup', 1742.668, 46.3363], ['execution', 1789.032, 26.2419], ['teardown', 1815.337, 50.0184]] as const},
  {number: '12', title: 'A real wait does not close the due window', outcome: 'failed' as Outcome, phases: [['setup', 1748.087, 46.3246], ['execution', 1794.436, 1108.6688], ['teardown', 2903.184, 7.3962]] as const, gap: [1886.303, 1006.522] as const},
  {number: '15', title: 'The address was hardcoded for one machine', outcome: 'failed' as Outcome, phases: [['setup', 1880.988, 37.3838], ['execution', 1918.419, 2028.7205], ['teardown', 3947.283, 10.3628]] as const, gap: [1918.419, 2028.7205] as const},
];

/** Test 12, the one the docs walk. Positions are the viewer's own, on the test clock. */
export const test = {
  number: '12',
  title: 'A real wait does not close the due window',
  group: 'Failure drills',
  method: 'ARealWaitDoesNotCloseTheDueWindow',
  verdict: {
    rule: 'Assertion',
    what: 'Assert response shape',
    on: 'REST · GET /api/v1/organization',
    path: '$.status',
    expected: '"past_due"',
    actual: '"active"',
  },
  phases: [
    {phase: 'setup', label: 'Setup', at: [0, 3.98287] as At, duration: '46 ms', summary: '20 operations, 6 clients initialized, Create · ProvisionTenantRequest (45 ms)', failed: false, untraced: ''},
    {phase: 'execution', label: 'Execution', at: [4.04093, 95.3203] as At, duration: '1.11 s', summary: '9 operations, Create · IssueInvoiceRequest (91 ms), REST · GET /api/v1/organization (7.6 ms)', failed: true, untraced: '1.01 s without an operation'},
    {phase: 'teardown', label: 'Teardown', at: [99.3038, 0.635909] as At, duration: '7.4 ms', summary: '21 operations, 4 resources released, 3 files published, Cleanup · TenantResponse (3.1 ms)', failed: false, untraced: ''},
  ],
  gap: {at: [11.9484, 86.4956] as At, duration: '1.01 s', hint: 'Until REST · GET /api/v1/organization started, +1.15 s into the test. A wait, or work the trace could not see.'},
  ticks: [['0', 0], ['+200 ms', 17.1955], ['+400 ms', 34.3909], ['+600 ms', 51.5864], ['+800 ms', 68.7818], ['+1.00 s', 85.9773]] as const,
};

export interface Step {
  kind: string;
  /** The execution-vocabulary token the kind is tinted with. */
  tone: string;
  title: string;
  facts?: string;
  app?: boolean;
  duration: string;
  depth: number;
  open?: boolean;
  leaf?: boolean;
  failed?: boolean;
  checks?: {label: string; passed: boolean}[];
  at: At;
}

/** The test body in Steps: the work before the gap, and the call the gap ended. */
export const bodySteps: Step[] = [
  {kind: 'Data', tone: '--type-data', title: 'Create · IssueInvoiceRequest', duration: '91 ms', depth: 0, open: true, at: [4.12691, 7.82146]},
  {kind: 'Data', tone: '--type-data', title: 'Build · IssueInvoiceRequest', duration: '667 µs', depth: 1, leaf: true, at: [4.12691, 0.0573252]},
  {kind: 'Data', tone: '--type-data', title: 'Provision · IssueInvoiceRequest → InvoiceResponse', facts: 'Context set, and 1 more', duration: '90 ms', depth: 1, open: true, at: [4.12691, 7.74444]},
  {kind: 'Northstar', tone: '--type-custom', title: 'invoice.issue', facts: 'Invoice created', app: true, duration: '6 µs', depth: 2, leaf: true, at: [11.6069, 0.0005]},
];

export const callStep: Step = {
  kind: 'Call',
  tone: '--type-call',
  title: 'REST · GET /api/v1/organization',
  duration: '7.6 ms',
  depth: 0,
  failed: true,
  at: [98.444, 0.64928],
  checks: [
    {label: 'status · 200 OK', passed: true},
    {label: 'response shape', passed: false},
  ],
};

export const observed = {what: 'http.response · GET /api/v1/organization', on: 'Northstar·Northstar'};

export interface TimelineRow {
  kind?: string;
  tone?: string;
  name: string;
  depth: number;
  /** How the viewer colours the bar: work, check, machinery, failed, or untraced time. */
  family: 'work' | 'check' | 'machinery' | 'failed' | 'untraced';
  at: At;
  duration: string;
  marks?: string;
  /** A fold the reader can open or close; undefined is a leaf. */
  open?: boolean;
  dim?: boolean;
}

/** Test 12 in the Timeline with setup and teardown folded: the viewer's own rows. */
export const timeline = {
  shown: '12 of 53 operations',
  all: 53,
  attention: 2,
  rows: [
    {kind: 'Phase', tone: '--type-lifecycle', name: 'Setup', depth: 0, family: 'machinery', at: [0, 3.98287], duration: '46 ms', open: false},
    {kind: 'Phase', tone: '--type-lifecycle', name: 'Test execution', depth: 0, family: 'failed', at: [4.04093, 95.3203], duration: '1.11 s', open: true},
    {kind: 'Data', tone: '--type-data', name: 'Create · IssueInvoiceRequest', depth: 1, family: 'work', at: [4.12691, 7.82146], duration: '91 ms', open: true},
    {kind: 'Data', tone: '--type-data', name: 'Build · IssueInvoiceRequest', depth: 2, family: 'work', at: [4.12691, 0.0573252], duration: '667 µs', marks: '3 moments'},
    {kind: 'Data', tone: '--type-data', name: 'Provision · IssueInvoiceRequest → InvoiceResponse', depth: 2, family: 'work', at: [4.12691, 7.74444], duration: '90 ms', open: true},
    {kind: 'Northstar', tone: '--type-custom', name: 'invoice.issue', depth: 3, family: 'work', at: [11.6069, 0.0005], duration: '6 µs'},
    {name: '1.01 s with no recorded operation', depth: 1, family: 'untraced', at: [11.9484, 86.4956], duration: '1.01 s'},
    {kind: 'Call', tone: '--type-call', name: 'REST · GET /api/v1/organization', depth: 1, family: 'work', at: [98.444, 0.64928], duration: '7.6 ms', marks: '1 observation, 1 attachment', open: true},
    {kind: 'Auth', tone: '--type-auth', name: 'Apply · TestUserAuthenticator', depth: 2, family: 'machinery', at: [98.444, 0.00575], duration: '67 µs', dim: true},
    {kind: 'Auth', tone: '--type-auth', name: 'Apply · NorthstarAuthenticator', depth: 2, family: 'machinery', at: [98.444, 0.00113], duration: '13 µs', dim: true},
    {kind: 'Check', tone: '--type-assertion', name: 'Assert status · 200 OK', depth: 2, family: 'check', at: [99.1318, 0.00076], duration: '9 µs'},
    {kind: 'Check', tone: '--type-assertion', name: 'Assert response shape', depth: 2, family: 'failed', at: [99.1318, 0.189125], duration: '2.2 ms', marks: '1 attachment'},
    {kind: 'Phase', tone: '--type-lifecycle', name: 'Teardown', depth: 0, family: 'machinery', at: [99.3038, 0.635909], duration: '7.4 ms', open: false},
  ] as TimelineRow[],
};

export interface StateItem {
  kind: string;
  name: string;
  facts: string;
  life: At;
  ticks: readonly (readonly [number, string])[];
  related?: boolean;
}

/** State with the framework hidden, so what the test ran on steps aside; invoice.issue is selected. */
export const state = {
  selected: {name: 'invoice.issue', when: '+135 ms to +135 ms', at: [11.6069, 0.0005] as At},
  groups: [
    {
      title: 'Reported by the application',
      items: [
        {kind: 'Invoice', name: 'Invoice INV-202610-0001', facts: 'status open, total 238,80', life: [11.6118, 0.6], ticks: [[11.6118, 'applicationside']], related: true},
      ] as StateItem[],
    },
    {
      title: 'Tracked by the test',
      items: [
        {kind: 'Application', name: 'Resource · application:services:Northstar', facts: 'id application:services:Northstar, kind application, and 4 more', life: [0.172027, 99.7756], ticks: [[0.172027, 'testside'], [99.9476, 'testside']]},
        {kind: 'Consumer', name: 'Resource · messaging:consumer:Default', facts: 'id messaging:consumer:Default, kind consumer, and 4 more', life: [0.0860134, 99.914], ticks: [[0.0860134, 'testside'], [100, 'testside']]},
        {kind: 'Data', name: 'Resource · data:TenantResponse:1', facts: 'id data:TenantResponse:1, kind data, and 4 more', life: [3.95662, 95.991], ticks: [[3.95662, 'testside'], [99.9476, 'testside']]},
        {kind: 'Tenant response', name: "Value · TenantResponse 'northstar-597539000012'", facts: 'type ProtoTest.SampleApp.Contracts.TenantResponse, identity northstar-597539000012, and 2 more', life: [3.95662, 0.6], ticks: [[3.95662, 'testside']]},
        {kind: 'Invoice response', name: "Value · InvoiceResponse 'INV-202610-0001'", facts: 'type ProtoTest.SampleApp.Contracts.InvoiceResponse, identity INV-202610-0001, and 2 more', life: [11.8699, 0.6], ticks: [[11.8699, 'testside']]},
      ] as StateItem[],
    },
  ],
  ticks: [['0', 0], ['200 ms', 17.2027], ['400 ms', 34.4054], ['600 ms', 51.6081], ['800 ms', 68.8107], ['1.00 s', 86.0134]] as const,
};

/** Every evidence entry of test 12, in the order the viewer lists them. */
export const evidence = {
  counts: [['All', 15], ['Observations', 3], ['Files', 3], ['Moments', 9]] as const,
  entries: [
    {at: '+1.0 ms', kind: 'Observation', name: 'scenario.started, scenario-597539000012-508c711113534af38939563f9d95655b', detail: 'on Northstar', from: 'Before · NorthstarScenarioHook'},
    {at: '+1.0 ms', kind: 'Moment', name: 'ASP.NET Core server · Northstar', detail: 'aspnetcore.server.initialize, 5 attributes', from: 'Initialize · Northstar (HttpClient)'},
    {at: '+1.0 ms', kind: 'Moment', name: 'Begin correlated Northstar scenario', detail: 'northstar.scenario.begin, 1 attribute', from: 'Before · NorthstarScenarioHook'},
    {at: '+2.0 ms', kind: 'Moment', name: 'Resolve · ProvisionTenantRequest.Name', detail: 'data.value.resolve, 7 attributes', from: 'Build · ProvisionTenantRequest'},
    {at: '+2.0 ms', kind: 'Moment', name: 'Resolve · ProvisionTenantRequest.PlanId', detail: 'data.value.resolve, 7 attributes', from: 'Build · ProvisionTenantRequest'},
    {at: '+47 ms', kind: 'Moment', name: 'Signed in as test-user', detail: 'auth.user.sign-in, 5 attributes', from: 'Before · SignedInAsAttribute'},
    {at: '+48 ms', kind: 'Moment', name: 'Resolve · IssueInvoiceRequest.Metric', detail: 'data.value.resolve, 7 attributes', from: 'Build · IssueInvoiceRequest'},
    {at: '+48 ms', kind: 'Moment', name: 'Resolve · IssueInvoiceRequest.Quantity', detail: 'data.value.resolve, 7 attributes', from: 'Build · IssueInvoiceRequest'},
    {at: '+48 ms', kind: 'Moment', name: 'Resolve · IssueInvoiceRequest.Days', detail: 'data.value.resolve, 7 attributes', from: 'Build · IssueInvoiceRequest'},
    {at: '+1.15 s', kind: 'File', name: '597539000012-rest-01-response', detail: 'application/json, 425 B, GET /api/v1/organization returned 200 (OK)', from: 'REST · GET /api/v1/organization', open: true},
    {at: '+1.15 s', kind: 'Observation', name: 'http.response, GET /api/v1/organization', detail: 'on Northstar:Northstar', from: 'REST · GET /api/v1/organization'},
    {at: '+1.15 s', kind: 'File', name: '597539000012-rest-01-expected-shape', detail: 'application/json, 21 B, GET /api/v1/organization', from: 'Assert response shape', open: true},
    {at: '+1.16 s', kind: 'Observation', name: 'scenario.completed, scenario-597539000012-508c711113534af38939563f9d95655b', detail: 'on Northstar', from: 'After · NorthstarScenarioHook'},
    {at: '+1.16 s', kind: 'File', name: '597539000012-scenario-summary.json', detail: 'application/json, 233 B, Correlation and custom-client milestones for this Northstar scenario.', from: 'After · NorthstarScenarioHook', open: true},
    {at: '+1.16 s', kind: 'Moment', name: 'Complete correlated Northstar scenario', detail: 'northstar.scenario.end, 2 attributes', from: 'After · NorthstarScenarioHook'},
  ],
};

/** The failing check in the details. */
export const check = {
  path: ['Test execution', 'REST · GET /api/v1/organization'],
  kind: 'Check',
  title: 'Assert response shape',
  hint: 'assert.json.shape from ProtoTest.Rest, execution phase',
  facts: ['2.2 ms', '+1.15 s into the test'],
  mismatch: {property: '"status"', expected: '"past_due"', actual: '"active"'},
  response: '{ "status": "active", "planId": "growth", "seatCount": 1, ... }',
  source: {
    file: 'FailureDrills.cs',
    line: 36,
    method: 'Northstar.ProtoTest.FailureDrills.ARealWaitDoesNotCloseTheDueWindow',
    lines: [
      [33, 'await Task.Delay(TimeSpan.FromSeconds(1));'],
      [34, ''],
      [35, 'using var organization = await Proto.Context.Rest().GetAsync("/api/v1/organization");'],
      [36, 'organization'],
      [37, '    .Should.HaveHttpStatus(HttpStatusCode.OK)'],
      [38, '    .Should.MatchShape(new { status = SubscriptionStatuses.PastDue });'],
      [39, '}'],
    ] as const,
  },
  file: {name: '597539000012-rest-01-expected-shape', detail: 'application/json, 21 B'},
  exception: {
    type: 'JsonShapeMismatchException',
    message:
      'Shape mismatch failed with 1 error(s): [$.status]: Values did not match. (Expected: "past_due", Actual: "active")',
  },
  attributes: 12,
};
