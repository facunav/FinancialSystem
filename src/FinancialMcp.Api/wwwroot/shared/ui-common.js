// Patch 0102 (Épica UI, primer paso de la consolidación de frontend -- ver
// docs/Architecture/PRUI1analisisarquitecturaui.md y la auditoría de Épica UI).
//
// Extrae exclusivamente esc() y showToast(): eran las dos únicas utilidades
// confirmadas byte-idénticas en todas las páginas que las tenían (esc() en
// las 7 páginas protegidas; showToast() en movements/accounts/imports/
// counterparties/audit -- dashboard.html y planning.html usan su propia
// showError(), sin relación, sin tocar acá).
//
// Ninguna otra utilidad se movió en este patch (getJson/postJson/putJson/
// deleteJson, formateo de fecha, formateo de moneda siguen duplicadas a
// propósito, para mantener el alcance acotado -- quedan como candidatas
// para un patch futuro, ver Observaciones del patch 0102).
//
// Ubicación: wwwroot/shared/, para que este archivo pueda crecer en
// patches futuros sin mezclar utilidades de UI con las páginas mismas.
// auth-guard.js (Patch 0067A) queda donde está, en la raíz de wwwroot/ --
// tiene una responsabilidad propia y completamente distinta (guardia de
// sesión), no forma parte de este paso de consolidación.
//
// Incluir con <script src="/shared/ui-common.js"></script> ANTES del
// <script> inline de cada página -- showToast() llama a esc(), y ambas
// deben estar definidas antes de que el código de la página las use. Sin
// módulos ni IIFE, a propósito: mismo estilo que el resto del frontend
// (scripts planos en scope global, sin build ni bundler).

function esc(s) {
    return String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

function showToast(elId, msg, type, ms = 3500) {
    const el = document.getElementById(elId);
    el.innerHTML = `<span class="toast ${type}">${esc(msg)}</span>`;
    setTimeout(() => { if (el) el.innerHTML = ''; }, ms);
}
