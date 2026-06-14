using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinancialSystem.Domain.Enums
{
    /// <summary>
    /// Origen de la confirmación de una conciliación.
    /// Permite distinguir si el score que se persistió vino del motor
    /// o fue una confirmación manual sin sugerencia previa.
    /// </summary>
    public enum ConfirmationSource
    {
        /// <summary>
        /// El usuario confirmó manualmente, sin sugerencia previa del motor.
        /// MatchScore y MatchConfidence pueden ser null.
        /// </summary>
        Manual = 0,

        /// <summary>
        /// El usuario confirmó a partir de una sugerencia de confianza media
        /// (NeedsReview). Score y confianza preservados del motor.
        /// </summary>
        FromSuggested = 1,

        /// <summary>
        /// El usuario confirmó a partir de una sugerencia de alta confianza
        /// (AutoConfirmable). Score y confianza preservados del motor.
        /// </summary>
        FromAutoConfirm = 2,
    }

}
