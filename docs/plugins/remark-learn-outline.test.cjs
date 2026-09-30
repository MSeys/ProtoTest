const {test} = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const path = require('node:path');
const {compileToJSX} = require('@docusaurus/mdx-loader/lib/utils');
const {DEFAULT_MARKDOWN_CONFIG} = require('@docusaurus/core/lib/server/configValidation');
const outline = require('./remark-learn-outline.cjs');

async function compile(content) {
  const result = await compileToJSX({
    filePath: path.resolve('learn/outline-fixture.mdx'),
    fileContent: content,
    frontMatter: {},
    compilerName: 'server',
    options: {
      siteDir: path.resolve('.'),
      staticDirs: [],
      markdownConfig: DEFAULT_MARKDOWN_CONFIG,
      remarkPlugins: [outline],
    },
  });
  const array = result.content.match(/export const toc = ([\s\S]*?);/)[1];
  return JSON.parse(JSON.stringify(vm.runInNewContext(array)));
}

test('a compiled lesson includes the shell sections around its real Markdown steps', async () => {
  const toc = await compile(`# A lesson

<LearnShell>

## 1. Add the file

Some code.

### Check the response

## 2. Run the test

</LearnShell>`);
  assert.deepEqual(
    toc.map((item) => item.id),
    [
      'the-scenario',
      '1-add-the-file',
      'check-the-response',
      '2-run-the-test',
      'checkpoint',
      'what-you-learned',
      'keep-exploring',
    ],
  );
  assert.equal(toc.find((item) => item.id === 'check-the-response').level, 3);
});

test('ordinary reference content keeps its own outline', async () => {
  const toc = await compile('# Reference\n\n## Setup\n\n## Run');
  assert.deepEqual(
    toc.map((item) => item.id),
    ['setup', 'run'],
  );
});
