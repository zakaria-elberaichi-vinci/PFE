using PFE.Models.Database;
using SQLite;

namespace PFE.Services
{
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
        public async Task InitializeAsync()
        {
            if (_isInitialized)
            {
                return;
            }

            await _initLock.WaitAsync();
            try
            {
                if (_isInitialized)
                {
                    return;
                }

                _database = new SQLiteAsyncConnection(_dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
                _ = await _database.CreateTableAsync<SeenLeaveNotification>();
                _ = await _database.CreateTableAsync<NotifiedLeaveStatusChange>();
                _ = await _database.CreateTableAsync<PendingLeaveRequest>();
                _ = await _database.CreateTableAsync<UserSession>();

                _isInitialized = true;
                System.Diagnostics.Debug.WriteLine($"DatabaseService: Base de données initialisée à {_dbPath}");
            }
            finally
            {
                _ = _initLock.Release();
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
            List<NotifiedLeaveStatusChange> notified = await db.Table<NotifiedLeaveStatusChange>()
                .Where(x => x.EmployeeId == employeeId && x.NotifiedStatus == status)
                .ToListAsync();

            return notified.Select(x => x.LeaveId).ToHashSet();
        }

        public async Task MarkLeaveAsNotifiedAsync(int employeeId, int leaveId, string status)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            NotifiedLeaveStatusChange? existing = await db.Table<NotifiedLeaveStatusChange>()
                .Where(x => x.EmployeeId == employeeId && x.LeaveId == leaveId && x.NotifiedStatus == status)
                .FirstOrDefaultAsync();

            if (existing == null)
            {
                _ = await db.InsertAsync(new NotifiedLeaveStatusChange
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
            _ = await db.ExecuteAsync("DELETE FROM notified_leave_status_changes WHERE EmployeeId = ?", employeeId);
            System.Diagnostics.Debug.WriteLine($"DatabaseService: Notifications supprimées pour employé {employeeId}");
        }

        #endregion

        #region SeenLeaveNotification (Managers)

        public async Task<HashSet<int>> GetSeenLeaveIdsAsync(int managerUserId)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            List<SeenLeaveNotification> seen = await db.Table<SeenLeaveNotification>()
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
                    _ = await db.InsertAsync(new SeenLeaveNotification
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
            _ = await db.ExecuteAsync("DELETE FROM seen_leave_notifications WHERE ManagerUserId = ?", managerUserId);
            System.Diagnostics.Debug.WriteLine($"DatabaseService: Notifications vues supprimées pour manager {managerUserId}");
        }

        #endregion

        #region PendingLeaveRequest (Offline)

        public async Task<PendingLeaveRequest> AddPendingLeaveRequestAsync(PendingLeaveRequest request)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            request.CreatedAt = DateTime.UtcNow;
            request.SyncStatus = SyncStatus.Pending;
            _ = await db.InsertAsync(request);
            System.Diagnostics.Debug.WriteLine($"DatabaseService: Demande de congé ajoutée en attente (ID local: {request.Id})");
            return request;
        }

        public async Task<List<PendingLeaveRequest>> GetPendingLeaveRequestsAsync(int employeeId)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            return await db.Table<PendingLeaveRequest>()
                .Where(x => x.EmployeeId == employeeId)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<PendingLeaveRequest>> GetUnsyncedLeaveRequestsAsync()
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            return await db.Table<PendingLeaveRequest>()
                .Where(x => x.SyncStatus == SyncStatus.Pending || x.SyncStatus == SyncStatus.Failed)
                .OrderBy(x => x.CreatedAt)
                .ToListAsync();
        }

        public async Task UpdateSyncStatusAsync(int requestId, SyncStatus status, string? errorMessage = null, int? odooLeaveId = null)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            PendingLeaveRequest? request = await db.Table<PendingLeaveRequest>()
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

                _ = await db.UpdateAsync(request);
                System.Diagnostics.Debug.WriteLine($"DatabaseService: Statut sync mis à jour pour demande {requestId}: {status}");
            }
        }

        public async Task CleanupSyncedRequestsAsync()
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            DateTime threshold = DateTime.UtcNow.AddDays(-7);
            _ = await db.ExecuteAsync(
                "DELETE FROM pending_leave_requests WHERE SyncStatus = ? AND LastSyncAttempt < ?",
                (int)SyncStatus.Synced, threshold);
        }

        #endregion

        #region UserSession

        public async Task SaveUserSessionAsync(UserSession session)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            session.LastLoginAt = DateTime.UtcNow;

            UserSession? existing = await db.Table<UserSession>()
                .Where(x => x.UserId == session.UserId)
                .FirstOrDefaultAsync();

            _ = existing != null ? await db.UpdateAsync(session) : await db.InsertAsync(session);
        }

        public async Task<UserSession?> GetUserSessionAsync(int userId)
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            return await db.Table<UserSession>()
                .Where(x => x.UserId == userId)
                .FirstOrDefaultAsync();
        }

        public async Task<UserSession?> GetLastActiveSessionAsync()
        {
            SQLiteAsyncConnection db = await GetDatabaseAsync();
            return await db.Table<UserSession>()
                .OrderByDescending(x => x.LastLoginAt)
                .FirstOrDefaultAsync();
        }

        #endregion
    }
}