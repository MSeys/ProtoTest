import {lessonTraces} from '@site/src/data/traceSources';

/*
 * The recorded walk of a skipped test, shared by the environments and skip-conditions pages.
 * Values are the archive's own: docs/static/lessons/l2-broker-skip.prototrace holds five entries
 * (two run reports, manifest, spans, state) and no test record at all, because the broker journey
 * skipped before its lifecycle started. The run state carries the loopback application, its readiness
 * probe, the broker resource, and seven capabilities with no broker among them.
 */

export const brokerSkipTest = 'Northstar.ProtoTest.BrokerJourney.PayingAnInvoicePublishesAnInvoicePaidEvent';

export const brokerSkipSource = lessonTraces.brokerSkip;

export const brokerSkipReason = 'No broker is configured; set ProtoTest:Messaging:Broker=container.';

export const brokerSkipLayers = [
  {
    id: 'run',
    label: 'Run',
    when: 'before the first test',
    lead: 'What the run composed. Seven capabilities, and no broker among them, so the broker journey cannot run.',
    entries: [
      {
        kind: 'capability',
        name: 'REST, GraphQL, ASP.NET Core, SQL, Data, Sheets, Playwright',
        meta: 'declared; the broker capability is absent',
      },
      {
        kind: 'application',
        name: 'Northstar web on a loopback listener',
        meta: 'readiness /health, 1 attempt, waited 92 ms',
      },
      {
        kind: 'broker',
        name: 'Messaging broker',
        meta: 'released; no container started and no connection string configured',
      },
    ],
  },
  {
    id: 'skip',
    label: 'No test record',
    when: 'nothing started',
    lead: 'The condition applied before StartTestAsync, so the archive holds no setup, no execution, and no teardown for this test.',
    entries: [
      {
        kind: 'skipped',
        name: 'BrokerJourney.PayingAnInvoicePublishesAnInvoicePaidEvent',
        meta: 'no context, no entries, no report item; the reason reached the runner',
      },
    ],
  },
];
