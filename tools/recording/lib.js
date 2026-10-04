// Wspólne narzędzia nagrań: kontekst telefonu z zapisem wideo i "ludzkie" tempo klikania.
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawn } = require('child_process');
const { chromium } = require('playwright');

const BASE = process.env.KBB_URL || 'http://localhost:5028/';
const OUT = path.join(__dirname, 'out');
const STATE = path.join(__dirname, '.state');
const VIEWPORT = { width: 390, height: 844 };
const SCALE = 2;
const FPS = 25;

// Kółko w miejscu dotknięcia, żeby na nagraniu było widać, gdzie klikamy.
const TAP_MARKER = () => {
    document.addEventListener('pointerdown', e => {
        const dot = document.createElement('div');
        dot.style.cssText = `position:fixed;left:${e.clientX - 22}px;top:${e.clientY - 22}px;width:44px;height:44px;border-radius:50%;`
            + 'background:rgba(11,79,138,.35);border:2px solid rgba(11,79,138,.9);pointer-events:none;z-index:2147483647;'
            + 'transition:transform .5s ease-out,opacity .5s ease-out;';
        // Otwarty <dialog> jest w warstwie nad stroną, więc kółko musi trafić do niego.
        (document.querySelector('dialog[open]') ?? document.documentElement).appendChild(dot);
        requestAnimationFrame(() => { dot.style.transform = 'scale(1.6)'; dot.style.opacity = '0'; });
        setTimeout(() => dot.remove(), 600);
    }, true);
};

async function context(browser, { storage } = {}) {
    const ctx = await browser.newContext({
        viewport: VIEWPORT,
        deviceScaleFactor: SCALE,
        isMobile: true,
        hasTouch: true,
        locale: 'pl-PL',
        timezoneId: 'Europe/Warsaw',
        userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1',
        storageState: storage && fs.existsSync(storage) ? storage : undefined,
    });
    await ctx.addInitScript(TAP_MARKER);
    return ctx;
}

class Human {
    constructor(page, name, recorder) {
        this.page = page;
        this.name = name;
        this.recorder = recorder;
        this.step = 0;
    }

    pause(ms = 900) { return this.page.waitForTimeout(ms); }

    /** Zrzut kontrolny kroku (tylko z KBB_SHOTS=1), do sprawdzenia nagrania bez oglądania wideo. */
    async shot(label) {
        if (!process.env.KBB_SHOTS) return;
        this.step++;
        await this.page.screenshot({ path: path.join(OUT, `${this.name}-${String(this.step).padStart(2, '0')}-${label}.png`) });
    }

    async open(route = '') {
        await this.page.goto(BASE + route);
        // Aplikacja WebAssembly: czekamy, aż Blazor narysuje nawigację i przestanie wczytywać.
        await this.page.locator('nav.app-nav').waitFor();
        await this.page.waitForFunction(() => !document.querySelector('[role=status] .spinner-border'), null, { timeout: 60000 }).catch(() => { });
        // Nagranie rusza po wczytaniu aplikacji, żeby nie zaczynało się od pustego ekranu.
        if (this.recorder && !this.recorder.started) await this.recorder.start();
    }

    async reveal(locator, block = 'center') {
        await locator.first().evaluate((el, b) => el.scrollIntoView({ behavior: 'smooth', block: b }), block);
        await this.pause(750);
    }

    async tap(locator, { after = 900, reveal = true } = {}) {
        const target = locator.first();
        await target.waitFor({ state: 'visible' });
        if (reveal) await this.reveal(target);
        await target.click();
        await this.pause(after);
    }

    async type(locator, text, { after = 600 } = {}) {
        await this.tap(locator, { after: 250 });
        await locator.first().pressSequentially(text, { delay: 55 });
        await this.pause(after);
    }

    /** Płynne przewinięcie strony o podaną liczbę pikseli. */
    async scroll(dy, after = 900) {
        await this.page.evaluate(y => window.scrollBy({ top: y, behavior: 'smooth' }), dy);
        await this.pause(after + Math.min(600, Math.abs(dy)));
    }

    /** Przeciągnięcie palcem (np. mapy): od punktu do punktu we współrzędnych okna. */
    async drag(from, to, steps = 12) {
        const mouse = this.page.mouse;
        await mouse.move(from.x, from.y);
        await mouse.down();
        for (let i = 1; i <= steps; i++) {
            await mouse.move(from.x + (to.x - from.x) * i / steps, from.y + (to.y - from.y) * i / steps);
            await this.pause(25);
        }
        await mouse.up();
        await this.pause(900);
    }
}

/** ffmpeg pobrany razem z przeglądarką przez `npx playwright install` (koduje tylko VP8/WebM). */
function ffmpegPath() {
    const cache = process.env.PLAYWRIGHT_BROWSERS_PATH
        || (process.platform === 'darwin' ? path.join(os.homedir(), 'Library/Caches/ms-playwright')
            : process.platform === 'win32' ? path.join(process.env.LOCALAPPDATA, 'ms-playwright')
            : path.join(os.homedir(), '.cache/ms-playwright'));
    for (const dir of fs.readdirSync(cache).filter(d => d.startsWith('ffmpeg-')).sort().reverse()) {
        const binary = fs.readdirSync(path.join(cache, dir)).find(f => f.startsWith('ffmpeg-'));
        if (binary) return path.join(cache, dir, binary);
    }
    throw new Error('Brak ffmpeg Playwrighta. Uruchom: npx playwright install chromium');
}

/**
 * Składa wideo ze zrzutów ekranu, ze stałą liczbą klatek. W odróżnieniu od wbudowanego nagrywania Playwrighta
 * pozwala zacząć dopiero po wczytaniu aplikacji i ustawić jakość obrazu.
 */
class Recorder {
    constructor(page, target) {
        this.page = page;
        this.ffmpeg = spawn(ffmpegPath(), [
            '-loglevel', 'error', '-f', 'image2pipe', '-c:v', 'mjpeg', '-framerate', String(FPS), '-i', 'pipe:0',
            '-y', '-an', '-c:v', 'vp8', '-qmin', '0', '-qmax', '36', '-crf', '6', '-b:v', '5M',
            '-deadline', 'realtime', '-speed', '6', '-threads', '4', target,
        ], { stdio: ['pipe', 'inherit', 'inherit'] });
        this.done = new Promise(resolve => this.ffmpeg.on('close', resolve));
        this.written = 0;
    }

    async start() {
        this.cdp = await this.page.context().newCDPSession(this.page);
        this.started = Date.now();
        this.running = true;
        this.loop = this.capture();
    }

    async capture() {
        let previous;
        while (this.running) {
            let frame;
            try {
                const shot = await this.cdp.send('Page.captureScreenshot', { format: 'jpeg', quality: 90 });
                frame = Buffer.from(shot.data, 'base64');
            } catch {
                await new Promise(resolve => setTimeout(resolve, 30));
                continue;
            }
            // Poprzednia klatka wypełnia czas do chwili zrobienia bieżącej.
            await this.write(previous ?? frame, Math.round((Date.now() - this.started) / 1000 * FPS));
            previous = frame;
        }
        if (previous) await this.write(previous, this.written + 1);
    }

    async write(frame, until) {
        while (this.written < until) {
            this.written++;
            if (!this.ffmpeg.stdin.write(frame))
                await new Promise(resolve => this.ffmpeg.stdin.once('drain', resolve));
        }
    }

    async stop() {
        this.running = false;
        await this.loop;
        this.ffmpeg.stdin.end();
        await this.done;
        return this.written / FPS;
    }
}

/** Nagrywa jeden scenariusz do out/<name>.webm. */
async function record(name, storage, scenario) {
    fs.mkdirSync(OUT, { recursive: true });
    const target = path.join(OUT, `${name}.webm`);
    // Bez tej flagi przeglądarka bez okna rysuje w pikselach CSS (390 px szerokości) i obraz jest nieostry.
    const browser = await chromium.launch({ args: [`--force-device-scale-factor=${SCALE}`] });
    const ctx = await context(browser, { storage });
    const page = await ctx.newPage();
    const recorder = new Recorder(page, target);
    let failure;
    try {
        await scenario(new Human(page, name, recorder), page, ctx);
    } catch (error) {
        failure = error;
        await page.screenshot({ path: path.join(OUT, `${name}-BLAD.png`) }).catch(() => { });
    }
    const seconds = recorder.started ? await recorder.stop() : 0;
    await browser.close();
    if (failure) throw failure;
    console.log(`Nagrano: ${path.relative(process.cwd(), target)} (${Math.round(seconds)} s, ${recorder.written} klatek)`);
}

module.exports = { BASE, OUT, STATE, chromium, context, record, Human };
