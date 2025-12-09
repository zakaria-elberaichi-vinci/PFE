using System.Text.Json;
using Microsoft.Extensions.Logging;
using PFE.Models;
using PFE.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using PFE.Models;

namespace PFE.Services
{
    public class OfflineService
    {
        private readonly string _filePath;
        private readonly SemaphoreSlim _mutex = new(1, 1);
        private readonly OdooClient _odooClient;
        private readonly ILogger<OfflineService> _logger;
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public OfflineService(OdooClient odooClient, ILogger<OfflineService> logger)
        {
            _odooClient = odooClient;
            _logger = logger;
            _filePath = Path.Combine(FileSystem.AppDataDirectory, "pending_leaves.json");

            Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;

            // Tentative initiale au démarrage si connecté
            _ = TryFlushPendingAsync();
        }

        public async Task AddPendingAsync(PendingLeaveRequest item)
        {
            await _mutex.WaitAsync();
            try
            {
                List<PendingLeaveRequest> list = await ReadAllAsync().ConfigureAwait(false);
                list.Add(item);
                await WriteAllAsync(list).ConfigureAwait(false);
                _logger.LogInformation("Demande hors-ligne enregistrée (Id={Id}).", item.Id);
            }
            finally
            {
                _mutex.Release();
            }

            // Essayer d'envoyer immédiatement si la connexion est disponible
            if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
                _ = TryFlushPendingAsync();
        }

        public async Task<List<PendingLeaveRequest>> GetAllPendingAsync()
        {
            await _mutex.WaitAsync();
            try
            {
                return await ReadAllAsync().ConfigureAwait(false);
            }
            finally
            {
                _mutex.Release();
            }
        }

        public async Task TryFlushPendingAsync()
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                _logger.LogDebug("Pas d'accès Internet, flush différé.");
                return;
            }

            // Nécessite session authentifiée
            if (!_odooClient.session.Current.IsAuthenticated)
            {
                _logger.LogDebug("Utilisateur non authentifié, flush différé.");
                return;
            }

            await _mutex.WaitAsync();
            try
            {
                List<PendingLeaveRequest> pending = await ReadAllAsync().ConfigureAwait(false);
                if (pending.Count == 0)
                {
                    _logger.LogDebug("Aucune demande hors-ligne à envoyer.");
                    return;
                }

                List<PendingLeaveRequest> remaining = new();
                foreach (PendingLeaveRequest p in pending)
                {
                    try
                    {
                        _logger.LogInformation("Envoi de la demande hors-ligne (Id={Id})...", p.Id);
                        int createdId = await _odooClient.CreateLeaveRequestAsync(
                            leaveTypeId: p.LeaveTypeId,
                            startDate: p.StartDate,
                            endDate: p.EndDate,
                            reason: p.Reason
                        ).ConfigureAwait(false);

                        _logger.LogInformation("Demande hors-ligne envoyée avec succès (tempId={TempId} -> odooId={OdooId}).", p.Id, createdId);
                    }
                    catch (InvalidOperationException ex)
                    {
                        // Erreur métier : ne pas réessayer infiniment
                        _logger.LogWarning(ex, "Erreur métier lors de l'envoi de la demande hors-ligne (Id={Id}). Suppression.", p.Id);
                    }
                    catch (Exception ex)
                    {
                        // Erreur réseau ou temporaire : garder pour réessayer plus tard
                        _logger.LogWarning(ex, "Échec envoi demande hors-ligne (Id={Id}). Conserver pour réessai.", p.Id);
                        remaining.Add(p);
                    }
                }

                await WriteAllAsync(remaining).ConfigureAwait(false);
            }
            finally
            {
                _mutex.Release();
            }
        }

        private async Task<List<PendingLeaveRequest>> ReadAllAsync()
        {
            try
            {
                if (!File.Exists(_filePath))
                    return new List<PendingLeaveRequest>();

                string json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                    return new List<PendingLeaveRequest>();

                return JsonSerializer.Deserialize<List<PendingLeaveRequest>>(json, _jsonOptions) ?? new List<PendingLeaveRequest>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Impossible de lire les demandes hors-ligne depuis le fichier. Retourne une liste vide.");
                return new List<PendingLeaveRequest>();
            }
        }

        private async Task WriteAllAsync(List<PendingLeaveRequest> list)
        {
            try
            {
                string tmp = JsonSerializer.Serialize(list, _jsonOptions);
                await File.WriteAllTextAsync(_filePath, tmp).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Impossible d'écrire les demandes hors-ligne sur le disque.");
            }
        }

        private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
        {
            if (e.NetworkAccess == NetworkAccess.Internet)
            {
                _logger.LogInformation("Connectivité Internet détectée. Tentative d'envoi des demandes hors-ligne.");
                _ = TryFlushPendingAsync();
            }
        }
    }
}