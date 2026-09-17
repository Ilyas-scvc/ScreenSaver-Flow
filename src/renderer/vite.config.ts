import { defineConfig } from 'vite';

/**
 * The production bundle is emitted flat and without content hashes on purpose:
 * the Windows host embeds every file in `dist/` as a .NET manifest resource,
 * and a flat, predictable file list keeps that mapping to a single MSBuild line
 * (and keeps the shipped `.scr` a single self-contained file).
 */
export default defineConfig({
  base: './',
  build: {
    target: 'es2022',
    outDir: 'dist',
    assetsDir: '.',
    emptyOutDir: true,
    sourcemap: false,
    // three is large; a warning at every build is just noise.
    chunkSizeWarningLimit: 2048,
    rollupOptions: {
      output: {
        entryFileNames: 'app.js',
        chunkFileNames: '[name].js',
        assetFileNames: '[name][extname]',
      },
    },
  },
  server: {
    host: '127.0.0.1',
    port: 5173,
    strictPort: true,
  },
  preview: {
    host: '127.0.0.1',
    port: 4173,
    strictPort: true,
  },
});
