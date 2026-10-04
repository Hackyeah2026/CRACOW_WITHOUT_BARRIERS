// Cienka nakładka na IndexedDB. Wartości to napisy JSON przygotowane po stronie .NET.
const DB = 'krakow-bez-barier';
const VERSION = 3;
// Te same nazwy co w LocalStores po stronie .NET. Bazy założone wcześniej mają też nieużywane już magazyny
// 'plans', 'reports' i 'hazards'; zostają puste, a ich usunięcie wymagałoby podniesienia wersji.
const STORES = ['profile', 'routeCache', 'placeCache'];

function open() {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(DB, VERSION);
        request.onupgradeneeded = () => {
            for (const store of STORES) {
                if (!request.result.objectStoreNames.contains(store)) request.result.createObjectStore(store);
            }
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
}

async function run(store, mode, action) {
    const db = await open();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(store, mode);
        const request = action(tx.objectStore(store));
        tx.oncomplete = () => { db.close(); resolve(request.result ?? null); };
        // Przerwanie (np. brak miejsca na dysku) nie zgłasza onerror: bez tego wywołanie czekałoby bez końca.
        tx.onerror = tx.onabort = () => { db.close(); reject(tx.error); };
    });
}

export const get = (store, key) => run(store, 'readonly', s => s.get(key));
export const put = (store, key, value) => run(store, 'readwrite', s => s.put(value, key)).then(() => { });
