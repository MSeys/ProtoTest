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
