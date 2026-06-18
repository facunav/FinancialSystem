using FinancialSystem.Domain.Reconciliation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinancialSystem.Application.Reconciliation
{
    public interface IMovementLoader
    {
        Task<IReadOnlyList<FinancialMovement>> LoadReferenceMovementsAsync(DateOnly from, DateOnly to, CancellationToken ct);

        Task<IReadOnlyList<FinancialMovement>> LoadCandidateMovementsAsync(DateOnly from, DateOnly to, CancellationToken ct);
    }
}
