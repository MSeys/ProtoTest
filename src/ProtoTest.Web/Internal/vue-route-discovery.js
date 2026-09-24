(() => {
  try {
    const walk = (routes, parent, paths) => {
      if (!Array.isArray(routes)) return;
      for (const route of routes) {
        if (!route || typeof route.path !== 'string') continue;
        if (!route.path) {
          // An empty path is the parent's default route: it adds no segment, but its children
          // still resolve against the parent path.
          if (route.children) walk(route.children, parent, paths);
          continue;
        }
        let path = route.path;
        if (path.charAt(0) !== '/') {
          // Vue 2 child paths are relative to their parent. A top-level relative path is not
          // resolvable against an application route, so it is not a page.
          if (!parent) continue;
          path = parent.replace(/\/+$/, '') + '/' + path.replace(/^\/+/, '');
        }
        paths.push(path);
        if (route.children) walk(route.children, path, paths);
      }
    };
    const app = document.querySelector('[data-v-app]');
    const vueApp = app && app.__vue_app__ ? app.__vue_app__ : null;
    const properties = vueApp && vueApp.config ? vueApp.config.globalProperties : null;
    const router = properties ? properties.$router : null;
    let routes = router && typeof router.getRoutes === 'function' ? router.getRoutes() : null;
    if (!routes) {
      const root = document.querySelector('#app');
      routes = root && root.__vue__ && root.__vue__.$router && root.__vue__.$router.options
        ? root.__vue__.$router.options.routes
        : null;
    }
    if (!routes) return null;
    const paths = [];
    walk(routes, null, paths);
    return JSON.stringify([...new Set(paths)]);
  } catch (error) {
    return null;
  }
})()
