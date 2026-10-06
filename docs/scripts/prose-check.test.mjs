import assert from 'node:assert/strict';
import {test} from 'node:test';
import {isLesson, readingWords} from './prose-check.mjs';

test('reading words count prose and component text but not code, tags or front matter', () => {
  const page = [
    '---',
    'title: Not counted here',
    '---',
    "import Lesson from '@site/src/components/Lesson';",
    '',
    'Start the host once.',
    '',
    '```csharp',
    'var host = builder.Build();',
    '```',
    '',
    '<AnnotatedCode code={`ignored code`} callouts={[{line: 1, title: \'Two words\', note: \'Three more words\'}]} />',
    '<Checkpoint question="Why once?">Because it is shared.</Checkpoint>',
  ].join('\n');
  assert.equal(readingWords(page), 4 + 2 + 3 + 2 + 4);
});

test('only Learn lessons have a lesson budget', () => {
  assert.equal(isLesson('docs/learn/start/install-and-run.md'), true);
  assert.equal(isLesson('docs/learn/index.md'), false);
  assert.equal(isLesson('docs/learn/why-it-gets-hard.md'), false);
  assert.equal(isLesson('docs/docs/foundation/execution-context.md'), false);
});

test('a paragraph of stacked abstract nouns is flagged, a concrete one is not', async () => {
  const {check} = await import('./prose-check.mjs');
  const abstract = 'Lifetime and ownership guide placement and isolation. Trace visibility depends on instrumentation and composition.';
  const concrete = 'Open the trace in the viewer. The application answered 404 because the project did not exist.';
  assert.equal(check(abstract).abstract.length, 1);
  assert.equal(check(concrete).abstract.length, 0);
});

test('component props are code, wherever their tag ends; the text a component wraps is prose', async () => {
  const {prose} = await import('./prose-check.mjs');
  const page = [
    '<Lesson',
    "  needs={[<>The previous lesson, <a href=\"/learn/x\">its name</a></>, 'A second need']}",
    '/>',
    '',
    '<AnnotatedCode',
    '  code={`if (a > b) return x => x;',
    '</div> />`}',
    "  callouts={[{line: 1, title: 'Not prose', note: <span>also not</span>}]}",
    '/>',
    '',
    'The paragraph after the figure is read.',
    '',
    '<Checkpoint',
    '  question="Why?"',
    "  verify={<>Open the drill's trace in the <a href=\"https://trace.prototest.dev\">viewer</a>.</>}>",
    '',
    'Because the answer is prose.',
    '',
    '</Checkpoint>',
    '',
    '<Checkpoint question="Short?">One line answer.</Checkpoint>',
  ].join('\n');
  assert.deepEqual(prose(page), ['The paragraph after the figure is read.', 'Because the answer is prose.', 'One line answer.']);
});
