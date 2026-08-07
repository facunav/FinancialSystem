// Patch 0102 (Épica UI, primer paso de la consolidación de frontend -- ver
// docs/Architecture/PRUI1analisisarquitecturaui.md y la auditoría de Épica UI).
//
// Extrae exclusivamente esc() y showToast(): eran las dos únicas utilidades
// confirmadas byte-idénticas en todas las páginas que las tenían (esc() en
// las 7 páginas protegidas; showToast() en movements/accounts/imports/
// counterparties/audit -- dashboard.html y planning.html usan su propia
// showError(), sin relación, sin tocar acá).
//
// Patch 0103 (Épica UI, segundo paso) suma acá las utilidades HTTP:
// getJson()/getJsonOrNull()/postJson()/putJson()/deleteJson() y el helper
// interno extractErrorMessage(). Existían tres variantes de getJson() con
// manejo de error distinto -- ver Resumen de cambios del patch 0103 para el
// detalle del análisis. Se adoptó la variante de planning.html (la más
// completa: distingue 204, delega en extractErrorMessage(), y suma
// getJsonOrNull() para el caso "puede no existir" que ya usaba esa página)
// como implementación única.
//
// Formateo de fecha y de moneda siguen duplicados a propósito, para
// mantener el alcance de cada patch acotado -- quedan como candidatas para
// un patch futuro, ver Observaciones del patch 0103.
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

// ─────────────────────────────────────────────────────────────────────────
// HTTP -- helpers de bajo nivel (fetch + manejo de error), sin saber nada
// de ninguna página en particular. Todos los endpoints de la API devuelven,
// ante un error, o bien un string plano (BadRequest/Conflict/NotFound) o
// bien un ProblemDetails ({ detail, title, ... }); extractErrorMessage()
// cubre ambos casos y cae a "status statusText" si el body ni siquiera es
// JSON válido (por ejemplo un 500 sin cuerpo, o HTML de un proxy).
// ─────────────────────────────────────────────────────────────────────────

async function extractErrorMessage(r) {
    const d = await r.json().catch(() => null);
    return typeof d === 'string' ? d : (d?.detail ?? d?.title ?? `${r.status} ${r.statusText}`);
}

async function getJson(url) {
    const r = await fetch(url);
    if (!r.ok) throw new Error(await extractErrorMessage(r));
    return r.json();
}

// Como getJson(), pero devuelve null en vez de lanzar ante un 404 -- para
// consultas donde "no existe" es un resultado válido (GET por período,
// GET /latest), a diferencia de un 404 que sí es un error real.
async function getJsonOrNull(url) {
    const r = await fetch(url);
    if (r.status === 404) return null;
    if (!r.ok) throw new Error(await extractErrorMessage(r));
    return r.json();
}

async function handleWriteResponse(r) {
    if (r.status === 204) return null;
    if (!r.ok) throw new Error(await extractErrorMessage(r));
    return r.json().catch(() => null);
}

async function postJson(url, body) {
    const opts = { method: 'POST' };
    if (body !== undefined) {
        opts.headers = { 'Content-Type': 'application/json' };
        opts.body = JSON.stringify(body);
    }
    return handleWriteResponse(await fetch(url, opts));
}

async function putJson(url, body) {
    const opts = {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
    };
    return handleWriteResponse(await fetch(url, opts));
}

async function deleteJson(url) {
    return handleWriteResponse(await fetch(url, { method: 'DELETE' }));
}
