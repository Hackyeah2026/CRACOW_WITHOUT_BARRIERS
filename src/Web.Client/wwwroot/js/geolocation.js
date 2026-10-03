// Bieżąca lokalizacja z przeglądarki. Zawsze zwraca wynik, bez wyjątków: { ok, lat, lon } albo { ok: false, reason }.
export function getCurrentPosition() {
    return new Promise(resolve => {
        if (!navigator.geolocation) {
            resolve({ ok: false, reason: 'unsupported' });
            return;
        }
        navigator.geolocation.getCurrentPosition(
            p => resolve({ ok: true, lat: p.coords.latitude, lon: p.coords.longitude }),
            e => resolve({ ok: false, reason: e.code === 1 ? 'denied' : e.code === 3 ? 'timeout' : 'unavailable' }),
            { enableHighAccuracy: true, timeout: 10000, maximumAge: 60000 });
    });
}
