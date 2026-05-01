import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'node:path';

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    port: 5173,
    strictPort: true, // never silently move to 5174 if 5173 is taken
    // Accept any Host header so cloudflared / ngrok / docker host-network
    // demos work without re-listing the random URL each time. The dev
    // server is only ever exposed to trusted demo audiences anyway.
    allowedHosts: true,
    proxy: {
      '/api': { target: 'http://localhost:5080', changeOrigin: true },
      '/ws': { target: 'ws://localhost:5080', ws: true, changeOrigin: true },
    },
  },
});
