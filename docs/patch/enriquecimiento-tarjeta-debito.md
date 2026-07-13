# Enriquecimiento de compras con débito usando el extracto de Tarjeta de Débito BBVA

## Problema actual

BBVA permite exportar, además de la Caja de Ahorro y los resúmenes de Visa/Mastercard, los
movimientos de la Tarjeta de Débito. Comparando extractos reales del mismo período se confirmó
que ambos archivos representan parcialmente las mismas compras: toda compra con débito que
aparece en la Tarjeta de Débito (con comercio real y hora exacta) también aparece en la Caja de
Ahorro como `"PAGO CON VISA DEBITO..."` (sin comercio, solo importe y fecha).

Si el archivo de Tarjeta de Débito se importara con el mismo mecanismo que Visa/Mastercard (cada
archivo crea movimientos nuevos), cada compra con débito quedaría duplicada: una vez desde Caja
de Ahorro y otra vez desde Tarjeta de Débito, inflando gastos en el dashboard y en la
clasificación.

## Decisión

- Caja de Ahorro sigue siendo la única fuente contable: crea todos los movimientos, como hoy.
- Tarjeta de Débito nunca crea movimientos.
- Tarjeta de Débito solo enriquece movimientos de Caja de Ahorro ya existentes (comercio y hora).
- El enriquecimiento ocurre únicamente cuando el match importe+fecha es único. Ante ambigüedad,
  no se hace nada.
- No hay tablas de staging ni movimientos "pendientes": si no hay match, se descarta y se resuelve
  solo más adelante, reimportando el archivo de Tarjeta de Débito.
- No se construye un motor de reconciliación genérico — es una relación puntual entre estas dos
  fuentes.

## Roadmap

**PR1 — Campos de enriquecimiento en `BankStatement`**
Agrega `Merchant` y `MerchantAtUtc` (nullable) a la entidad, sin lógica ni importer. Cambio sin
impacto funcional.

**PR2 — Importador de Tarjeta de Débito**
Nuevo handler que lee el extracto de Tarjeta de Débito, matchea por importe + fecha contra
`BankStatement` existentes y completa `Merchant`/`MerchantAtUtc` solo cuando el match es único.

**PR3 — Mostrar el comercio en `movements.html`**
Cuando `Merchant` no es null, se muestra en vez de la descripción genérica de Caja de Ahorro.

No se documentan PRs adicionales.

## Fuera de alcance

- Motor genérico de matching/reconciliación.
- Soporte para múltiples fuentes simultáneas (N fuentes).
- Uso de IA para resolver ambigüedades.
- Optimizaciones de performance.

## Estado esperado al terminar PR3

El usuario importa Caja de Ahorro y Tarjeta de Débito en cualquier orden. Caja de Ahorro sigue
siendo la única fuente de movimientos y de saldo. Cuando también se importó la Tarjeta de Débito y
existe un match único, `movements.html` muestra el comercio real en vez de
`"PAGO CON VISA DEBITO..."`. No hay movimientos duplicados ni tablas intermedias.
