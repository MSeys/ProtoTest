/*
 * Selected evidence from the saved Northstar failure drills and their paired tests.
 * Durations and failure messages come from the archives under docs/static/lessons.
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
      elapsed: '1.25 s',
      record: [
        {
          kind: 'data.create',
          name: 'Create · IssueInvoiceRequest',
          status: 'succeeded',
          detail: '152.2 ms, provisioned in the test tenant',
        },
        {
          kind: 'http.request',
          name: 'REST · GET /api/v1/organization',
          status: 'succeeded',
          detail: '74.0 ms, HTTP 200',
        },
        {
          kind: 'assert.json.shape',
          name: 'Assert response shape',
          status: 'failed',
          detail: `Shape mismatch failed with 1 error(s): • [$.status]: Values did not match. (Expected: "past_due", Actual: "active")`,
        },
      ],
    },
    fix: {
      test: 'TheTestClockClosesTheDueWindow',
      what: 'Advances the test clock eight days, then reads the organization and pays the invoice.',
      elapsed: '286.8 ms',
      record: [
        {
          kind: 'data.create',
          name: 'Create · IssueInvoiceRequest',
          status: 'succeeded',
          detail: '165.8 ms, provisioned in the test tenant',
        },
        {
          kind: 'clock.advance',
          name: 'Clock advanced by 8:0:00:00',
          detail: 'event on test.execution, from the test side',
        },
        {
          kind: 'http.request',
          name: 'REST · GET /api/v1/organization',
          status: 'succeeded',
          detail: '65.3 ms, HTTP 200',
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
          detail: '35.0 ms, HTTP 200',
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
    ask: 'What does the test leave behind?',
    drill: {
      test: 'AnUnknownProjectIdIsTreatedAsMine',
      what: 'Reads the hardcoded project id prj_1 in its test tenant.',
      elapsed: '134.6 ms',
      record: [
        {
          kind: 'http.request',
          name: 'REST · GET /api/v1/projects/prj_1',
          status: 'succeeded',
          detail: '123.8 ms, HTTP 404',
        },
        {
          kind: 'assert.http.status',
          name: 'Assert status · 200 OK',
          status: 'failed',
          detail:
            'Expected HTTP status 200 (OK), but received 404 (NotFound). Response body: {"code":"not_found","message":"The requested project does not exist.","details":null}',
        },
      ],
    },
    fix: {
      test: 'EachTenantSeesOnlyItsOwnProjects',
      what: 'Creates a project, then lists the projects its tenant can see.',
      elapsed: '185.3 ms',
      record: [
        {
          kind: 'data.create',
          name: 'Create · CreateProjectRequest',
          status: 'succeeded',
          detail: '100.2 ms, provisioned in the test tenant',
        },
        {
          kind: 'http.request',
          name: 'REST · GET /api/v1/projects',
          status: 'succeeded',
          detail: '72.6 ms, HTTP 200',
        },
        {
          kind: 'assert.http.status',
          name: 'Assert status · 200 OK',
          status: 'succeeded',
          detail: 'HTTP 200; separate NUnit checks verify the returned project',
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
      what: 'Calls 127.0.0.1:5099 with a raw HttpClient inside the test. This client bypasses ProtoTest REST instrumentation, so this trace has no HTTP request operation.',
      elapsed: '2.05 s',
      record: [
        {
          kind: 'test.execution',
          name: 'Test execution',
          status: 'failed',
          detail:
            '2.05 s, failed: ConnectionError reaching http://127.0.0.1:5099: connection refused.',
        },
      ],
    },
    fix: {
      test: 'TheAddressComesFromTheComposition',
      what: 'Calls the same endpoint through the composed REST client.',
      elapsed: '149.8 ms',
      record: [
        {
          kind: 'http.request',
          name: 'REST · GET /api/v1/organization',
          status: 'succeeded',
          detail: '131.7 ms through the composed client, HTTP 200',
        },
      ],
    },
    change: 'Get the address from ProtoTest instead of hardcoding it.',
  },
  {
    id: 'visibility',
    question: 'Visibility',
    ask: 'When a test fails, what does it show you?',
    drill: {
      test: 'ABareStatusHidesWhatTheApplicationSaid',
      what: 'Sends an empty project name and asserts the status alone.',
      elapsed: '150.4 ms',
      record: [
        {
          kind: 'http.request',
          name: 'REST · POST /api/v1/projects',
          status: 'succeeded',
          detail: '140.1 ms, HTTP 400',
        },
        {
          kind: 'assert.http.status',
          name: 'Assert status · 201 Created',
          status: 'failed',
          detail: `Expected HTTP status 201 (Created), but received 400 (BadRequest). Response body: {"code":"validation_failed","message":"The value cannot be an empty string or composed entirely of whitespace. (Parameter 'name')","details":null}`,
        },
      ],
    },
    fix: {
      test: 'TheProblemBodyNamesTheCodeAndDetail',
      what: 'Sends the same empty-name request and checks HTTP 400 and the problem body.',
      elapsed: '155.6 ms',
      record: [
        {
          kind: 'http.request',
          name: 'REST · POST /api/v1/projects',
          status: 'succeeded',
          detail: '135.0 ms, HTTP 400',
        },
        {
          kind: 'assert.json.shape',
          name: 'Assert response shape',
          status: 'succeeded',
          detail: 'code validation_failed, message contains "name"',
        },
      ],
    },
    change: 'Check the expected rejection and its validation problem body.',
  },
];
