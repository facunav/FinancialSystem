# Validación estructurada — módulo piloto (Patch 0065, PATCH-016)

Este patch introduce [FluentValidation](https://docs.fluentvalidation.net/) como
mecanismo de validación estructurada para `FinancialMcp.Api`, aplicado únicamente al
módulo **Categories** (`CategoryEndpoints.cs`) como piloto. No reemplaza la validación
manual del resto de la API — esa migración, si se decide hacer, es trabajo de patches
futuros, module por módulo.

## Por qué FluentValidation

- Es la librería de validación de facto para .NET/ASP.NET Core (activamente
  mantenida, sin dependencias pesadas, sin acoplar la regla de validación a ningún
  otro framework).
- Se integra vía inyección de dependencias estándar
  (`FluentValidation.DependencyInjectionExtensions` + `AddValidatorsFromAssemblyContaining<T>()`
  en `Program.cs`), sin necesitar ningún framework propio de validación.
- Las reglas quedan en clases dedicadas (`AbstractValidator<T>`), completamente
  separadas de los endpoints, del acceso a datos y de la lógica de negocio — ver
  sección "Arquitectura" del patch.

## Por qué Categories como piloto

Es el módulo de datos maestros más simple del catálogo (Cuentas/Categorías/
Contrapartes, protegidas en el Patch 0060): dos DTOs (`CreateCategoryRequest`,
`UpdateCategoryRequest`), un puñado de validaciones manuales (`string.IsNullOrWhiteSpace`)
y límites de longitud ya declarados en `CategoryConfiguration` (Infrastructure) pero
nunca validados antes de llegar a la base de datos — un caso representativo sin ser el
módulo más riesgoso para experimentar el patrón por primera vez.

## Patrón de integración (para migrar otro módulo más adelante)

1. Crear `Validation/<NombreDelRequest>Validator.cs`, heredando de
   `AbstractValidator<TRequest>` (ver `CreateCategoryRequestValidator`/
   `UpdateCategoryRequestValidator` como referencia).
2. Los límites de longitud que ya existan en la configuración de EF Core
   (`HasMaxLength`) se repiten como constantes en el validador -- la capa Api no
   referencia Infrastructure, así que no hay una única fuente de verdad compartida en
   código; si el límite de columna cambia, hay que actualizar el validador a mano.
3. En el endpoint, inyectar `IValidator<TRequest>` vía `[FromServices]`, llamar
   `await validator.ValidateAsync(request, ct)` al principio del handler, y si
   `!result.IsValid` devolver `Results.BadRequest(...)` con los mensajes unidos --
   **nunca lanzar una excepción por un error de validación** (sección 3 del patch).
4. El formato de la respuesta de error se mantiene igual al que ya usaba el endpoint
   (`Results.BadRequest(string)`) -- no se introduce `Results.ValidationProblem()` ni
   ningún otro formato nuevo en este patch, para no romper compatibilidad con
   clientes existentes de un módulo que hoy no lo espera. Un patch futuro que decida
   adoptar un formato de error estructurado distinto (por ejemplo alineado con el
   Problem Details del Patch 0064) debería hacerlo de forma explícita y para toda la
   API a la vez, no módulo por módulo.
5. Registrar el validador: si vive en el mismo assembly que
   `CreateCategoryRequestValidator`, `AddValidatorsFromAssemblyContaining<CreateCategoryRequestValidator>()`
   en `Program.cs` ya lo detecta automáticamente (no hace falta registrarlo a mano).
