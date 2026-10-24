/* ============================================================
   site.js  v1.0
   Supplementary UI behaviour only. All navigation, rendering,
   validation and data handling are done server-side by the
   ASP.NET Web Forms pages; nothing here talks to data.
     • sidebar accordion groups (one open at a time)
     • toast auto-dismiss
     • session-expiry warning (server session = 15 minutes)
     • duplicate-submit guard + busy state while a postback (and the
       API calls behind it) is in flight
     • small helpers used by individual pages (live vitals calc,
       vital unit switch, phrase-chip insert, chip-select into hidden fields)
   ============================================================ */
(function () {
  'use strict';

  /* Duplicate-submit guard: one postback at a time. Validation failures (defaultPrevented) do not lock;
     the lock lifts after 15 s or when the page is shown again (e.g. after a file download or Back). */
  (function () {
    var busyUntil = 0;
    var lock = function (source) {
      busyUntil = Date.now() + 15000;
      document.body.classList.add('is-submitting');
      if (source && source.classList) { source.classList.add('is-busy'); source.setAttribute('aria-busy', 'true'); }
      setTimeout(unlock, 15000);
    };
    var unlock = function () {
      busyUntil = 0;
      document.body.classList.remove('is-submitting');
      document.querySelectorAll('.is-busy').forEach(function (b) { b.classList.remove('is-busy'); b.removeAttribute('aria-busy'); });
    };
    var lastClicked = null;
    document.addEventListener('click', function (e) {
      lastClicked = e.target.closest ? e.target.closest('button, input[type=submit], a') : null;
    }, true);
    document.addEventListener('submit', function (e) {
      if (e.defaultPrevented) return;
      if (Date.now() < busyUntil) { e.preventDefault(); return; }
      lock(e.submitter || lastClicked);
    });
    window.addEventListener('pageshow', unlock);
    var wrap = function () {
      if (typeof window.__doPostBack !== 'function' || window.__doPostBack.c360Guarded) return;
      var inner = window.__doPostBack;
      window.__doPostBack = function (target, arg) {
        if (Date.now() < busyUntil) return;
        lock(lastClicked);
        return inner.apply(this, arguments);
      };
      window.__doPostBack.c360Guarded = true;
    };
    wrap();
    document.addEventListener('DOMContentLoaded', wrap);
  })();

  /* Date picker: every <input type="date"> is shown as a DD-MM-YYYY text box with a popup calendar
     (month/year dropdowns, Today / Done). The original input stays in the form as a hidden field that
     still posts yyyy-MM-dd, so server code, validators, AutoPostBack and page scripts are unchanged.
     The input's min/max (default 1900–2099) limit the year list, disable days and reject typed dates. */
  (function () {
    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    var DAYS = ['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa'];
    var valueProp = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
    var pad = function (n) { return (n < 10 ? '0' : '') + n; };
    var iso = function (d) { return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()); };
    var fromIso = function (s) {
      var m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(s || '');
      if (!m) return null;
      var d = new Date(+m[1], m[2] - 1, +m[3]);
      return d.getMonth() === m[2] - 1 ? d : null;
    };
    var display = function (d) { return d ? pad(d.getDate()) + '-' + pad(d.getMonth() + 1) + '-' + d.getFullYear() : ''; };
    var fromDisplay = function (s) {
      var m = /^(\d{1,2})[-\/.](\d{1,2})[-\/.](\d{4})$/.exec((s || '').trim());
      if (!m) return null;
      var d = new Date(+m[3], m[2] - 1, +m[1]);
      return d.getDate() === +m[1] && d.getMonth() === m[2] - 1 ? d : null;
    };
    var today = function () { var t = new Date(); return new Date(t.getFullYear(), t.getMonth(), t.getDate()); };

    var popup, current;   // one shared popup; current = the field it is open for

    function Field(hidden) {
      var self = this;
      this.hidden = hidden;
      this.min = fromIso(hidden.getAttribute('min')) || new Date(1900, 0, 1);
      this.max = fromIso(hidden.getAttribute('max')) || new Date(2099, 11, 31);
      var box = this.box = document.createElement('input');
      box.type = 'text';
      box.id = hidden.id ? hidden.id + '_display' : '';
      box.className = hidden.className + ' datepicker-input';
      box.style.cssText = hidden.style.cssText;
      box.placeholder = 'DD-MM-YYYY';
      box.autocomplete = 'off';
      box.inputMode = 'numeric';
      box.maxLength = 10;
      box.disabled = hidden.disabled;
      box.readOnly = hidden.readOnly;
      ['aria-label', 'title', 'required'].forEach(function (a) { if (hidden.hasAttribute(a)) box.setAttribute(a, hidden.getAttribute(a)); });
      box.value = display(fromIso(valueProp.get.call(hidden)));
      hidden.parentNode.insertBefore(box, hidden);
      hidden.type = 'hidden';
      if (hidden.id) document.querySelectorAll('label[for="' + hidden.id + '"]').forEach(function (l) { l.htmlFor = box.id; });
      // A page script that sets input.value keeps the visible box in step.
      Object.defineProperty(hidden, 'value', {
        configurable: true,
        get: function () { return valueProp.get.call(hidden); },
        set: function (v) { valueProp.set.call(hidden, v); box.value = display(fromIso(v)); }
      });

      box.addEventListener('focus', function () { self.open(); });
      box.addEventListener('click', function () { self.open(); });
      box.addEventListener('input', function () {
        // Typing digits fills in the dashes: 23102026 → 23-10-2026.
        var digits = box.value.replace(/\D/g, '').slice(0, 8);
        box.value = digits.length > 4 ? digits.slice(0, 2) + '-' + digits.slice(2, 4) + '-' + digits.slice(4)
          : digits.length > 2 ? digits.slice(0, 2) + '-' + digits.slice(2) : digits;
        box.setCustomValidity('');
        box.classList.remove('is-invalid');
        var d = fromDisplay(box.value);
        if (d && self.allowed(d) && current === self) { self.view = new Date(d.getFullYear(), d.getMonth(), 1); render(); }
      });
      box.addEventListener('change', function () { self.commitTyped(); });
      box.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' || e.key === 'Tab') close();
        if (e.key === 'Enter') { e.preventDefault(); self.commitTyped(); close(); }
      });
    }
    Field.prototype.allowed = function (d) { return d >= this.min && d <= this.max; };
    Field.prototype.selected = function () { return fromIso(valueProp.get.call(this.hidden)); };
    Field.prototype.set = function (d) {
      var v = d ? iso(d) : '';
      this.box.value = display(d);
      this.box.setCustomValidity('');
      this.box.classList.remove('is-invalid');
      if (v === valueProp.get.call(this.hidden)) return;
      valueProp.set.call(this.hidden, v);
      // Same events the date input fired: page scripts, validators and AutoPostBack listen for them.
      this.hidden.dispatchEvent(new Event('input', { bubbles: true }));
      this.hidden.dispatchEvent(new Event('change', { bubbles: true }));
    };
    Field.prototype.commitTyped = function () {
      var text = this.box.value.trim();
      if (!text) { this.set(null); return; }
      var d = fromDisplay(text);
      var msg = !d ? 'Enter a valid date as DD-MM-YYYY.'
        : !this.allowed(d) ? 'Choose a date between ' + display(this.min) + ' and ' + display(this.max) + '.' : '';
      if (!msg) { this.set(d); return; }
      valueProp.set.call(this.hidden, '');   // never post a half-typed or out-of-range date
      this.box.setCustomValidity(msg);
      this.box.classList.add('is-invalid');
      this.box.reportValidity();
    };
    Field.prototype.open = function () {
      if (this.box.disabled || this.box.readOnly) return;
      if (current === this) return;
      current = this;
      var d = this.selected() || fromDisplay(this.box.value) || today();
      if (d < this.min) d = this.min;
      if (d > this.max) d = this.max;
      this.view = new Date(d.getFullYear(), d.getMonth(), 1);
      if (!popup) build();
      popup.hidden = false;
      render();
      place();
    };

    function close() {
      if (!popup || !current) return;
      popup.hidden = true;
      current = null;
    }

    // Inline SVG, so the arrows show even before the icon font has loaded.
    function chevron(points) {
      return '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="' + points + '"/></svg>';
    }

    function build() {
      popup = document.createElement('div');
      popup.className = 'datepicker';
      popup.setAttribute('role', 'dialog');
      popup.setAttribute('aria-label', 'Choose date');
      popup.innerHTML =
        '<div class="datepicker-head">' +
          '<button type="button" class="datepicker-nav" data-step="-1" aria-label="Previous month">' + chevron('15 6 9 12 15 18') + '</button>' +
          '<select class="datepicker-month" aria-label="Month"></select>' +
          '<select class="datepicker-year" aria-label="Year"></select>' +
          '<button type="button" class="datepicker-nav" data-step="1" aria-label="Next month">' + chevron('9 6 15 12 9 18') + '</button>' +
        '</div>' +
        '<div class="datepicker-grid" role="grid"></div>' +
        '<div class="datepicker-foot">' +
          '<button type="button" class="btn btn--sm" data-dp="today">Today</button>' +
          '<button type="button" class="btn btn--sm btn--primary" data-dp="done">Done</button>' +
        '</div>';
      document.body.appendChild(popup);
      // Keep focus in the text box so clicks on the calendar do not blur it.
      popup.addEventListener('mousedown', function (e) { if (e.target.tagName !== 'SELECT') e.preventDefault(); });
      popup.addEventListener('click', function (e) {
        if (!current) return;
        var nav = e.target.closest('[data-step]'), day = e.target.closest('[data-date]'), act = e.target.closest('[data-dp]');
        if (nav && !nav.disabled) { current.view.setMonth(current.view.getMonth() + +nav.getAttribute('data-step')); render(); }
        else if (day && !day.disabled) { current.set(fromIso(day.getAttribute('data-date'))); close(); }
        else if (act && act.getAttribute('data-dp') === 'today') {
          var t = today();
          if (current.allowed(t)) { current.set(t); close(); }
          else { current.view = new Date(t.getFullYear(), t.getMonth(), 1); render(); }
        }
        else if (act) close();
      });
      popup.addEventListener('change', function (e) {
        if (!current) return;
        if (e.target.classList.contains('datepicker-month')) current.view.setMonth(+e.target.value);
        if (e.target.classList.contains('datepicker-year')) current.view.setFullYear(+e.target.value);
        render();
      });
      document.addEventListener('mousedown', function (e) {
        if (current && !popup.contains(e.target) && e.target !== current.box) close();
      });
      window.addEventListener('resize', place);
      window.addEventListener('scroll', place, true);
    }

    function render() {
      var f = current, v = f.view, y = v.getFullYear(), m = v.getMonth();
      // Clamp the shown month into the allowed range.
      if (new Date(y, m + 1, 0) < f.min) { v.setFullYear(f.min.getFullYear(), f.min.getMonth(), 1); y = v.getFullYear(); m = v.getMonth(); }
      if (v > f.max) { v.setFullYear(f.max.getFullYear(), f.max.getMonth(), 1); y = v.getFullYear(); m = v.getMonth(); }
      var months = popup.querySelector('.datepicker-month'), years = popup.querySelector('.datepicker-year');
      months.innerHTML = MONTHS.map(function (name, i) {
        var off = new Date(y, i + 1, 0) < f.min || new Date(y, i, 1) > f.max;
        return '<option value="' + i + '"' + (i === m ? ' selected' : '') + (off ? ' disabled' : '') + '>' + name + '</option>';
      }).join('');
      var yearOpts = [];
      for (var yr = f.max.getFullYear(); yr >= f.min.getFullYear(); yr--) yearOpts.push('<option' + (yr === y ? ' selected' : '') + '>' + yr + '</option>');
      years.innerHTML = yearOpts.join('');
      popup.querySelector('[data-step="-1"]').disabled = new Date(y, m, 0) < f.min;
      popup.querySelector('[data-step="1"]').disabled = new Date(y, m + 1, 1) > f.max;

      var sel = f.selected(), now = today(), html = DAYS.map(function (d) { return '<div class="datepicker-dow">' + d + '</div>'; });
      var first = new Date(y, m, 1).getDay(), count = new Date(y, m + 1, 0).getDate();
      for (var i = 0; i < first; i++) html.push('<div></div>');
      for (var day = 1; day <= count; day++) {
        var d = new Date(y, m, day), cls = 'datepicker-day';
        if (sel && +d === +sel) cls += ' is-selected';
        if (+d === +now) cls += ' is-today';
        html.push('<button type="button" class="' + cls + '" data-date="' + iso(d) + '"' + (f.allowed(d) ? '' : ' disabled') + '>' + day + '</button>');
      }
      popup.querySelector('.datepicker-grid').innerHTML = html.join('');
    }

    function place() {
      if (!current) return;
      var r = current.box.getBoundingClientRect(), h = popup.offsetHeight, w = popup.offsetWidth;
      var top = r.bottom + 4;
      if (top + h > window.innerHeight && r.top - h - 4 > 0) top = r.top - h - 4;
      popup.style.top = top + 'px';
      popup.style.left = Math.max(8, Math.min(r.left, window.innerWidth - w - 8)) + 'px';
    }

    document.querySelectorAll('input[type=date]').forEach(function (i) { new Field(i); });
  })();

  /* Sidebar groups */
  document.addEventListener('click', function (e) {
    var header = e.target.closest('[data-action="toggle-sidebar-group"]');
    if (!header) return;
    var group = header.parentNode;
    var opening = group.querySelector('.sidebar-group-items').style.display === 'none';
    document.querySelectorAll('.sidebar-group').forEach(function (g) {
      var open = opening && g === group;
      g.querySelector('.sidebar-group-items').style.display = open ? '' : 'none';
      var chevron = g.querySelector('.sidebar-group-header i');
      if (chevron) chevron.className = 'ti ti-chevron-' + (open ? 'up' : 'down');
    });
  });

  /* Back links return to the previous page. Web Forms postbacks reload the same URL, so when the
     referrer is this page (or another site) the link's own href is followed instead. */
  document.addEventListener('click', function (e) {
    var link = e.target.closest('[data-action="go-back"]');
    if (!link || !document.referrer) return;
    var prev;
    try { prev = new URL(document.referrer); } catch (err) { return; }
    if (prev.origin !== location.origin || prev.pathname === location.pathname) return;
    e.preventDefault();
    history.back();
  });

  /* Toasts disappear after 4 seconds, or on click (anywhere, including the × close button).
     Hovering pauses the timer so a message can be read or closed. */
  document.querySelectorAll('[data-autohide]').forEach(function (t) {
    var timer;
    var hide = function () { clearTimeout(timer); t.classList.add('toast--hiding'); setTimeout(function () { t.remove(); }, 400); };
    var start = function () { clearTimeout(timer); timer = setTimeout(hide, 2000); };
    start();
    t.addEventListener('mouseenter', function () { clearTimeout(timer); });
    t.addEventListener('mouseleave', start);
    t.addEventListener('click', hide);
  });

  /* Session-expiry warning: the server session times out after 15 minutes without a request.
     The countdown restarts whenever the notification poll reaches the server ('c360:server-contact'). */
  if (document.getElementById('sidebar')) {
    var sessionTimer, sessionBanner;
    var scheduleSessionWarning = function () {
      clearTimeout(sessionTimer);
      if (sessionBanner) { sessionBanner.remove(); sessionBanner = null; }
      sessionTimer = setTimeout(function () {
        sessionBanner = document.createElement('div');
        sessionBanner.style.cssText = 'position:fixed; bottom:20px; right:20px; z-index:10000; background:#fef3c7; border:1px solid #f59e0b; border-radius:8px; padding:12px 20px; box-shadow:0 4px 12px rgba(0,0,0,0.15); font-size:13px; max-width:320px;';
        sessionBanner.innerHTML = '<div style="font-weight:700; color:#92400e; margin-bottom:4px;">⏱ Session expiring soon</div><div style="color:#78350f;">Your session will expire in 3 minutes due to inactivity. Save your work or reload the page to stay signed in.</div>';
        document.body.appendChild(sessionBanner);
      }, 12 * 60 * 1000);
    };
    scheduleSessionWarning();
    window.addEventListener('c360:server-contact', scheduleSessionWarning);
  }

  /* Notification bell: list + live polling for new notifications (e.g. lab results for the ordering doctor).
     Polls every 30 s, but only while the tab is visible and the user has been active in the last minute,
     so an idle session still expires after 15 minutes. New notifications pop up as a toast. */
  (function () {
    var widgets = document.querySelectorAll('[data-notif]');
    if (!widgets.length) return;
    var endpoint = widgets[0].getAttribute('data-endpoint');
    var known = null;
    var stopped = false;
    var lastActivity = Date.now();
    ['mousedown', 'keydown', 'scroll', 'touchstart'].forEach(function (ev) {
      document.addEventListener(ev, function () { lastActivity = Date.now(); }, { passive: true, capture: true });
    });

    var el = function (tag, cls, text) {
      var n = document.createElement(tag);
      if (cls) n.className = cls;
      if (text != null) n.textContent = text;
      return n;
    };

    var render = function (data) {
      widgets.forEach(function (w) {
        var count = w.querySelector('[data-notif-count]');
        count.textContent = data.unread > 9 ? '9+' : String(data.unread);
        count.hidden = data.unread === 0;
        var list = w.querySelector('[data-notif-list]');
        list.innerHTML = '';
        if (!data.items.length) { list.appendChild(el('div', 'notif-empty', 'No notifications yet.')); return; }
        data.items.forEach(function (n) {
          var a = el('a', 'notif-item' + (n.read ? '' : ' notif-item--unread') + (n.critical ? ' notif-item--critical' : ''));
          a.href = endpoint + '?open=' + encodeURIComponent(n.id);
          a.appendChild(el('div', 'notif-item-title', n.title));
          a.appendChild(el('div', null, n.message));
          a.appendChild(el('div', 'notif-item-time', n.ago));
          list.appendChild(a);
        });
      });
    };

    var toast = function (n) {
      var stack = document.getElementById('toastStack');
      if (!stack) return;
      var t = el('div', 'toast toast--' + (n.critical ? 'danger' : 'info'));
      t.setAttribute('role', n.critical ? 'alert' : 'status');
      var close = el('button', 'toast-close', '×');
      close.type = 'button';
      close.setAttribute('aria-label', 'Close message');
      t.appendChild(close);
      t.appendChild(el('strong', null, n.title));
      t.appendChild(el('div', 'text-xs text-secondary mt-1', n.message));
      var link = el('a', 'text-xs', 'View result →');
      link.href = endpoint + '?open=' + encodeURIComponent(n.id);
      link.style.display = 'inline-block';
      link.style.marginTop = '6px';
      t.appendChild(link);
      var hide = function () { t.classList.add('toast--hiding'); setTimeout(function () { t.remove(); }, 200); };
      close.addEventListener('click', hide);
      stack.appendChild(t);
      if (!n.critical) setTimeout(hide, 10000); /* critical results stay until closed */
    };

    var handle = function (data) {
      render(data);
      if (known) data.items.forEach(function (n) { if (!n.read && !known[n.id]) toast(n); });
      known = {};
      data.items.forEach(function (n) { known[n.id] = true; });
      window.dispatchEvent(new Event('c360:server-contact'));
    };

    var request = function (body) {
      if (stopped) return;
      var opts = { credentials: 'same-origin', headers: { 'X-Requested-With': 'fetch' } };
      if (body) {
        opts.method = 'POST';
        opts.headers['Content-Type'] = 'application/x-www-form-urlencoded';
        opts.body = body;
      }
      fetch(endpoint, opts).then(function (r) {
        if (r.status === 401) { stopped = true; return null; } /* signed out / session expired */
        return r.ok ? r.json() : null;
      }).then(function (data) { if (data) handle(data); }).catch(function () { /* offline: try again next tick */ });
    };

    request();
    setInterval(function () {
      if (document.visibilityState === 'visible' && Date.now() - lastActivity < 60 * 1000) request();
    }, 30 * 1000);

    var closeAll = function () {
      widgets.forEach(function (w) {
        w.querySelector('[data-notif-panel]').hidden = true;
        w.querySelector('[data-action="toggle-notif"]').setAttribute('aria-expanded', 'false');
      });
    };
    document.addEventListener('click', function (e) {
      var bell = e.target.closest('[data-action="toggle-notif"]');
      if (bell) {
        var panel = bell.parentNode.querySelector('[data-notif-panel]');
        var opening = panel.hidden;
        closeAll();
        if (opening) { panel.hidden = false; bell.setAttribute('aria-expanded', 'true'); request(); }
        return;
      }
      if (e.target.closest('[data-action="notif-read-all"]')) { request('action=readall'); return; }
      if (!e.target.closest('[data-notif-panel]')) closeAll();
    });
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape') closeAll(); });
  })();

  /* Phrase chips: insert the phrase text into the target textarea (no postback) */
  document.addEventListener('click', function (e) {
    var chip = e.target.closest('[data-insert-target]');
    if (!chip) return;
    e.preventDefault();
    var ta = document.getElementById(chip.getAttribute('data-insert-target'));
    if (!ta) return;
    var text = chip.getAttribute('data-insert-text');
    ta.value = ta.value ? ta.value.replace(/\s*$/, '') + ' ' + text : text;
    chip.classList.add('phrase-chip--used');
    ta.focus();
  });

  /* Chip groups that set a hidden field (e.g. prescription duration unit) */
  document.addEventListener('click', function (e) {
    var chip = e.target.closest('[data-chip-for]');
    if (!chip) return;
    e.preventDefault();
    var hidden = document.getElementById(chip.getAttribute('data-chip-for'));
    if (hidden) hidden.value = chip.getAttribute('data-val');
    chip.parentNode.querySelectorAll('[data-chip-for]').forEach(function (c) { c.classList.toggle('rx-chip--active', c === chip); });
  });

  /* Vital units (Consultation Setup). A vital input carries its unit as data-f / data-o (base = value × f + o),
     data-d (decimals) and data-fi (feet-and-inches) — on itself, or on the options of the unit select named by
     data-unit-select. Values are converted to the stored base unit on the server; this only keeps the screen right. */
  function unitOf(input) {
    var sel = input.getAttribute('data-unit-select') ? document.getElementById(input.getAttribute('data-unit-select')) : null;
    var src = sel && sel.selectedIndex >= 0 ? sel.options[sel.selectedIndex] : input;
    return { f: parseFloat(src.getAttribute('data-f')) || 1, o: parseFloat(src.getAttribute('data-o')) || 0,
             d: parseInt(src.getAttribute('data-d'), 10) || 0, fi: src.getAttribute('data-fi') === '1' };
  }
  function toBase(u, raw) {
    raw = (raw || '').trim();
    if (!raw) return NaN;
    if (u.fi) {
      var m = /^\s*(\d+(?:\.\d+)?)\s*(?:'|ft|feet)?\s*(?:[-\s]\s*)?(?:(\d+(?:\.\d+)?)\s*(?:"|in|inches)?)?\s*$/i.exec(raw);
      return m ? (parseFloat(m[1]) * 12 + (m[2] ? parseFloat(m[2]) : 0)) * 2.54 : NaN;
    }
    var v = Number(raw);
    return isNaN(v) ? NaN : v * u.f + u.o;
  }
  function fromBase(u, base) {
    if (u.fi) {
      var inches = Math.round(base / 2.54 * 2) / 2, feet = Math.floor(inches / 12);
      return feet + "'" + (Math.round((inches - feet * 12) * 10) / 10) + '"';
    }
    var p = Math.pow(10, u.d);
    return String(Math.round((base - u.o) / u.f * p) / p);
  }
  function vitalBase(input) { return input.getAttribute('data-f') || input.getAttribute('data-unit-select') ? toBase(unitOf(input), input.value) : parseFloat(input.value); }

  /* Switching a vital's unit converts what is typed (so 70 kg becomes 154.3 lb, never "70 lb") */
  document.querySelectorAll('select[data-vital-unit]').forEach(function (sel) { sel.setAttribute('data-prev', sel.selectedIndex); });
  document.addEventListener('change', function (e) {
    var sel = e.target;
    if (!sel.matches || !sel.matches('select[data-vital-unit]')) return;
    var input = document.getElementById(sel.getAttribute('data-vital-unit'));
    if (!input) return;
    var prevIndex = parseInt(sel.getAttribute('data-prev'), 10), now = sel.selectedIndex;
    if (!isNaN(prevIndex) && prevIndex !== now) {
      sel.selectedIndex = prevIndex;
      var base = toBase(unitOf(input), input.value);
      sel.selectedIndex = now;
      var u = unitOf(input);
      if (input.type === 'range') {
        if (input.getAttribute('data-min-base')) input.min = fromBase(u, parseFloat(input.getAttribute('data-min-base')));
        if (input.getAttribute('data-max-base')) input.max = fromBase(u, parseFloat(input.getAttribute('data-max-base')));
      }
      if (!isNaN(base)) input.value = fromBase(u, base);
      input.dispatchEvent(new Event('input', { bubbles: true }));
    }
    sel.setAttribute('data-prev', now);
  });

  /* Live BMI / BSA / MAP preview while typing vitals (the saved values are computed on the server) */
  /* Merge, don't replace: page controls (e.g. the Observations card) define their own DocNyle helpers before this file loads */
  window.DocNyle = Object.assign(window.DocNyle || {}, {
    liveVitals: function (ids, outId) {
      function num(id) { var el = document.getElementById(id); var v = el ? vitalBase(el) : 0; return v > 0 ? v : 0; }
      function update() {
        var h = num(ids.height), w = num(ids.weight), sys = num(ids.sys), dia = num(ids.dia), parts = [];
        if (h > 0 && w > 0) {
          var bmi = Math.round((w / ((h / 100) * (h / 100))) * 10) / 10;
          var cls = bmi < 18.5 ? 'Underweight' : bmi < 25 ? 'Normal' : bmi < 30 ? 'Overweight' : 'Obese';
          parts.push('<strong>BMI:</strong> ' + bmi + ' (' + cls + ')');
          parts.push('<strong>BSA:</strong> ' + (Math.round(Math.sqrt(h * w / 3600) * 100) / 100) + ' m²');
        }
        if (sys > 0 && dia > 0) parts.push('<strong>MAP:</strong> ' + Math.round(dia + (sys - dia) / 3) + ' mmHg');
        var out = document.getElementById(outId);
        if (out) out.innerHTML = parts.length ? parts.join(' · ') : 'BMI: — · BSA: — · MAP: —';
      }
      [ids.height, ids.weight, ids.sys, ids.dia].forEach(function (id) {
        var el = document.getElementById(id);
        if (el) el.addEventListener('input', update);
      });
    },
  });
})();
document.addEventListener('click', function (e) {
   if (e.target.closest('#sidebar .nav-item')) document.getElementById('app-shell').classList.remove('nav-open');
});