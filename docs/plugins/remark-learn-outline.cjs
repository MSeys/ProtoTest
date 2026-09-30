const sections = require('../src/data/lessonSections.json');

// LearnShell renders these sections around the Markdown steps. Extend the generated outline at build time.
module.exports = function remarkLearnOutline() {
  return async function transform(tree) {
    function hasShell(node) {
      return (
        (node.type === 'mdxJsxFlowElement' && node.name === 'LearnShell') ||
        (node.children ?? []).some(hasShell)
      );
    }
    if (!hasShell(tree)) return;
    const {valueToEstree} = await import('estree-util-value-to-estree');
    const declaration = tree.children
      .flatMap((node) => node.data?.estree?.body ?? [])
      .find(
        (node) =>
          node.type === 'ExportNamedDeclaration' && node.declaration?.declarations?.[0]?.id?.name === 'toc',
      );
    if (!declaration) throw new Error('Learn outline requires the Docusaurus TOC export.');
    const items = declaration.declaration.declarations[0].init.elements;
    items.unshift(valueToEstree(sections[0]));
    items.push(...sections.slice(1).map(valueToEstree));
  };
};
