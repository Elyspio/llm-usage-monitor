// PROTOTYPE jetable (#7) — captures d'écran des variantes via le serveur de dev Vite + Playwright.
// Usage : pnpm shots  (Chromium Playwright, ou Chrome installé en repli)
import { chromium } from 'playwright';
import { createServer } from 'vite';

const shots = [
	['A', 'nominal'],
	['B', 'nominal'],
	['A', 'erreurs'],
	['B', 'quasi-epuise'],
	['B', 'degrade'],
];

const server = await createServer({ server: { port: 5179 }, logLevel: 'error' });
await server.listen();
const base = server.resolvedUrls.local[0];

let browser;
try {
	browser = await chromium.launch();
} catch {
	browser = await chromium.launch({ channel: 'chrome' });
}
try {
	const page = await browser.newPage({ viewport: { width: 1440, height: 900 }, colorScheme: 'dark' });
	for (const [variant, scenario] of shots) {
		await page.goto(`${base}?variant=${variant}&scenario=${scenario}`, { waitUntil: 'networkidle' });
		await page.waitForSelector('svg');
		await page.waitForTimeout(600);
		const file = `screenshot-${variant}-${scenario}.png`;
		await page.screenshot({ path: file, fullPage: true });
		console.log('écrit', file);
	}
} finally {
	await browser.close();
	await server.close();
}
