namespace FinancialSystem.Domain.Entities
{
    /// <summary>
    /// Categoría financiera. Catálogo normalizado usado por el MCP para agrupar gastos.
    ///
    /// DISEÑO:
    ///   Name es la clave técnica invariante (en inglés, nunca cambia).
    ///   DisplayName es el label visible en la UI (en español, renombrable).
    ///   Renombrar DisplayName no rompe históricos porque la FK es por Id.
    ///
    /// SISTEMA vs USUARIO:
    ///   IsSystem = true  → categorías base, no eliminables.
    ///   IsSystem = false → categorías creadas por el usuario (ej: "Veterinaria").
    ///
    /// El MCP siempre agrupa por CategoryId, nunca por strings libres.
    /// </summary>
    public class Category
    {
        public Guid Id { get; init; } = Guid.NewGuid();

        /// <summary>
        /// Clave técnica invariante. Ejemplos: "Health", "Food", "Transport".
        /// No se expone en la UI directamente. Única en la tabla.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Nombre visible en la UI. Ejemplos: "Salud", "Alimentación", "Transporte".
        /// Puede renombrarse sin impacto en históricos.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Orden de presentación en listas y selectores.
        /// </summary>
        public int SortOrder { get; set; }

        /// <summary>
        /// True = categoría del sistema, no eliminable.
        /// False = creada por el usuario.
        /// </summary>
        public bool IsSystem { get; set; } = true;
    }
}
