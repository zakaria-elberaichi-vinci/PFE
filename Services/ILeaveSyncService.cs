using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PFE.Models;

namespace PFE.Services
{
    public interface ILeaveSyncService
    {
        /// <summary>
        /// Enregistre une demande de congé en attente (hors-ligne) dans SQLite.
        /// </summary>
        Task<int> SavePendingAsync(int leaveTypeId, DateTime startDate, DateTime endDate, string? reason);

        /// <summary>
        /// (On l’implémentera plus tard) : synchroniser toutes les demandes en attente avec Odoo.
        /// </summary>
        Task<int> SyncAllAsync();

        /// <summary>
        /// (Optionnel) Récupérer les demandes non synchronisées.
        /// </summary>
        Task<List<PendingLeaveRequest>> GetPendingAsync();
    }
}
