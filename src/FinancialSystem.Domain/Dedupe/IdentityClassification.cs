namespace FinancialSystem.Domain.Dedupe;

/// <summary>
/// Nivel de confianza de una relación de identidad entre dos representaciones físicas,
/// según la matriz de DEDUPE-003-CONV, sección E.
///
/// SOLO "Fuerte" SE PERSISTE HOY (ver verificación de cardinalidad, Etapa 4-CONV):
/// Posible/Indeterminado/Descartado pueden aparecer como resultado de
/// <c>IDedupeEngine.PreviewAsync</c> (Descartado se filtra ahí mismo, nunca llega al
/// llamador -- ver DedupeEngine) pero <c>ApplyAsync</c> además rechaza persistir
/// cualquier resultado que no sea Fuerte, así que ningún valor salvo Fuerte llega jamás
/// a una fila real de <see cref="MovementIdentityLink"/>. El valor existe en el enum
/// para que la lógica de clasificación interna del motor sea testeable directamente
/// (ver DedupeEngineTests), no porque vaya a persistirse.
/// </summary>
public enum IdentityClassification
{
    Fuerte = 0,
    Posible = 1,
    Indeterminado = 2,
    Descartado = 3,
}
