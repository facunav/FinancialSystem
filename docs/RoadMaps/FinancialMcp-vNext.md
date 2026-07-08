# FinancialMcp vNext

Estado:

* Review & Classification Engine v2 finalizado.
* Modelo de dominio refactorizado.
* Fuentes financieras separadas y trazables.
* Próxima etapa enfocada en Reconciliation Engine, Dashboard y MCP financiero.

Este documento define la evolución del producto desde un sistema de importación/clasificación hacia una plataforma de gestión financiera personal con conciliación inteligente.

---

# Visión del Producto

FinancialMcp debe permitir:

* Importar información financiera desde múltiples fuentes.
* Mantener trazabilidad del origen de cada movimiento.
* Detectar relaciones entre movimientos de distintas fuentes.
* Confirmar conciliaciones automáticamente o mediante revisión.
* Generar métricas financieras confiables.
* Exponer información mediante Dashboard y MCP.

---

# Arquitectura Objetivo

## Fuentes de Datos

Las fuentes originales deben mantenerse separadas:

* Movimientos bancarios.
* Movimientos de tarjeta de crédito.
* Gastos dinámicos.
* Gastos fijos.
* Gastos manuales.

Cada fuente mantiene su información original para auditoría y trazabilidad.

---

## Flujo de Datos

```
Source Data
    ↓
Importación / Normalización
    ↓
ProcessedExpense
    ↓
Reconciliation Engine
    ↓
ReconciledExpense
    ↓
Metrics / Dashboard / MCP
```

---

# Sprint 1 - Confiabilidad e Importación

## B1

Corregir y robustecer parsers.

Objetivos:

* Mejor detección de columnas.
* Manejo consistente de formatos Excel.
* Mejor procesamiento de PDFs bancarios y tarjetas.

---

## B2

Importación idempotente.

Objetivos:

* Evitar duplicados.
* Detectar archivos ya procesados.
* Mantener historial de importaciones.

---

## B3

Constraints y reglas de integridad.

Objetivos:

* Constraints únicos necesarios.
* Validaciones de dominio.
* Garantizar consistencia entre entidades.

---

# Sprint 2 - Reconciliation Engine

Objetivo:
Crear el motor que permita unir información proveniente de distintas fuentes.

---

## R1

Modelo de conciliación.

Implementar:

* ReconciledExpense.
* ReconciledExpenseItem.
* Estados de conciliación.
* Confirmación manual/automática.

---

## R2

Matching inteligente.

Implementar:

* ReconciliationOrchestrator.
* MatchScorer.
* SuspicionDetector.
* Adapters por tipo de movimiento.

Casos soportados:

* 1 ↔ 1.
* N ↔ 1.
* 1 ↔ N.
* N ↔ M.

---

## R3

Flujo de revisión.

Pantalla para:

* Mostrar sugerencias.
* Comparar movimientos relacionados.
* Confirmar conciliación.
* Marcar como revisado.
* Descartar sugerencias.

---

# Sprint 3 - Modelo Financiero

## M1

Impacto financiero.

Definir correctamente:

* Ingresos.
* Gastos.
* Transferencias.
* Inversiones.
* Movimientos neutros.

---

## M2

Clasificación financiera.

Mejorar:

* Categorías.
* Subcategorías.
* Tipo de movimiento.
* Reglas de clasificación.

---

## M3

Contraparte.

Consolidar:

* Beneficiario.
* Comercio.
* Persona.
* Cuenta relacionada.

---

# Sprint 4 - Métricas y Dashboard

Objetivo:
Convertir datos conciliados en información financiera útil.

---

## D1

Financial Metrics Service.

Crear servicio para calcular:

* Gastos mensuales.
* Ingresos.
* Balance.
* Evolución temporal.
* Distribución por categorías.
* Comparación presupuestaria.

---

## D2

Dashboard principal.

Pantalla inicial del sistema:

* Resumen financiero.
* Gastos del mes.
* Alertas.
* Evolución.
* Indicadores principales.

---

# Sprint 5 - Calidad y Producción

## Q1

Tests.

Agregar:

* Tests del motor de matching.
* Tests de clasificación.
* Tests de importación.

---

## Q2

Monedas.

Soporte para:

* ARS.
* USD.
* Conversión.
* Tipo de cambio histórico.

---

## Q3

Configuración.

Centralizar:

* Parámetros del sistema.
* Reglas de clasificación.
* Preferencias del usuario.

---

# Sprint 6 - MCP e Inteligencia Artificial

Objetivo:
Exponer información financiera procesada a agentes inteligentes.

---

## AI1

MCP financiero.

Permitir consultas sobre:

* Gastos.
* Categorías.
* Evolución financiera.
* Presupuestos.

---

## AI2

Insights automáticos.

Generar:

* Detección de anomalías.
* Recomendaciones.
* Resúmenes mensuales.
* Alertas financieras.

---

# UX

Principios:

* Reducir carga manual.
* Mostrar información confiable.
* Mantener trazabilidad.
* Priorizar revisión sobre edición directa.

Pantallas principales:

1. Dashboard.
2. Conciliación.
3. Gastos.
4. Importaciones.
5. Configuración.

---

# Estado Actual

Completado:

* Review & Classification Engine v2.
* Refactorización inicial del dominio.
* Procesamiento de fuentes financieras.
* Base de conciliación.

Próximos pasos:

1. Finalizar Reconciliation Engine.
2. Construir flujo de revisión.
3. Crear métricas financieras.
4. Implementar Dashboard.
5. Exponer capacidades mediante MCP.
