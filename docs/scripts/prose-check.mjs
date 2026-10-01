// Mechanical prose checks for the docs, from docs/WRITING.md: long sentences, em-dashes, semicolon chains, stacked
// asides, filler words, long paragraphs and likely passives. Code, front matter, imports and JSX are skipped.
// Usage: node docs/scripts/prose-check.mjs [--base <git-ref>] [files or folders...]
// Default: docs/docs and docs/learn, or with --base only the pages changed since that ref, with their word counts
// before and after. It reports; it does not fail. The judgement on each finding stays with the writer.
import {execFileSync} from 'node:child_process';
import {existsSync, readFileSync, readdirSync, statSync} from 'node:fs';
import {join, relative} from 'node:path';
import {fileURLToPath} from 'node:url';

const docs = join(fileURLToPath(import.meta.url), '..', '..');
const LONG = 25;
const TOO_LONG = 35;
// A lesson a reader finishes in about ten minutes, counting callouts and checkpoint answers but not code.
const LESSON_WORDS = 750;
const FILLER = /\b(simply|just|easily|obviously|of course)\b/gi;
// Words docs/WRITING.md replaces with one fixed term. Span is right on the
// OpenTelemetry and archive-format pages, and the Aspire dashboard is Aspire's own.
const TERMS = [
  [/\bspans?\b(?!\.json)/gi, 'operation', /opentelemetry|prototrace-archive/],
  [/\bdecorators?\b/gi, 'attribute'],
  [/\binterceptors?\b/gi, 'hook'],
  [/(?<!Aspire )\bdashboard\b/gi, 'viewer'],
  [/\bbootstrap(per)?\b/gi, 'setup class'],
];
// Abstract nouns (-tion, -ment, -ity...) stacked in a paragraph read as theory, not as something the reader saw.
// Words that name concrete things in these docs are not counted.
const NOMINAL = /\b[a-z]{3,}(?:tions?|ments?|ity|ities|ances?|ences?|ness)\b/gi;
const CONCRETE = new Set(['application', 'applications', 'operation', 'operations', 'configuration', 'assertion',
  'assertions', 'connection', 'connections', 'version', 'section', 'sections', 'function', 'notification',
  'notifications', 'integration', 'integrations', 'instance', 'instances', 'reference', 'references', 'sequence',
  'environment', 'environments', 'attachment', 'attachments', 'document', 'documents', 'action', 'question',
  'questions', 'exception', 'exceptions', 'collection', 'registration', 'registrations', 'element', 'elements',
  'argument', 'arguments', 'statement', 'experience', 'audience', 'evidence', 'reason', 'payment', 'deployment',
  'capability', 'capabilities', 'expectation', 'expectations', 'visibility', 'identity', 'identities', 'completion',
  'validation', 'isolation', 'substitution', 'substitutions', 'credentials', 'requirement', 'requirements']);
// More abstract nouns than this per sentence, over two or more sentences, flags the paragraph.
const ABSTRACT_PER_SENTENCE = 0.8;
const PASSIVE =/\b(is|are|was|were|be|been|being)\s+(\w+ed|built|written|run|kept|made|shown|given|held|set|sent|read|done|seen)\b/gi;

function files(target) {
  if (statSync(target).isFile()) return /\.mdx?$/.test(target) ? [target] : [];
  return readdirSync(target).flatMap((name) => files(join(target, name)));
}

/** Every word a reader reads: prose, headings, component text and props, without code, front matter or tags. */
export function readingWords(text) {
  const body = text
    .replace(/^---\n[\s\S]*?\n---\n/, '')
    .replace(/^(```|~~~)[\s\S]*?^\1/gm, ' ')
    .replace(/\bcode=\{`[\s\S]*?`\}/g, ' ')
    .replace(/^(import|export) [^\n]*$/gm, ' ')
    .replace(/<\/?[A-Za-z][\w.]*|\/?>/g, ' ')
    .replace(/\b\w+[:=](?=\s*["'{`[\d])/g, ' ')
    .replace(/\]\([^)]*\)/g, ']');
  return (body.match(/[A-Za-z][\w'’.-]*/g) ?? []).length;
}

/** A Learn lesson, not the Learn index or a page at the Learn root. */
export const isLesson = (path) => /[\\/]learn[\\/][^\\/]+[\\/](?!index\.mdx?$)[^\\/]+$/.test(path);

/** The prose of a page: paragraphs and list items, without code, front matter, imports, JSX or tables. */
export function prose(text) {
  const lines = text.replace(/^---\n[\s\S]*?\n---\n/, '').split('\n');
  const blocks = [];
  let current = [];
  let fence = false;
  let jsx = 0;
  let block = false;
  const flush = () => {
    if (current.length) blocks.push(current.join(' '));
    current = [];
  };
  for (const raw of lines) {
    const line = raw.trim();
    if (/^(```|~~~)/.test(line)) {
      fence = !fence;
      flush();
      continue;
    }
    if (fence) continue;
    // A multi-line export (lesson data, figure layers) is code until its closing line at the left margin.
    if (block) {
      if (/^[\]})]+;?$/.test(raw)) block = false;
      continue;
    }
    if (/^export /.test(raw) && !/;\s*$/.test(raw)) {
      block = true;
      flush();
      continue;
    }
    if (jsx > 0 || /^<[A-Z]/.test(line) || /^(import|export) /.test(line)) {
      jsx += (line.match(/<[A-Z][^/]*?(?<!\/)>/g) ?? []).length + (/^<[A-Z][^>]*$/.test(line) ? 1 : 0);
      jsx -= (line.match(/<\/[A-Z]\w*>|\/>/g) ?? []).length;
      if (jsx < 0) jsx = 0;
      flush();
      continue;
    }
    if (!line || /^(#|\||:::|<\/?\w|!\[)/.test(line)) {
      flush();
      continue;
    }
    if (/^([-*]|\d+\.)\s/.test(line)) flush();
    current.push(line.replace(/^([-*]|\d+\.)\s+/, ''));
  }
  flush();
  return blocks.map((block) =>
    block
      .replace(/`[^`]*`/g, 'CODE')
      .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
      .replace(/[*_]{1,2}([^*_]+)[*_]{1,2}/g, '$1'),
  );
}

export function sentences(block) {
  return block.split(/(?<=[.!?])\s+(?=[A-Z0-9"'(])/).filter((sentence) => /[a-z]/i.test(sentence));
}

const words = (sentence) => sentence.split(/\s+/).filter(Boolean).length;

export function check(text, path = '') {
  const result = {terms: [], sentences: 0, long: 0, tooLong: [], emDash: 0, semicolons: 0, asides: 0, filler: [], passive: 0, longParagraphs: 0, abstract: []};
  for (const block of prose(text)) {
    const list = sentences(block);
    if (list.length > 5) result.longParagraphs += 1;
    const nominals = (block.match(NOMINAL) ?? []).filter((word) => !CONCRETE.has(word.toLowerCase()));
    if (list.length >= 2 && nominals.length / list.length > ABSTRACT_PER_SENTENCE) {
      result.abstract.push(block.length > 90 ? `${block.slice(0, 87)}...` : block);
    }
    for (const sentence of list) {
      result.sentences += 1;
      const count = words(sentence);
      if (count > LONG) result.long += 1;
      if (count > TOO_LONG) result.tooLong.push(sentence.length > 140 ? `${sentence.slice(0, 137)}...` : sentence);
      result.emDash += (sentence.match(/—/g) ?? []).length;
      result.semicolons += (sentence.match(/;/g) ?? []).length;
      if ((sentence.match(/\(/g) ?? []).length >= 2) result.asides += 1;
      for (const match of sentence.matchAll(FILLER)) result.filler.push(match[0].toLowerCase());
      result.passive += (sentence.match(PASSIVE) ?? []).length;
      for (const [pattern, term, exempt] of TERMS) {
        if (exempt && exempt.test(path)) continue;
        for (const match of sentence.matchAll(pattern)) result.terms.push(`${match[0]} -> ${term}`);
      }
    }
  }
  return result;
}

/** One number to sort pages by: how much a reader has to work through, per hundred sentences. */
export function score(result) {
  if (!result.sentences) return 0;
  const weight = result.terms.length + result.long + result.tooLong.length * 2 + result.emDash * 2 + result.semicolons + result.asides + result.filler.length + result.longParagraphs * 2 + result.abstract.length * 2;
  return Math.round((weight / result.sentences) * 100);
}

const isMain = process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1];
if (isMain) {
  const targets = process.argv.slice(2);
  const at = targets.indexOf('--base');
  const base = at >= 0 ? targets.splice(at, 2)[1] : null;
  const root = join(docs, '..');
  const git = (...args) => execFileSync('git', ['-C', root, ...args], {encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore']});
  const changed = () =>
    git('diff', '--name-only', base, '--', 'docs/docs', 'docs/learn')
      .split('\n')
      .filter((name) => /\.mdx?$/.test(name) && existsSync(join(root, name)))
      .map((name) => join(root, name));
  const before = (path) => {
    try {
      return readingWords(git('show', `${base}:${relative(root, path).replaceAll('\\', '/')}`));
    } catch {
      return null;
    }
  };
  const paths = (targets.length ? targets : base ? changed() : [join(docs, 'docs'), join(docs, 'learn')]).flatMap(files);
  const rows = paths.map((path) => {
    const text = readFileSync(path, 'utf8');
    return {path: relative(root, path), result: check(text, path), words: readingWords(text), before: base ? before(path) : null};
  });
  rows.sort((left, right) => score(right.result) - score(left.result));
  const detail = targets.length > 0 || Boolean(base);
  let total = 0;
  for (const {path, result, words, before: old} of rows) {
    total += result.sentences;
    const parts = [
      old === null ? `${words} words` : `${old} -> ${words} words`,
      isLesson(path) && words > LESSON_WORDS && `over the ${LESSON_WORDS}-word lesson line (say why in the report)`,
      `${result.sentences} sentences`,
      `${result.long} over ${LONG} words`,
      result.tooLong.length && `${result.tooLong.length} over ${TOO_LONG}`,
      result.emDash && `${result.emDash} em-dashes`,
      result.semicolons && `${result.semicolons} semicolons`,
      result.asides && `${result.asides} stacked asides`,
      result.filler.length && `filler: ${[...new Set(result.filler)].join(', ')}`,
      result.longParagraphs && `${result.longParagraphs} long paragraphs`,
      result.abstract.length && `${result.abstract.length} abstract paragraphs`,
      result.passive && `${result.passive} likely passives`,
      result.terms.length && `terms: ${[...new Set(result.terms)].join(', ')}`,
    ].filter(Boolean);
    console.log(`${String(score(result)).padStart(3)}  ${path}  ${parts.join(', ')}`);
    if (detail) for (const sentence of result.tooLong) console.log(`       > ${sentence}`);
    if (detail) for (const block of result.abstract) console.log(`       ~ ${block}`);
  }
  console.log(`\n${rows.length} pages, ${total} sentences. Score: weighted issues per hundred sentences, highest first.`);
}
