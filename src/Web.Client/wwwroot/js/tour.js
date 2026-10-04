// Pomocnik samouczka: czeka, aż strona po nawigacji pokaże elementy wskazywane przez kroki.
const SETTLE_MS = 800;
const exists = (selector) => document.querySelector(selector) !== null;
const loading = () => exists('#tresc .spinner-border');

// Kończy od razu, gdy są wszystkie elementy. Elementy opcjonalne mogą się nie pojawić wcale,
// więc bez nich wystarczy, że strona ma elementy wymagane i przez chwilę niczego nie wczytuje.
export async function ready(required, optional, timeoutMs) {
    const deadline = Date.now() + timeoutMs;
    let settledSince = null;
    while (Date.now() < deadline) {
        if (required.every(exists) && !loading()) {
            if (optional.every(exists)) return;
            settledSince ??= Date.now();
            if (Date.now() - settledSince >= SETTLE_MS) return;
        } else {
            settledSince = null;
        }
        await new Promise(resolve => setTimeout(resolve, 100));
    }
}

export const present = (selectors) => selectors.map(exists);

export const reducedMotion = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches;
