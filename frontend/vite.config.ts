import { defineConfig, loadEnv } from 'vite';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  return { server: { proxy: {
    '/services/attractions': { target: env.ATTRACTIONS_PROXY_TARGET || 'http://localhost:5104', changeOrigin: true, rewrite: path => path.replace(/^\/services\/attractions/, '') },
    '/services/transport': { target: env.TRANSPORT_PROXY_TARGET || 'http://localhost:5105', changeOrigin: true, rewrite: path => path.replace(/^\/services\/transport/, '') },
  } } };
});
