// Blazor.DriverJs przekazuje do .NET numer aktywnego kroku także wtedy, gdy driver.js go nie ma
// (przed pierwszym krokiem i po zamknięciu). Brak numeru kończył się wyjątkiem w konsoli, więc zastępujemy go -1.
(() => {
    const create = window.driver.js.driver;
    window.driver.js.driver = (config) => {
        const instance = create(config);
        return { ...instance, getActiveIndex: () => instance.getActiveIndex() ?? -1 };
    };
})();
