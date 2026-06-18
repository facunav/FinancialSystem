namespace FinancialSystem.Domain.Enums
{
    public enum ReconciliationGroupingMode
    {
        /// <summary>1 Reference + 1 Candidate, generado por el motor de matching.</summary>
        EngineSuggested = 0,

        /// <summary>N↔M, armado manualmente por el usuario en el modo "Por agrupar".</summary>
        ManualGroup = 1,
    }
}
