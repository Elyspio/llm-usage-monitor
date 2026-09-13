import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// PROTOTYPE jetable (#7) : config Vite minimale, sans Vite+, sans proxy /api (aucun backend).
export default defineConfig({
	plugins: [react()],
	server: { port: 5174 },
	preview: { port: 5174 },
});
