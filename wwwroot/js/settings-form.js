(function (global) {
  'use strict';

  const getByPath = (obj, path) => {
    if (!obj || !path) return undefined;
    const parts = path.split('.');
    let cur = obj;
    for (const p of parts) {
      if (cur == null) return undefined;
      cur = cur[p];
    }
    return cur;
  };

  const setByPath = (obj, path, value) => {
    const parts = path.split('.');
    let cur = obj;
    for (let i = 0; i < parts.length - 1; i++) {
      const p = parts[i];
      if (cur[p] == null || typeof cur[p] !== 'object') cur[p] = {};
      cur = cur[p];
    }
    cur[parts[parts.length - 1]] = value;
  };

  const escapeHtml = (str) => {
    if (str == null) return '';
    return String(str)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  };

  const deepClone = (obj) => JSON.parse(JSON.stringify(obj ?? {}));

  /** STJ used to serialize JObject as [] — ignore scalar arrays from stale/corrupt API payloads. */
  const normalizeRaw = (raw, field) => {
    if (raw == null) return raw;
    if (!Array.isArray(raw)) return raw;
    if (field.type === 'stringList' || field.type === 'json') return raw;
    return field.type === 'bool' ? false : field.type === 'int' ? 0 : '';
  };

  const stringListToText = (val) => {
    if (!Array.isArray(val)) return '';
    return val.join('\n');
  };

  const textToStringList = (text) =>
    String(text || '')
      .split('\n')
      .map((s) => s.trim())
      .filter(Boolean);

  const parseFieldValue = (field, el) => {
    if (!el) return undefined;
    switch (field.type) {
      case 'bool':
        return el.checked;
      case 'int': {
        const n = parseInt(el.value, 10);
        return Number.isFinite(n) ? n : 0;
      }
      case 'stringList':
        return textToStringList(el.value);
      case 'json':
        try {
          const parsed = JSON.parse(el.value || 'null');
          return parsed;
        } catch (_) {
          return el.dataset.lastValid ? JSON.parse(el.dataset.lastValid) : null;
        }
      case 'select':
      case 'string':
      case 'password':
      default:
        return el.value;
    }
  };

  const renderField = (field, data, prefix) => {
    const path = prefix ? `${prefix}.${field.key}` : field.key;
    const id = `cfg-${path.replace(/[^a-zA-Z0-9_-]/g, '_')}`;
    const raw = normalizeRaw(getByPath(data, path), field);
    const wrap = document.createElement('div');
    wrap.className = 'col-md-6 col-lg-4 mb-3';
    wrap.dataset.fieldPath = path;

    let controlHtml = '';
    const desc = field.description
      ? `<div class="form-text">${escapeHtml(field.description)}</div>`
      : '';

    if (field.type === 'bool') {
      controlHtml =
        `<div class="form-check form-switch mt-1">` +
        `<input class="form-check-input" type="checkbox" role="switch" id="${id}" ${raw ? 'checked' : ''}>` +
        `<label class="form-check-label" for="${id}">${escapeHtml(field.label)}</label>` +
        `</div>${desc}`;
      wrap.className = 'col-md-6 col-lg-4 mb-3 d-flex flex-column justify-content-end';
      wrap.innerHTML = controlHtml;
      return wrap;
    }

    let inputValue = raw;
    if (field.type === 'stringList') inputValue = stringListToText(raw);
    if (field.type === 'json') {
      inputValue = raw != null ? JSON.stringify(raw, null, 2) : '[]';
      wrap.className = 'col-12 mb-3';
    }

    let inputHtml = '';
    if (field.type === 'stringList' || field.type === 'json') {
      const rows = field.type === 'json' ? 6 : 4;
      inputHtml =
        `<textarea class="form-control form-control-sm font-monospace" id="${id}" rows="${rows}"` +
        ` spellcheck="false">${escapeHtml(inputValue ?? '')}</textarea>`;
    } else if (field.type === 'select') {
      const opts = (field.enumValues || []).map((v) => {
        const sel = String(raw ?? '') === String(v) ? ' selected' : '';
        return `<option value="${escapeHtml(v)}"${sel}>${escapeHtml(v)}</option>`;
      }).join('');
      inputHtml = `<select class="form-select form-select-sm" id="${id}">${opts}</select>`;
    } else if (field.type === 'int') {
      const min = field.min != null ? ` min="${field.min}"` : '';
      const max = field.max != null ? ` max="${field.max}"` : '';
      const numVal = raw != null && raw !== '' ? raw : '';
      inputHtml =
        `<input type="number" class="form-control form-control-sm" id="${id}"` +
        ` value="${escapeHtml(numVal)}"${min}${max}>`;
    } else if (field.type === 'password') {
      inputHtml =
        `<input type="text" class="form-control form-control-sm font-monospace" id="${id}"` +
        ` value="${escapeHtml(inputValue ?? '')}" autocomplete="off">`;
    } else {
      inputHtml =
        `<input type="text" class="form-control form-control-sm" id="${id}"` +
        ` value="${escapeHtml(raw ?? '')}">`;
    }

    wrap.innerHTML =
      `<label class="form-label jr-label mb-1" for="${id}">${escapeHtml(field.label)}</label>` +
      inputHtml + desc;
    return wrap;
  };

  const renderCheckboxList = (field, data) => {
    const path = field.key;
    const selected = new Set(Array.isArray(getByPath(data, path)) ? getByPath(data, path) : []);
    const wrap = document.createElement('div');
    wrap.className = 'col-12 mb-3';
    wrap.dataset.fieldPath = path;

    const items = (field.enumValues || []).map((slug) => {
      const id = `cfg-${path}-${slug}`;
      const checked = selected.has(slug) ? ' checked' : '';
      return (
        `<div class="col-6 col-md-4 col-lg-3">` +
        `<div class="form-check">` +
        `<input class="form-check-input" type="checkbox" id="${id}" value="${escapeHtml(slug)}"${checked}>` +
        `<label class="form-check-label small" for="${id}">${escapeHtml(slug)}</label>` +
        `</div></div>`
      );
    }).join('');

    wrap.innerHTML =
      `<label class="form-label jr-label">${escapeHtml(field.label)}</label>` +
      (field.description ? `<div class="form-text mb-2">${escapeHtml(field.description)}</div>` : '') +
      `<div class="row g-1">${items}</div>`;
    return wrap;
  };

  const collectCheckboxList = (container, path) => {
    const values = [];
    container.querySelectorAll(`[data-field-path="${path}"] input[type="checkbox"]:checked`).forEach((el) => {
      values.push(el.value);
    });
    return values;
  };

  const buildForm = (schema, data, rootEl) => {
    if (!rootEl || !schema?.groups) return;
    rootEl.innerHTML = '';

    const nav = document.createElement('div');
    nav.className = 'nav nav-pills jr-settings-nav flex-wrap gap-1 mb-3';
    nav.setAttribute('role', 'tablist');

    const content = document.createElement('div');
    content.className = 'tab-content';

    schema.groups.forEach((group, idx) => {
      if (group.id === 'trackers') {
        const tabId = `tab-trackers`;
        nav.insertAdjacentHTML('beforeend',
          `<button class="nav-link${idx === 0 ? ' active' : ''}" id="${tabId}-btn" data-bs-toggle="pill"` +
          ` data-bs-target="#${tabId}" type="button" role="tab">${escapeHtml(group.title)}</button>`);

        const pane = document.createElement('div');
        pane.className = `tab-pane fade${idx === 0 ? ' show active' : ''}`;
        pane.id = tabId;
        pane.setAttribute('role', 'tabpanel');

        const accordion = document.createElement('div');
        accordion.className = 'accordion jr-settings-trackers';
        accordion.id = 'trackersAccordion';

        (group.trackers || []).forEach((tracker, tIdx) => {
          const collapseId = `tracker-${tracker.id}`;
          const host = getByPath(data, `${tracker.id}.host`);
          const item = document.createElement('div');
          item.className = 'accordion-item';
          item.innerHTML =
            `<h3 class="accordion-header">` +
            `<button class="accordion-button${tIdx > 0 ? ' collapsed' : ''}" type="button"` +
            ` data-bs-toggle="collapse" data-bs-target="#${collapseId}">` +
            `<span>${escapeHtml(tracker.title)}</span>` +
            (host ? `<span class="small text-secondary ms-2">${escapeHtml(host)}</span>` : '') +
            `</button></h3>` +
            `<div id="${collapseId}" class="accordion-collapse collapse${tIdx === 0 ? ' show' : ''}"` +
            ` data-bs-parent="#trackersAccordion">` +
            `<div class="accordion-body"><div class="row g-2 fields-row"></div></div></div>`;

          const row = item.querySelector('.fields-row');
          (tracker.fields || []).forEach((field) => {
            row.appendChild(renderField(field, data, tracker.id));
          });
          accordion.appendChild(item);
        });

        pane.appendChild(accordion);
        content.appendChild(pane);
        return;
      }

      const tabId = `tab-${group.id}`;
      nav.insertAdjacentHTML('beforeend',
        `<button class="nav-link${idx === 0 ? ' active' : ''}" id="${tabId}-btn" data-bs-toggle="pill"` +
        ` data-bs-target="#${tabId}" type="button" role="tab">${escapeHtml(group.title)}</button>`);

      const pane = document.createElement('div');
      pane.className = `tab-pane fade${idx === 0 ? ' show active' : ''}`;
      pane.id = tabId;
      pane.setAttribute('role', 'tabpanel');

      if (group.description) {
        const intro = document.createElement('p');
        intro.className = 'small text-secondary mb-3';
        intro.textContent = group.description;
        pane.appendChild(intro);
      }

      const row = document.createElement('div');
      row.className = 'row g-2 fields-row';

      (group.fields || []).forEach((field) => {
        if (field.type === 'stringList' && field.enumValues?.length && (field.key === 'synctrackers' || field.key === 'disable_trackers')) {
          row.appendChild(renderCheckboxList(field, data));
        } else {
          row.appendChild(renderField(field, data, ''));
        }
      });

      pane.appendChild(row);
      content.appendChild(pane);
    });

    rootEl.appendChild(nav);
    rootEl.appendChild(content);
  };

  const collectFormData = (schema, baseData, rootEl) => {
    const result = deepClone(baseData);

    // Repair nested objects if a scalar was corrupted to [] (legacy STJ bug).
    const ensureObjectPath = (obj, path) => {
      const parts = path.split('.');
      if (parts.length < 2) return;
      let cur = obj;
      for (let i = 0; i < parts.length - 1; i++) {
        const p = parts[i];
        if (cur[p] == null || typeof cur[p] !== 'object' || Array.isArray(cur[p])) {
          cur[p] = {};
        }
        cur = cur[p];
      }
    };

    const applyField = (path, field, el) => {
      let val = parseFieldValue(field, el);
      if (path.includes('.')) ensureObjectPath(result, path);
      setByPath(result, path, val);
    };

    rootEl.querySelectorAll('[data-field-path]').forEach((wrap) => {
      const path = wrap.dataset.fieldPath;
      if (path === 'synctrackers' || path === 'disable_trackers') {
        setByPath(result, path, collectCheckboxList(rootEl, path));
        return;
      }
      const input = wrap.querySelector('input:not([type="checkbox"]), select, textarea') ||
        wrap.querySelector('input[type="checkbox"]');
      if (!input) return;
      const fieldMeta = findFieldMeta(schema, path);
      applyField(path, fieldMeta || { type: input.type === 'checkbox' ? 'bool' : 'string' }, input);
    });

    return result;
  };

  const findFieldMeta = (schema, path) => {
    if (!schema?.groups) return null;
    for (const group of schema.groups) {
      if (group.id === 'trackers') {
        const [trackerName, ...rest] = path.split('.');
        const tracker = (group.trackers || []).find((t) => t.id === trackerName);
        if (tracker && rest.length) {
          return (tracker.fields || []).find((f) => f.key === rest.join('.')) || null;
        }
      } else {
        const field = (group.fields || []).find((f) => f.key === path);
        if (field) return field;
      }
    }
    return null;
  };

  global.JacredSettingsForm = Object.freeze({
    buildForm,
    collectFormData,
    deepClone,
    getByPath
  });
})(window);
