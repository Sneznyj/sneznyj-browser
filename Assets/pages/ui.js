// Общее для внутренних страниц: связь с браузером, значки, окошки подтверждения.
'use strict';
if (top !== self) { document.documentElement.textContent = ''; throw new Error('framed'); }

const Bridge = {
  handlers: {},
  post(msg) { try { window.chrome.webview.postMessage(msg); } catch (e) { } },
  on(type, fn) { (this.handlers[type] ||= []).push(fn); },
};
try {
  window.chrome.webview.addEventListener('message', e => {
    const m = e.data;
    if (!m || typeof m.type !== 'string') return;
    if (m.type === 'theme') { const l = document.getElementById('theme'); if (l) l.href = 'theme.css?v=' + Date.now(); }
    (Bridge.handlers[m.type] || []).forEach(f => f(m));
  });
} catch (e) { }

const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));

/** Элемент без innerHTML: текст всегда через textContent, поэтому названия сайтов не могут вставить свой код. */
function el(tag, props, ...kids) {
  const e = document.createElement(tag);
  if (props) for (const [k, v] of Object.entries(props)) {
    if (v == null || v === false) continue;
    if (k === 'class') e.className = v;
    else if (k === 'text') e.textContent = v;
    else if (k === 'style') Object.assign(e.style, v);
    else if (k.startsWith('on')) e.addEventListener(k.slice(2), v);
    else if (k === 'html') e.innerHTML = v; // только для своих значков ниже
    else e.setAttribute(k, v === true ? '' : v);
  }
  for (const k of kids.flat()) if (k != null && k !== false) e.append(k.nodeType ? k : document.createTextNode(String(k)));
  return e;
}

const ICONS = {
  snow: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round"><path d="M12 2v20M3.3 7l17.4 10M3.3 17L20.7 7M12 5.5 9.8 3.4M12 5.5l2.2-2.1M12 18.5l-2.2 2.1M12 18.5l2.2 2.1"/></svg>',
  search: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="11" cy="11" r="7"/><path d="M20 20l-3.5-3.5"/></svg>',
  x: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M6 6l12 12M18 6L6 18"/></svg>',
  trash: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M4 7h16M10 11v6M14 11v6M5 7l1 13h12l1-13M9 7V4h6v3"/></svg>',
  folder: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><path d="M3 6.5A1.5 1.5 0 0 1 4.5 5H9l2 2.5h8.5A1.5 1.5 0 0 1 21 9v9.5a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 18.5z"/></svg>',
  open: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M14 4h6v6M20 4l-9 9M18 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5"/></svg>',
  edit: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M4 20h4L19 9l-4-4L4 16z"/></svg>',
  plus: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M12 5v14M5 12h14"/></svg>',
  star: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><path d="M12 3.5l2.6 5.3 5.9.9-4.3 4.1 1 5.8L12 16.9l-5.2 2.7 1-5.8-4.3-4.1 5.9-.9z"/></svg>',
  clock: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><circle cx="12" cy="12" r="8.5"/><path d="M12 7.5V12l3 2"/></svg>',
  down: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 4v11M7 10l5 5 5-5M5 20h14"/></svg>',
  gear: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/></svg>',
  palette: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7"><path d="M12 3a9 9 0 1 0 0 18c1 0 1.6-.8 1.6-1.6 0-.5-.2-.8-.4-1.1-.3-.3-.4-.7-.4-1.1 0-.9.7-1.6 1.6-1.6H16a5 5 0 0 0 5-5C21 6.4 17 3 12 3z"/><circle cx="7.5" cy="11" r="1.2" fill="currentColor"/><circle cx="10.5" cy="7.5" r="1.2" fill="currentColor"/><circle cx="15" cy="7.5" r="1.2" fill="currentColor"/></svg>',
  home: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linejoin="round"><path d="M4 11l8-7 8 7v8.5a.5.5 0 0 1-.5.5H15v-6H9v6H4.5a.5.5 0 0 1-.5-.5z"/></svg>',
  tabs: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linejoin="round"><rect x="3" y="6" width="18" height="14" rx="2"/><path d="M3 10h18M8 6V4h6v2"/></svg>',
  shield: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linejoin="round"><path d="M12 3l8 3v6c0 4.5-3.4 8-8 9-4.6-1-8-4.5-8-9V6z"/><path d="M8.5 12l2.5 2.5 4.5-5"/></svg>',
  panel: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7"><rect x="3" y="4" width="18" height="16" rx="2"/><path d="M15 4v16"/></svg>',
  info: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round"><circle cx="12" cy="12" r="9"/><path d="M12 11v6M12 7.5v.5"/></svg>',
  file: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round"><path d="M6 3h8l5 5v12a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z"/><path d="M14 3v5h5"/></svg>',
  mouse: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7"><rect x="6" y="3" width="12" height="18" rx="6"/><path d="M12 7v4"/></svg>',
  import: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3v12M7 10l5 5 5-5M4 17v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-2"/></svg>',
};
const icon = name => el('span', { html: ICONS[name] || '', style: { display: 'inline-flex' } }).firstChild;

function hostOf(url) { try { return new URL(url).hostname.replace(/^www\./, ''); } catch { return ''; } }
function isWeb(url) { return /^https?:\/\//i.test(url || ''); }
function hue(s) { let h = 0; for (const c of s || '?') h = (h * 31 + c.charCodeAt(0)) % 360; return h; }

/** Значок сайта: сохранённый favicon или цветная буква. */
function favicon(url, fav, title) {
  const box = el('span', { class: 'fav' });
  const letter = () => { box.textContent = (title || hostOf(url) || '?').trim().charAt(0).toUpperCase(); box.style.background = `hsl(${hue(hostOf(url) || title)} 65% 62%)`; };
  if (fav) {
    const img = el('img', { src: fav, alt: '' });
    img.onerror = () => { img.remove(); letter(); };
    box.append(img);
  } else letter();
  return box;
}

function toast(text) {
  $$('.toast').forEach(t => t.remove());
  const t = el('div', { class: 'toast', text });
  document.body.append(t);
  setTimeout(() => t.remove(), 2600);
}

/** Окошко подтверждения по центру. Возвращает true, если нажали главную кнопку. */
function confirmBox(title, text, okText = 'Удалить', danger = true) {
  return new Promise(resolve => {
    const done = v => { back.remove(); resolve(v); };
    const ok = el('button', { class: 'btn ' + (danger ? 'danger' : 'primary'), text: okText, onclick: () => done(true) });
    const back = el('div', { class: 'modal-back', onclick: e => { if (e.target === back) done(false); } },
      el('div', { class: 'modal' }, el('h4', { text: title }), text ? el('p', { text }) : null,
        el('div', { class: 'btns' }, el('button', { class: 'btn', text: 'Отмена', onclick: () => done(false) }), ok)));
    back.addEventListener('keydown', e => { if (e.key === 'Escape') done(false); });
    document.body.append(back);
    ok.focus();
  });
}

/** Окошко с полями. fields: [{name, label, value, type}] → объект значений или null. */
function formBox(title, fields, okText = 'Сохранить') {
  return new Promise(resolve => {
    const inputs = {};
    const done = v => { back.remove(); resolve(v); };
    const submit = e => { e?.preventDefault(); const r = {}; for (const [k, i] of Object.entries(inputs)) r[k] = i.value; done(r); };
    const form = el('form', { onsubmit: submit },
      el('h4', { text: title }),
      fields.map(f => {
        let input;
        if (f.options) {
          input = el('select', null, f.options.map(o => el('option', { value: o.value, text: o.label, selected: o.value === f.value })));
        } else input = el('input', { type: 'text', value: f.value || '', placeholder: f.placeholder || '' });
        inputs[f.name] = input;
        return el('label', { class: 'field' }, el('span', { text: f.label }), input);
      }),
      el('div', { class: 'btns' }, el('button', { type: 'button', class: 'btn', text: 'Отмена', onclick: () => done(null) }),
        el('button', { type: 'submit', class: 'btn primary', text: okText })));
    const back = el('div', { class: 'modal-back', onclick: e => { if (e.target === back) done(null); } }, el('div', { class: 'modal' }, form));
    back.addEventListener('keydown', e => { if (e.key === 'Escape') done(null); });
    document.body.append(back);
    const first = Object.values(inputs)[0];
    if (first) { first.focus(); first.select?.(); }
  });
}

function sideNav(active) {
  const links = [
    ['settings', 'Настройки', 'gear'], ['bookmarks', 'Закладки', 'star'], ['history', 'История', 'clock'], ['downloads', 'Загрузки', 'down'],
  ];
  return links.map(([page, name, ic]) => el('a', { class: 'nav' + (page === active ? ' on' : ''), href: page + '.html' }, icon(ic), name));
}

function openUrl(url, e) {
  const mode = e && (e.ctrlKey || e.button === 1) ? 'background' : e && e.shiftKey ? 'new' : 'same';
  Bridge.post({ cmd: 'open', url, mode });
}

const DAYS = ['воскресенье', 'понедельник', 'вторник', 'среда', 'четверг', 'пятница', 'суббота'];
function dayTitle(d) {
  const today = new Date(); today.setHours(0, 0, 0, 0);
  const day = new Date(d); day.setHours(0, 0, 0, 0);
  const diff = Math.round((today - day) / 86400000);
  const date = d.toLocaleDateString('ru-RU', { day: 'numeric', month: 'long', year: d.getFullYear() !== today.getFullYear() ? 'numeric' : undefined });
  if (diff === 0) return `Сегодня — ${DAYS[d.getDay()]}, ${date}`;
  if (diff === 1) return `Вчера — ${DAYS[d.getDay()]}, ${date}`;
  return `${DAYS[d.getDay()].charAt(0).toUpperCase() + DAYS[d.getDay()].slice(1)}, ${date}`;
}
