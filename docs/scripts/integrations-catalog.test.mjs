// The integrations catalog feeds the integration cards and the package builder. It has to name every package
// the repository ships, and the builder may only offer packages the catalog lists.
import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync, readdirSync, existsSync} from 'node:fs';
import {dirname, join} from 'node:path';
import {fileURLToPath} from 'node:url';

const docs = join(dirname(fileURLToPath(import.meta.url)), '..');
const source = join(docs, '..', 'src');
const catalog = JSON.parse(readFileSync(join(docs, 'src', 'data', 'integrations.json'), 'utf8'));
const entries = Object.values(catalog).flat();
const listed = new Set(entries.flatMap((entry) => entry.packages));

function shipped() {
  return readdirSync(source)
    .filter((name) => name.startsWith('ProtoTest.') && existsSync(join(source, name, `${name}.csproj`)))
    .filter((name) => !/<IsPackable>\s*false/i.test(readFileSync(join(source, name, `${name}.csproj`), 'utf8')));
}

test('the catalog names every shipped package, and nothing else', () => {
  const packages = shipped();
  assert.deepEqual(packages.filter((name) => !listed.has(name)), [], 'shipped but missing from the catalog');
  assert.deepEqual([...listed].filter((name) => !packages.includes(name)), [], 'in the catalog but not shipped');
});

test('the builder offers only catalog packages, each choice once', () => {
  const choices = entries.flatMap((entry) => entry.builder ?? []);
  assert.ok(choices.length > 0);
  assert.equal(new Set(choices.map((choice) => choice.id)).size, choices.length, 'duplicate builder ids');
  for (const choice of choices) {
    assert.ok(choice.label && choice.note, `${choice.id} needs a label and a note`);
    for (const name of choice.packages) assert.ok(listed.has(name), `${choice.id} offers ${name}, which the catalog does not list`);
  }
});
