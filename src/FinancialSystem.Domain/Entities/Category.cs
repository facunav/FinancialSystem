namespace FinancialSystem.Domain.Entities;

/// <summary>
/// Categoría financiera. Responde "¿para qué se usó el dinero?".
///
/// Es una de las 4 dimensiones de clasificación de ClassifiedMovement.
///
/// ADMINISTRABLE POR CRUD:
///   El sistema inicializa un conjunto de categorías del sistema mediante seed.
///   El usuario puede crear categorías propias, editarlas, desactivarlas y reordenarlas.
///   Las categorías del sistema (IsSystem=true) no pueden eliminarse, pero sí desactivarse.
///
/// NAMING:
///   Name es la clave técnica invariante (en inglés, nunca cambia).
///   Permite referencias internas estables aunque el DisplayName cambie.
///   DisplayName es el label visible en la UI (en español, renombrable sin impacto en FK).
///
/// JERARQUÍA FUTURA:
///   ParentId está reservado para implementar subcategorías.
///   Hoy siempre es null — todas las categorías son de primer nivel.
///
/// EL MCP agrupa por CategoryId. Renombrar el DisplayName no rompe históricos
/// porque la FK en ClassifiedMovement apunta al Id, no al nombre.
/// </summary>
public class Category
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Clave técnica invariante. Ejemplos: "Health", "Food", "Transport".
    /// No se expone directamente en la UI. Única en la tabla.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Nombre visible en la UI. Ejemplos: "Salud", "Alimentación", "Transporte".
    /// Puede renombrarse sin impacto en históricos.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Orden de presentación en listas y selectores.</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// True = categoría del sistema (seed), no eliminable.
    /// False = creada por el usuario, puede desactivarse.
    /// </summary>
    public bool IsSystem { get; set; } = true;

    /// <summary>
    /// False = activa, aparece en selectores.
    /// True = desactivada, no aparece en nuevos movimientos pero históricos la siguen referenciando.
    /// Las categorías de sistema no pueden desactivarse.
    /// </summary>
    public bool IsDeactivated { get; set; }

    /// <summary>
    /// Reservado para jerarquía futura: categoría padre.
    /// Hoy siempre null (todas las categorías son de primer nivel).
    /// </summary>
    public Guid? ParentId { get; set; }
    public Category? Parent { get; set; }
}