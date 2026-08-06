using System.Text;

namespace FinancialSystem.Application.Helpers;

/// <summary>
/// Normalización de texto compartida para comparar descripciones/títulos de forma
/// tolerante a diferencias triviales de formato -- usada por los dos motores de
/// coincidencia por descripción del sistema
/// (<c>FinancialSystem.Infrastructure.Suggestions.ClassificationSuggestionService</c>,
/// <c>FinancialSystem.Infrastructure.Planning.PlanningMatchSuggestionService</c>).
///
/// Extraída a un único lugar (PATCH-043, Epic V): antes existían dos copias del mismo
/// "espíritu" de normalización -- una hecha con un bucle manual sobre cada carácter,
/// la otra con <c>Split(' ', ...)</c> -- que coincidían en todo salvo un detalle
/// incidental de implementación (una colapsaba cualquier carácter de espacio en
/// blanco, la otra solo el carácter espacio literal). Ningún test ni comportamiento
/// observable de ninguno de los dos consumidores distinguía ese detalle -- ver el
/// resumen del patch para el análisis completo.
///
/// Operaciones que realiza, en este orden:
///   1. <c>null</c> o cadena compuesta solo de espacios en blanco -> cadena vacía.
///   2. <c>Trim()</c> de los extremos.
///   3. Colapsa cualquier corrida de uno o más caracteres de espacio en blanco
///      (espacio, tab, salto de línea, etc. -- <see cref="char.IsWhiteSpace(char)"/>)
///      en un único carácter espacio.
///   4. <c>ToUpperInvariant()</c>.
///
/// Lo que NO hace (a propósito, sin cambios respecto al comportamiento anterior de
/// ambos consumidores): no elimina acentos ni diacríticos, no elimina puntuación ni
/// caracteres especiales, no aplica fuzzy matching ni distancia de edición -- ningún
/// consumidor actual lo necesitaba. Cualquier lógica adicional específica de un solo
/// consumidor (ej. el monto USD embebido o el contador de cuotas que
/// ClassificationSuggestionService quita de las descripciones de tarjeta antes de
/// llamar acá) queda en ese consumidor -- no está duplicada en ningún otro lugar del
/// sistema, así que no se movió.
/// </summary>
public static class TextNormalization
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var trimmed = text.Trim();
        var collapsed = new StringBuilder(trimmed.Length);
        var lastWasSpace = false;
        foreach (var c in trimmed)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace) collapsed.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                collapsed.Append(c);
                lastWasSpace = false;
            }
        }

        return collapsed.ToString().ToUpperInvariant();
    }
}
