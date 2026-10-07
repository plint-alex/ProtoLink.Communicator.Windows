/**
 * ProtoLink shared notes editor shell (Windows WebView2 + Android WebView).
 * Host bridge: postMessage JSON { type, ... } via chrome.webview or ProtoLinkNotes.onMessage.
 */
(function () {
  'use strict';

  var editor = document.getElementById('editor');
  if (!editor) return;

  function postToHost(payload) {
    try {
      var msg = JSON.stringify(payload);
      if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
        window.chrome.webview.postMessage(msg);
        return;
      }
      if (window.ProtoLinkNotes && typeof ProtoLinkNotes.onMessage === 'function') {
        ProtoLinkNotes.onMessage(msg);
      }
    } catch (e) { /* ignore */ }
  }

  function notifyChanged() {
    clearTimeout(window._noteT);
    window._noteT = setTimeout(function () {
      postToHost({ type: 'contentChanged' });
    }, 300);
  }

  function fireInput() {
    editor.dispatchEvent(new Event('input', { bubbles: true }));
  }

  // --- bootstrap content from base64 ---
  (function loadContent() {
    var base64 = editor.getAttribute('data-content-base64');
    if (base64) {
      try {
        var bin = atob(base64);
        var bytes = new Uint8Array(bin.length);
        for (var i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
        var html = new TextDecoder('utf-8').decode(bytes);
        var d = document.createElement('div');
        d.innerHTML = html;
        while (d.firstChild) editor.appendChild(d.firstChild);
      } catch (e) {
        editor.innerHTML = '<p><br></p>';
      }
      editor.removeAttribute('data-content-base64');
    }
    if (!editor.innerHTML.trim()) editor.innerHTML = '<p><br></p>';
    editor.querySelectorAll('ul.checkbox-list li label[for]').forEach(function (label) {
      label.removeAttribute('for');
    });
  })();

  // --- paste sanitize ---
  var BLOCK_TAGS = {
    P: 1, BR: 1, DIV: 1, H1: 1, H2: 1, H3: 1,
    UL: 1, OL: 1, LI: 1, PRE: 1, BLOCKQUOTE: 1,
    TABLE: 1, THEAD: 1, TBODY: 1, TR: 1, TH: 1, TD: 1
  };
  var INLINE_TAGS = {
    STRONG: 1, B: 1, EM: 1, I: 1, U: 1, S: 1, STRIKE: 1,
    CODE: 1, A: 1, SPAN: 1, LABEL: 1, INPUT: 1
  };

  function isSafeHref(href) {
    if (!href) return false;
    var h = String(href).trim().toLowerCase();
    return h.indexOf('http://') === 0 || h.indexOf('https://') === 0;
  }

  function sanitizeNode(node, into) {
    if (node.nodeType === Node.TEXT_NODE) {
      into.appendChild(document.createTextNode(node.nodeValue || ''));
      return;
    }
    if (node.nodeType !== Node.ELEMENT_NODE) return;

    var tag = node.tagName;
    if (tag === 'SCRIPT' || tag === 'STYLE' || tag === 'META' || tag === 'LINK' || tag === 'IFRAME' || tag === 'IMG' || tag === 'OBJECT' || tag === 'SVG') {
      return;
    }

    // Word junk
    if (tag.indexOf(':') >= 0) {
      sanitizeChildren(node, into);
      return;
    }

    if (tag === 'BR') {
      into.appendChild(document.createElement('br'));
      return;
    }

    if (tag === 'INPUT') {
      if (node.type === 'checkbox') {
        var cb = document.createElement('input');
        cb.type = 'checkbox';
        if (node.checked) cb.checked = true;
        into.appendChild(cb);
      }
      return;
    }

    if (tag === 'A') {
      var href = node.getAttribute('href') || '';
      if (!isSafeHref(href)) {
        sanitizeChildren(node, into);
        return;
      }
      var a = document.createElement('a');
      a.setAttribute('href', href);
      sanitizeChildren(node, a);
      if (!a.textContent.trim()) a.textContent = href;
      into.appendChild(a);
      return;
    }

    if (tag === 'DIV' || tag === 'SPAN' || tag === 'FONT') {
      // unwrap; DIV between blocks becomes paragraph break via trailing br if needed
      if (tag === 'DIV' && into.lastChild && into.lastChild.nodeName !== 'BR') {
        // keep block separation when unwrapping sequential divs into a fragment that will be inserted
      }
      sanitizeChildren(node, into);
      if (tag === 'DIV') into.appendChild(document.createElement('br'));
      return;
    }

    var mapTag = tag;
    if (tag === 'B') mapTag = 'STRONG';
    if (tag === 'I') mapTag = 'EM';
    if (tag === 'STRIKE') mapTag = 'S';

    if (BLOCK_TAGS[tag] || INLINE_TAGS[tag]) {
      var el = document.createElement(mapTag.toLowerCase());
      if (tag === 'UL' && node.classList && node.classList.contains('checkbox-list')) {
        el.className = 'checkbox-list';
      }
      sanitizeChildren(node, el);
      into.appendChild(el);
      return;
    }

    sanitizeChildren(node, into);
  }

  function sanitizeChildren(node, into) {
    var child = node.firstChild;
    while (child) {
      var next = child.nextSibling;
      sanitizeNode(child, into);
      child = next;
    }
  }

  function sanitizeHtml(html) {
    var src = document.createElement('div');
    src.innerHTML = html;
    var out = document.createElement('div');
    sanitizeChildren(src, out);
    // collapse excessive br from unwrapped divs
    var cleaned = out.innerHTML.replace(/(?:<br\s*\/?>\s*){3,}/gi, '<br><br>');
    return cleaned || '';
  }

  function plainToHtml(text) {
    var lines = String(text || '').replace(/\r\n/g, '\n').replace(/\r/g, '\n').split('\n');
    if (lines.length === 1) {
      return escapeText(lines[0]) || '<br>';
    }
    return lines.map(function (line) {
      return '<p>' + (escapeText(line) || '<br>') + '</p>';
    }).join('');
  }

  function escapeText(s) {
    return String(s)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;');
  }

  editor.addEventListener('paste', function (e) {
    e.preventDefault();
    var clip = e.clipboardData || window.clipboardData;
    if (!clip) return;
    var html = clip.getData('text/html');
    var plain = clip.getData('text/plain');
    var insert = '';
    if (html && html.trim()) {
      insert = sanitizeHtml(html);
    } else if (plain != null) {
      insert = plainToHtml(plain);
    }
    if (!insert) return;
    try {
      document.execCommand('insertHTML', false, insert);
    } catch (err) {
      // fallback
      var sel = window.getSelection();
      if (!sel || !sel.rangeCount) return;
      var range = sel.getRangeAt(0);
      range.deleteContents();
      var tmp = document.createElement('div');
      tmp.innerHTML = insert;
      var frag = document.createDocumentFragment();
      while (tmp.firstChild) frag.appendChild(tmp.firstChild);
      range.insertNode(frag);
    }
    fireInput();
    notifyChanged();
    scheduleSelectionState();
  });

  // --- copy (plain cleanup) ---
  editor.addEventListener('copy', function (e) {
    var sel = window.getSelection();
    if (!sel || !sel.rangeCount || sel.isCollapsed) return;
    var range = sel.getRangeAt(0);
    if (!editor.contains(range.commonAncestorContainer)) return;
    try {
      var htmlDiv = document.createElement('div');
      htmlDiv.appendChild(range.cloneContents());
      var htmlClip = htmlDiv.innerHTML;
      var host = document.createElement('div');
      host.setAttribute('aria-hidden', 'true');
      host.style.cssText = 'position:fixed;left:-10000px;top:0;width:10000px;min-height:1px;opacity:0;pointer-events:none;';
      host.appendChild(range.cloneContents());
      document.body.appendChild(host);
      var plain = (host.innerText || '').replace(/\r\n?/g, '\n');
      var prevPlain;
      do {
        prevPlain = plain;
        plain = plain.replace(/(\S)(?:\n\s*){2,}(\S)/g, '$1\n$2');
      } while (plain !== prevPlain);
      plain = plain.replace(/^\n+/, '').replace(/\n+$/, '');
      document.body.removeChild(host);
      e.clipboardData.setData('text/plain', plain);
      if (htmlClip) e.clipboardData.setData('text/html', htmlClip);
      e.preventDefault();
    } catch (err) { /* ignore */ }
  });

  // --- checklist helpers ---
  function closestCheckboxLi(node) {
    if (!node) return null;
    if (node.nodeType !== Node.ELEMENT_NODE) node = node.parentNode;
    while (node && node !== editor) {
      if (node.tagName === 'LI') {
        var ul = node.parentElement;
        if (ul && ul.tagName === 'UL' && ul.classList.contains('checkbox-list')) return node;
      }
      node = node.parentNode;
    }
    return null;
  }

  function closestCheckboxUl(node) {
    var li = closestCheckboxLi(node);
    return li ? li.parentElement : null;
  }

  function checkboxItemIsEmpty(li) {
    var label = li.querySelector('label');
    var text = label ? (label.textContent || '') : (li.textContent || '');
    return text.replace(/\u200b/g, '').trim() === '';
  }

  function isCaretAtStartOfLabel(li, sel) {
    if (!sel || !sel.isCollapsed || !sel.rangeCount) return false;
    var label = li.querySelector('label');
    if (!label) return false;
    var range = sel.getRangeAt(0);
    if (!label.contains(range.startContainer) && range.startContainer !== label) return false;
    try {
      var pre = document.createRange();
      pre.selectNodeContents(label);
      pre.setEnd(range.startContainer, range.startOffset);
      return pre.toString().replace(/\u200b/g, '').length === 0;
    } catch (e) {
      return false;
    }
  }

  function focusLabelEnd(label) {
    var sel = window.getSelection();
    var r = document.createRange();
    r.selectNodeContents(label);
    r.collapse(false);
    sel.removeAllRanges();
    sel.addRange(r);
  }

  function focusLabelStart(label) {
    var sel = window.getSelection();
    var r = document.createRange();
    r.selectNodeContents(label);
    r.collapse(true);
    sel.removeAllRanges();
    sel.addRange(r);
  }

  function labelEffectivelyEmpty(label) {
    if (!label) return true;
    return (label.textContent || '').replace(/\u200b/g, '').trim() === '';
  }

  /**
   * Cut HTML after the caret inside a checklist label (selection deleted first).
   * Leaves the before-caret part in the label; returns HTML for the new item.
   */
  function extractLabelAfterCaret(label, sel) {
    if (!label || !sel || !sel.rangeCount) return '';
    var range = sel.getRangeAt(0);
    if (!label.contains(range.startContainer) && range.startContainer !== label) return '';

    if (!sel.isCollapsed) {
      range.deleteContents();
      range = sel.getRangeAt(0);
    }

    var after = range.cloneRange();
    after.setStart(range.startContainer, range.startOffset);
    after.setEndAfter(label.lastChild || label);
    // Prefer end of label contents
    try {
      after.setEnd(label, label.childNodes.length);
    } catch (e) { /* keep setEndAfter */ }

    var frag = after.extractContents();
    var tmp = document.createElement('div');
    tmp.appendChild(frag);
    if (labelEffectivelyEmpty(label)) label.innerHTML = '<br>';
    return tmp.innerHTML;
  }

  function makeCheckboxLi(text) {
    var li = document.createElement('li');
    var cb = document.createElement('input');
    cb.type = 'checkbox';
    var label = document.createElement('label');
    if (text && String(text).trim()) label.textContent = String(text).trim();
    else label.innerHTML = '<br>';
    li.appendChild(cb);
    li.appendChild(label);
    return li;
  }

  function makeCheckboxLiFromHtml(html) {
    var li = makeCheckboxLi('');
    var label = li.querySelector('label');
    var cleaned = String(html || '').replace(/\u200b/g, '');
    if (cleaned && cleaned.replace(/<br\s*\/?>/gi, '').trim()) {
      label.innerHTML = html;
    } else {
      label.innerHTML = '<br>';
    }
    return li;
  }

  /** Turn checkbox-list into a normal bullet list (same items, no inputs). */
  function unwrapCheckboxList(ul) {
    if (!ul || !ul.classList.contains('checkbox-list')) return;
    ul.classList.remove('checkbox-list');
    Array.prototype.forEach.call(ul.children, function (li) {
      if (!li || li.tagName !== 'LI') return;
      var label = li.querySelector('label');
      var text = label ? (label.textContent || '') : (li.textContent || '');
      text = text.replace(/\u200b/g, '');
      li.innerHTML = '';
      if (text.trim()) li.appendChild(document.createTextNode(text));
      else li.innerHTML = '<br>';
    });
    var sel = window.getSelection();
    var first = ul.querySelector('li');
    if (first && sel) {
      var r = document.createRange();
      r.selectNodeContents(first);
      r.collapse(true);
      sel.removeAllRanges();
      sel.addRange(r);
    }
    fireInput();
    notifyChanged();
    scheduleSelectionState();
  }

  /** Lift one checkbox item out as a paragraph (Backspace at start / empty Enter). */
  function exitCheckboxList(li) {
    var ul = li.parentElement;
    if (!ul) return;
    var label = li.querySelector('label');
    var html = label ? (label.innerHTML || '<br>') : '<br>';
    if (checkboxItemIsEmpty(li)) html = '<br>';
    var p = document.createElement('p');
    p.innerHTML = html;
    var parent = ul.parentNode;

    if (ul.children.length <= 1) {
      parent.insertBefore(p, ul);
      parent.removeChild(ul);
    } else {
      var after = document.createElement('ul');
      after.className = 'checkbox-list';
      while (li.nextSibling) after.appendChild(li.nextSibling);
      parent.insertBefore(p, ul.nextSibling);
      if (after.children.length) parent.insertBefore(after, p.nextSibling);
      ul.removeChild(li);
      if (!ul.children.length) parent.removeChild(ul);
    }

    var sel = window.getSelection();
    var r = document.createRange();
    r.selectNodeContents(p);
    r.collapse(true);
    sel.removeAllRanges();
    sel.addRange(r);
    fireInput();
    notifyChanged();
    scheduleSelectionState();
  }

  function insertCheckboxItemAfter(li) {
    var label = li.querySelector('label');
    var sel = window.getSelection();
    var afterHtml = '';
    if (label && sel && sel.rangeCount) {
      afterHtml = extractLabelAfterCaret(label, sel);
    }
    var fresh = makeCheckboxLiFromHtml(afterHtml);
    if (li.nextSibling) li.parentNode.insertBefore(fresh, li.nextSibling);
    else li.parentNode.appendChild(fresh);
    // Caret at start of the moved text (or empty new item) so typing continues there.
    focusLabelStart(fresh.querySelector('label'));
    fireInput();
    notifyChanged();
    scheduleSelectionState();
  }

  /** Turn one checkbox item into a normal paragraph (keep text). */
  function unwrapCheckboxItem(li) {
    exitCheckboxList(li);
  }

  function isFormatBlock(el) {
    if (!el || el.nodeType !== Node.ELEMENT_NODE) return false;
    var t = el.tagName;
    return t === 'P' || t === 'DIV' || t === 'H1' || t === 'H2' || t === 'H3' ||
      t === 'LI' || t === 'PRE' || t === 'BLOCKQUOTE';
  }

  function findFormatBlock(node) {
    while (node && node !== editor) {
      if (isFormatBlock(node)) return node;
      node = node.parentNode;
    }
    return null;
  }

  function blockPlainText(block) {
    if (!block) return '';
    if (block.tagName === 'LI') {
      var label = block.querySelector('label');
      if (label) return (label.textContent || '').replace(/\u200b/g, '').trim();
    }
    return (block.textContent || '').replace(/\u200b/g, '').trim();
  }

  function cleanupEmptyList(ul) {
    if (!ul || !ul.parentNode) return;
    if (ul.tagName !== 'UL' && ul.tagName !== 'OL') return;
    if (!ul.children.length) ul.parentNode.removeChild(ul);
  }

  /** Convert several selected plain blocks into one checkbox list. */
  function convertSelectedBlocksToCheckbox() {
    var sel = window.getSelection();
    if (!sel || !sel.rangeCount || sel.isCollapsed) return false;
    var range = sel.getRangeAt(0);
    var startBlock = findFormatBlock(range.startContainer);
    var endBlock = findFormatBlock(range.endContainer);
    if (!startBlock || !endBlock || startBlock === endBlock) return false;
    if (closestCheckboxLi(startBlock) || closestCheckboxLi(endBlock)) return false;
    if (startBlock.parentNode !== endBlock.parentNode) return false;

    var parent = startBlock.parentNode;
    var blocks = [];
    var collecting = false;
    for (var child = parent.firstChild; child; child = child.nextSibling) {
      if (child === startBlock) collecting = true;
      if (collecting && isFormatBlock(child) && !closestCheckboxLi(child)) blocks.push(child);
      if (child === endBlock) break;
    }
    if (blocks.length < 2) return false;

    var checkUl = document.createElement('ul');
    checkUl.className = 'checkbox-list';
    var firstParentList = null;
    blocks.forEach(function (block) {
      checkUl.appendChild(makeCheckboxLi(blockPlainText(block) || 'Item'));
      if ((block.tagName === 'LI') && block.parentElement &&
          (block.parentElement.tagName === 'UL' || block.parentElement.tagName === 'OL')) {
        firstParentList = firstParentList || block.parentElement;
      }
    });

    parent.insertBefore(checkUl, blocks[0]);
    blocks.forEach(function (block) {
      var listParent = block.parentNode;
      listParent.removeChild(block);
      cleanupEmptyList(listParent);
    });
    focusLabelEnd(checkUl.querySelector('label'));
    fireInput();
    notifyChanged();
    scheduleSelectionState();
    return true;
  }

  /** Convert a single plain list item / paragraph into one checklist row. */
  function convertBlockToCheckbox() {
    var sel = window.getSelection();
    if (!sel || !sel.rangeCount) return false;
    var node = sel.getRangeAt(0).startContainer;
    if (node.nodeType !== Node.ELEMENT_NODE) node = node.parentNode;

    // Already a checklist item — caller handles.
    if (closestCheckboxLi(node)) return false;

    // Inside plain UL/OL → only this LI becomes a checklist (split list).
    var li = node;
    while (li && li !== editor && li.tagName !== 'LI') li = li.parentNode;
    if (li && li.tagName === 'LI') {
      var ul = li.parentElement;
      if (ul && (ul.tagName === 'UL' || ul.tagName === 'OL') && editor.contains(ul)) {
        var text = blockPlainText(li);
        var parent = ul.parentNode;
        var before = document.createElement(ul.tagName.toLowerCase());
        while (ul.firstChild && ul.firstChild !== li) before.appendChild(ul.firstChild);
        var checkUl = document.createElement('ul');
        checkUl.className = 'checkbox-list';
        checkUl.appendChild(makeCheckboxLi(text || 'Item'));
        ul.removeChild(li);
        if (before.children.length) parent.insertBefore(before, ul);
        parent.insertBefore(checkUl, ul);
        cleanupEmptyList(ul);
        focusLabelEnd(checkUl.querySelector('label'));
        fireInput();
        notifyChanged();
        scheduleSelectionState();
        return true;
      }
    }

    // Inside a paragraph / heading → wrap that block as one checklist item.
    var block = findFormatBlock(node);
    if (block && block.tagName !== 'LI' && block !== editor && editor.contains(block)) {
      var bText = blockPlainText(block);
      var checkUl2 = document.createElement('ul');
      checkUl2.className = 'checkbox-list';
      checkUl2.appendChild(makeCheckboxLi(bText || 'Item'));
      block.parentNode.insertBefore(checkUl2, block);
      block.parentNode.removeChild(block);
      focusLabelEnd(checkUl2.querySelector('label'));
      fireInput();
      notifyChanged();
      scheduleSelectionState();
      return true;
    }
    return false;
  }

  /**
   * Toggle checklist for the current line / selection only (OneNote-like Ctrl+1).
   * Does NOT clear the entire list when the caret is on one item.
   */
  function toggleCheckboxList() {
    editor.focus();
    var sel = window.getSelection();
    if (!sel) return;

    if (sel.rangeCount > 0) {
      var startLi = closestCheckboxLi(sel.getRangeAt(0).startContainer);
      var endLi = closestCheckboxLi(sel.getRangeAt(0).endContainer);

      // Selection across multiple checklist items → unwrap each selected item.
      if (startLi && endLi && startLi !== endLi) {
        var ul = startLi.parentElement;
        if (ul && endLi.parentElement === ul) {
          var items = Array.prototype.slice.call(ul.children);
          var a = items.indexOf(startLi);
          var b = items.indexOf(endLi);
          if (a > b) { var tmp = a; a = b; b = tmp; }
          // Unwrap from the end so indices stay valid via DOM moves.
          for (var i = b; i >= a; i--) {
            if (items[i] && items[i].tagName === 'LI') unwrapCheckboxItem(items[i]);
          }
          return;
        }
      }

      // Caret / selection in one checklist item → remove checkbox from that item only.
      if (startLi) {
        unwrapCheckboxItem(startLi);
        return;
      }

      // Multi-line plain selection → each block becomes a checklist item.
      if (convertSelectedBlocksToCheckbox()) return;

      // Plain list item or paragraph → add checkbox to that block only.
      if (convertBlockToCheckbox()) return;
    }

    // Fallback: insert a new single checklist item at caret.
    var range;
    if (sel.rangeCount > 0) range = sel.getRangeAt(0);
    else {
      range = document.createRange();
      range.selectNodeContents(editor);
      range.collapse(false);
    }
    var newUl = document.createElement('ul');
    newUl.className = 'checkbox-list';
    var selectedText = sel.toString().trim();
    if (selectedText) {
      selectedText.split(/\r?\n/).forEach(function (line) {
        if (line.trim()) newUl.appendChild(makeCheckboxLi(line.trim()));
      });
    }
    if (newUl.children.length === 0) newUl.appendChild(makeCheckboxLi('Item'));
    range.deleteContents();
    range.insertNode(newUl);
    var firstLabel = newUl.querySelector('label');
    if (firstLabel) focusLabelEnd(firstLabel);
    fireInput();
    notifyChanged();
    scheduleSelectionState();
  }

  function handleChecklistKeydown(e) {
    if (e.key !== 'Enter' && e.key !== 'Backspace') return false;
    var sel = window.getSelection();
    if (!sel || !sel.rangeCount) return false;
    var li = closestCheckboxLi(sel.getRangeAt(0).startContainer);
    if (!li) return false;

    if (e.key === 'Enter') {
      e.preventDefault();
      if (checkboxItemIsEmpty(li)) exitCheckboxList(li);
      else insertCheckboxItemAfter(li);
      return true;
    }

    // Backspace on empty item OR at start of item → leave checklist (remove checkbox for this line)
    if (e.key === 'Backspace' && sel.isCollapsed && (checkboxItemIsEmpty(li) || isCaretAtStartOfLabel(li, sel))) {
      e.preventDefault();
      exitCheckboxList(li);
      return true;
    }
    return false;
  }

  // --- table tab / indent ---
  function handleTabKey(e) {
    if (e.key !== 'Tab') return false;
    var sel = window.getSelection();
    if (!sel || !sel.rangeCount) return false;
    var node = sel.getRangeAt(0).startContainer;
    if (node.nodeType !== Node.ELEMENT_NODE) node = node.parentNode;
    var cell = node && node.closest ? node.closest('td,th') : null;
    if (cell && editor.contains(cell)) {
      e.preventDefault();
      var cells = Array.prototype.slice.call(cell.closest('table').querySelectorAll('td,th'));
      var next = cells[cells.indexOf(cell) + (e.shiftKey ? -1 : 1)];
      if (!next && !e.shiftKey) {
        var row = cell.closest('tr');
        var fresh = document.createElement('tr');
        for (var i = 0; i < row.cells.length; i++) {
          var c = document.createElement('td');
          c.innerHTML = '<br>';
          fresh.appendChild(c);
        }
        row.parentNode.insertBefore(fresh, row.nextSibling);
        next = fresh.cells[0];
        fireInput();
        notifyChanged();
      }
      if (!next) return true;
      var r = document.createRange();
      r.selectNodeContents(next);
      r.collapse(true);
      sel.removeAllRanges();
      sel.addRange(r);
      return true;
    }

    // Outside table: indent / outdent
    e.preventDefault();
    document.execCommand(e.shiftKey ? 'outdent' : 'indent', false, null);
    fireInput();
    notifyChanged();
    scheduleSelectionState();
    return true;
  }

  function applyCommand(cmd) {
    editor.focus();
    document.execCommand(cmd, false, null);
    fireInput();
    notifyChanged();
    scheduleSelectionState();
  }

  function applyIndent(outdent) {
    editor.focus();
    document.execCommand(outdent ? 'outdent' : 'indent', false, null);
    fireInput();
    notifyChanged();
    scheduleSelectionState();
  }

  function applyFormatBlock(tag) {
    editor.focus();
    var t = tag || 'p';
    document.execCommand('formatBlock', false, '<' + t + '>');
    fireInput();
    notifyChanged();
    scheduleSelectionState();
  }

  function insertLink(url) {
    if (!isSafeHref(url)) return;
    editor.focus();
    var sel = window.getSelection();
    var safe = String(url).replace(/\\/g, '\\\\').replace(/'/g, "\\'");
    if (sel && sel.rangeCount > 0 && !sel.isCollapsed) {
      document.execCommand('createLink', false, url);
    } else {
      var link = document.createElement('a');
      link.href = url;
      link.textContent = url;
      var range = sel && sel.rangeCount > 0 ? sel.getRangeAt(0) : document.createRange();
      if (!sel || !sel.rangeCount) {
        range.selectNodeContents(editor);
        range.collapse(false);
      }
      range.deleteContents();
      range.insertNode(link);
      range.setStartAfter(link);
      range.collapse(true);
      sel.removeAllRanges();
      sel.addRange(range);
    }
    fireInput();
    notifyChanged();
    scheduleSelectionState();
  }

  // --- selection state ---
  function currentBlockTag() {
    var sel = window.getSelection();
    if (!sel || !sel.rangeCount) return 'p';
    var node = sel.getRangeAt(0).startContainer;
    if (node.nodeType !== Node.ELEMENT_NODE) node = node.parentNode;
    while (node && node !== editor) {
      var t = node.tagName && node.tagName.toLowerCase();
      if (t === 'h1' || t === 'h2' || t === 'h3' || t === 'pre' || t === 'blockquote' || t === 'p') return t;
      node = node.parentNode;
    }
    return 'p';
  }

  function collectSelectionState() {
    var bold = false, italic = false, underline = false, strike = false;
    try {
      bold = document.queryCommandState('bold');
      italic = document.queryCommandState('italic');
      underline = document.queryCommandState('underline');
      strike = document.queryCommandState('strikeThrough');
    } catch (e) { /* ignore */ }
    var checkbox = false;
    try {
      var sel = window.getSelection();
      if (sel && sel.rangeCount) checkbox = !!closestCheckboxLi(sel.getRangeAt(0).startContainer);
    } catch (e2) { /* ignore */ }
    return {
      type: 'selectionState',
      bold: !!bold,
      italic: !!italic,
      underline: !!underline,
      strike: !!strike,
      checkbox: !!checkbox,
      block: currentBlockTag()
    };
  }

  var _selTimer = null;
  function scheduleSelectionState() {
    clearTimeout(_selTimer);
    _selTimer = setTimeout(function () {
      postToHost(collectSelectionState());
    }, 80);
  }

  // --- keydown ---
  editor.addEventListener('keydown', function (e) {
    if (handleChecklistKeydown(e)) return;
    if (handleTabKey(e)) return;

    var mod = e.ctrlKey || e.metaKey;
    if (!mod) return;

    var key = e.key.toLowerCase();
    if (key === 'b') {
      e.preventDefault();
      applyCommand('bold');
    } else if (key === 'i') {
      e.preventDefault();
      applyCommand('italic');
    } else if (key === 'u') {
      e.preventDefault();
      applyCommand('underline');
    } else if (key === 'k') {
      e.preventDefault();
      postToHost({ type: 'requestLink' });
    } else if (key === '1' && !e.shiftKey && !e.altKey) {
      // OneNote-like To Do tag toggle
      e.preventDefault();
      toggleCheckboxList();
    }
  });

  document.addEventListener('keydown', function (e) {
    if (e.key === 'Control' || e.key === 'Meta') document.body.dataset.ctrlKey = 'true';
  });
  document.addEventListener('keyup', function (e) {
    if (e.key === 'Control' || e.key === 'Meta') document.body.dataset.ctrlKey = 'false';
  });

  // contenteditable often eats / double-fires checkbox clicks — toggle once on mousedown
  editor.addEventListener('mousedown', function (e) {
    var t = e.target;
    if (t && t.tagName === 'INPUT' && t.type === 'checkbox' && editor.contains(t)) {
      e.preventDefault();
      e.stopPropagation();
      t.checked = !t.checked;
      fireInput();
      notifyChanged();
      scheduleSelectionState();
    }
  }, true);

  editor.addEventListener('click', function (e) {
    var t = e.target;
    if (t && t.tagName === 'INPUT' && t.type === 'checkbox' && editor.contains(t)) {
      e.preventDefault();
      e.stopPropagation();
    }
  }, true);

  document.addEventListener('click', function (e) {
    var a = e.target && e.target.closest ? e.target.closest('a') : null;
    if (e.ctrlKey && a && a.href && isSafeHref(a.href)) {
      e.preventDefault();
      e.stopPropagation();
      postToHost({ type: 'openLink', url: a.href });
    }
  }, true);

  editor.addEventListener('input', function () {
    notifyChanged();
    scheduleSelectionState();
  });
  document.addEventListener('selectionchange', function () {
    if (!editor.contains(document.activeElement) && document.activeElement !== editor) {
      // still report if selection is inside editor
      var sel = window.getSelection();
      if (!sel || !sel.rangeCount) return;
      if (!editor.contains(sel.anchorNode)) return;
    }
    scheduleSelectionState();
  });
  editor.addEventListener('keyup', scheduleSelectionState);
  editor.addEventListener('mouseup', scheduleSelectionState);

  window.getHtml = function () {
    return editor.innerHTML || '';
  };

  window.notesEditor = {
    apply: function (cmd) {
      if (cmd === 'indent') return applyIndent(false);
      if (cmd === 'outdent') return applyIndent(true);
      if (cmd === 'h1' || cmd === 'h2' || cmd === 'h3' || cmd === 'p') return applyFormatBlock(cmd === 'p' ? 'p' : cmd);
      if (cmd === 'checkboxList') return toggleCheckboxList();
      applyCommand(cmd);
    },
    insertLink: insertLink,
    getState: collectSelectionState,
    focus: function () { editor.focus(); }
  };

  scheduleSelectionState();
})();
