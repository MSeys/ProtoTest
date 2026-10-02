(() => {
  const root = document.documentElement;
  const search = document.getElementById('reportSearch');
  const views = document.querySelector('.views');
  const tabs = [...document.querySelectorAll('[role="tab"][data-view]')];
  const sections = [...document.querySelectorAll('[data-report-section]')];
  const attention = document.querySelector('[data-attention]');
  const issues = attention ? [...attention.querySelectorAll('.issue')] : [];
  const title = document.getElementById('viewTitle');
  const count = document.getElementById('resultCount');
  const empty = document.getElementById('emptyState');
  let view = tabs.length ? tabs[0].dataset.view : 'all';

  let savedTheme;
  try { savedTheme = localStorage.getItem('prototest-report-theme'); } catch { }
  root.dataset.theme = savedTheme || (matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark');
  document.getElementById('themeToggle').addEventListener('click', () => {
    root.dataset.theme = root.dataset.theme === 'dark' ? 'light' : 'dark';
    try { localStorage.setItem('prototest-report-theme', root.dataset.theme); } catch { }
  });

  // A search looks through every kind; without one, the selected tab decides what shows.
  function apply() {
    const query = search.value.trim().toLocaleLowerCase();
    const searching = query.length > 0;
    const matches = element => !query || element.dataset.search.includes(query);
    views.classList.toggle('searching', searching);
    let visible = 0;
    let total = 0;

    const showAttention = !searching && view === 'attention';
    if (attention) {
      attention.hidden = !showAttention;
      if (showAttention) {
        for (const issue of issues) issue.hidden = false;
        visible += issues.length;
        total += issues.length;
      }
    }

    for (const section of sections) {
      const inView = searching || view === 'all' || section.dataset.kind === view;
      const entries = [...section.querySelectorAll('[data-report-item="root"]')];
      let shown = 0;
      for (const entry of entries) {
        entry.hidden = !(inView && matches(entry));
        if (!entry.hidden) shown++;
      }
      section.hidden = shown === 0;
      const counter = section.querySelector('[data-section-count]');
      const sectionTotal = Number(counter.dataset.total);
      counter.textContent = shown === sectionTotal
        ? `${sectionTotal} ${sectionTotal === 1 ? 'entry' : 'entries'}`
        : `${shown} of ${sectionTotal} entries`;
      if (inView) {
        visible += shown;
        total += sectionTotal;
      }
    }

    const selected = tabs.find(tab => tab.dataset.view === view);
    title.textContent = searching ? 'Search results' : selected ? selected.dataset.title : 'All entries';
    count.textContent = `${visible} of ${total} shown`;
    empty.hidden = visible !== 0;
  }

  function select(next, focus) {
    view = next;
    for (const tab of tabs) {
      const active = tab.dataset.view === view;
      tab.setAttribute('aria-selected', String(active));
      tab.tabIndex = active ? 0 : -1;
      if (active && focus) tab.focus();
    }
    apply();
  }

  // A strip tick or an attention row opens its entry in its own tab and marks it for a moment.
  function goTo(id) {
    const entry = document.getElementById(id);
    if (!entry) return;
    search.value = '';
    select(entry.closest('[data-report-section]').dataset.kind, false);
    if (entry.tagName === 'DETAILS') entry.open = true;
    entry.scrollIntoView({ block: 'center' });
    entry.classList.add('target');
    setTimeout(() => entry.classList.remove('target'), 1600);
  }

  for (const tab of tabs) {
    tab.addEventListener('click', () => { search.value = ''; select(tab.dataset.view, false); });
    tab.addEventListener('keydown', event => {
      const index = tabs.indexOf(tab);
      const step = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0;
      if (step !== 0) {
        event.preventDefault();
        select(tabs[(index + step + tabs.length) % tabs.length].dataset.view, true);
      } else if (event.key === 'Home' || event.key === 'End') {
        event.preventDefault();
        select(tabs[event.key === 'Home' ? 0 : tabs.length - 1].dataset.view, true);
      }
    });
  }

  for (const target of document.querySelectorAll('[data-goto]')) {
    target.addEventListener('click', () => goTo(target.dataset.goto));
  }

  search.addEventListener('input', apply);
  document.addEventListener('keydown', event => {
    if (event.key === '/' && document.activeElement !== search) {
      event.preventDefault();
      search.focus();
    }
    if (event.key === 'Escape' && document.activeElement === search) {
      search.value = '';
      search.blur();
      apply();
    }
  });
  select(view, false);
})();
