Backlog de implementación — Review & Classification Engine v2
(Basado en el documento de arquitectura aprobado. PRs con dependencia de una decisión pendiente de la sección 18 están marcados con [Decisión #N].)

Épica A — Fundamentos de lectura (sin escritura, sin endpoint)
PR	Objetivo único	Tamaño est.
A1	ReviewEngineOptions (Application) + registro/binding en DI (Api + Worker), sin ningún consumidor todavía	1-2h
A2	IMovementLoader (contrato) + implementación mínima: carga BankStatement → FinancialMovement (Reference)	2-3h
A3	Extender el loader para incluir Transaction (tarjeta) como Reference	1-2h
A4	Extender el loader para incluir LegacyImportedExpense no descartado como Candidate	2h
A5	Excluir del loader los movimientos que ya tienen ClassifiedMovementItem (JOIN por SourceEntityType+SourceId) — cierra la Épica	2h
Épica B — Motor de sugerencias (sin escritura, sin endpoint)
PR	Objetivo único	Tamaño est.
B1	IMatchScorer (contrato) + AmountRule + scorer mínimo con esa sola regla	2-3h
B2	Agregar DateRule al scorer compuesto	1-2h
B3	Agregar DescriptionRule (similitud de texto) al scorer	2-3h
B4	Agregar PaymentMethodRule — cierra el scorer con las 4 reglas y los pesos de ReviewEngineOptions [Decisión #5]	1-2h
B5	ISuspicionDetector (contrato + implementación): duplicados posibles y split transactions	2-3h
B6	IReviewEngine (contrato + implementación): orquesta Loader+Scorer+SuspicionDetector, arma el ReviewResult completo aplicando umbrales de confianza	2-3h
Épica C — Primer endpoint y primera escritura
PR	Objetivo único	Tamaño est.
C1	DTOs de request/response de revisión (MovementReviewDtos.cs), sin endpoint todavía	1h
C2	GET /api/movement-review/unclassified — expone IReviewEngine (solo lectura) [Decisión #1 ruta, #6 límite de rango]	2h
C3	ClassifyMovementCommand/Handler + POST classify (Reviewed, sin dependencia del motor de sugerencias)	2-3h
C4	ConfirmMatchCommand/Handler + POST confirm-match (Confirmed, soporta grupos N↔M)	3h
C5	DiscardLegacyCandidatesCommand/Handler + POST discard-candidates	1h
C6	RestoreLegacyCandidatesCommand/Handler + POST restore-candidates	1h
Épica D — UI
PR	Objetivo único	Tamaño est.
D1	Repuntar group-reconciliation.html a /api/movement-review/* y adaptar el payload mínimo para restaurar la funcionalidad básica end-to-end [Decisión #4 alcance de UI]	3h
D2	Reemplazar el combo "Motivo" por el selector de Tipo de Movimiento	2h
D3	Agregar autocompletado de Contraparte, con o sin pre-carga de valores sugeridos según corresponda [Decisión #2]	2-3h
Total: 20 PRs (A: 5, B: 6, C: 6, D: 3). Ningún PR de la Épica A o B depende de una decisión pendiente — pueden arrancar hoy. Las Épicas C y D sí tienen puntos bloqueados por decisiones de la sección 18 del documento de arquitectura.

Mismo falso positivo de firma ya diagnosticado varias veces en esta sesión (committer y firma SSH correctos; el entorno no puede verificarlo localmente por falta de gpg.ssh.allowedSignersFile). No hice commits en este turno — solo generé el backlog, sin tocar el repositorio.