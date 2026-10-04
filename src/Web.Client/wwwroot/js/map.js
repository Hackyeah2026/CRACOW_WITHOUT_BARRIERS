// Mapa Leaflet sterowana z komponentu MapView.
const maps = {};

const WALK_STYLE = { color: '#0B4F8A', weight: 5, opacity: 0.8, dashArray: '6 6' };
const TRANSIT_STYLE = { color: '#7A2E8F', weight: 6, opacity: 0.9 };
// Najkrótsza droga, którą trasa omija z powodu potwierdzonego utrudnienia.
const BYPASSED_STYLE = { color: '#BA1A1A', weight: 4, opacity: 0.9, dashArray: '2 8' };

// Kolejność prób ustawienia stałej etykiety względem pinezki.
const LABEL_OFFSETS = { top: [0, -16], right: [16, 0], left: [-16, 0], bottom: [0, 16] };

// O ile pikseli strzałka klawiatury przesuwa pinezkę wybieranego punktu.
const PICK_STEP_PX = 10;

export function init(id, lat, lon, zoom, dotnet, reportBounds, pickable, pickLabel) {
    // Canvas zamiast SVG: katalog całego miasta to kilka tysięcy pinezek.
    const map = L.map(id, { preferCanvas: true }).setView([lat, lon], zoom);
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '&copy; autorzy <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
    }).addTo(map);

    const entry = {
        map, dotnet, frame: map.getContainer().parentElement,
        markers: L.layerGroup().addTo(map), route: L.layerGroup().addTo(map),
        labelled: [], routeLabels: [], allBounds: null, pick: null, pickLabel: pickLabel || 'Punkt startu'
    };
    maps[id] = entry;

    map.on('zoomend', () => layoutLabels(entry));
    entry.frame.addEventListener('fullscreenchange', () => fullscreenChanged(entry, document.fullscreenElement === entry.frame));
    entry.onKeydown = e => {
        if (e.key === 'Escape') setCssFullscreen(entry, false);
    };

    if (reportBounds) {
        // moveend przychodzi po przesunięciu i po zmianie powiększenia; krótka zwłoka scala serię zdarzeń w jedno zgłoszenie.
        const report = () => {
            const b = map.getBounds();
            dotnet.invokeMethodAsync('BoundsChanged', b.getSouth(), b.getWest(), b.getNorth(), b.getEast());
        };
        map.on('moveend', () => {
            clearTimeout(entry.boundsTimer);
            entry.boundsTimer = setTimeout(report, 150);
        });
        report();
    }

    if (pickable) {
        map.on('click', e => {
            placePick(entry, e.latlng);
            pickChanged(entry);
        });
    }
}

// Nazwy pochodzą z danych zewnętrznych, więc trafiają do etykiet jako tekst, nie HTML.
function text(value) {
    const span = document.createElement('span');
    span.textContent = value;
    return span;
}

// Ikona Material Symbols; nazwa ikony to ligatura czcionki.
function symbol(name, color, fill) {
    const span = document.createElement('span');
    span.className = fill ? 'msym msym-fill' : 'msym';
    span.textContent = name;
    if (color) span.style.color = color;
    return span;
}

function iconMarker(item, html, className, size) {
    const icon = L.divIcon({ html, className, iconSize: [size, size], iconAnchor: [size / 2, size / 2] });
    return L.marker([item.lat, item.lon], { icon, keyboard: true });
}

export function setMarkers(id, items, fit) {
    const entry = maps[id];
    if (!entry) return;
    entry.markers.clearLayers();
    entry.labelled = [];

    for (const item of items) {
        let marker;
        if (item.label) {
            // Przystanek planu: numer w kolorowej pinezce, nazwa w stałej etykiecie.
            const pin = document.createElement('span');
            pin.className = 'map-pin';
            pin.style.background = item.color;
            pin.textContent = item.label;
            marker = iconMarker(item, pin, 'map-pin-icon', 30);
            entry.labelled.push({ marker, content: text(`${item.label}. ${item.name}`) });
        } else if (item.certified) {
            // Miejsce z certyfikatem: gwiazdka w złotej obwódce, kolor wypełnienia nadal mówi o ocenie.
            const pin = document.createElement('span');
            pin.className = 'map-certified msym-fill';
            pin.style.background = item.color;
            pin.textContent = 'star';
            marker = iconMarker(item, pin, 'map-certified-icon', 34);
            marker.setZIndexOffset(500);
            marker.bindTooltip(text(item.name), { direction: 'top', offset: [0, -16] });
        } else if (item.icon) {
            // Punkt odpoczynku (toaleta, ławka, ciche miejsce): sam symbol, bez kółka.
            marker = iconMarker(item, symbol(item.icon, item.color), 'map-symbol-icon', 28);
            marker.bindTooltip(text(item.name), { direction: 'top', offset: [0, -12] });
        } else {
            marker = L.circleMarker([item.lat, item.lon], {
                radius: 8, color: '#191C20', weight: 1.5, fillColor: item.color, fillOpacity: 0.95
            });
            marker.bindTooltip(text(item.name));
        }
        marker.on('click', () => entry.dotnet.invokeMethodAsync('MarkerClicked', item.id));
        marker.addTo(entry.markers);
        if (item.label || item.icon || item.certified)
            marker.getElement()?.setAttribute('aria-label', item.name);
    }

    entry.allBounds = items.length > 0 ? L.latLngBounds(items.map(i => [i.lat, i.lon])).pad(0.35) : null;
    entry.bounds = fit ? entry.allBounds : null;
    fitAndLayout(entry);
}

export function showAllMarkers(id) {
    const entry = maps[id];
    if (entry?.allBounds) entry.map.fitBounds(entry.allBounds, { animate: false, maxZoom: 17 });
}

// Przesuwalna pinezka wybieranego punktu (start planu albo zgłaszane utrudnienie). point: { lat, lon } albo null, gdy punkt nie jest wybrany.
export function setPick(id, point) {
    const entry = maps[id];
    if (!entry) return;
    if (!point) {
        entry.pick?.remove();
        entry.pick = null;
        return;
    }
    placePick(entry, L.latLng(point.lat, point.lon));
}

function placePick(entry, latlng) {
    if (entry.pick) {
        entry.pick.setLatLng(latlng);
    } else {
        const icon = L.divIcon({ html: symbol('location_on', null, true), className: 'map-pick-icon', iconSize: [40, 40], iconAnchor: [20, 38] });
        entry.pick = L.marker(latlng, { icon, draggable: true, keyboard: true, zIndexOffset: 1000 }).addTo(entry.map);
        entry.pick.bindTooltip(text(`${entry.pickLabel}: przeciągnij, żeby poprawić`), { direction: 'top', offset: [0, -38] });
        entry.pick.on('dragend', () => pickChanged(entry));

        const element = entry.pick.getElement();
        element.setAttribute('aria-label', `${entry.pickLabel}. Strzałki przesuwają pinezkę.`);
        element.addEventListener('keydown', e => {
            const step = { ArrowUp: [0, -1], ArrowDown: [0, 1], ArrowLeft: [-1, 0], ArrowRight: [1, 0] }[e.key];
            if (!step) return;
            // Strzałki przesuwają pinezkę, a nie mapę.
            e.preventDefault();
            e.stopPropagation();
            const at = entry.map.latLngToContainerPoint(entry.pick.getLatLng());
            entry.pick.setLatLng(entry.map.containerPointToLatLng([at.x + step[0] * PICK_STEP_PX, at.y + step[1] * PICK_STEP_PX]));
            reveal(entry);
            clearTimeout(entry.pickTimer);
            entry.pickTimer = setTimeout(() => pickChanged(entry), 300);
        });
    }
    reveal(entry);
}

// Pinezka poza kadrem (np. lokalizacja daleko od wybranych miejsc): przesuwamy mapę, żeby była widoczna.
function reveal(entry) {
    const latlng = entry.pick.getLatLng();
    if (!entry.map.getBounds().pad(-0.1).contains(latlng)) entry.map.panTo(latlng, { animate: false });
}

function pickChanged(entry) {
    const latlng = entry.pick.getLatLng();
    entry.dotnet.invokeMethodAsync('PointPicked', latlng.lat, latlng.lng);
}

// segments: [{ points: [{lat, lon}], isTransit, label, isBypassed }]
export function setRoute(id, segments) {
    const entry = maps[id];
    if (!entry) return;
    entry.route.clearLayers();
    entry.routeLabels = [];

    for (const segment of segments) {
        if (segment.points.length < 2) continue;
        const style = segment.isBypassed ? BYPASSED_STYLE : segment.isTransit ? TRANSIT_STYLE : WALK_STYLE;
        const line = L.polyline(segment.points.map(p => [p.lat, p.lon]), style).addTo(entry.route);
        if (segment.label) {
            line.bindTooltip(text(segment.label), { permanent: true, direction: 'center', className: 'map-label-transit' });
            entry.routeLabels.push(line);
        }
    }
    layoutLabels(entry);
}

function fitAndLayout(entry) {
    if (entry.bounds) entry.map.fitBounds(entry.bounds, { animate: false, maxZoom: 17 });
    layoutLabels(entry);
}

function overlaps(a, b) {
    return a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom;
}

// Stałe etykiety przystanków nie mogą zasłaniać pinezek, etykiet linii ani siebie nawzajem.
// Dla każdej próbujemy kolejnych stron pinezki: najlepiej wolna i w całości na mapie, w drugiej kolejności tylko wolna.
// Gdy żadna strona nie jest wolna, etykieta pokazuje się dopiero po wskazaniu pinezki.
function layoutLabels(entry) {
    const bounds = entry.map.getContainer().getBoundingClientRect();
    const inside = r => r.left >= bounds.left && r.right <= bounds.right && r.top >= bounds.top && r.bottom <= bounds.bottom;
    const taken = [
        ...entry.labelled.map(l => l.marker.getElement()),
        ...entry.routeLabels.map(l => l.getTooltip()?.getElement())
    ].filter(Boolean).map(e => e.getBoundingClientRect());

    for (const { marker, content } of entry.labelled) {
        const place = direction => {
            marker.unbindTooltip();
            marker.bindTooltip(content, { permanent: true, direction, offset: LABEL_OFFSETS[direction], className: 'map-label' });
            return marker.getTooltip().getElement()?.getBoundingClientRect();
        };

        const free = Object.keys(LABEL_OFFSETS)
            .map(direction => ({ direction, rect: place(direction) }))
            .filter(c => c.rect && !taken.some(t => overlaps(t, c.rect)));
        const best = free.find(c => inside(c.rect)) ?? free[0];

        if (best) {
            taken.push(place(best.direction));
        } else {
            marker.unbindTooltip();
            marker.bindTooltip(content, { direction: 'top', offset: LABEL_OFFSETS.top, className: 'map-label' });
        }
    }
}

export async function toggleFullscreen(id) {
    const entry = maps[id];
    if (!entry) return;
    const frame = entry.frame;

    if (frame.classList.contains('map-frame-fullscreen')) {
        setCssFullscreen(entry, false);
    } else if (document.fullscreenElement === frame) {
        await document.exitFullscreen();
    } else {
        try {
            await frame.requestFullscreen();
        } catch {
            // Brak Fullscreen API (np. iPhone) albo odmowa przeglądarki: rozciągamy mapę na całe okno stylami.
            setCssFullscreen(entry, true);
        }
    }
}

function setCssFullscreen(entry, on) {
    entry.frame.classList.toggle('map-frame-fullscreen', on);
    if (on) document.addEventListener('keydown', entry.onKeydown);
    else document.removeEventListener('keydown', entry.onKeydown);
    fullscreenChanged(entry, on);
}

function fullscreenChanged(entry, on) {
    // Po zmianie rozmiaru dopasowujemy widok na nowo, żeby plan wypełnił cały ekran.
    entry.map.invalidateSize();
    fitAndLayout(entry);
    entry.dotnet.invokeMethodAsync('FullscreenChanged', on);
}

export function dispose(id) {
    const entry = maps[id];
    if (!entry) return;
    document.removeEventListener('keydown', entry.onKeydown);
    clearTimeout(entry.boundsTimer);
    clearTimeout(entry.pickTimer);
    if (document.fullscreenElement === entry.frame) document.exitFullscreen();
    entry.map.remove();
    delete maps[id];
}
