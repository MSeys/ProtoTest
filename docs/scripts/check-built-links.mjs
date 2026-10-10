import {existsSync, readFileSync, readdirSync, statSync} from 'node:fs';
import {resolve, join, relative, sep} from 'node:path';

const root = resolve(process.argv[2] ?? 'build');
const failures = [];
let links = 0;
function walk(directory) {
  for (const entry of readdirSync(directory, {withFileTypes: true})) {
    const file = join(directory, entry.name);
    if (entry.isDirectory()) walk(file);
    else if (entry.name.endsWith('.html')) {
      const html = readFileSync(file, 'utf8');
      for (const match of html.matchAll(/<a\b[^>]*\bhref=(?:"([^"<>]*)"|'([^'<>]*)'|([^\s>]+))/g)) {
        const url = new URL((match[1] ?? match[2] ?? match[3]).replaceAll('&amp;', '&'), 'https://prototest.dev/');
        if (url.origin !== 'https://prototest.dev' || !url.pathname.startsWith('/assets/files/')) continue;
        links++;
        const target = resolve(root, '.' + decodeURIComponent(url.pathname));
        if (url.pathname.endsWith('/') || !target.startsWith(root + sep) || !existsSync(target) || !statSync(target).isFile()) {
          failures.push(`${relative(root, file)}: invalid download ${url.pathname}`);
        }
      }
    }
  }
}
walk(root);
const sitemap = readFileSync(join(root, 'sitemap.xml'), 'utf8');
if (/<loc>https:\/\/prototest\.dev\/search\/?<\/loc>/.test(sitemap)) failures.push('Search must not appear in the sitemap while it has noindex.');
if (failures.length) {
  console.error(failures.join('\n'));
  process.exitCode = 1;
} else console.log(`Built-site checks passed: ${links} bundled download links; search excluded from sitemap.`);
