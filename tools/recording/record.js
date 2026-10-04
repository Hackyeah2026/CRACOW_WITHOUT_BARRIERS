// Nagrania demonstracyjne w widoku telefonu. Użycie: node record.js [1-5 | all]
// Wymaga aplikacji uruchomionej na bazie testowej (konfiguracja web-test-5028 z .claude/launch.json).
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { STATE, chromium, context, record, Human } = require('./lib');

const PHOTO = path.join(__dirname, 'przeszkoda.jpg');
const ACCOUNTS = path.join(STATE, 'accounts.json');
// Numer z testów jednostkowych (BusinessTests.ValidTaxId): poprawna suma kontrolna, żadna prawdziwa firma.
const TEST_TAX_ID = '1234563218';

function accounts() {
    return fs.existsSync(ACCOUNTS) ? JSON.parse(fs.readFileSync(ACCOUNTS, 'utf8')) : { runs: 0, users: [] };
}

/** Kolejny login testowy; hasło losowe, zapisane tylko w .state (poza repozytorium). */
function newAccount(prefix) {
    const all = accounts();
    all.runs++;
    const account = { login: `${prefix}${all.runs}`, password: crypto.randomBytes(9).toString('base64url') };
    all.users.push(account);
    fs.mkdirSync(STATE, { recursive: true });
    fs.writeFileSync(ACCOUNTS, JSON.stringify(all, null, 2));
    return account;
}

const preset = (page, name) => page.locator('label.preset-option', { hasText: name });

/** Bez nagrywania: profil potrzeb gościa, opcjonalnie także nowe konto. Zwraca plik stanu przeglądarki. */
async function prepare(name, { withAccount }) {
    const browser = await chromium.launch();
    const ctx = await context(browser);
    const page = await ctx.newPage();
    const h = new Human(page, name);
    await h.open('profil');
    await preset(page, 'Wózek ręczny').click();
    await preset(page, 'Senior').click();
    await page.getByRole('button', { name: 'Gotowe, pokaż miejsca' }).first().click();
    await page.waitForURL('**/miejsca');

    if (withAccount) {
        const account = newAccount('ania.krk');
        await h.open('konto?nowe=true');
        await page.locator('#account-login').fill(account.login);
        await page.locator('#account-password').fill(account.password);
        await page.locator('#account-password-repeat').fill(account.password);
        await page.getByRole('button', { name: 'Załóż konto' }).click();
        await page.getByText('Zalogowano jako').first().waitFor();
    }

    fs.mkdirSync(STATE, { recursive: true });
    const file = path.join(STATE, `${name}.json`);
    await ctx.storageState({ path: file, indexedDB: true });
    await browser.close();
    return file;
}

const nav = (page, name) => page.locator('nav.app-nav a', { hasText: name });

// 1. Tworzenie profilu potrzeb (gość).
async function profile(h, page) {
    await h.open();
    await h.pause(1500);
    await h.tap(nav(page, 'Profil potrzeb'));
    await h.pause(1200);
    await h.shot('profil');

    await h.tap(preset(page, 'Wózek ręczny'));
    await h.tap(preset(page, 'Senior'));
    await h.shot('presety');

    await h.reveal(page.getByRole('heading', { name: 'Dostosuj parametry' }), 'start');
    await h.pause(1200);
    await h.tap(page.locator('label[for=toilet]'));
    await h.tap(page.locator('label[for=cobble]'));
    await h.reveal(page.locator('#max-noise'));
    await page.locator('#max-noise').selectOption({ label: 'Najwyżej umiarkowany' });
    await h.pause(900);
    await h.reveal(page.locator('#max-distance'));
    await h.pause(1200);
    await h.shot('parametry');

    await h.reveal(page.getByRole('heading', { name: 'Podgląd profilu' }), 'start');
    await h.pause(2500);
    await h.shot('podglad');

    await h.tap(page.getByRole('button', { name: 'Gotowe, pokaż miejsca' }).last());
    await page.waitForURL('**/miejsca');
    await page.locator('.places-stats').waitFor();
    await h.pause(1500);
    await h.reveal(page.locator('.places-stats'));
    await h.pause(2500);
    await h.shot('miejsca');
}

/**
 * Pinezki katalogu są rysowane na canvasie, więc nie mają elementów DOM. Leaflet oznacza canvas klasą
 * leaflet-interactive, gdy wskaźnik stoi nad pinezką: szukamy takiego punktu po spirali od środka mapy.
 */
async function findMarker(page, leaflet) {
    const box = await leaflet.boundingBox();
    const center = { x: box.x + box.width / 2, y: box.y + box.height * 0.55 };
    let fallback;
    for (let radius = 0; radius < box.width / 2 - 24; radius += 10) {
        const points = radius === 0 ? 1 : Math.ceil(2 * Math.PI * radius / 10);
        for (let i = 0; i < points; i++) {
            const angle = 2 * Math.PI * i / points;
            const point = { x: center.x + radius * Math.cos(angle), y: center.y + radius * Math.sin(angle) };
            if (point.y < box.y + 110 || point.y > box.y + box.height - 40) continue;
            await page.mouse.move(point.x, point.y);
            const hit = await leaflet.evaluate((el, at) => {
                const canvas = el.querySelector('canvas.leaflet-interactive');
                if (!canvas) return null;
                const rect = canvas.getBoundingClientRect();
                const scale = canvas.width / rect.width;
                const [r, g, b] = canvas.getContext('2d').getImageData((at.x - rect.left) * scale, (at.y - rect.top) * scale, 1, 1).data;
                return { green: g > r + 40 && g > b + 30 };
            }, point);
            // Zielona pinezka to miejsce ocenione jako dostępne: jej karta ma najwięcej do pokazania.
            if (hit?.green) return point;
            if (hit) fallback ??= point;
        }
    }
    if (fallback) return fallback;
    throw new Error('Brak pinezki w widocznej części mapy.');
}

// 2. Przeglądanie punktów na mapie (gość z ustawionym profilem).
async function map(h, page) {
    await h.open('miejsca');
    await page.locator('.places-stats').waitFor();
    await h.pause(1500);
    await h.reveal(page.locator('.places-stats'));
    await h.pause(2000);

    const frame = page.locator('.places-map');
    const leaflet = frame.locator('.leaflet-container');
    await h.reveal(frame);
    await h.pause(2000);
    await h.shot('mapa');

    const box = await leaflet.boundingBox();
    const middle = { x: box.x + box.width / 2, y: box.y + box.height / 2 };
    await h.drag(middle, { x: middle.x - 70, y: middle.y + 60 });
    await h.tap(frame.locator('.leaflet-control-zoom-in'), { reveal: false, after: 1400 });
    await h.tap(frame.locator('.leaflet-control-zoom-in'), { reveal: false, after: 1800 });
    await h.shot('mapa-zoom');

    // Lista i liczniki pokazują tylko to, co widać na mapie.
    await h.reveal(page.locator('.place-card'), 'start');
    await h.pause(1500);
    await h.scroll(450);
    await h.scroll(450);
    await h.shot('lista');

    await h.reveal(frame);
    await h.pause(800);
    const marker = await findMarker(page, leaflet);
    await page.mouse.click(marker.x, marker.y);
    await page.waitForURL('**/miejsca/*');
    await page.locator('.page-header h1').waitFor();
    await h.pause(2500);
    await h.shot('karta');
    await h.reveal(page.getByRole('heading', { name: 'Dlaczego taka ocena' }), 'start');
    await h.pause(2500);
    await h.reveal(page.getByRole('heading', { name: 'Wszystkie znane cechy' }), 'start');
    await h.pause(2500);
    await h.shot('cechy');
    await h.reveal(page.locator('.app-main .leaflet-container'));
    await h.pause(2200);
    await h.tap(page.getByRole('button', { name: 'Wróć' }));
    await page.locator('.places-stats').waitFor();
    await h.pause(1500);

    // Filtr kategorii: mapa dopasowuje się do wyników.
    await h.reveal(page.locator('#category'));
    await h.pause(700);
    const museum = await page.locator('#category option').filter({ hasText: /Muze/ }).first().getAttribute('value');
    await page.locator('#category').selectOption(museum);
    await page.locator('.places-list[aria-busy=false] .place-card').first().waitFor();
    await h.pause(1800);
    await h.shot('kategoria');
    await h.reveal(frame);
    await h.pause(2200);
    await h.reveal(page.locator('.place-card'), 'start');
    await h.pause(1500);
    await h.scroll(450);
    await h.pause(1200);
    await h.shot('muzea');
}

async function searchPlaces(h, page, phrase) {
    const search = page.locator('#search');
    await h.reveal(search);
    await search.fill('');
    await h.type(search, phrase);
    await h.tap(page.getByRole('button', { name: 'Szukaj', exact: true }), { after: 400 });
    await page.locator('.places-list[aria-busy=false] .place-card').first().waitFor();
    await h.pause(800);
}

// 3. Tworzenie planu i wyznaczanie trasy (konto z profilem).
async function plan(h, page) {
    const planName = 'Sobota na Starym Mieście';
    await h.open('miejsca');
    await page.locator('.places-stats').waitFor();
    await h.pause(1500);

    await searchPlaces(h, page, 'Muzeum Narodowe');
    let card = page.locator('.place-card').first();
    await h.reveal(card);
    await h.pause(1000);
    await h.tap(card.getByRole('button', { name: /Dodaj do planu/ }));
    const dialog = page.locator('dialog[open]');
    await dialog.waitFor();
    await h.shot('dialog');
    await h.type(dialog.locator('input.plan-name-input'), planName, { after: 800 });
    await h.tap(dialog.getByRole('button', { name: 'Utwórz i dodaj miejsce' }), { after: 2200 });
    await h.shot('dodano');

    for (const phrase of ['Collegium Maius', 'Sukiennice']) {
        await searchPlaces(h, page, phrase);
        card = page.locator('.place-card').first();
        await h.reveal(card);
        await h.pause(800);
        await h.tap(card.getByRole('button', { name: /Dodaj do planu/ }));
        await dialog.waitFor();
        await h.tap(dialog.locator('.plan-picker-list button', { hasText: planName }), { after: 2000 });
    }
    await h.shot('trzy-miejsca');

    await h.tap(nav(page, 'Plan'), { reveal: false });
    await page.locator('[data-tour=plan-list]').waitFor();
    await h.pause(1800);
    await h.shot('plan');
    await h.reveal(page.locator('[data-tour=plan-list]'), 'start');
    await h.pause(2200);

    await h.tap(page.getByRole('button', { name: 'Ułóż plan' }), { after: 500 });
    await h.shot('ukladam');
    const heading = page.locator('#plan-heading');
    await heading.waitFor({ timeout: 120000 });
    await h.pause(800);
    await h.reveal(heading, 'start');
    await h.pause(2500);
    await h.shot('trasa');
    for (let i = 0; i < 3; i++) await h.scroll(430, 1300);
    await h.shot('odcinki');
    await h.reveal(page.locator('.app-main .leaflet-container').last());
    await h.pause(3500);
    await h.shot('mapa-trasy');
}

// 4. Zgłaszanie nowej przeszkody z analizą zdjęcia przez AI (konto z profilem).
async function hazard(h, page) {
    await h.open();
    await h.pause(1200);
    await h.tap(nav(page, 'Zgłoś na mapie'), { reveal: false });
    const leaflet = page.locator('.app-main .leaflet-container').first();
    await leaflet.waitFor();
    await h.pause(1800);
    await h.reveal(leaflet);
    await h.pause(1200);

    const box = await leaflet.boundingBox();
    await page.mouse.click(box.x + box.width * 0.58, box.y + box.height * 0.62);
    await page.locator('.map-pick-icon').waitFor();
    await h.pause(1800);
    await h.shot('punkt');

    await h.reveal(page.locator('#hazard-heading'), 'start');
    await h.pause(1800);
    const pick = page.locator('label[for=hazard-photo-file]');
    await h.reveal(pick);
    const chooser = page.waitForEvent('filechooser');
    await h.tap(pick, { reveal: false, after: 300 });
    await (await chooser).setFiles(PHOTO);
    await page.locator('.photo-preview').waitFor({ timeout: 30000 });
    await h.reveal(page.locator('.photo-preview'));
    await h.shot('analiza');

    const verdict = page.locator('.photo-verdict');
    await verdict.waitFor({ timeout: 120000 });
    await h.reveal(verdict);
    await h.pause(3500);
    await h.shot('werdykt');
    const apply = page.getByRole('button', { name: 'Uzupełnij zgłoszenie na podstawie zdjęcia' });
    if (await apply.count() > 0)
        await h.tap(apply, { after: 1200 });

    await h.reveal(page.locator('.hazard-kinds'));
    await h.pause(1800);
    await h.reveal(page.locator('#hazard-description'));
    await h.pause(2500);
    await h.shot('formularz');

    await h.tap(page.getByRole('button', { name: 'Wyślij zgłoszenie' }), { after: 500 });
    await page.locator('.alert-success').waitFor({ timeout: 60000 });
    await h.reveal(page.locator('.alert-success'));
    await h.pause(2500);
    await h.shot('wyslane');

    await h.tap(page.locator('.alert-success a', { hasText: 'Konto' }));
    await page.getByRole('heading', { name: 'Punkty zaznaczone na mapie' }).waitFor();
    await h.reveal(page.getByRole('heading', { name: 'Punkty zaznaczone na mapie' }), 'start');
    await h.pause(3000);
    await h.shot('konto');
}

// 5. Rejestracja konta firmowego: nowe konto, potem wniosek dla lokalu.
async function business(h, page) {
    const account = newAccount('bistro.demo');
    await h.open();
    await h.pause(1200);
    await h.tap(page.locator('.app-footer a', { hasText: 'Konto firmowe' }));
    await page.locator('#account-login').waitFor();
    await h.pause(1800);
    await h.shot('start');

    await h.tap(page.getByRole('button', { name: 'Zakładam konto' }));
    await h.type(page.locator('#account-login'), account.login);
    await h.type(page.locator('#account-password'), account.password);
    await h.type(page.locator('#account-password-repeat'), account.password);
    await h.shot('rejestracja');
    await h.tap(page.getByRole('button', { name: 'Załóż konto' }), { after: 500 });

    const form = page.locator('form[aria-labelledby=application-heading]');
    await form.waitFor({ timeout: 30000 });
    await h.pause(1500);
    await h.reveal(form, 'start');
    await h.pause(2000);
    await h.shot('wniosek');

    await h.type(page.locator('#business-search'), 'Cafe', { after: 300 });
    await form.locator('.list-group-item button').first().waitFor({ timeout: 30000 });
    await h.reveal(form.locator('.list-group'));
    await h.pause(1500);
    await h.shot('wyniki');
    await h.tap(form.locator('.list-group-item button').first());
    await h.pause(1200);

    await h.reveal(page.locator('#business-name'));
    await h.pause(1200);
    await h.type(page.locator('#business-tax-id'), TEST_TAX_ID);
    await h.type(page.locator('#business-contact'), 'kontakt@bistro-demo.example');
    await h.type(page.locator('#business-note'), 'Wejście z poziomu chodnika, toaleta dostosowana.');
    await h.shot('dane');

    await h.tap(form.getByRole('button', { name: 'Wyślij wniosek' }), { after: 500 });
    const status = page.locator('section[aria-labelledby=business-heading]');
    await status.waitFor({ timeout: 30000 });
    await h.reveal(status, 'start');
    await h.pause(4000);
    await h.shot('wyslany');
}

const SCENARIOS = {
    1: { name: '1-profil-potrzeb', run: profile },
    2: { name: '2-punkty-na-mapie', run: map, state: { withAccount: false } },
    3: { name: '3-plan-i-trasa', run: plan, state: { withAccount: true } },
    4: { name: '4-zgloszenie-przeszkody', run: hazard, state: { withAccount: true } },
    5: { name: '5-konto-firmowe', run: business },
};

(async () => {
    const wanted = process.argv[2] ?? 'all';
    const ids = wanted === 'all' ? Object.keys(SCENARIOS) : wanted.split(',');
    for (const id of ids) {
        const scenario = SCENARIOS[id];
        if (!scenario) throw new Error(`Nieznany scenariusz: ${id}`);
        const storage = scenario.state ? await prepare(scenario.name, scenario.state) : undefined;
        await record(scenario.name, storage, scenario.run);
    }
})().catch(error => {
    console.error(error);
    process.exit(1);
});
