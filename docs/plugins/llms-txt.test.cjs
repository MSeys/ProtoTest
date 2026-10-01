const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const {frontMatter, collect, render} = require('./llms-txt.cjs');

function tree(files) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'llms-txt-'));
  for (const [file, content] of Object.entries(files)) {
    fs.mkdirSync(path.dirname(path.join(root, file)), {recursive: true});
    fs.writeFileSync(path.join(root, file), content);
  }
  return root;
}

test('front matter reads plain and quoted values', () => {
  const meta = frontMatter('---\ntitle: Setup\ndescription: "Install it: then run it."\nsidebar_position: 2\n---\n# Body');
  assert.deepEqual(meta, {title: 'Setup', description: 'Install it: then run it.', sidebar_position: '2'});
});

test('sections follow sidebar order and index pages map to the folder', () => {
  const root = tree({
    'index.md': '---\ntitle: Overview\n---\n',
    'b/_category_.json': '{"label": "Second", "position": 2}',
    'b/page.md': '---\ntitle: B page\ndescription: About B.\n---\n',
    'a/_category_.json': '{"label": "First", "position": 1}',
    'a/two.md': '---\ntitle: Two\nsidebar_position: 2\n---\n',
    'a/one.md': '---\ntitle: One\nsidebar_position: 1\n---\n',
    'c/index.md': '---\ntitle: Third things\nsidebar_label: Third\n---\n',
  });
  const sections = collect(root, 'docs', '', 'Reference');
  assert.deepEqual(
    sections.map((section) => [section.title, section.pages.map((page) => page.url)]),
    [
      ['Reference', ['/docs']],
      ['Reference / First', ['/docs/a/one', '/docs/a/two']],
      ['Reference / Second', ['/docs/b/page']],
      ['Reference / Third', ['/docs/c']],
    ],
  );
});

test('render writes one link per page with its description', () => {
  const text = render({
    title: 'ProtoTest',
    summary: 'Integration testing for .NET.',
    siteUrl: 'https://prototest.dev/',
    sections: [{title: 'Reference', pages: [{title: 'Setup', description: 'Install it.', url: '/docs/setup'}]}],
  });
  assert.equal(
    text,
    '# ProtoTest\n\n> Integration testing for .NET.\n\n## Reference\n\n- [Setup](https://prototest.dev/docs/setup): Install it.\n',
  );
});
