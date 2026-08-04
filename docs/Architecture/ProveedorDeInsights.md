# Proveedor de insights financieros (Worker) — qué sale del sistema y cuándo

Documento operativo del Patch 0066 (PATCH-017, épica de Seguridad y endurecimiento).
Cubre exclusivamente `TransactionInsightsWorker` (`hosts/FinancialSystem.Worker`), el
proceso en background que genera insights financieros ("gastos hormiga", categorías
dominantes, suscripciones, hábitos repetitivos) a partir de las transacciones
importadas. No cubre ningún otro módulo del sistema.

## Resumen

Por default, **ningún dato financiero sale de la máquina donde corre el sistema**. El
Worker usa [Ollama](https://ollama.com/) (un modelo de lenguaje local) salvo que un
operador elija explícitamente lo contrario en la configuración. OpenAI nunca se activa
por sí solo, ni siquiera si `OpenAI:ApiKey` está configurada — hace falta elegirlo a
propósito.

## Qué información sale del sistema si se habilita OpenAI

Cuando `InsightsWorker:Provider` es `OpenAI` o `Both`, cada ciclo del Worker envía a la
API de OpenAI (`https://api.openai.com/v1/chat/completions` por default), por cada
transacción del lote (`TransactionBatchSize`, 50 por default):

* Fecha (`Date`)
* Descripción (`Description`) — texto crudo tal como quedó importado del extracto
  bancario/tarjeta, puede incluir el nombre del comercio o la persona
* Monto (`Amount`)
* Moneda (`Currency`)

No se envían: nombre del titular, número de cuenta/tarjeta, categoría, contraparte,
ni ningún otro dato del sistema — solo esos cuatro campos por transacción (ver
`TransactionSummary`, `OpenAIFinancialInsightsService.BuildUserPrompt`).

## Cuándo ocurre

Únicamente durante el ciclo periódico de `TransactionInsightsWorker`
(`InsightsWorker:IntervalMinutes`, 5 minutos por default), y solo si
`InsightsWorker:Enabled` es `true` y `InsightsWorker:Provider` es `OpenAI` o `Both`.
Ningún otro componente del sistema (API, servidor MCP, importación) llama a la API de
OpenAI — `IOpenAIFinancialInsightsService` solo lo consume este Worker.

## Cómo elegir el proveedor

`hosts/FinancialSystem.Worker/appsettings.json`, sección `InsightsWorker`:

```json
"InsightsWorker": {
  "Enabled": true,
  "Provider": "Ollama"
}
```

`Provider` acepta exactamente estos valores (ver `InsightsProviderSelector.Resolve`):

| Valor | Qué corre | Sale algún dato del sistema |
|---|---|---|
| `None` | Nada — el Worker sigue corriendo pero no genera insights | No |
| `Ollama` (**default**) | Solo Ollama, local | No |
| `OpenAI` | Solo OpenAI | **Sí** — ver tabla de arriba |
| `Both` | Ollama y OpenAI | **Sí** (por la parte de OpenAI) |

Cualquier otro valor (vacío, con un typo) se trata como no reconocido: el Worker lo
loguea como advertencia y no genera insights en ese ciclo — nunca "cae" en OpenAI por
default ni por error de tipeo.

## Cómo habilitar OpenAI (decisión explícita del operador)

1. Cambiar `InsightsWorker:Provider` a `"OpenAI"` o `"Both"` en
   `appsettings.json`/`appsettings.{Environment}.json`, o vía variable de entorno
   estándar de .NET (`InsightsWorker__Provider=OpenAI`).
2. Configurar `OpenAI:ApiKey` — vía User Secrets (`dotnet user-secrets set "OpenAI:ApiKey" "sk-..."`
   parado en `hosts/FinancialSystem.Worker`) o la variable de entorno
   `OPENAI_API_KEY` (ver `OpenAIOptions`/`DependencyInjection.AddInfrastructure`).
   Configurar solo la API key, sin cambiar `Provider`, **no habilita OpenAI** — ambos
   pasos son independientes y necesarios (sección 5 del patch).
3. Reiniciar el Worker.

## Cómo deshabilitarlo

Volver `InsightsWorker:Provider` a `"Ollama"` (o `"None"` para no generar insights con
ningún proveedor) y reiniciar el Worker. No hace falta borrar `OpenAI:ApiKey` — con
`Provider` en `Ollama`/`None` esa clave queda configurada pero sin ningún uso.

## Qué NO cambió con este patch

* Los prompts, el formato de las respuestas, y el pipeline de generación de insights
  en sí (`OpenAIFinancialInsightsService`/`OllamaFinancialInsightsService`) — cero
  cambios de lógica ahí.
* El comportamiento con `Provider` ya en `Ollama`, `OpenAI`, o `Both` — el default ya
  era `Ollama` antes de este patch; lo que se cierra acá es la posibilidad de que un
  valor vacío o mal escrito quedara ambiguo, y se deja documentado por primera vez qué
  información concreta sale del sistema si se elige OpenAI.
