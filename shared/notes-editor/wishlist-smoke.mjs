/**
 * Acceptance smoke on a copy of «Вишлист» HTML (non-destructive for disk).
 * Run: node wishlist-smoke.mjs
 */
import { chromium } from 'playwright';
import fs from 'fs';
import path from 'path';
import os from 'os';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const editorJs = fs.readFileSync(path.join(__dirname, 'notes-editor.js'), 'utf8');
const wishlistPath = path.join(
  process.env.LOCALAPPDATA,
  'ProtoLinkCommunicator',
  'NotesFromOneNote',
  'Вишлист',
  'index.html'
);

function assert(cond, msg) {
  if (!cond) throw new Error(msg);
}

function buildHarness(innerHtml) {
  const b64 = Buffer.from(innerHtml, 'utf8').toString('base64');
  return `<!DOCTYPE html>
<html><head><meta charset="utf-8"/>
<style>
ul.checkbox-list{list-style:none;padding-left:0;}
ul.checkbox-list li{display:flex;align-items:flex-start;}
ul.checkbox-list li input[type="checkbox"]{margin-right:8px;}
ul.checkbox-list li label{flex:1;}
ul.checkbox-list li:has(input:checked){text-decoration:line-through;opacity:0.6;}
</style></head>
<body>
<div id="editor" contenteditable="true" data-content-base64="${b64}"></div>
<script>${editorJs}</script>
</body></html>`;
}

async function main() {
  assert(fs.existsSync(wishlistPath), `missing wishlist: ${wishlistPath}`);
  const inner = fs.readFileSync(wishlistPath, 'utf8');
  assert(inner.includes('checkbox-list'), 'wishlist has no checkbox-list');

  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const page = await browser.newPage();
  const tmp = path.join(os.tmpdir(), `wishlist-smoke-${Date.now()}.html`);
  fs.writeFileSync(tmp, buildHarness(inner), 'utf8');
  await page.goto('file:///' + tmp.replace(/\\/g, '/'));
  await page.waitForFunction(() => window.notesEditor);

  const initialCount = await page.evaluate(() => document.querySelectorAll('ul.checkbox-list li').length);
  console.log('wishlist checklist items:', initialCount);
  assert(initialCount >= 2, 'expected >=2 checklist items');

  // 1) Toggle ☐ on middle item only
  await page.evaluate(() => {
    const labels = document.querySelectorAll('ul.checkbox-list label');
    const idx = Math.min(1, labels.length - 1);
    const label = labels[idx];
    const r = document.createRange();
    r.selectNodeContents(label);
    r.collapse(true);
    const sel = window.getSelection();
    sel.removeAllRanges();
    sel.addRange(r);
    document.getElementById('editor').focus();
    window.notesEditor.apply('checkboxList');
  });
  const afterOne = await page.evaluate(() => ({
    checks: document.querySelectorAll('ul.checkbox-list li').length,
    text: document.getElementById('editor').innerText
  }));
  assert(afterOne.checks === initialCount - 1, `single unwrap: ${afterOne.checks} vs ${initialCount - 1}`);
  assert(afterOne.text.length > 20, 'text still present');
  console.log('PASS single-item unwrap');

  // 2) Re-apply ☐ on a plain paragraph
  await page.evaluate(() => {
    let p = Array.from(document.querySelectorAll('#editor p')).find((el) => el.textContent.trim().length > 0);
    if (!p) {
      p = document.createElement('p');
      p.textContent = 'Smoke line';
      document.getElementById('editor').appendChild(p);
    }
    const r = document.createRange();
    r.selectNodeContents(p);
    r.collapse(true);
    const sel = window.getSelection();
    sel.removeAllRanges();
    sel.addRange(r);
    window.notesEditor.apply('checkboxList');
  });
  const afterAdd = await page.evaluate(() => document.querySelectorAll('ul.checkbox-list li').length);
  assert(afterAdd >= afterOne.checks, 'checkbox add should not shrink list');
  console.log('PASS paragraph → checkbox');

  // 3) Click square toggle
  const clicked = await page.evaluate(() => {
    const cb = document.querySelector('ul.checkbox-list input[type=checkbox]');
    if (!cb) return null;
    const before = cb.checked;
    cb.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }));
    return { before, after: cb.checked };
  });
  assert(clicked && clicked.before !== clicked.after, 'checkbox click should toggle');
  console.log('PASS checkbox square click');

  // 4) Enter on non-empty → new item
  await page.evaluate(() => {
    const label = document.querySelector('ul.checkbox-list label');
    const r = document.createRange();
    r.selectNodeContents(label);
    r.collapse(false);
    const sel = window.getSelection();
    sel.removeAllRanges();
    sel.addRange(r);
    document.getElementById('editor').focus();
  });
  const beforeEnter = await page.evaluate(() => document.querySelectorAll('ul.checkbox-list li').length);
  await page.keyboard.press('Enter');
  const afterEnter = await page.evaluate(() => document.querySelectorAll('ul.checkbox-list li').length);
  assert(afterEnter === beforeEnter + 1, `Enter should add item ${beforeEnter}→${afterEnter}`);
  console.log('PASS Enter new item');

  // 5) Enter on empty → exit
  await page.keyboard.press('Enter');
  const afterExit = await page.evaluate(() => document.querySelectorAll('ul.checkbox-list li').length);
  assert(afterExit === beforeEnter, `empty Enter should exit ${afterEnter}→${afterExit}`);
  console.log('PASS Enter empty exits');

  // 6) Paste sanitize
  await page.evaluate(() => {
    const editor = document.getElementById('editor');
    const p = document.createElement('p');
    p.innerHTML = '<br>';
    editor.appendChild(p);
    const r = document.createRange();
    r.selectNodeContents(p);
    r.collapse(true);
    const sel = window.getSelection();
    sel.removeAllRanges();
    sel.addRange(r);
    const dt = new DataTransfer();
    dt.setData('text/html', '<p style="color:red">pasted<script>x</script></p><img src=x>');
    editor.dispatchEvent(new ClipboardEvent('paste', { clipboardData: dt, bubbles: true, cancelable: true }));
  });
  const pasteHtml = await page.evaluate(() => window.getHtml());
  assert(pasteHtml.includes('pasted'), 'paste text missing');
  assert(!pasteHtml.includes('<script') && !pasteHtml.includes('<img'), 'paste sanitize failed');
  console.log('PASS paste sanitize');

  // 7) Toolbar formats: bold / italic / bullet / indent / link / heading
  await page.evaluate(() => {
    const p = Array.from(document.querySelectorAll('#editor p')).find((el) => el.textContent.includes('pasted')) ||
      document.querySelector('#editor p');
    const r = document.createRange();
    r.selectNodeContents(p);
    const sel = window.getSelection();
    sel.removeAllRanges();
    sel.addRange(r);
    window.notesEditor.apply('bold');
    window.notesEditor.apply('italic');
    window.notesEditor.insertLink('https://example.com/wishlist-smoke');
  });
  let html = await page.evaluate(() => window.getHtml());
  assert(/<(b|strong)\b/i.test(html), 'bold missing');
  assert(/<(i|em)\b/i.test(html), 'italic missing');
  assert(html.includes('https://example.com/wishlist-smoke'), 'link missing');
  console.log('PASS B/I/link');

  await page.evaluate(() => {
    const p = document.querySelector('#editor p, #editor h2') || document.getElementById('editor').firstElementChild;
    const r = document.createRange();
    r.selectNodeContents(p);
    r.collapse(true);
    const sel = window.getSelection();
    sel.removeAllRanges();
    sel.addRange(r);
    window.notesEditor.apply('h2');
    window.notesEditor.apply('indent');
  });
  const state = await page.evaluate(() => window.notesEditor.getState());
  assert(state.block === 'h2', `heading sync ${state.block}`);
  console.log('PASS heading + indent');

  // 8) selectionState checkbox flag
  await page.evaluate(() => {
    const label = document.querySelector('ul.checkbox-list label');
    if (!label) return;
    const r = document.createRange();
    r.selectNodeContents(label);
    r.collapse(true);
    const sel = window.getSelection();
    sel.removeAllRanges();
    sel.addRange(r);
  });
  const cbState = await page.evaluate(() => window.notesEditor.getState().checkbox);
  assert(cbState === true, 'checkbox active state');
  console.log('PASS selectionState.checkbox');

  await browser.close();
  fs.unlinkSync(tmp);
  // Disk wishlist untouched
  const disk = fs.readFileSync(wishlistPath, 'utf8');
  assert(disk === inner, 'disk wishlist must be unchanged');
  console.log('\nWishlist smoke: all checks passed (disk unchanged)');
}

main().catch((e) => {
  console.error('FAIL', e);
  process.exit(1);
});
