/*
 * The Learning demo's four failure drills and the tests that fix them. Every name, status and message
 * comes from a run of samples/Northstar.ProtoTest with the drills enabled; the durations are read from
 * the committed archives under docs/static/lessons, so the gallery shows what the reader downloads.
 * Nothing here is invented. A rerun writes its own durations, never different names or statuses.
 */

export interface DrillRecord {
  /** The trace entry's kind, as the viewer prints it. */
  kind: string;
  name: string;
  status?: 'succeeded' | 'failed';
  detail?: string;
}

export interface DrillSide {
  test: string;
  what: string;
  elapsed: string;
  record: DrillRecord[];
}

export interface DrillPair {
  id: 'time' | 'state' | 'environment' | 'visibility';
  question: string;
  ask: string;
  drill: DrillSide;
  fix: DrillSide;
  change: string;
}

export const failureDrills: DrillPair[] = [
  {
    id: 'time',
    question: 'Time',
    ask: 'Who moves the clock?',
    drill: {
      test: 'ARealWaitDoesNotCloseTheDueWindow',
      what: 'Waits one real second while the application runs on the test clock.',
      elapsed: '1.37 s',
      record: [
        {
          kind: 'data.create',
          name: 'Create · IssueInvoiceRequest',
          status: 'succeeded',
          detail: '230.3 ms, provisioned in the test tenant',
        },
        {
          kind: 'http.request',
          name: 'REST · GET /api/v1/organization',
          status: 'succeeded',
          detail: '98.1 ms, HTTP 200',
        },
        {
          kind: 'assert.json.shape',
          name: 'Assert response shape',
          status: 'failed',
          detail: 'Shape mismatch failed with 1 error: [$.status]: Values did not match. (Expected: "past_due", Actual: "active")',
        },
      ],
    },
    fix: {
      test: 'TheTestClockClosesTheDueWindow',
      what: 'Advances the test clock eight days, then reads the organization and pays the invoice.',
      elapsed: '295.3 ms',
      record: [
        {
          kind: 'clock.advance',
          name: 'Clock advanced by 8:0:00:00',
          status: 'succeeded',
          detail: 'recorded on test.execution, from the test side',
        },
        {
          kind: 'data.create',
          name: 'Create · IssueInvoiceRequest',
          status: 'succeeded',
          detail: '170.8 ms, provisioned in the test tenant',
        },
        {
          kind: 'http.request',
          name: 'REST · GET /api/v1/organization',
          status: 'succeeded',
          detail: '66.1 ms, HTTP 200',
        },
        {
          kind: 'assert.json.shape',
          name: 'Assert response shape',
          status: 'succeeded',
          detail: 'the same property, now past_due',
        },
        {
          kind: 'http.request',
          name: 'REST · POST /api/v1/invoices/{invoiceId}/pay',
          status: 'succeeded',
          detail: '37.5 ms, HTTP 200',
        },
        {
          kind: 'assert.json.shape',
          name: 'Assert response shape',
          status: 'succeeded',
          detail: 'status paid',
        },
      ],
    },
    change: 'Move the test clock instead of waiting on real time.',
  },
  {
    id: 'state',
    question: 'State',
    ask: 'What does the test share, and what does it leave behind?',
    drill: {
      test: 'AnUnknownProjectIdIsTreatedAsMine',
      what: 'Reads a project id no test in the run created.',
      elapsed: '129.0 ms',
      record: [
        {
          kind: 'http.request',
          name: 'REST · GET /api/v1/projects/prj_1',
          status: 'succeeded',
          detail: '119.1 ms, HTTP 404',
        },
        {
          kind: 'assert.http.status',
          name: 'Assert status · 200 OK',
          status: 'failed',
          detail:
            'Expected HTTP status 200 (OK), but received 404 (NotFound). Body: {"code":"not_found","message":"The requested project does not exist.","details":null}',
        },
      ],
    },
    fix: {
      test: 'EachTenantSeesOnlyItsOwnProjects',
      what: 'Creates a project, then lists the projects its tenant can see.',
      elapsed: '237.0 ms',
      record: [
        {
          kind: 'data.create',
          name: 'Create · CreateProjectRequest',
          status: 'succeeded',
          detail: '119.5 ms, provisioned in the test tenant',
        },
        {
          kind: 'http.request',
          name: 'REST · GET /api/v1/projects',
          status: 'succeeded',
          detail: '105.1 ms, HTTP 200',
        },
        {
          kind: 'assert.http.status',
          name: 'Assert status · 200 OK',
          status: 'succeeded',
          detail: 'the list holds one project, the one this test created',
        },
      ],
    },
    change: 'Read only the state the test created.',
  },
  {
    id: 'environment',
    question: 'Environment',
    ask: 'Where does the address come from?',
    drill: {
      test: 'TheAddressWasHardcodedForOneMachine',
      what: 'Connects a raw HttpClient to a fixed address, 127.0.0.1:5099.',
      elapsed: '2.08 s',
      record: [
        {
          kind: 'test.execution',
          name: 'Test execution',
          detail: '2.08 s, no operation recorded: the raw client is outside the composition',
        },
      ],
    },
    fix: {
      test: 'TheAddressComesFromTheComposition',
      what: 'Calls the same endpoint through the REST client the run composed.',
      elapsed: '157.6 ms',
      record: [
        {
          kind: 'http.request',
          name: 'REST · GET /api/v1/organization',
          status: 'succeeded',
          detail: '139.9 ms through the composed client, HTTP 200',
        },
      ],
    },
    change: 'Take the address from the composition.',
  },
  {
    id: 'visibility',
    question: 'Visibility',
    ask: 'When it fails, what can it show you?',
    drill: {
      test: 'ABareStatusHidesWhatTheApplicationSaid',
      what: 'Sends an empty project name and asserts the status alone.',
      elapsed: '140.1 ms',
      record: [
        {
          kind: 'http.request',
          name: 'REST · POST /api/v1/projects',
          status: 'succeeded',
          detail: '129.9 ms, HTTP 400',
        },
        {
          kind: 'assert.http.status',
          name: 'Assert status · 201 Created',
          status: 'failed',
          detail:
            'Expected HTTP status 201 (Created), but received 400 (BadRequest). The response body named validation_failed and the empty parameter.',
        },
      ],
    },
    fix: {
      test: 'TheProblemBodyNamesTheCodeAndDetail',
      what: 'Sends the same request and asserts the problem body.',
      elapsed: '140.8 ms',
      record: [
        {
          kind: 'http.request',
          name: 'REST · POST /api/v1/projects',
          status: 'succeeded',
          detail: '121.8 ms, HTTP 400',
        },
        {
          kind: 'assert.json.shape',
          name: 'Assert response shape',
          status: 'succeeded',
          detail: 'code validation_failed, message contains "name"',
        },
      ],
    },
    change: 'Assert the body the application sent.',
  },
];
