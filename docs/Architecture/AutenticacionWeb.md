# Autenticación de la UI web (Cookie) — Patch 0067A

Completa un gap que quedó abierto al final de la Epic Q (Seguridad y endurecimiento,
Patches 0058-0071/PATCH-009 a PATCH-019): la API quedó protegida con API Key, pero el
frontend nunca fue adaptado para enviarla — toda la UI respondía 401. Este documento
cubre exclusivamente el mecanismo nuevo (Cookie Authentication para la UI); para API
Key ver `docs/Architecture/ConfiguracionCredenciales.md` (Patch 0062, credenciales de
Postgres, mismo criterio) y los doc-comments de `ApiKeyAuthenticationHandler`.

## Los dos mecanismos, y cuándo usar cada uno

| Mecanismo | Para qué | Cómo se presenta |
|---|---|---|
| **Cookie Authentication** | La interfaz web (`dashboard.html`, `movements.html`, etc.) | Login una vez en `/login.html`; el navegador reenvía la cookie solo en cada request, sin código adicional en cada página |
| **API Key** (`X-Api-Key`, sin cambios desde el Patch 0058) | Integraciones externas, scripts, herramientas, el futuro cliente MCP por HTTP | El caller arma la request a mano y adjunta el header explícitamente |

Ambos coexisten sobre los mismos endpoints protegidos (`.RequireAuthorization()`, sin
cambios en ningún grupo desde los Patches 0059-0061): cada request se resuelve, sola,
según si trae el header `X-Api-Key` (usa el esquema ApiKey) o no (usa el esquema
Cookie) — ver `ApiAuthenticationServiceCollectionExtensions.AddApiAuthentication`.
Ninguno de los dos "gana" por sobre el otro ni hace falta elegir uno: un script con
`X-Api-Key` sigue funcionando exactamente igual que antes; un navegador con sesión
iniciada también.

## Cómo configurar la contraseña inicial

Nueva sección de configuración, independiente de `ApiAuthentication:ApiKey` (para que
rotar una no obligue a rotar la otra):

```json
"WebAuthentication": {
  "Password": ""
}
```

Vacía por defecto en los `appsettings.json` versionados (mismo criterio que el Patch
0062 para credenciales) — hay que configurarla para poder iniciar sesión:

**Desarrollo, vía User Secrets** (no queda en ningún archivo versionado):

```bash
cd src/FinancialMcp.Api
dotnet user-secrets set "WebAuthentication:Password" "tu-contraseña"
```

**Cualquier entorno, vía variable de entorno estándar de .NET**:

```bash
export WebAuthentication__Password="tu-contraseña"
```

**Alternativa explícita** (mismo patrón que `FINANCIALMCP_API_KEY`):

```bash
export FINANCIALMCP_WEB_PASSWORD="tu-contraseña"
```

Si la contraseña queda vacía o ausente, `POST /api/auth/login` rechaza cualquier
intento (mismo criterio que `ApiKeyAuthenticationHandler`: una configuración ausente
nunca autentica a nadie, no "acepta cualquier cosa").

## Cómo iniciar sesión

1. Abrir `/login.html` (o dejar que `auth-guard.js` redirija ahí automáticamente al
   entrar a cualquier pantalla sin sesión válida).
2. Ingresar la contraseña configurada arriba.
3. El servidor, si coincide, emite una cookie de sesión (`FinancialMcp.Auth`,
   `HttpOnly`, `SameSite=Strict`, 14 días con expiración deslizante). El navegador la
   guarda y la reenvía solo — ninguna página necesita código adicional para eso.
4. Redirige a `dashboard.html` (o a la página que se intentaba abrir originalmente, si
   se llegó a `/login.html` vía una redirección de `auth-guard.js`).

Para cerrar sesión: el link "Cerrar sesión" que `auth-guard.js` agrega en cada
pantalla (o `POST /api/auth/logout` directamente).

## Qué NO incluye este módulo (a propósito)

* Registro de usuarios — una única contraseña compartida, aplicación single-user.
* Recuperación de contraseña — si se pierde, se reconfigura por los mismos medios de
  arriba (User Secrets/variable de entorno), no hay flujo de "olvidé mi contraseña".
* Multi-usuario, roles, permisos — fuera del alcance de esta épica, igual que la
  infraestructura de ApiKey del Patch 0058 tampoco los tiene.

## Qué NO cambió

* Ningún endpoint de negocio (`/api/categories`, `/api/movements`, etc.) ni su nivel
  de protección — todos los `.RequireAuthorization()` de los Patches 0059-0061 siguen
  intactos.
* `ApiKeyAuthenticationHandler`, `ApiAuthenticationOptions` — sin ningún cambio.
* `AddApiKeyAuthentication` (el método original del Patch 0058) sigue existiendo tal
  cual, sin tocarse — varios tests de patches anteriores lo llaman directamente.
  `Program.cs` ahora llama a `AddApiAuthentication` (nuevo) en su lugar.
* Ningún `fetch()` de `dashboard.html`/`movements.html`/`planning.html`/etc. se
  modificó — la única adición por página es una línea
  `<script src="/auth-guard.js"></script>`.
