using PFE.Models;
using SQLite;
using DB = PFE.Models.Database;

namespace PFE.Services
{
    /// <summary>
    /// Service de base de données SQLite locale
    /// </summary>
    public class DatabaseService : IDatabaseService
    {
        private SQLiteAsyncConnection? _database;
        private readonly string _dbPath;
        private bool _isInitialized = false;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        public DatabaseService()
        {
            _dbPath = Path.Combine(FileSystem.AppDataDirectory, "pfe_local.db3");
        }

        /// <summary>
        /// Initialise la base de données et crée les tables si nécessaire
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_isInitialized) return;

            await _initLock.WaitAsync();
            try
            {
                if (_isInitialized) return;

                _database = new SQLiteAsyncConnection(_dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);

                // Créer les tables
                await _database.CreateTableAsync<DB.SeenLeaveNotification>();
                await _database.CreateTableAsync<DB.NotifiedLeaveStatusChange>();
                await _database.CreateTableAsync<DB.PendingLeaveRequest>();
                await _database.CreateTableAsync<DB.PendingLeaveDecision>();
                await _database.CreateTableAsync<DB.CachedLeaveToApprove>();
                await _database.CreateTableAsync<DB.UserSession>();
                await _database.CreateTableAsync<DB.CachedLeaveAllocation>();
                await _database.CreateTableAsync<DB.CachedLeaveType>();
                await _database.CreateTableAsync<DB.CachedBlockedDate>();

                _isInitialized = true;
                System.Diagnostics.Debug.WriteLine($"DatabaseService: Base de données initialisée à {_dbPath}");
            }
            finally
            {
                _initLock.Release();
            }
        }

        private async Task<SQLiteAsyncConnection> GetDatabaseAsync()
        {
            if (!_isInitialized)
            {
                await InitializeAsync();
            }
            return _database!;
        }

        #region NotifiedLeaveStatusChange (Employés)

        public async Task<HashSet<int>> GetNotifiedLeaveIdsAsync(int employeeId, string status)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            List<DB.NotifiedLeaveStatusChange> notified = await db.Table<DB.NotifiedLeaveStatusChange>()
                .Where(x => x.EmployeeId == employeeId && x.NotifiedStatus == status)
                .ToListAsync();

            return notified.Select(x => x.LeaveId).ToHashSet();
        }

        public async Task MarkLeaveAsNotifiedAsync(int employeeId, int leaveId, string status)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();

            // Vérifier si déjà notifié
            DB.NotifiedLeaveStatusChange? existing = await db.Table<DB.NotifiedLeaveStatusChange>()
                .Where(x => x.EmployeeId == employeeId && x.LeaveId == leaveId && x.NotifiedStatus == status)
                .FirstOrDefaultAsync();

            if (existing == null)
            {
                await db.InsertAsync(new DB.NotifiedLeaveStatusChange
                {
                    EmployeeId = employeeId,
                    LeaveId = leaveId,
                    NotifiedStatus = status,
                    NotifiedAt = DateTime.UtcNow
                });
                System.Diagnostics.Debug.WriteLine($"DatabaseService: Congé {leaveId} marqué comme notifié ({status}) pour employé {employeeId}");
            }
        }

        public async Task ClearNotifiedLeavesAsync(int employeeId)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            await db.ExecuteAsync("DELETE FROM notified_leave_status_changes WHERE EmployeeId = ?", employeeId);
            System.Diagnostics.Debug.WriteLine($"DatabaseService: Notifications supprimées pour employé {employeeId}");
        }

        #endregion

        #region SeenLeaveNotification (Managers)

        public async Task<HashSet<int>> GetSeenLeaveIdsAsync(int managerUserId)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            List<DB.SeenLeaveNotification> seen = await db.Table<DB.SeenLeaveNotification>()
                .Where(x => x.ManagerUserId == managerUserId)
                .ToListAsync();

            return seen.Select(x => x.LeaveId).ToHashSet();
        }

        public async Task MarkLeavesAsSeenAsync(int managerUserId, IEnumerable<int> leaveIds)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            HashSet<int> existingIds = await GetSeenLeaveIdsAsync(managerUserId);
            DateTime now = DateTime.UtcNow;

            foreach (int leaveId in leaveIds)
            {
                if (!existingIds.Contains(leaveId))
                {
                    await db.InsertAsync(new DB.SeenLeaveNotification
                    {
                        ManagerUserId = managerUserId,
                        LeaveId = leaveId,
                        SeenAt = now
                    });
                }
            }
        }

        public async Task ClearSeenNotificationsAsync(int managerUserId)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            await db.ExecuteAsync("DELETE FROM seen_leave_notifications WHERE ManagerUserId = ?", managerUserId);
            System.Diagnostics.Debug.WriteLine($"DatabaseService: Notifications vues supprimées pour manager {managerUserId}");
        }

        #endregion

        #region CachedLeaveToApprove (Managers - Cache offline)

        public async Task UpdateLeavesToApproveCacheAsync(int managerUserId, IEnumerable<DB.CachedLeaveToApprove> leaves)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            
            // Supprimer l'ancien cache pour ce manager
            await db.ExecuteAsync("DELETE FROM cached_leaves_to_approve WHERE ManagerUserId = ?", managerUserId);

            // Insérer les nouvelles demandes
            DateTime now = DateTime.UtcNow;
            foreach (DB.CachedLeaveToApprove leave in leaves)
            {
                leave.ManagerUserId = managerUserId;
                leave.CachedAt = now;
                await db.InsertOrReplaceAsync(leave);
            }

            System.Diagnostics.Debug.WriteLine($"DatabaseService: Cache mis à jour avec {leaves.Count()} demandes pour manager {managerUserId}");
        }

        public async Task<List<DB.CachedLeaveToApprove>> GetCachedLeavesToApproveAsync(int managerUserId)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            return await db.Table<DB.CachedLeaveToApprove>()
                .Where(x => x.ManagerUserId == managerUserId)
                .OrderBy(x => x.StartDate)
                .ToListAsync();
        }

        public async Task RemoveFromCacheAsync(int leaveId)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            await db.ExecuteAsync("DELETE FROM cached_leaves_to_approve WHERE LeaveId = ?", leaveId);
            System.Diagnostics.Debug.WriteLine($"DatabaseService: Demande {leaveId} supprimée du cache");
        }

        public async Task ClearLeavesToApproveCacheAsync(int managerUserId)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            await db.ExecuteAsync("DELETE FROM cached_leaves_to_approve WHERE ManagerUserId = ?", managerUserId);
        }

        #endregion

        #region PendingLeaveDecision (Managers - Offline)

        public async Task<DB.PendingLeaveDecision> AddPendingLeaveDecisionAsync(DB.PendingLeaveDecision decision)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            decision.DecisionDate = DateTime.UtcNow;
            decision.SyncStatus = DB.SyncStatus.Pending;
            await db.InsertAsync(decision);
            System.Diagnostics.Debug.WriteLine($"DatabaseService: Décision {decision.DecisionType} ajoutée pour congé {decision.LeaveId} (ID local: {decision.Id})");
            return decision;
        }

        public async Task<List<DB.PendingLeaveDecision>> GetAllLeaveDecisionsAsync(int managerUserId)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            return await db.Table<DB.PendingLeaveDecision>()
                .Where(x => x.ManagerUserId == managerUserId)
                .OrderByDescending(x => x.DecisionDate)
                .ToListAsync();
        }

        public async Task<List<DB.PendingLeaveDecision>> GetPendingLeaveDecisionsAsync(int managerUserId)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            return await db.Table<DB.PendingLeaveDecision>()
                .Where(x => x.ManagerUserId == managerUserId && 
                       (x.SyncStatus == DB.SyncStatus.Pending || x.SyncStatus == DB.SyncStatus.Failed))
                .OrderByDescending(x => x.DecisionDate)
                .ToListAsync();
        }

        public async Task<List<DB.PendingLeaveDecision>> GetSyncedLeaveDecisionsAsync(int managerUserId)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            return await db.Table<DB.PendingLeaveDecision>()
                .Where(x => x.ManagerUserId == managerUserId && x.SyncStatus == DB.SyncStatus.Synced)
                .OrderByDescending(x => x.DecisionDate)
                .ToListAsync();
        }

        public async Task<List<DB.PendingLeaveDecision>> GetUnsyncedLeaveDecisionsAsync()
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            return await db.Table<DB.PendingLeaveDecision>()
                .Where(x => x.SyncStatus == DB.SyncStatus.Pending || x.SyncStatus == DB.SyncStatus.Failed)
                .OrderBy(x => x.DecisionDate)
                .ToListAsync();
        }

        public async Task UpdateDecisionSyncStatusAsync(int decisionId, DB.SyncStatus status, string? errorMessage = null)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            DB.PendingLeaveDecision? decision = await db.Table<DB.PendingLeaveDecision>()
                .Where(x => x.Id == decisionId)
                .FirstOrDefaultAsync();

            if (decision != null)
            {
                decision.SyncStatus = status;
                decision.SyncErrorMessage = errorMessage;
                decision.LastSyncAttempt = DateTime.UtcNow;
                decision.SyncAttempts++;

                await db.UpdateAsync(decision);
                System.Diagnostics.Debug.WriteLine($"DatabaseService: Statut sync mis à jour pour décision {decisionId}: {status}");
            }
        }

        public async Task DeletePendingLeaveDecisionAsync(int decisionId)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            await db.DeleteAsync<DB.PendingLeaveDecision>(decisionId);
            System.Diagnostics.Debug.WriteLine($"DatabaseService: Décision {decisionId} supprimée");
        }

        public async Task<bool> HasDecisionForLeaveAsync(int leaveId)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            int count = await db.Table<DB.PendingLeaveDecision>()
                .Where(x => x.LeaveId == leaveId)
                .CountAsync();
            return count > 0;
        }

        public async Task CleanupOldSyncedDecisionsAsync(int daysOld = 30)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            DateTime threshold = DateTime.UtcNow.AddDays(-daysOld);
            await db.ExecuteAsync(
                "DELETE FROM pending_leave_decisions WHERE SyncStatus = ? AND DecisionDate < ?",
                (int)DB.SyncStatus.Synced, threshold);
            System.Diagnostics.Debug.WriteLine($"DatabaseService: Anciennes décisions synchronisées supprimées (> {daysOld} jours)");
        }

        #endregion

        #region PendingLeaveRequest (Employés - Offline)

        public async Task<DB.PendingLeaveRequest> AddPendingLeaveRequestAsync(DB.PendingLeaveRequest request)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            request.CreatedAt = DateTime.UtcNow;
            request.SyncStatus = DB.SyncStatus.Pending;
            await db.InsertAsync(request);
            System.Diagnostics.Debug.WriteLine($"DatabaseService: Demande de congé ajoutée en attente (ID local: {request.Id})");
            return request;
        }

        public async Task<List<DB.PendingLeaveRequest>> GetPendingLeaveRequestsAsync(int employeeId)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            return await db.Table<DB.PendingLeaveRequest>()
                .Where(x => x.EmployeeId == employeeId)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<DB.PendingLeaveRequest>> GetUnsyncedLeaveRequestsAsync()
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            return await db.Table<DB.PendingLeaveRequest>()
                .Where(x => x.SyncStatus == DB.SyncStatus.Pending || x.SyncStatus == DB.SyncStatus.Failed)
                .OrderBy(x => x.CreatedAt)
                .ToListAsync();
        }

        public async Task UpdateSyncStatusAsync(int requestId, DB.SyncStatus status, string? errorMessage = null, int? odooLeaveId = null)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            DB.PendingLeaveRequest? request = await db.Table<DB.PendingLeaveRequest>()
                .Where(x => x.Id == requestId)
                .FirstOrDefaultAsync();

            if (request != null)
            {
                request.SyncStatus = status;
                request.SyncErrorMessage = errorMessage;
                request.LastSyncAttempt = DateTime.UtcNow;
                request.SyncAttempts++;

                if (odooLeaveId.HasValue)
                {
                    request.OdooLeaveId = odooLeaveId;
                }

                await db.UpdateAsync(request);
                System.Diagnostics.Debug.WriteLine($"DatabaseService: Statut sync mis à jour pour demande {requestId}: {status}");
            }
        }

        public async Task CleanupSyncedRequestsAsync()
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            // Supprimer les demandes synchronisées avec succès depuis plus de 7 jours
            DateTime threshold = DateTime.UtcNow.AddDays(-7);
            await db.ExecuteAsync(
                "DELETE FROM pending_leave_requests WHERE SyncStatus = ? AND LastSyncAttempt < ?",
                (int)DB.SyncStatus.Synced, threshold);
        }

        #endregion

        #region UserSession

        public async Task SaveUserSessionAsync(DB.UserSession session)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            session.LastLoginAt = DateTime.UtcNow;

            DB.UserSession? existing = await db.Table<DB.UserSession>()
                .Where(x => x.UserId == session.UserId)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                await db.UpdateAsync(session);
            }
            else
            {
                await db.InsertAsync(session);
            }
        }

        public async Task<DB.UserSession?> GetUserSessionAsync(int userId)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            return await db.Table<DB.UserSession>()
                .Where(x => x.UserId == userId)
                .FirstOrDefaultAsync();
        }

        public async Task<DB.UserSession?> GetLastActiveSessionAsync()
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            return await db.Table<DB.UserSession>()
                .OrderByDescending(x => x.LastLoginAt)
                .FirstOrDefaultAsync();
        }

        #endregion

        #region CachedLeaveAllocation

        public async Task SaveLeaveAllocationAsync(int employeeId, int year, int allocated, int taken, int remaining)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();

            DB.CachedLeaveAllocation? existing = await db.Table<DB.CachedLeaveAllocation>()
                .Where(x => x.EmployeeId == employeeId && x.Year == year)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                existing.Allocated = allocated;
                existing.Taken = taken;
                existing.Remaining = remaining;
                existing.LastUpdated = DateTime.UtcNow;
                await db.UpdateAsync(existing);
                System.Diagnostics.Debug.WriteLine($"DatabaseService: Allocations mises à jour pour employé {employeeId}, année {year}");
            }
            else
            {
                await db.InsertAsync(new DB.CachedLeaveAllocation
                {
                    EmployeeId = employeeId,
                    Year = year,
                    Allocated = allocated,
                    Taken = taken,
                    Remaining = remaining,
                    LastUpdated = DateTime.UtcNow
                });
                System.Diagnostics.Debug.WriteLine($"DatabaseService: Allocations sauvegardées pour employé {employeeId}, année {year}");
            }
        }

        public async Task<DB.CachedLeaveAllocation?> GetLeaveAllocationAsync(int employeeId, int year)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            return await db.Table<DB.CachedLeaveAllocation>()
                .Where(x => x.EmployeeId == employeeId && x.Year == year)
                .FirstOrDefaultAsync();
        }

        #endregion

        #region CachedLeaveType

        public async Task SaveLeaveTypesAsync(int employeeId, List<PFE.Models.LeaveTypeItem> leaveTypes, int? year, bool requiresAllocation)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();

            // Supprimer les anciens types de congés en cache pour cet employé et ces critères
            await db.ExecuteAsync(
                "DELETE FROM cached_leave_types WHERE EmployeeId = ? AND RequiresAllocation = ? AND (Year = ? OR Year IS NULL)",
                employeeId, requiresAllocation, year);

            // Insérer les nouveaux types
            foreach (var leaveType in leaveTypes)
            {
                await db.InsertAsync(new DB.CachedLeaveType
                {
                    EmployeeId = employeeId,
                    LeaveTypeId = leaveType.Id,
                    Name = leaveType.Name,
                    Year = year,
                    RequiresAllocation = requiresAllocation,
                    LastUpdated = DateTime.UtcNow
                });
            }

            System.Diagnostics.Debug.WriteLine($"DatabaseService: {leaveTypes.Count} types de congés sauvegardés en cache pour employé {employeeId}");
        }

        public async Task<List<PFE.Models.LeaveTypeItem>> GetLeaveTypesAsync(int employeeId, int? year, bool requiresAllocation)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();

            // Récupérer tous les types pour cet employé
            List<DB.CachedLeaveType> allCached = await db.Table<DB.CachedLeaveType>()
                .Where(x => x.EmployeeId == employeeId)
                .ToListAsync();

            System.Diagnostics.Debug.WriteLine($"DatabaseService: {allCached.Count} types de congés trouvés en cache total pour employé {employeeId}");

            // D'abord essayer avec le filtre requiresAllocation exact
            List<DB.CachedLeaveType> cached = allCached
                .Where(x => x.RequiresAllocation == requiresAllocation)
                .ToList();

            // Si year est spécifié, filtrer par année
            if (year.HasValue && cached.Count > 0)
            {
                cached = cached.Where(x => x.Year == year.Value || x.Year == null).ToList();
            }

            // Si rien trouvé avec requiresAllocation, essayer sans ce filtre (fallback)
            if (cached.Count == 0 && allCached.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"DatabaseService: Aucun type avec requiresAllocation={requiresAllocation}, fallback sur tous les types");
                cached = allCached;
                
                if (year.HasValue)
                {
                    cached = cached.Where(x => x.Year == year.Value || x.Year == null).ToList();
                }
            }

            var result = cached.Select(c => new PFE.Models.LeaveTypeItem(c.LeaveTypeId, c.Name, c.RequiresAllocation)).ToList();
            
            System.Diagnostics.Debug.WriteLine($"DatabaseService: {result.Count} types de congés récupérés depuis le cache pour employé {employeeId}, year={year}, requiresAllocation={requiresAllocation}");
            
            return result;
        }

        public async Task ClearLeaveTypesAsync(int employeeId)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            await db.ExecuteAsync("DELETE FROM cached_leave_types WHERE EmployeeId = ?", employeeId);
            System.Diagnostics.Debug.WriteLine($"DatabaseService: Types de congés supprimés du cache pour employé {employeeId}");
        }

        #endregion

        #region CachedBlockedDates (Congés pour le calendrier)

        public async Task SaveBlockedDatesAsync(int employeeId, List<(DateTime date, int leaveId, string status)> blockedDates)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();

            // Supprimer les anciennes dates bloquées pour cet employé
            await db.ExecuteAsync("DELETE FROM cached_blocked_dates WHERE EmployeeId = ?", employeeId);

            // Insérer les nouvelles dates
            foreach (var (date, leaveId, status) in blockedDates)
            {
                await db.InsertAsync(new DB.CachedBlockedDate
                {
                    EmployeeId = employeeId,
                    LeaveId = leaveId,
                    BlockedDate = date,
                    Status = status,
                    LastUpdated = DateTime.UtcNow
                });
            }

            System.Diagnostics.Debug.WriteLine($"DatabaseService: {blockedDates.Count} dates bloquées sauvegardées en cache pour employé {employeeId}");
        }

        public async Task<HashSet<DateTime>> GetBlockedDatesAsync(int employeeId)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();

            List<DB.CachedBlockedDate> cached = await db.Table<DB.CachedBlockedDate>()
                .Where(x => x.EmployeeId == employeeId)
                .ToListAsync();

            HashSet<DateTime> result = cached.Select(c => c.BlockedDate.Date).ToHashSet();

            System.Diagnostics.Debug.WriteLine($"DatabaseService: {result.Count} dates bloquées récupérées depuis le cache");

            return result;
        }

        public async Task ClearBlockedDatesAsync(int employeeId)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            await db.ExecuteAsync("DELETE FROM cached_blocked_dates WHERE EmployeeId = ?", employeeId);
            System.Diagnostics.Debug.WriteLine($"DatabaseService: Dates bloquées supprimées du cache pour employé {employeeId}");
        }

        #endregion
    }
}
