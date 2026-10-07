/**
 * Headless tests for shared notes-editor.js (Edge/Chromium via Playwright).
 * Run: node run-tests.mjs
 */
import { chromium } from 'playwright';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';
import os from 'os';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const editorJs = fs.readFileSync(path.join(__dirname, 'notes-editor.js'), 'utf8');

function buildHarness(innerHtml) {
  const b64 = Buffer.from(innerHtml, 'utf8').toString('base64');
  return `<!DOCTYPE html>
<html><head><meta charset="utf-8"/>
<style>
ul.checkbox-list{list-style:none;padding-left:0;}
ul.checkbox-list li{display:flex;align-items:flex-start;}
ul.checkbox-list li label{flex:1;}
</style></head>
<body>
<div id="editor" contenteditable="true" data-content-base64="${b64}"></div>
<script>${editorJs}</script>
</body></html>`;
}

function assert(cond, msg) {
  if (!cond) throw new Error(msg);
}

const results = [];
function ok(name) { results.push({ name, ok: true }); console.log('PASS', name); }
function fail(name, err) { results.push({ name, ok: false, err: String(err) }); console.error('FAIL', name, err); }

async function withPage(fn) {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  try {
    const page = await browser.newPage();
    await fn(page);
  } finally {
    await browser.close();
  }
}

async function loadEditor(page, innerHtml) {
  const html = buildHarness(innerHtml);
  const tmp = path.join(os.tmpdir(), `notes-editor-test-${Date.now()}.html`);
  fs.writeFileSync(tmp, html, 'utf8');
  await page.goto('file:///' + tmp.replace(/\\/g, '/'));
  await page.waitForFunction(() => window.notesEditor && document.getElementById('editor'));
  return tmp;
}

async function testBootstrap() {
  await withPage(async (page) => {
    const tmp = await loadEditor(page, '<p>Hello</p>');
    const text = await page.evaluate(() => document.getElementById('editor').innerText.trim());
    assert(text === 'Hello', `expected Hello, got ${JSON.stringify(text)}`);
    const html = await page.evaluate(() => window.getHtml());
    assert(html.includes('Hello'), 'getHtml missing Hello');
    fs.unlinkSync(tmp);
  });
}

async function testPasteSanitize() {
  await withPage(async (page) => {
    const tmp = await loadEditor(page, '<p><br></p>');
    await page.focus('#editor');
    // Simulate paste of dirty Word-like HTML
    await page.evaluate(() => {
      const editor = document.getElementById('editor');
      const dirty = `<p style="margin:0" class="MsoNormal">Clean <b>bold</b></p>
<script>alert(1)</script>
<img src="x" onerror="alert(1)">
<a href="javascript:alert(1)">bad</a>
<a href="https://example.com">ok</a>`;
      const dt = new DataTransfer();
      dt.setData('text/html', dirty);
      editor.dispatchEvent(new ClipboardEvent('paste', { clipboardData: dt, bubbles: true, cancelable: true }));
    });
    const html = await page.evaluate(() => window.getHtml());
    assert(!html.includes('<script'), 'script leaked');
    assert(!html.includes('<img'), 'img leaked');
    assert(!html.includes('javascript:'), 'javascript href leaked');
    assert(html.includes('https://example.com'), 'safe link missing');
    assert(html.includes('bold') || html.includes('<strong>') || html.includes('<b>'), 'bold missing');
    assert(!/style\s*=/.test(html), 'inline style leaked');
    fs.unlinkSync(tmp);
  });
}

async function testPastePlain() {
  await withPage(async (page) => {
    const tmp = await loadEditor(page, '<p><br></p>');
    await page.focus('#editor');
    await page.evaluate(() => {
      const editor = document.getElementById('editor');
      const dt = new DataTransfer();
      dt.setData('text/plain', 'line1\nline2');
      editor.dispatchEvent(new ClipboardEvent('paste', { clipboardData: dt, bubbles: true, cancelable: true }));
    });
    const html = await page.evaluate(() => window.getHtml());
    assert(html.includes('line1'), 'line1 missing');
    assert(html.includes('line2'), 'line2 missing');
    fs.unlinkSync(tmp);
  });
}

async function testChecklistEnterBackspace() {
  await withPage(async (page) => {
    const start = `<ul class="checkbox-list"><li><input type="checkbox"><label>One</label></li></ul>`;
    const tmp = await loadEditor(page, start);
    // Focus end of label "One"
    await page.evaluate(() => {
      const label = document.querySelector('ul.checkbox-list li label');
      const sel = window.getSelection();
      const r = document.createRange();
      r.selectNodeContents(label);
      r.collapse(false);
      sel.removeAllRanges();
      sel.addRange(r);
      document.getElementById('editor').focus();
    });
    await page.keyboard.press('Enter');
    let count = await page.evaluate(() => document.querySelectorAll('ul.checkbox-list li').length);
    assert(count === 2, `expected 2 items after Enter, got ${count}`);

    // New item should be empty — Enter again exits list
    await page.keyboard.press('Enter');
    const afterExit = await page.evaluate(() => ({
      items: document.querySelectorAll('ul.checkbox-list li').length,
      hasP: !!document.querySelector('#editor > p, #editor p')
    }));
    assert(afterExit.items === 1, `expected 1 item after exit Enter, got ${afterExit.items}`);
    assert(afterExit.hasP, 'expected paragraph after exiting checklist');

    // Rebuild empty item and Backspace-exit
    await page.evaluate(() => {
      const editor = document.getElementById('editor');
      editor.innerHTML = `<ul class="checkbox-list"><li><input type="checkbox"><label><br></label></li></ul>`;
      const label = document.querySelector('label');
      const sel = window.getSelection();
      const r = document.createRange();
      r.selectNodeContents(label);
      r.collapse(true);
      sel.removeAllRanges();
      sel.addRange(r);
      editor.focus();
    });
    await page.keyboard.press('Backspace');
    const afterBs = await page.evaluate(() => ({
      lists: document.querySelectorAll('ul.checkbox-list').length,
      html: window.getHtml()
    }));
    assert(afterBs.lists === 0, 'checkbox list should be removed on Backspace empty');
    fs.unlinkSync(tmp);
  });
}

async function testChecklistEnterSplitsMiddle() {
  await withPage(async (page) => {
    const start = `<ul class="checkbox-list"><li><input type="checkbox"><label>Hello World Tail</label></li></ul>`;
    const tmp = await loadEditor(page, start);
    await page.evaluate(() => {
      const label = document.querySelector('label');
      const text = label.firstChild;
      const sel = window.getSelection();
      const r = document.createRange();
      // caret after "Hello " (index 6)
      r.setStart(text, 6);
      r.collapse(true);
      sel.removeAllRanges();
      sel.addRange(r);
      document.getElementById('editor').focus();
    });
    await page.keyboard.press('Enter');
    const after = await page.evaluate(() => {
      const labels = Array.from(document.querySelectorAll('ul.checkbox-list label')).map((l) => l.textContent);
      return {
        count: document.querySelectorAll('ul.checkbox-list li').length,
        labels
      };
    });
    assert(after.count === 2, `expected 2 items after mid-Enter, got ${after.count}`);
    assert(after.labels[0] === 'Hello ', `first should keep before-caret, got ${JSON.stringify(after.labels[0])}`);
    assert(after.labels[1] === 'World Tail', `second should get after-caret, got ${JSON.stringify(after.labels[1])}`);
    fs.unlinkSync(tmp);
  });
}

async function testCheckboxToggleOff() {
  await withPage(async (page) => {
    const start = `<ul class="checkbox-list">
<li><input type="checkbox"><label>Keep A</label></li>
<li><input type="checkbox"><label>Remove B</label></li>
<li><input type="checkbox"><label>Keep C</label></li>
</ul>`;
    const tmp = await loadEditor(page, start);
    await page.evaluate(() => {
      const labels = document.querySelectorAll('label');
      const label = labels[1]; // middle item
      const sel = window.getSelection();
      const r = document.createRange();
      r.selectNodeContents(label);
      r.collapse(false);
      sel.removeAllRanges();
      sel.addRange(r);
      document.getElementById('editor').focus();
      window.notesEditor.apply('checkboxList');
    });
    const after = await page.evaluate(() => {
      const checks = document.querySelectorAll('ul.checkbox-list li');
      const labels = Array.from(document.querySelectorAll('ul.checkbox-list label')).map((l) => l.textContent.trim());
      const paras = Array.from(document.querySelectorAll('#editor > p, #editor p')).map((p) => p.textContent.trim()).filter(Boolean);
      return {
        checklistCount: checks.length,
        labels,
        paras,
        hasInputOnB: Array.from(document.querySelectorAll('label')).some((l) => l.textContent.includes('Remove B') && l.parentElement && l.parentElement.querySelector('input'))
      };
    });
    assert(after.checklistCount === 2, `expected 2 checklist items left, got ${after.checklistCount}`);
    assert(after.labels.includes('Keep A') && after.labels.includes('Keep C'), 'A and C should stay checklist');
    assert(!after.hasInputOnB, 'B should no longer be a checkbox item');
    assert(after.paras.some((t) => t.includes('Remove B')), 'B text should remain as paragraph');
    fs.unlinkSync(tmp);
  });
}

async function testCheckboxMultiSelectUnwrap() {
  await withPage(async (page) => {
    const start = `<ul class="checkbox-list">
<li><input type="checkbox"><label>A</label></li>
<li><input type="checkbox"><label>B</label></li>
<li><input type="checkbox"><label>C</label></li>
</ul>`;
    const tmp = await loadEditor(page, start);
    await page.evaluate(() => {
      const labels = document.querySelectorAll('label');
      const sel = window.getSelection();
      const r = document.createRange();
      r.setStart(labels[0].firstChild, 0);
      r.setEnd(labels[1].firstChild, labels[1].firstChild.length);
      sel.removeAllRanges();
      sel.addRange(r);
      document.getElementById('editor').focus();
      window.notesEditor.apply('checkboxList');
    });
    const after = await page.evaluate(() => ({
      checklist: Array.from(document.querySelectorAll('ul.checkbox-list label')).map((l) => l.textContent.trim()),
      paras: Array.from(document.querySelectorAll('#editor p')).map((p) => p.textContent.trim()).filter(Boolean)
    }));
    assert(after.checklist.length === 1 && after.checklist[0] === 'C', `only C should remain checklist: ${JSON.stringify(after)}`);
    assert(after.paras.includes('A') && after.paras.includes('B'), 'A and B text kept');
    fs.unlinkSync(tmp);
  });
}

async function testParagraphToCheckbox() {
  await withPage(async (page) => {
    const tmp = await loadEditor(page, '<p>Solo line</p><p>Other</p>');
    await page.evaluate(() => {
      const p = document.querySelector('p');
      const sel = window.getSelection();
      const r = document.createRange();
      r.selectNodeContents(p);
      r.collapse(true);
      sel.removeAllRanges();
      sel.addRange(r);
      document.getElementById('editor').focus();
      window.notesEditor.apply('checkboxList');
    });
    const after = await page.evaluate(() => ({
      items: document.querySelectorAll('ul.checkbox-list li').length,
      labels: Array.from(document.querySelectorAll('ul.checkbox-list label')).map((l) => l.textContent.trim()),
      otherStillP: Array.from(document.querySelectorAll('#editor > p')).some((p) => p.textContent.trim() === 'Other')
    }));
    assert(after.items === 1, `expected 1 checklist item, got ${after.items}`);
    assert(after.labels[0] === 'Solo line', 'text preserved');
    assert(after.otherStillP, 'neighbor paragraph untouched');
    fs.unlinkSync(tmp);
  });
}

async function testMultiParagraphToCheckbox() {
  await withPage(async (page) => {
    const tmp = await loadEditor(page, '<p>Line1</p><p>Line2</p><p>Line3</p>');
    await page.evaluate(() => {
      const ps = document.querySelectorAll('p');
      const sel = window.getSelection();
      const r = document.createRange();
      r.setStart(ps[0].firstChild, 0);
      r.setEnd(ps[1].firstChild, ps[1].firstChild.length);
      sel.removeAllRanges();
      sel.addRange(r);
      document.getElementById('editor').focus();
      window.notesEditor.apply('checkboxList');
    });
    const after = await page.evaluate(() => ({
      items: Array.from(document.querySelectorAll('ul.checkbox-list label')).map((l) => l.textContent.trim()),
      leftover: Array.from(document.querySelectorAll('#editor > p')).map((p) => p.textContent.trim()).filter(Boolean)
    }));
    assert(after.items.length === 2, `expected 2 items, got ${after.items.length}`);
    assert(after.items.includes('Line1') && after.items.includes('Line2'), 'Line1/2 checklist');
    assert(after.leftover.includes('Line3'), 'Line3 stays paragraph');
    fs.unlinkSync(tmp);
  });
}

async function testCheckboxClickToggle() {
  await withPage(async (page) => {
    const start = `<ul class="checkbox-list"><li><input type="checkbox"><label>Task</label></li></ul>`;
    const tmp = await loadEditor(page, start);
    await page.click('ul.checkbox-list input[type=checkbox]');
    let checked = await page.evaluate(() => document.querySelector('input[type=checkbox]').checked);
    assert(checked === true, 'first click should check');
    await page.click('ul.checkbox-list input[type=checkbox]');
    checked = await page.evaluate(() => document.querySelector('input[type=checkbox]').checked);
    assert(checked === false, 'second click should uncheck');
    const text = await page.evaluate(() => document.querySelector('label').textContent.trim());
    assert(text === 'Task', 'text must remain after checkbox clicks');
    fs.unlinkSync(tmp);
  });
}

async function testCheckboxBackspaceRemoves() {
  await withPage(async (page) => {
    const start = `<ul class="checkbox-list"><li><input type="checkbox"><label>Keep</label></li></ul>`;
    const tmp = await loadEditor(page, start);
    await page.evaluate(() => {
      const label = document.querySelector('label');
      const sel = window.getSelection();
      const r = document.createRange();
      r.setStart(label.firstChild, 0);
      r.collapse(true);
      sel.removeAllRanges();
      sel.addRange(r);
      document.getElementById('editor').focus();
    });
    await page.keyboard.press('Backspace');
    const after = await page.evaluate(() => ({
      hasCheckbox: !!document.querySelector('ul.checkbox-list'),
      html: window.getHtml(),
      text: document.getElementById('editor').innerText.trim()
    }));
    assert(!after.hasCheckbox, 'checklist should be gone after Backspace at start');
    assert(after.text.includes('Keep'), 'text should remain as paragraph');
    fs.unlinkSync(tmp);
  });
}

async function testFormatApi() {
  await withPage(async (page) => {
    const tmp = await loadEditor(page, '<p>abc</p>');
    await page.evaluate(() => {
      const editor = document.getElementById('editor');
      const p = editor.querySelector('p');
      const sel = window.getSelection();
      const r = document.createRange();
      r.selectNodeContents(p);
      sel.removeAllRanges();
      sel.addRange(r);
      window.notesEditor.apply('bold');
    });
    const html = await page.evaluate(() => window.getHtml());
    assert(/<(b|strong)\b/i.test(html), `bold not applied: ${html}`);
    await page.evaluate(() => window.notesEditor.insertLink('https://protolink.ru/'));
    const withLink = await page.evaluate(() => window.getHtml());
    assert(withLink.includes('https://protolink.ru/'), 'link missing');

    // unsafe link rejected
    await page.evaluate(() => window.notesEditor.insertLink('javascript:alert(1)'));
    const noJs = await page.evaluate(() => window.getHtml());
    assert(!noJs.includes('javascript:'), 'javascript link must be rejected');

    // heading + selection state
    await page.evaluate(() => {
      const p = document.querySelector('p, h1, h2, h3') || document.getElementById('editor').firstChild;
      const sel = window.getSelection();
      const r = document.createRange();
      r.selectNodeContents(p);
      r.collapse(true);
      sel.removeAllRanges();
      sel.addRange(r);
      window.notesEditor.apply('h2');
    });
    const state = await page.evaluate(() => window.notesEditor.getState());
    assert(state.block === 'h2', `expected h2 block, got ${state.block}`);

    // italic / underline / strike toggles
    await page.evaluate(() => {
      const block = document.querySelector('h2') || document.querySelector('p');
      const sel = window.getSelection();
      const r = document.createRange();
      r.selectNodeContents(block);
      sel.removeAllRanges();
      sel.addRange(r);
      window.notesEditor.apply('italic');
      window.notesEditor.apply('underline');
      window.notesEditor.apply('strikeThrough');
    });
    const rich = await page.evaluate(() => window.getHtml());
    assert(/<(i|em)\b/i.test(rich) || /italic/i.test(rich), 'italic missing');
    assert(/<u\b/i.test(rich), 'underline missing');
    assert(/<(s|strike)\b/i.test(rich), 'strike missing');
    fs.unlinkSync(tmp);
  });
}

async function testSelectionStateCheckbox() {
  await withPage(async (page) => {
    const start = `<ul class="checkbox-list"><li><input type="checkbox"><label>X</label></li></ul><p>plain</p>`;
    const tmp = await loadEditor(page, start);
    const inCheck = await page.evaluate(() => {
      const label = document.querySelector('label');
      const sel = window.getSelection();
      const r = document.createRange();
      r.selectNodeContents(label);
      r.collapse(true);
      sel.removeAllRanges();
      sel.addRange(r);
      return window.notesEditor.getState().checkbox;
    });
    assert(inCheck === true, 'checkbox state true inside item');
    const inPlain = await page.evaluate(() => {
      const p = document.querySelector('p');
      const sel = window.getSelection();
      const r = document.createRange();
      r.selectNodeContents(p);
      r.collapse(true);
      sel.removeAllRanges();
      sel.addRange(r);
      return window.notesEditor.getState().checkbox;
    });
    assert(inPlain === false, 'checkbox state false in paragraph');
    fs.unlinkSync(tmp);
  });
}

async function testPasteWordJunk() {
  await withPage(async (page) => {
    const tmp = await loadEditor(page, '<p><br></p>');
    await page.focus('#editor');
    await page.evaluate(() => {
      const editor = document.getElementById('editor');
      const dirty = `<p class="MsoNormal">Hello <o:p></o:p></p><p style="color:red">World</p>`;
      const dt = new DataTransfer();
      dt.setData('text/html', dirty);
      editor.dispatchEvent(new ClipboardEvent('paste', { clipboardData: dt, bubbles: true, cancelable: true }));
    });
    const html = await page.evaluate(() => window.getHtml());
    assert(html.includes('Hello'), 'Hello missing');
    assert(html.includes('World'), 'World missing');
    assert(!html.includes('o:p') && !html.includes('<o:'), 'Word o:p leaked');
    assert(!/style\s*=/.test(html), 'style leaked');
    fs.unlinkSync(tmp);
  });
}

async function testCtrlOneToggle() {
  await withPage(async (page) => {
    const tmp = await loadEditor(page, '<p>Tagged</p>');
    await page.evaluate(() => {
      const p = document.querySelector('p');
      const sel = window.getSelection();
      const r = document.createRange();
      r.selectNodeContents(p);
      r.collapse(true);
      sel.removeAllRanges();
      sel.addRange(r);
      document.getElementById('editor').focus();
    });
    await page.keyboard.press('Control+1');
    let items = await page.evaluate(() => document.querySelectorAll('ul.checkbox-list li').length);
    assert(items === 1, 'Ctrl+1 should create checklist');
    await page.keyboard.press('Control+1');
    items = await page.evaluate(() => document.querySelectorAll('ul.checkbox-list li').length);
    assert(items === 0, 'Ctrl+1 again should remove checklist from line');
    fs.unlinkSync(tmp);
  });
}

async function main() {
  const tests = [
    ['bootstrap + getHtml', testBootstrap],
    ['paste sanitize', testPasteSanitize],
    ['paste plain multiline', testPastePlain],
    ['paste Word junk', testPasteWordJunk],
    ['checklist Enter/Backspace', testChecklistEnterBackspace],
    ['checklist Enter splits middle', testChecklistEnterSplitsMiddle],
    ['checkbox toolbar toggle off', testCheckboxToggleOff],
    ['checkbox multi-select unwrap', testCheckboxMultiSelectUnwrap],
    ['paragraph to checkbox', testParagraphToCheckbox],
    ['multi-paragraph to checkbox', testMultiParagraphToCheckbox],
    ['checkbox click toggle', testCheckboxClickToggle],
    ['checkbox Backspace removes', testCheckboxBackspaceRemoves],
    ['format bold/link/heading/I/U/S', testFormatApi],
    ['selectionState checkbox', testSelectionStateCheckbox],
    ['Ctrl+1 checkbox toggle', testCtrlOneToggle]
  ];
  for (const [name, fn] of tests) {
    try {
      await fn();
      ok(name);
    } catch (e) {
      fail(name, e && e.stack ? e.stack : e);
    }
  }
  const failed = results.filter((r) => !r.ok);
  console.log(`\n${results.length - failed.length}/${results.length} passed`);
  if (failed.length) process.exit(1);
}

main();
