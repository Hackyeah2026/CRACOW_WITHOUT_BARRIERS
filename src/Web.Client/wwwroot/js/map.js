// Mapa Leaflet sterowana z komponentu MapView.
const maps = {};

export function init(id, lat, lon, zoom, dotnet) {
    const map = L.map(id).setView([lat, lon], zoom);
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '&copy; autorzy <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
    }).addTo(map);
    maps[id] = { map, markers: L.layerGroup().addTo(map), route: L.layerGroup().addTo(map), dotnet };
}

export function setMarkers(id, items, fit) {
    const entry = maps[id];
    if (!entry) return;
    entry.markers.clearLayers();
    for (const item of items) {
        const marker = L.circleMarker([item.lat, item.lon], {
            radius: item.label ? 13 : 8, color: '#1b1b1b', weight: 1.5, fillColor: item.color, fillOpacity: 0.95
        });
        marker.bindTooltip(item.label ? `${item.label}. ${item.name}` : item.name, item.label
            ? { permanent: true, direction: 'top', offset: [0, -10] } : {});
        marker.on('click', () => entry.dotnet.invokeMethodAsync('MarkerClicked', item.id));
        marker.addTo(entry.markers);
    }
    if (fit && items.length > 0) {
        entry.map.fitBounds(L.latLngBounds(items.map(i => [i.lat, i.lon])).pad(0.2));
    }
}

export function setRoute(id, points) {
    const entry = maps[id];
    if (!entry) return;
    entry.route.clearLayers();
    if (points.length > 1) {
        L.polyline(points.map(p => [p.lat, p.lon]), { color: '#0b5ed7', weight: 5, opacity: 0.8, dashArray: '8 8' }).addTo(entry.route);
    }
}

export function dispose(id) {
    maps[id]?.map.remove();
    delete maps[id];
}
