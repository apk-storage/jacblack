(function () {
  'use strict';

  const {
    LS,
    initThemeToggle,
    showToast,
    withDevKeyHeaders
  } = window.Jacred;

  const { buildForm, collectFormData, deepClone } = window.JacredSettingsForm;

  const API_URL = '/api/v1.0/config';
  const FETCH_TIMEOUT_MS = 15000;

  const els = {
    accessDenied: document.getElementById('accessDenied'),
    settingsPanel: document.getElementById('settingsPanel'),
    settingsLoading: document.getElementById('settingsLoading'),
    configFormRoot: document.getElementById('configFormRoot'),
    configEditor: document.getElementById('configEditor'),
    formatSelect: document.getElementById('formatSelect'),
    configMeta: document.getElementById('configMeta'),
    validationResult: document.getElementById('validationResult'),
    reloadBtn: document.getElementById('reloadBtn'),
    validateBtn: document.getElementById('validateBtn'),
    saveBtn: document.getElementById('saveBtn'),
    devKeyBtn: document.getElementById('devKeyBtn'),
    devKeyInput: document.getElementById('devKeyInput'),
    devKeyModalSave: document.getElementById('devKeyModalSave'),
    devKeyModal: document.getElementById('devKeyModal'),
    modeFormTab: document.getElementById('modeFormTab'),
    modeRawTab: document.getElementById('modeRawTab'),
    diffModal: document.getElementById('diffModal'),
    diffTableBody: document.getElementById('diffTableBody'),
    diffSummary: document.getElementById('diffSummary'),
    diffValidation: document.getElementById('diffValidation'),
    diffEmpty: document.getElementById('diffEmpty'),
    diffConfirmBtn: document.getElementById('diffConfirmBtn')
  };

  let schema = null;
  let baseData = null;
  let activeFormat = 'yaml';
  let dirty = false;
  let diffModalInstance = null;
  let pendingSavePayload = null;

  const isFormMode = () => els.modeFormTab?.classList.contains('active');

  const escapeHtml = (str) => {
    if (str == null) return '';
    return String(str)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  };

  const apiFetch = (url, options = {}) => {
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), FETCH_TIMEOUT_MS);
    return fetch(url, withDevKeyHeaders({ ...options, signal: controller.signal }))
      .then(async (response) => {
        clearTimeout(timeoutId);
        const ct = response.headers.get('content-type') || '';
        if (!ct.includes('application/json')) {
          const text = await response.text();
          throw new Error(response.ok ? 'Ответ сервера не JSON' : (text || `HTTP ${response.status}`));
        }
        const json = await response.json();
        return { response, json };
      })
      .catch((err) => {
        clearTimeout(timeoutId);
        throw err;
      });
  };

  const getPayload = () => {
    if (isFormMode()) {
      return { data: collectFormData(schema, baseData, els.configFormRoot) };
    }
    return {
      content: els.configEditor?.value || '',
      format: els.formatSelect?.value || activeFormat
    };
  };

  const setLoading = (state) => {
    if (els.settingsLoading) {
      els.settingsLoading.classList.toggle('d-none', !state);
    }
    if (els.reloadBtn) els.reloadBtn.disabled = state;
    if (els.validateBtn) els.validateBtn.disabled = state;
    if (els.saveBtn) els.saveBtn.disabled = state;
  };

  const showAccessDenied = () => {
    els.accessDenied?.classList.remove('d-none');
    els.settingsPanel?.classList.add('d-none');
    els.settingsLoading?.classList.add('d-none');
  };

  const showEditor = () => {
    els.accessDenied?.classList.add('d-none');
    els.settingsPanel?.classList.remove('d-none');
    els.settingsLoading?.classList.add('d-none');
  };

  const formatDate = (iso) => {
    if (!iso) return '—';
    try { return new Date(iso).toLocaleString('ru-RU'); } catch (_) { return iso; }
  };

  const renderMeta = (data) => {
    if (!els.configMeta) return;
    const parts = [];
    if (data.path) parts.push(`Файл: <code>${escapeHtml(data.path)}</code>`);
    if (data.format) parts.push(`Источник: ${escapeHtml(String(data.format).toUpperCase())}`);
    if (data.lastModifiedUtc) parts.push(`Изменён: ${escapeHtml(formatDate(data.lastModifiedUtc))}`);
    els.configMeta.innerHTML = parts.join(' · ');
  };

  const renderValidation = (result) => {
    if (!els.validationResult) return;
    if (!result) { els.validationResult.innerHTML = ''; return; }

    const errors = Array.isArray(result.errors) ? result.errors : [];
    const warnings = Array.isArray(result.warnings) ? result.warnings : [];

    if (result.error && errors.length === 0) {
      els.validationResult.innerHTML =
        `<div class="alert alert-danger mb-0 py-2 small">${escapeHtml(result.error)}</div>`;
      return;
    }

    if (errors.length) {
      const list = errors.map((e) => `<li>${escapeHtml(e)}</li>`).join('');
      els.validationResult.innerHTML =
        `<div class="alert alert-danger mb-0 py-2 small"><strong>Ошибки:</strong><ul class="mb-0 mt-1">${list}</ul></div>`;
      return;
    }

    if (result.ok && !warnings.length) {
      els.validationResult.innerHTML =
        '<div class="alert alert-success mb-0 py-2 small">Конфигурация корректна</div>';
      return;
    }

    const list = warnings.map((w) => `<li>${escapeHtml(w)}</li>`).join('');
    els.validationResult.innerHTML =
      `<div class="alert alert-warning mb-0 py-2 small"><strong>Есть предупреждения:</strong><ul class="mb-0 mt-1">${list}</ul></div>`;
  };

  const renderForm = () => {
    if (!schema || !baseData || !els.configFormRoot) return;
    buildForm(schema, baseData, els.configFormRoot);
    els.configFormRoot.querySelectorAll('input, select, textarea').forEach((el) => {
      el.addEventListener('input', () => { dirty = true; });
      el.addEventListener('change', () => { dirty = true; });
    });
  };

  const loadConfig = () => {
    if (dirty && !window.confirm('Есть несохранённые изменения. Перезагрузить?')) return;

    setLoading(true);
    renderValidation(null);

    apiFetch(API_URL)
      .then(({ response, json }) => {
        if (response.status === 403 || response.status === 401) {
          showAccessDenied();
          return;
        }
        if (!json.ok) throw new Error(json.error || 'Не удалось загрузить конфигурацию');

        showEditor();
        schema = json.schema;
        baseData = deepClone(json.data);
        activeFormat = json.format || 'yaml';
        if (els.formatSelect) els.formatSelect.value = activeFormat;
        if (els.configEditor) els.configEditor.value = json.content || '';
        renderForm();
        renderMeta(json);
        dirty = false;
      })
      .catch((err) => {
        showEditor();
        showToast(err?.name === 'AbortError' ? 'Истекло время ожидания' : (err.message || 'Ошибка загрузки'), { type: 'error' });
      })
      .finally(() => setLoading(false));
  };

  const validateConfig = () => {
    setLoading(true);
    apiFetch(`${API_URL}/validate`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify(getPayload())
    })
      .then(({ response, json }) => {
        if (response.status === 403 || response.status === 401) { showAccessDenied(); return; }
        renderValidation(json);
        showToast(json.ok ? 'Проверка пройдена' : (json.error || 'Ошибка валидации'), { type: json.ok ? 'success' : 'error' });
      })
      .catch(() => showToast('Ошибка проверки', { type: 'error' }))
      .finally(() => setLoading(false));
  };

  const renderDiffModal = (diffResult) => {
    const diffs = diffResult.diffs || [];
    const validation = diffResult.validation || {};

    if (els.diffSummary) {
      els.diffSummary.textContent = diffs.length
        ? `Будет изменено полей: ${diffs.length}`
        : 'Изменений не обнаружено';
    }

    if (els.diffValidation) {
      if (!validation.ok && (validation.errors?.length || validation.error)) {
        const errs = validation.errors?.length ? validation.errors : [validation.error];
        els.diffValidation.innerHTML =
          `<div class="alert alert-danger py-2 small mb-0"><strong>Ошибки валидации:</strong> ${escapeHtml(errs.join('; '))}</div>`;
      } else if (validation.warnings?.length) {
        els.diffValidation.innerHTML =
          `<div class="alert alert-warning py-2 small mb-0">${escapeHtml(validation.warnings.join('; '))}</div>`;
      } else {
        els.diffValidation.innerHTML = '';
      }
    }

    if (els.diffTableBody) {
      els.diffTableBody.innerHTML = diffs.map((d) =>
        `<tr class="jr-diff-row jr-diff-row--${escapeHtml(d.change || 'changed')}">` +
        `<td><code class="small">${escapeHtml(d.path)}</code></td>` +
        `<td class="small font-monospace">${escapeHtml(d.oldValue ?? '—')}</td>` +
        `<td class="small font-monospace">${escapeHtml(d.newValue ?? '—')}</td>` +
        `</tr>`
      ).join('');
    }

    els.diffEmpty?.classList.toggle('d-none', diffs.length > 0);
    if (els.diffConfirmBtn) {
      els.diffConfirmBtn.disabled = !validation.ok;
    }
  };

  const performSave = (payload) => {
    setLoading(true);
    apiFetch(API_URL, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify(payload)
    })
      .then(({ response, json }) => {
        if (response.status === 403 || response.status === 401) { showAccessDenied(); return; }
        if (!json.ok) {
          renderValidation({ error: json.error });
          showToast(json.error || 'Ошибка сохранения', { type: 'error' });
          return;
        }
        dirty = false;
        diffModalInstance?.hide();
        showToast('Конфигурация сохранена', { type: 'success' });
        loadConfig();
      })
      .catch(() => showToast('Ошибка сохранения', { type: 'error' }))
      .finally(() => setLoading(false));
  };

  const saveConfig = () => {
    const payload = getPayload();
    setLoading(true);

    apiFetch(`${API_URL}/diff`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify(payload)
    })
      .then(({ response, json }) => {
        if (response.status === 403 || response.status === 401) { showAccessDenied(); return; }
        if (!json.ok) {
          renderValidation({ error: json.error });
          showToast(json.error || 'Ошибка diff', { type: 'error' });
          return;
        }
        pendingSavePayload = payload;
        renderDiffModal(json);
        if (!diffModalInstance && els.diffModal) {
          diffModalInstance = new bootstrap.Modal(els.diffModal);
        }
        diffModalInstance?.show();
      })
      .catch(() => showToast('Ошибка подготовки diff', { type: 'error' }))
      .finally(() => setLoading(false));
  };

  const initDevKeyModal = () => {
    if (!els.devKeyModal || typeof bootstrap === 'undefined') return;
    const modal = new bootstrap.Modal(els.devKeyModal);
    els.devKeyBtn?.addEventListener('click', () => {
      if (els.devKeyInput) els.devKeyInput.value = LS.get('dev_key');
      modal.show();
    });
    els.devKeyModalSave?.addEventListener('click', () => {
      LS.set('dev_key', (els.devKeyInput?.value || '').trim());
      modal.hide();
      showToast('Dev ключ сохранён', { type: 'success' });
      loadConfig();
    });
  };

  const initUi = () => {
    els.reloadBtn?.addEventListener('click', loadConfig);
    els.validateBtn?.addEventListener('click', validateConfig);
    els.saveBtn?.addEventListener('click', saveConfig);
    els.diffConfirmBtn?.addEventListener('click', () => {
      if (pendingSavePayload) performSave(pendingSavePayload);
    });
    els.configEditor?.addEventListener('input', () => { dirty = true; });
    els.formatSelect?.addEventListener('change', () => { dirty = true; });

    window.addEventListener('beforeunload', (e) => {
      if (!dirty) return;
      e.preventDefault();
      e.returnValue = '';
    });
  };

  initThemeToggle({
    toggleEl: document.getElementById('toggleTheme'),
    iconMoonEl: document.getElementById('iconMoon'),
    iconSunEl: document.getElementById('iconSun')
  });

  initDevKeyModal();
  initUi();
  loadConfig();
})();
