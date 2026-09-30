const test = require('node:test');
const assert = require('node:assert/strict');
const rehypeTableLabels = require('./rehype-table-labels.cjs');

const el = (tagName, children = [], properties = {}) => ({type: 'element', tagName, properties, children});
const t = (value) => ({type: 'text', value});

function table(headings, rows) {
  return el('table', [
    el('thead', [el('tr', headings.map((heading) => el('th', [t(heading)])))]),
    el('tbody', rows.map((row) => el('tr', row.map((cell) => el('td', [t(cell)]))))),
  ]);
}

test('labels every body cell with its column heading', () => {
  const tree = {type: 'root', children: [table(['Collector', 'What it reports'], [['OpenApi', 'the whole document']])]};
  rehypeTableLabels()(tree);
  const cells = tree.children[0].children[1].children[0].children;
  assert.deepEqual(cells.map((cell) => cell.properties.dataLabel), ['Collector', 'What it reports']);
});

test('reads a heading made of code and text, and skips an empty one', () => {
  const heading = el('tr', [el('th', []), el('th', [el('code', [t('AddClient')]), t(' overload')])]);
  const tree = {type: 'root', children: [el('table', [el('thead', [heading]), el('tbody', [el('tr', [el('td', [t('a')]), el('td', [t('b')])])])])]};
  rehypeTableLabels()(tree);
  const cells = tree.children[0].children[1].children[0].children;
  assert.equal(cells[0].properties.dataLabel, undefined);
  assert.equal(cells[1].properties.dataLabel, 'AddClient overload');
});
