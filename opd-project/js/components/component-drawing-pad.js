/* ============================================================
   components/component-drawing-pad.js  v1.0
   Stylus & finger drawing pad using PointerEvents.
   Supports pressure sensitivity, undo, clear, save as PNG.
   Stores strokes as vector JSON for replay and Base64 PNG for display.
   Phase 4 destination: Components/DrawingPad.razor (JS interop)
   ============================================================ */

(function () {
  'use strict';

  /**
   * Create a drawing pad inside a container element.
   * @param {Object} options
   * @param {HTMLElement} options.container — where to mount
   * @param {number} options.width — canvas width (default 600)
   * @param {number} options.height — canvas height (default 300)
   * @param {Function} options.onSave — callback({png, strokes})
   * @returns {Object} { destroy }
   */
  function create(options) {
    const { container, width = 600, height = 300, onSave } = options;

    const wrapper = document.createElement('div');
    wrapper.style.cssText = 'border:1px solid var(--border-default); border-radius:8px; overflow:hidden; background:#fff; display:inline-block;';

    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    canvas.style.cssText = `display:block; cursor:crosshair; touch-action:none; width:${width}px; height:${height}px;`;

    const toolbar = document.createElement('div');
    toolbar.style.cssText = 'display:flex; flex-wrap:wrap; gap:6px; padding:6px; background:var(--surface-page); border-top:1px solid var(--border-default);';
    toolbar.innerHTML = `
      <button class="btn btn--xs btn--ghost" data-pad-action="undo" type="button"><i class="ti ti-arrow-back-up"></i> Undo</button>
      <button class="btn btn--xs btn--ghost" data-pad-action="clear" type="button"><i class="ti ti-trash"></i> Clear</button>
      <select data-pad-action="color" class="form-select" style="width:80px; height:28px; font-size:11px;">
        <option value="#1a1a1a">Black</option>
        <option value="#2563eb">Blue</option>
        <option value="#dc2626">Red</option>
        <option value="#16a34a">Green</option>
      </select>
      <select data-pad-action="size" class="form-select" style="width:70px; height:28px; font-size:11px;">
        <option value="2">Fine</option>
        <option value="3" selected>Normal</option>
        <option value="5">Thick</option>
      </select>
      <input data-pad-action="title" class="form-control" placeholder="Sketch title (optional)" style="flex:1; min-width:140px; height:28px; font-size:11px;" />
      <button class="btn btn--xs btn--primary" data-pad-action="save" type="button"><i class="ti ti-device-floppy"></i> Save sketch</button>
    `;

    wrapper.appendChild(canvas);
    wrapper.appendChild(toolbar);
    container.appendChild(wrapper);

    const ctx = canvas.getContext('2d');
    if (!ctx) { console.warn('DrawingPad: Canvas 2D context unavailable'); return { destroy: () => wrapper.remove(), getStrokes: () => [], getPng: () => '' }; }
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    _paintBackground();

    let isDrawing = false;
    let currentStroke = null;
    const strokes = []; /* [{points: [{x,y,pressure}], color, size}] */
    let strokeColor = '#1a1a1a';
    let strokeSize = 3;

    /* Paint a white background: the sketch is saved as JPEG, which has no transparency,
       so an unpainted (transparent) canvas would be stored with a black background. */
    function _paintBackground() {
      ctx.fillStyle = '#fff';
      ctx.fillRect(0, 0, width, height);
    }

    function _redraw() {
      _paintBackground();
      for (const stroke of strokes) {
        if (stroke.points.length < 2) continue;
        ctx.strokeStyle = stroke.color;
        ctx.beginPath();
        ctx.moveTo(stroke.points[0].x, stroke.points[0].y);
        for (let i = 1; i < stroke.points.length; i++) {
          const p = stroke.points[i];
          ctx.lineWidth = stroke.size * (p.pressure || 0.5) * 2;
          ctx.lineTo(p.x, p.y);
        }
        ctx.stroke();
      }
    }

    function _getPos(e) {
      const rect = canvas.getBoundingClientRect();
      return {
        x: (e.clientX - rect.left) * (width / rect.width),
        y: (e.clientY - rect.top) * (height / rect.height),
        pressure: e.pressure || 0.5,
      };
    }

    canvas.addEventListener('pointerdown', (e) => {
      isDrawing = true;
      canvas.setPointerCapture(e.pointerId);
      const pos = _getPos(e);
      currentStroke = { points: [pos], color: strokeColor, size: strokeSize };
    });

    canvas.addEventListener('pointermove', (e) => {
      if (!isDrawing || !currentStroke) return;
      const pos = _getPos(e);
      currentStroke.points.push(pos);
      /* Draw incrementally for performance */
      const pts = currentStroke.points;
      if (pts.length >= 2) {
        const prev = pts[pts.length - 2];
        ctx.strokeStyle = currentStroke.color;
        ctx.lineWidth = currentStroke.size * (pos.pressure || 0.5) * 2;
        ctx.beginPath();
        ctx.moveTo(prev.x, prev.y);
        ctx.lineTo(pos.x, pos.y);
        ctx.stroke();
      }
    });

    canvas.addEventListener('pointerup', (e) => {
      if (currentStroke && currentStroke.points.length >= 2) {
        strokes.push(currentStroke);
      }
      isDrawing = false;
      currentStroke = null;
    });

    canvas.addEventListener('pointerleave', () => {
      if (currentStroke && currentStroke.points.length >= 2) {
        strokes.push(currentStroke);
      }
      isDrawing = false;
      currentStroke = null;
    });

    toolbar.addEventListener('click', (e) => {
      const btn = e.target.closest('[data-pad-action]');
      if (!btn) return;
      const action = btn.dataset.padAction;
      if (action === 'undo') {
        strokes.pop();
        _redraw();
      }
      if (action === 'clear') {
        strokes.length = 0;
        _redraw();
      }
      if (action === 'save') {
        const png = canvas.toDataURL('image/jpeg', 0.85);
        const titleEl = toolbar.querySelector('[data-pad-action="title"]'); const title = titleEl ? titleEl.value.trim() : ''; if (onSave) onSave({ jpeg: png, title: title, strokes: JSON.parse(JSON.stringify(strokes)) });
      }
    });

    toolbar.addEventListener('change', (e) => {
      const sel = e.target.closest('[data-pad-action]');
      if (!sel) return;
      if (sel.dataset.padAction === 'color') strokeColor = sel.value;
      if (sel.dataset.padAction === 'size') strokeSize = parseInt(sel.value, 10);
    });

    function destroy() {
      wrapper.remove();
    }

    return { destroy, getStrokes: () => strokes, getJpeg: () => canvas.toDataURL('image/jpeg', 0.85) };
  }

  window.DrawingPad = { create };
})();
