// Czytanie na głos syntezatorem mowy przeglądarki (Web Speech API). Używamy tylko głosu działającego na urządzeniu:
// plan i ocena wynikają z profilu potrzeb, więc tekst nie może trafić do syntezatora w sieci.
let run = 0;
// Chrome potrafi usunąć z pamięci wypowiedź czekającą w kolejce, a wtedy nie zgłasza jej końca.
let queued = [];

function supported() {
    return 'speechSynthesis' in window && 'SpeechSynthesisUtterance' in window;
}

// Chrome wczytuje listę głosów z opóźnieniem, więc pusta lista nie oznacza jeszcze ich braku.
function voices() {
    return new Promise(resolve => {
        const list = speechSynthesis.getVoices();
        if (list.length > 0) {
            resolve(list);
            return;
        }
        const done = () => {
            clearTimeout(timer);
            speechSynthesis.removeEventListener('voiceschanged', done);
            resolve(speechSynthesis.getVoices());
        };
        const timer = setTimeout(done, 1500);
        speechSynthesis.addEventListener('voiceschanged', done);
    });
}

async function polishVoice() {
    if (!supported()) return null;
    const local = (await voices()).filter(v => v.localService && v.lang.toLowerCase().replace('_', '-').startsWith('pl'));
    return local.find(v => v.default) ?? local[0] ?? null;
}

export async function isAvailable() {
    return await polishVoice() !== null;
}

// Czyta fragmenty po kolei. Po ostatnim woła dotnet.SpeechFinished(); przerwane czytanie niczego nie zgłasza.
// Zwraca false, gdy na urządzeniu nie ma polskiego głosu.
export async function speak(parts, dotnet) {
    const voice = await polishVoice();
    if (!voice || parts.length === 0) return false;

    stop();
    const mine = ++run;
    queued = parts.map((part, index) => {
        const utterance = new SpeechSynthesisUtterance(part);
        utterance.voice = voice;
        utterance.lang = voice.lang;
        if (index === parts.length - 1) {
            utterance.onend = utterance.onerror = () => {
                if (mine !== run) return;
                queued = [];
                dotnet.invokeMethodAsync('SpeechFinished');
            };
        }
        return utterance;
    });
    queued.forEach(utterance => speechSynthesis.speak(utterance));
    return true;
}

export function stop() {
    run++;
    queued = [];
    if (supported()) speechSynthesis.cancel();
}
