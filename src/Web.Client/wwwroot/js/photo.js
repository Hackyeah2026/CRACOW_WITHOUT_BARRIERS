// Zmniejsza zdjęcie przed dołączeniem do zgłoszenia: kolejne próby mają mniejszy bok i niższą jakość,
// aż plik zmieści się w limicie. Zwraca bajty JPEG albo null, gdy się nie udało.
const ATTEMPTS = [[1, 0.72], [0.8, 0.6], [0.6, 0.5], [0.45, 0.4]];

export async function shrink(bytes, maxDimension, maxBytes) {
    const bitmap = await createImageBitmap(new Blob([bytes], { type: 'image/jpeg' }));
    try {
        for (const [factor, quality] of ATTEMPTS) {
            const limit = maxDimension * factor;
            const scale = Math.min(1, limit / Math.max(bitmap.width, bitmap.height));
            const canvas = document.createElement('canvas');
            canvas.width = Math.max(1, Math.round(bitmap.width * scale));
            canvas.height = Math.max(1, Math.round(bitmap.height * scale));
            canvas.getContext('2d').drawImage(bitmap, 0, 0, canvas.width, canvas.height);

            const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', quality));
            if (blob && blob.size <= maxBytes) return new Uint8Array(await blob.arrayBuffer());
        }
        return null;
    } finally {
        bitmap.close();
    }
}
