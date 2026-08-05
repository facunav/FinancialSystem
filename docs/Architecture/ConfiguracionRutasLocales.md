# Configuración de rutas locales (Worker) — sin rutas personales versionadas

Documento operativo del Patch 0067 (PATCH-018, épica de Seguridad y endurecimiento).
Mismo criterio que `docs/Architecture/ConfiguracionCredenciales.md` (Patch 0062,
credenciales de Postgres), aplicado acá a rutas de archivos específicas de una
máquina o de un desarrollador en particular.

## Qué cambió

`hosts/FinancialSystem.Worker/appsettings.json` y `appsettings.Development.json`
traían `FileIngestion:ImportsPath` con una ruta absoluta real de un entorno de
desarrollo puntual (`C:\...\Imports`). Ambos archivos quedan ahora con el valor
vacío:

```json
"FileIngestion": {
  "ImportsPath": ""
}
```

## Por qué esto no rompe nada por defecto

`ImportsFolderWatcherHostedService.ResolveImportsPath()` (el único consumidor de este
valor) ya tenía, desde antes de este patch, un fallback explícito para el caso vacío:

```
Si ImportsPath está vacío o ausente → usa <directorio del ejecutable>/imports
Si ImportsPath es una ruta relativa → se resuelve contra el ContentRootPath del host
Si ImportsPath es una ruta absoluta → se usa tal cual
```

La carpeta se crea automáticamente si no existe (`Directory.CreateDirectory`). Con el
repositorio recién clonado, el Worker arranca y observa
`<carpeta de salida del build>/imports` sin que nadie configure nada — un default
portable, válido en cualquier máquina, sin depender de la estructura de carpetas de
ningún desarrollador en particular.

## Cómo configurar una ruta propia (opcional)

Para observar una carpeta específica (por ejemplo, la carpeta de descargas del banco),
sin versionarla:

**Desarrollo local**, editando `hosts/FinancialSystem.Worker/appsettings.Development.json`
(archivo versionado, pero pensado para poder pisarse localmente sin commitear el
cambio -- ver la nota más abajo):

```json
"FileIngestion": {
  "ImportsPath": "/ruta/absoluta/a/tu/carpeta/de/importaciones"
}
```

**Cualquier entorno, vía variable de entorno estándar de .NET** (sin tocar ningún
archivo versionado, mismo mecanismo ya documentado para `ConnectionStrings__Postgres`):

```bash
export FileIngestion__ImportsPath="/ruta/absoluta/a/tu/carpeta/de/importaciones"
```

En Windows, cualquiera de las dos formas acepta una ruta con el mismo formato que ya
usa el resto de la configuración de Windows (por ejemplo `C:\ImportsFinancialMcp`) --
el punto de este patch no es prohibir rutas de Windows, es no dejar la ruta real de
un desarrollador puntual versionada en git.

> **Nota:** si editás `appsettings.Development.json` localmente para apuntar a tu
> propia carpeta, evitá commitear ese cambio (`git update-index --skip-worktree
> hosts/FinancialSystem.Worker/appsettings.Development.json` si querés que git ignore
> silenciosamente tus ediciones locales a ese archivo, o simplemente prestá atención a
> no incluirlo en el commit). El valor por defecto que sí queda versionado debe seguir
> siendo `""`.

## Qué NO cambió

* El comportamiento cuando `ImportsPath` está correctamente configurado (ruta relativa
  o absoluta) -- cero cambios en `ResolveImportsPath()` ni en el resto del pipeline de
  importación.
* El ejemplo ya genérico de `_ImportsPathHelp` (dentro del propio `appsettings.json`),
  que ya usaba `C:\imports` como ilustración neutra, sin ninguna referencia a una
  máquina real.
