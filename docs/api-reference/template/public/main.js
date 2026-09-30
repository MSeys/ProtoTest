// DocFX uses data-bs-theme; all ProtoTest surfaces use data-theme.
// Mirror the resolved light/dark value so the shared CSS remains the only palette.
const root = document.documentElement;
function syncTheme() {
  const theme = root.getAttribute('data-bs-theme') === 'dark' ? 'dark' : 'light';
  root.setAttribute('data-theme', theme);
  for (const block of document.querySelectorAll('pre')) {
    block.setAttribute('data-surface', 'blueprint');
    block.tabIndex = 0;
    block.setAttribute('role', 'region');
    block.setAttribute('aria-label', 'Code example');
  }
  const logo = document.getElementById('logo');
  if (logo) {
    const src = logo.getAttribute('src');
    if (src) logo.setAttribute('src', src.replace(/prototest-mark(?:-white)?\.svg$/, theme === 'dark' ? 'prototest-mark-white.svg' : 'prototest-mark.svg'));
  }
}
syncTheme();
new MutationObserver(syncTheme).observe(root, {attributes: true, attributeFilter: ['data-bs-theme']});
export default {start: syncTheme};