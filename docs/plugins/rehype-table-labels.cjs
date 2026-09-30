// A table cell carries its column's heading as data-label, so a narrow page can read each row as a short
// card - the heading beside each value - instead of squeezing every column into a few characters.
function text(node) {
  if (node.type === 'text') return node.value;
  return (node.children ?? []).map(text).join('');
}

function elements(node, tagName) {
  return (node.children ?? []).filter((child) => child.type === 'element' && child.tagName === tagName);
}

function label(table) {
  const head = elements(table, 'thead')[0];
  const headings = head ? elements(elements(head, 'tr')[0] ?? {}, 'th').map((cell) => text(cell).trim()) : [];
  if (!headings.some(Boolean)) return;
  for (const body of elements(table, 'tbody')) {
    for (const row of elements(body, 'tr')) {
      elements(row, 'td').forEach((cell, index) => {
        if (!headings[index]) return;
        cell.properties = {...cell.properties, dataLabel: headings[index]};
      });
    }
  }
}

module.exports = function rehypeTableLabels() {
  return function transform(tree) {
    const walk = (node) => {
      if (node.type === 'element' && node.tagName === 'table') label(node);
      (node.children ?? []).forEach(walk);
    };
    walk(tree);
  };
};
