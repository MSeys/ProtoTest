import assert from 'node:assert/strict';
import {createRequire} from 'node:module';

const require = createRequire(new URL('../../viewer/package.json', import.meta.url));
const puppeteer = require('puppeteer-core');
const url = process.argv[2];
assert.ok(url, 'Pass the URL of the built ProtoTrace docs page.');
const browser = await puppeteer.launch({
  executablePath: 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  headless: true,
});
try {
  const page = await browser.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  const screens = {
    Run: ['Assertion', 'Runner failure', 'What this run could see'],
    Steps: ['Assertion', '1.01 s with no recorded operation', 'Teardown'],
    Timeline: ['Operation excerpt', 'Assert response shape', '1.01 s with no recorded operation'],
    State: ['invoice.issue', 'Invoice INV-202610-0001', 'Messaging consumer Default'],
    Evidence: ['scenario.started', '597539000012-rest-01-response', 'Assert response shape'],
    Check: ['Parts of this operation', 'FailureDrills.cs:36', 'Validated document'],
  };
  for (const width of [1440, 1200, 375]) {
    await page.setViewport({width, height: 900});
    await page.goto(url, {waitUntil: 'networkidle0'});
    await page.waitForSelector('[aria-label="Views of the demo trace"]');
    for (const [label, expected] of Object.entries(screens)) {
      const content = await page.evaluate(label => {
        const tabs = document.querySelector('[aria-label="Views of the demo trace"]');
        const button = [...tabs.querySelectorAll('button')].find(button => button.textContent === label);
        button.click();
        return button.getAttribute('aria-controls');
      }, label);
      await page.waitForFunction((id, label) => document.getElementById(id)?.getAttribute('aria-labelledby')?.endsWith(`-${label.toLowerCase()}`), {}, content, label);
      const text = await page.$eval(`[id="${content}"]`, node => node.textContent);
      for (const value of expected) {
        if (value === 'Parts of this operation') {
          assert.ok(await page.$('[aria-label="Parts of this operation"]'));
        } else assert.ok(text.includes(value), `${width}px ${label}: missing ${value}`);
      }
      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${width}px ${label}: horizontal overflow`);
    }
    await page.focus('[aria-label="Views of the demo trace"] [aria-selected="true"]');
    await page.keyboard.press('Home');
    await page.waitForFunction(() => document.activeElement?.textContent === 'Run');
    await page.keyboard.press('ArrowRight');
    await page.waitForFunction(() => document.activeElement?.textContent === 'Steps' && document.activeElement.getAttribute('aria-selected') === 'true');
    await page.keyboard.press('End');
    await page.waitForFunction(() => document.activeElement?.textContent === 'Check');
    await page.click('[aria-label="Parts of this operation"] a:first-child');
    assert.ok(await page.evaluate(() => location.hash.endsWith('-comparison')));
    await page.click('[aria-label="Parts of this operation"] a:last-child');
    assert.ok(await page.$eval('details[id$="-attributes"]', node => node.open));
    console.log(`walkthrough ${width}px: six views, keyboard navigation, section index and no horizontal overflow PASS`);
  }
  assert.deepEqual(errors, []);
} finally {
  await browser.close();
}
