(() => {
  const root = document.documentElement;
  const search = document.getElementById('reportSearch');
  const entries = [...document.querySelectorAll('[data-report-item="root"]')];
  const sections = [...document.querySelectorAll('[data-report-section]')];
  const filters = [...document.querySelectorAll('[data-filter]')];
  const count = document.getElementById('resultCount');
  const empty = document.getElementById('emptyState');
  let activeFilter = 'all';

  let savedTheme;
  try { savedTheme = localStorage.getItem('prototest-report-theme'); } catch { }
  root.dataset.theme = savedTheme || (matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark');
  document.getElementById('themeToggle').addEventListener('click', () => {
    root.dataset.theme = root.dataset.theme === 'dark' ? 'light' : 'dark';
    try { localStorage.setItem('prototest-report-theme', root.dataset.theme); } catch { }
  });

  function applyFilters() {
    const query = search.value.trim().toLocaleLowerCase();
    let visible = 0;
    for (const section of sections) {
      const sectionEntries = [...section.querySelectorAll('[data-report-item="root"]')];
      let sectionVisible = 0;
      for (const entry of sectionEntries) {
        const matchesText = !query || entry.dataset.search.includes(query);
        const matchesFilter = activeFilter === 'all' || entry.dataset.filters.split(' ').includes(activeFilter);
        entry.hidden = !(matchesText && matchesFilter);
        if (!entry.hidden) sectionVisible++;
      }
      section.hidden = sectionVisible === 0;
      const counter = section.querySelector('[data-section-count]');
      const total = Number(counter.dataset.total);
      counter.textContent = sectionVisible === total
        ? `${total} ${total === 1 ? 'entry' : 'entries'}`
        : `${sectionVisible} of ${total} entries`;
      visible += sectionVisible;
    }
    count.textContent = `${visible} of ${entries.length} shown`;
    empty.hidden = visible !== 0;
  }

  search.addEventListener('input', applyFilters);
  for (const filter of filters) {
    filter.addEventListener('click', () => {
      activeFilter = filter.dataset.filter;
      for (const button of filters) button.classList.toggle('active', button === filter);
      applyFilters();
    });
  }
  document.addEventListener('keydown', event => {
    if (event.key === '/' && document.activeElement !== search) {
      event.preventDefault();
      search.focus();
    }
    if (event.key === 'Escape' && document.activeElement === search) {
      search.value = '';
      search.blur();
      applyFilters();
    }
  });
  applyFilters();
})();
