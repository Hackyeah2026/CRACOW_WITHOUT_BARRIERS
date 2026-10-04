// Okno potwierdzenia (komponent ConfirmDialog): natywny element <dialog>.
export function show(dialog) {
    if (!dialog.open) dialog.showModal();
}

export function close(dialog) {
    if (dialog.open) dialog.close();
}
