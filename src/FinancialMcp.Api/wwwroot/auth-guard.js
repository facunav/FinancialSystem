// Patch 0067A: guardia de sesión para la UI web. Incluido (una sola línea por
// página, <script src="/auth-guard.js"></script> al principio de <head>) en cada
// pantalla protegida -- nunca en login.html.
//
// Deliberadamente NO se modificó ningún fetch() existente de ninguna pantalla (así lo
// pedía el patch): la cookie de sesión viaja sola en cada request same-origin, así
// que las llamadas a la API que ya tenía cada página siguen funcionando sin ningún
// cambio una vez que existe una sesión válida. Este script solo se ocupa de dos cosas
// que ninguna pantalla resolvía: redirigir a login.html si no hay sesión, y ofrecer
// un control para cerrarla.
//
// No es, ni pretende ser, el límite de seguridad -- eso lo sigue haciendo el servidor
// (.RequireAuthorization() en cada endpoint). Es pura UX: sin este script, una
// request no autenticada de todas formas recibe 401 del backend: acá solo evitamos
// que el usuario se quede mirando una pantalla rota sin saber por qué.
(function () {
    function redirectToLogin() {
        var next = encodeURIComponent(window.location.pathname + window.location.search);
        window.location.href = '/login.html?next=' + next;
    }

    function injectLogoutControl() {
        var link = document.createElement('a');
        link.textContent = 'Cerrar sesión';
        link.href = '#';
        link.style.cssText = 'position:fixed;bottom:10px;right:14px;font:12px sans-serif;' +
            'color:var(--dim,#8b98b0);text-decoration:none;z-index:50;opacity:.8;';
        link.addEventListener('mouseenter', function () { link.style.opacity = '1'; });
        link.addEventListener('mouseleave', function () { link.style.opacity = '.8'; });
        link.addEventListener('click', function (e) {
            e.preventDefault();
            fetch('/api/auth/logout', { method: 'POST', credentials: 'same-origin' })
                .catch(function () { /* logout es best-effort del lado del cliente */ })
                .then(function () { window.location.href = '/login.html'; });
        });
        document.body.appendChild(link);
    }

    fetch('/api/auth/session', { credentials: 'same-origin' })
        .then(function (response) {
            if (!response.ok) {
                redirectToLogin();
                return;
            }
            if (document.body) {
                injectLogoutControl();
            } else {
                document.addEventListener('DOMContentLoaded', injectLogoutControl);
            }
        })
        .catch(redirectToLogin);
})();
