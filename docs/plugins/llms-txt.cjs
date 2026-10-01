// Writes /llms.txt (https://llmstxt.org) after the build: one link per reference page and lesson, with the
// page's own front-matter description, grouped by section in sidebar order. It reads the Markdown sources,
// so a page added, moved or reworded is in the file on the next build without a second list to keep.
const fs = require('node:fs');
const path = require('node:path');

/** Reads the simple `key: value` front matter the docs use. Quoted values lose their quotes. */
function frontMatter(text) {
  const match = /^---\r?\n([\s\S]*?)\r?\n---/.exec(text);
  const result = {};
  if (!match) return result;
  for (const line of match[1].split(/\r?\n/)) {
    const pair = /^([A-Za-z_]+):\s*(.*)$/.exec(line);
    if (!pair) continue;
    let value = pair[2].trim();
    if (/^(["']).*\1$/.test(value)) value = value.slice(1, -1).replace(/\\"/g, '"');
    result[pair[1]] = value;
  }
  return result;
}

function readJson(file) {
  return fs.existsSync(file) ? JSON.parse(fs.readFileSync(file, 'utf8')) : {};
}

/** A folder's label: its category label, else its index page's sidebar label or title, else its name. */
function folderLabel(directory, name) {
  const category = readJson(path.join(directory, '_category_.json'));
  if (category.label) return category.label;
  for (const index of ['index.md', 'index.mdx']) {
    const file = path.join(directory, index);
    if (fs.existsSync(file)) {
      const meta = frontMatter(fs.readFileSync(file, 'utf8'));
      if (meta.sidebar_label || meta.title) return meta.sidebar_label || meta.title;
    }
  }
  return name;
}

function position(value) {
  const number = Number(value);
  return Number.isFinite(number) ? number : Number.MAX_SAFE_INTEGER;
}

/** Collects one section per folder: the folder's own pages, then its subfolders as their own sections. */
function collect(root, routeBase, folder = '', label = null) {
  const directory = path.join(root, folder);
  const category = readJson(path.join(directory, '_category_.json'));
  const pages = [];
  const children = [];
  for (const entry of fs.readdirSync(directory, {withFileTypes: true})) {
    if (entry.isDirectory()) {
      const child = readJson(path.join(directory, entry.name, '_category_.json'));
      children.push({name: entry.name, position: position(child.position)});
      continue;
    }
    if (!/\.mdx?$/.test(entry.name)) continue;
    const meta = frontMatter(fs.readFileSync(path.join(directory, entry.name), 'utf8'));
    const name = entry.name.replace(/\.mdx?$/, '');
    const route = name === 'index' ? folder : path.posix.join(folder, name);
    pages.push({
      title: meta.title || name,
      description: meta.description || '',
      url: `/${routeBase}/${route ? `${route}/` : ''}`.replace(/\/+/g, '/').replace(/\/$/, ''),
      position: name === 'index' ? -1 : position(meta.sidebar_position),
    });
  }
  pages.sort((a, b) => a.position - b.position || a.title.localeCompare(b.title));
  children.sort((a, b) => a.position - b.position || a.name.localeCompare(b.name));
  const sections = [];
  if (pages.length > 0) {
    sections.push({title: label || category.label || folder, pages});
  }
  for (const child of children) {
    const childFolder = path.posix.join(folder, child.name);
    const childLabel = [label || category.label, folderLabel(path.join(root, childFolder), child.name)]
      .filter(Boolean)
      .join(' / ');
    sections.push(...collect(root, routeBase, childFolder, childLabel));
  }
  return sections;
}

/** Renders the sections as llms.txt Markdown against the site's absolute URL. */
function render({title, summary, siteUrl, sections}) {
  const lines = [`# ${title}`, '', `> ${summary}`, ''];
  for (const section of sections) {
    lines.push(`## ${section.title}`, '');
    for (const page of section.pages) {
      const url = `${siteUrl.replace(/\/$/, '')}${page.url || '/'}`;
      lines.push(`- [${page.title}](${url})${page.description ? `: ${page.description}` : ''}`);
    }
    lines.push('');
  }
  return lines.join('\n');
}

module.exports = function llmsTxt(context, options) {
  return {
    name: 'llms-txt',
    async postBuild({outDir}) {
      const sections = options.sources.flatMap((source) =>
        collect(path.join(context.siteDir, source.path), source.routeBase, '', source.label),
      );
      const text = render({title: options.title, summary: options.summary, siteUrl: context.siteConfig.url, sections});
      fs.writeFileSync(path.join(outDir, 'llms.txt'), text);
    },
  };
};

module.exports.frontMatter = frontMatter;
module.exports.collect = collect;
module.exports.render = render;
