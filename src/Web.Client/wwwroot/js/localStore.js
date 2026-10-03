// Cienka nakładka na IndexedDB. Wartości to napisy JSON przygotowane po stronie .NET.
const DB = 'krakow-bez-barier';
const VERSION = 2;
const STORES = ['profile', 'plans', 'reports', 'hazards', 'routeCache'];

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
        tx.onerror = () => { db.close(); reject(tx.error); };
    });
}

export const get = (store, key) => run(store, 'readonly', s => s.get(key));
export const put = (store, key, value) => run(store, 'readwrite', s => s.put(value, key)).then(() => { });
export const remove = (store, key) => run(store, 'readwrite', s => s.delete(key)).then(() => { });
export const list = (store) => run(store, 'readonly', s => s.getAll());
