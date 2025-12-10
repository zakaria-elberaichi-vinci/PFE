using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SQLite;
using PFE.Models;

namespace PFE.Services
{
    public class LeaveSyncService : ILeaveSyncService
    {
        private readonly SQLiteAsyncConnection _db;

        public LeaveSyncService(SQLiteAsyncConnection db)
        {
            _db = db;
        }

        /// <summary>
        /// Sauvegarde en local une demande de congé hors-ligne.
        /// </summary>
        public async Task<int> SavePendingAsync(int leaveTypeId, DateTime startDate, DateTime endDate, string? reason)
        {
            var pending = new PendingLeaveRequest
            {
                LeaveTypeId = leaveTypeId,
                StartDate = startDate,
                EndDate = endDate,
                Reason = reason,
                CreatedAt = DateTime.UtcNow,
                IsSynced = false,
                OdooId = null
            };

            await _db.InsertAsync(pending);

            // après InsertAsync, l’Id auto-incrémenté est rempli
            return pending.Id;
        }

        /// <summary>
        /// On implémentera la synchro vers Odoo plus tard.
        /// Pour le moment, on renvoie 0.
        /// </summary>
        public Task<int> SyncAllAsync()
        {
            // TODO: sera rempli plus tard
            return Task.FromResult(0);
        }

        /// <summary>
        /// Récupère les demandes non synchronisées en base locale.
        /// </summary>
        public Task<List<PendingLeaveRequest>> GetPendingAsync()
        {
            return _db.Table<PendingLeaveRequest>()
                      .Where(x => !x.IsSynced)
                      .ToListAsync();
        }
    }
}
