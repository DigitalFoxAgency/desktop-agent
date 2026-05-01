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
    proxy: {
      '/api': { target: 'http://localhost:5080', changeOrigin: true },
      '/ws': { target: 'ws://localhost:5080', ws: true, changeOrigin: true },
    },
  },
});
