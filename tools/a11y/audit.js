// Automatyczny audyt dostępności stron aplikacji: axe-core z regułami WCAG 2.0-2.2 na poziomach A i AA.
// Użycie: uruchom aplikację (dotnet run --project src/Web --launch-profile http), potem: npm run audit
// Adres zmienisz zmienną KBB_URL. Automat wykrywa część problemów; test czytnikiem ekranu i klawiaturą robi człowiek.
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');
const { default: AxeBuilder } = require('@axe-core/playwright');

const BASE = process.env.KBB_URL || 'http://localhost:5008/';
const TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];
const VIEWPORTS = { telefon: { width: 390, height: 844 }, komputer: { width: 1280, height: 900 } };

function samplePlaceId() {
    const file = path.join(__dirname, '..', '..', 'src', 'Web.Client', 'wwwroot', 'data', 'krakow', 'places', 'Museum.json');
    return JSON.parse(fs.readFileSync(file, 'utf8'))[0].id;
}

const PAGES = [
    ['start', ''],
    ['profil', 'profil'],
    ['miejsca', 'miejsca'],
    ['karta miejsca', `miejsca/${samplePlaceId()}`],
    ['plan', 'plan'],
    ['zgłoś na mapie', 'zglos'],
    ['konto', 'konto'],
    ['panel urzędnika', 'urzednik']
];

// Lista miejsc czyta pliki katalogu kilka sekund, więc audyt czeka na pierwszą pozycję, a nie tylko na nagłówek.
const READY = { miejsca: 'button[aria-label^="Dodaj do planu"]' };

async function open(page, url) {
    await page.goto(BASE + url, { waitUntil: 'networkidle' });
    // Blazor WebAssembly renderuje po pobraniu runtime; czekamy na nagłówek strony.
    await page.waitForSelector(READY[url] ?? 'main h1', { timeout: 60000 }).catch(() => {});
    await page.waitForTimeout(1500);
}

// Widoki z danymi: profil z dwoma potrzebami, lista z ocenami, plan z dwoma miejscami, karta miejsca, samouczek.
const FLOW = [
    ['profil ustawiony', async page => {
        await open(page, 'profil');
        for (const name of ['Wózek elektryczny', 'Spektrum autyzmu'])
            await page.locator('label.preset-option', { hasText: name }).first().click();
        await page.locator('[data-tour="profile-save"] button.btn-primary').click();
        await page.waitForTimeout(800);
    }],
    ['miejsca z oceną', async page => open(page, 'miejsca')],
    ['plan ułożony', async page => {
        await open(page, 'miejsca');
        // Dodane miejsce zmienia etykietę przycisku, więc za każdym razem klikamy pierwszy z pozostałych.
        const add = page.locator(READY.miejsca);
        for (let i = 0; i < 2; i++) {
            await add.first().click();
            await page.waitForTimeout(700);
        }
        await open(page, 'plan');
        await page.getByRole('button', { name: /Ułóż plan/ }).click();
        await page.waitForTimeout(6000);
    }],
    ['karta miejsca z oceną', async page => open(page, `miejsca/${samplePlaceId()}`)],
    ['samouczek otwarty', async page => {
        await open(page, '');
        await page.getByRole('button', { name: 'Samouczek' }).click();
        await page.waitForTimeout(800);
    }]
];

async function audit(page, viewportName, name, report) {
    const result = await new AxeBuilder({ page }).withTags(TAGS).analyze();
    report.push({ viewport: viewportName, page: name, url: page.url(), violations: result.violations });

    console.log(`${viewportName.padEnd(9)} ${name.padEnd(22)} ${result.violations.length ? `${result.violations.length} problemów` : 'bez problemów'}`);
    for (const v of result.violations)
        console.log(`    [${v.impact}] ${v.id}: ${v.help} (${v.nodes.length}×) ${v.nodes.slice(0, 3).map(n => n.target.join(' ')).join(' | ')}`);
    return result.violations.length;
}

// Klawiatura (WCAG 2.4.1, 2.4.3, 2.4.7). Po wejściu na stronę aplikacja przenosi fokus na nagłówek h1 (czytnik ogłasza
// stronę). Od początku dokumentu pierwszy Tab ma trafić na link do treści, widoczny po fokusie, a link ma prowadzić do treści.
async function keyboard(page, viewportName) {
    await open(page, '');
    const onHeading = await page.evaluate(() => document.activeElement?.matches('main h1') ?? false);

    await page.evaluate(() => { document.body.tabIndex = -1; document.body.focus(); document.body.removeAttribute('tabindex'); });
    await page.keyboard.press('Tab');
    const first = await page.evaluate(() => ({
        text: document.activeElement?.textContent?.trim(),
        outline: getComputedStyle(document.activeElement).outlineStyle,
        shadow: getComputedStyle(document.activeElement).boxShadow,
        visible: document.activeElement.getBoundingClientRect().width > 1
    }));
    await page.keyboard.press('Enter');
    await page.waitForTimeout(300);
    await page.keyboard.press('Tab');
    const afterSkip = await page.evaluate(() => !!document.activeElement?.closest('main'));

    const problems = [];
    if (!onHeading) problems.push('po wejściu na stronę fokus nie jest na nagłówku h1');
    if (first.text !== 'Przejdź do treści') problems.push(`pierwszy Tab trafia na "${first.text?.slice(0, 30)}", a nie na link do treści`);
    else if (!first.visible || (first.outline === 'none' && first.shadow === 'none')) problems.push('link do treści po fokusie nie jest widoczny');
    if (!afterSkip) problems.push('po linku do treści kolejny Tab nie trafia do treści strony');
    console.log(`${viewportName.padEnd(9)} ${'klawiatura'.padEnd(22)} ${problems.length ? problems.join('; ') : 'bez problemów'}`);
    return problems.length;
}

(async () => {
    const browser = await chromium.launch();
    const report = [];
    let total = 0;

    for (const [viewportName, viewport] of Object.entries(VIEWPORTS)) {
        const context = await browser.newContext({ viewport, locale: 'pl-PL' });
        const page = await context.newPage();
        total += await keyboard(page, viewportName);
        for (const [name, url] of PAGES) {
            await open(page, url);
            total += await audit(page, viewportName, name, report);
        }
        for (const [name, step] of FLOW) {
            try {
                await step(page);
            } catch (e) {
                console.log(`${viewportName.padEnd(9)} ${name.padEnd(22)} nie udało się przejść ścieżki: ${e.message.split('\n')[0]}`);
                continue;
            }
            total += await audit(page, viewportName, name, report);
        }
        await context.close();
    }

    await browser.close();
    fs.mkdirSync(path.join(__dirname, 'report'), { recursive: true });
    fs.writeFileSync(path.join(__dirname, 'report', 'axe.json'), JSON.stringify(report, null, 2));
    console.log(`\nRazem: ${total} problemów. Szczegóły: tools/a11y/report/axe.json`);
    process.exitCode = total > 0 ? 1 : 0;
})();
