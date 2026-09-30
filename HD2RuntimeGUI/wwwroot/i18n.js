// The UI language of the page: <html lang> and dir (a right-to-left language sets dir="rtl"), and the text of the error banner that
// lives outside the Blazor root.
window.hd2Ui = {
    setLanguage(lang, dir, errorMessage, reload) {
        document.documentElement.lang = lang;
        document.documentElement.dir = dir;
        const banner = document.getElementById('blazor-error-ui');
        if (banner) {
            const text = banner.querySelector('[data-error-message]'); if (text) text.textContent = errorMessage;
            const link = banner.querySelector('.reload'); if (link) link.textContent = reload;
        }
    },
};
