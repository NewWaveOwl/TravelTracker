import { defineConfig, loadEnv } from 'vite';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  return { plugins: [{
    name: 'auth0-browser-entry',
    // This app is entirely client-rendered; the SDK's React Server Components
    // directive has no role here and otherwise produces a Rollup warning.
    transform(code, id) {
      if (id.replaceAll('\\', '/').endsWith('/@auth0/auth0-react/dist/auth0-react.esm.js')) {
        return { code: code.replace(/^['"]use client['"];?\s*/, ''), map: null };
      }
    },
  }], server: { proxy: {
    '/services/attractions': { target: env.ATTRACTIONS_PROXY_TARGET || 'http://localhost:5104', changeOrigin: true, rewrite: path => path.replace(/^\/services\/attractions/, '') },
    '/services/transport': { target: env.TRANSPORT_PROXY_TARGET || 'http://localhost:5105', changeOrigin: true, rewrite: path => path.replace(/^\/services\/transport/, '') },
  } } };
});
