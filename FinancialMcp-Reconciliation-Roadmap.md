# FinancialMcp - Reconciliation Roadmap

## Estado del documento

Versión: 1.0

Propósito:

Este documento representa la fuente de verdad funcional del proyecto FinancialMcp.

Debe utilizarse para retomar el proyecto en futuras sesiones sin perder contexto.

---

# Visión del Proyecto

FinancialMcp no es un sistema de conciliación bancaria tradicional.

El objetivo final es construir una plataforma de gestión financiera personal que permita:

* Importar movimientos bancarios.
* Importar movimientos de tarjetas.
* Importar registros manuales desde Excel.
* Gestionar gastos fijos.
* Construir historial financiero anual.
* Alimentar un MCP financiero capaz de:

  * analizar gastos
  * generar presupuestos
  * detectar desvíos
  * proyectar gastos futuros
  * responder preguntas financieras históricas

---

# Fuentes de Datos

## 1. Movimientos Bancarios

Representan hechos financieros reales.

Ejemplos:

* FARMACIA AMANCAY
* CAMPO VERDE
* MERCADOPAGO
* TRANSFERENCIA

Son la fuente de verdad de lo que realmente ocurrió.

---

## 2. Registro Manual Diario

Proviene del Excel personal.

Ejemplos:

* Farmacia
* Almacén
* Cigarrillos
* Otros

Características:

* Se registra manualmente en aproximadamente el 90% de las compras.
* La fecha puede diferir de la fecha bancaria.
* El importe puede variar ligeramente.
* La descripción puede ser diferente.

Ejemplo:

Excel:

Farmacia
$50.000

Banco:

FARMACIA AMANCAY
$49.974

Representan el mismo gasto.

---

## 3. Gastos Fijos

Representan planificación futura.

Ejemplo:

15/06

Internet Julio
Seguro Julio
Gas Julio

La fecha de creación NO representa la fecha de pago.

Los gastos fijos constituyen un flujo independiente de la conciliación.

---

# Objetivo de la Conciliación

La conciliación NO busca precisión contable.

La conciliación busca enriquecer movimientos financieros reales utilizando información proveniente del Excel.

Objetivos:

- Evitar doble contabilización.
- Clasificar movimientos bancarios.
- Detectar gastos no registrados manualmente.
- Detectar registros manuales sin movimiento financiero asociado.
- Mejorar la calidad de los datos para el MCP.
- Generar métricas confiables.

La conciliación debe entenderse como un proceso de asociación y clasificación, más que como una conciliación bancaria tradicional.

# Modelo Conceptual

La entidad principal del sistema es el Movimiento Financiero.

Todo análisis futuro debe partir de movimientos financieros reales.

Modelo conceptual:

Movimiento Financiero
|
+-- Clasificación
+-- Categoría
+-- Notas
+-- Información proveniente de Excel
+-- Estado de revisión

El Excel complementa al movimiento financiero pero no lo reemplaza.

---

# Casos Reales Detectados

## Gasto registrado correctamente

Excel:

Farmacia $50.000

Banco:

FARMACIA AMANCAY $49.974

Resultado:

Conciliado

---

## Transferencia Personal

Banco:

TRANSFERENCIA $410.200

Destino:

* Tati
* Regalo
* Familiar

Resultado:

Revisado Manualmente

No requiere contraparte.

---

## Movimiento Bancario Sin Registro

Banco:

FARMACIA AMANCAY

No existe registro manual.

Resultado:

Pendiente de Clasificación

Es un gasto real.

Debe clasificarse o revisarse.

No requiere necesariamente una contraparte en Excel.

---
## Gasto Real No Registrado

Banco:

FARMACIA AMANCAY $25.000

Excel:

No existe registro.

Resultado:

Gasto válido.

Debe poder clasificarse manualmente.

Debe formar parte de estadísticas y métricas aunque nunca haya sido registrado en Excel.

---

# Modelo de Estados

## Movimiento Bancario

Pending

Movimiento aún no revisado.

Matched

Movimiento conciliado con una contraparte.

Reviewed

Movimiento revisado manualmente sin contraparte.

Ejemplos:

* Transferencias personales
* Regalos
* Movimientos internos
* Comisiones
* Intereses

---

## Movimiento Excel

Pending

No conciliado.

Matched

Conciliado.

---

# Modelo de Conciliación

Debe soportar:

* 1 ↔ 1
* N ↔ 1
* 1 ↔ N
* N ↔ M

Ejemplos:

---

## 1 ↔ 1

Farmacia ↔ FARMACIA AMANCAY

---

## N ↔ 1

Excel:

Farmacia 20.000
Farmacia 30.000

Banco:

FARMACIA AMANCAY 50.000

---

## 1 ↔ N

Excel:

Supermercado 100.000

Banco:

Compra 1 60.000
Compra 2 40.000

---

## N ↔ M

Grupo completo.

---

# Flujo Operativo Mensual

1. Importar movimientos bancarios.
2. Importar Excel.
3. Ejecutar matching automático.
4. Mostrar pendientes.
5. Permitir conciliación manual.
6. Permitir marcar revisados.
7. Asociar gastos fijos pagados.
8. Generar métricas.

---
# UI Objetivo

## Pendientes

Mostrar movimientos financieros sin clasificar o sin revisar.

Acciones:

- Conciliar
- Marcar Revisado
- Clasificar Manualmente

---

## Conciliados

Movimientos que obtuvieron información desde Excel.

Mostrar:

- Categoría
- Fecha
- Monto
- Contraparte utilizada

---

## Revisados

Movimientos sin contraparte pero validados manualmente.

Ejemplos:

- Transferencias personales
- Regalos
- Comisiones
- Intereses
- Movimientos internos

---

## Gastos Fijos

Pantalla independiente.

No forma parte de la conciliación principal.

## Pantalla Principal

### Columna Izquierda

Movimientos Bancarios

### Columna Derecha

Movimientos Excel

### Acciones

Conciliar

Permite:

* 1↔1
* N↔1
* 1↔N
* N↔M

---

Marcar Revisado

Disponible para movimientos bancarios sin contraparte.

---

Deshacer

Permite revertir conciliaciones.

---

# Matching Automático

Debe considerar:

## Fecha

No exigir igualdad exacta.

Permitir tolerancia de varios días.

## Importe

Permitir pequeñas diferencias.

## Texto

Utilizar similitud de descripciones.

## Categoría

Ayudar en la sugerencia.

---

# Gastos Fijos

Flujo separado.

Objetivo:

Determinar:

* Pendiente
* Pagado

Relacionar movimientos bancarios con gastos fijos cuando corresponda.

---

# Información que debe persistirse

Movimiento

Monto

Fecha

Descripción

Categoría

Estado

Origen

Usuario

Fecha de revisión

Fecha de conciliación

Motivo de revisión

---

# Motivos de Revisión

Transferencia Personal

Regalo

Movimiento Interno

Comisión Bancaria

Interés

Otro

---

# Roadmap

## Fase 1

Estabilización Funcional

Objetivos:

- Validar flujo completo de importación.
- Validar conciliación existente.
- Incorporar estado Reviewed.
- Permitir marcar movimientos revisados.
- Mejorar experiencia de clasificación.
- Reducir movimientos pendientes sin resolver.

N↔M queda sujeto a validación posterior según uso real.

---

## Fase 2

Matching Inteligente.

Objetivos:

* Matching por similitud.
* Matching por fecha flexible.
* Matching por monto flexible.

---

## Fase 3

Gestión de Gastos Fijos.

Objetivos:

* Asociación automática.
* Seguimiento de pagos.

---

## Fase 4

Motor de Clasificación

Objetivos:

- Reglas automáticas.
- Aprendizaje de categorías frecuentes.
- Sugerencias automáticas.
- Clasificación semiautomática.

Ejemplo:

FARMACIA AMANCAY
→ Farmacia

CAMPO VERDE
→ Almacén

MERCADOPAGO*NETFLIX
→ Streaming

---

## Fase 5

MCP Financiero.

Objetivos:

* Consultas históricas.
* Presupuestos.
* Proyecciones.
* Alertas.
* Recomendaciones.

---

# Métricas Futuras del MCP

¿Cuánto gasté en farmacia este año?

¿Cuánto gasto en cigarrillos por mes?

¿Cuánto aumentaron mis gastos?

¿Cuáles son mis categorías más costosas?

¿Qué gastos olvidé registrar?

¿Qué gastos fijos están creciendo?

¿Cuánto necesito para mantener mi nivel de vida actual?

---

# Próxima Tarea Recomendada

Implementar el estado Reviewed para movimientos financieros sin contraparte.

Objetivos:

- Reducir ruido en pendientes.
- Resolver transferencias personales.
- Resolver regalos.
- Resolver movimientos internos.
- Resolver comisiones e intereses.

Una vez implementado:

- Medir cuántos casos reales siguen requiriendo N↔M.
- Evaluar si la complejidad adicional está justificada.

No asumir que N↔M es prioritario hasta contar con evidencia de uso real.
