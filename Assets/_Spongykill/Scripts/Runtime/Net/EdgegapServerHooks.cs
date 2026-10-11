using System;
using System.Collections;
using System.Collections.Generic;
using Edgegap;
using Edgegap.Matchmaking;
using UnityEngine;
using UnityEngine.Networking;

namespace Spongykill.Net
{
    /// <summary>
    /// Dedicated server side of Edgegap hosting. When the matchmaker started this deployment it keeps
    /// backfill tickets open so Quick Match can drop new players into the running match (the free tier
    /// allows one deployment at a time), and when the server sits empty it stops its own deployment
    /// through Edgegap's self-stop URL so it doesn't burn hosting time.
    /// </summary>
    public class EdgegapServerHooks : MonoBehaviour
    {
        ServerAgent<QuickMatchAttributes> agent;
        DeploymentEnvironmentDTO deployment;
        SafeHttpRequest http;
        readonly Dictionary<ulong, string> tickets = new Dictionary<ulong, string>();

        IEnumerator Start()
        {
            // NetSession decides the mode in its own Start.
            yield return null;
            if (!NetSession.IsDedicatedServer)
            {
                enabled = false;
                yield break;
            }

            IDictionary environment = Environment.GetEnvironmentVariables();
            if (!environment.Contains("ARBITRIUM_REQUEST_ID"))
            {
                // A local test server: nothing to register or stop.
                Debug.Log("[Spongykill] Not running on Edgegap (no deployment variables).");
                enabled = false;
                yield break;
            }
            try
            {
                deployment = new DeploymentEnvironmentDTO(environment);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Spongykill] Couldn't read the Edgegap deployment variables: {exception.Message}");
                enabled = false;
                yield break;
            }
            http = new SafeHttpRequest(this);
            NetSession.IdleShutdown += OnIdleShutdown;
            Debug.Log($"[Spongykill] Edgegap deployment {deployment.RequestID} at {deployment.PublicIP}.");

            OnlineConfig config = OnlineConfig.Instance;
            bool startedByMatchmaker = environment.Contains("MM_MATCH_PROFILE");
            if (startedByMatchmaker && config != null && config.MatchmakingConfigured) StartBackfill(environment, config);
        }

        void OnDestroy()
        {
            NetSession.IdleShutdown -= OnIdleShutdown;
            NetSession.ClientApproved -= OnClientApproved;
            NetSession.ClientLeft -= OnClientLeft;
        }

        void StartBackfill(IDictionary environment, OnlineConfig config)
        {
            var match = new MatchEnvironmentDTO<QuickMatchAttributes>(environment);
            var assignment = new BackfillAssignment
            {
                RequestID = deployment.RequestID,
                Fqdn = deployment.Fqdn,
                PublicIP = deployment.PublicIP,
                Ports = deployment.PortMapping,
                Location = deployment.Location,
            };
            // The SDK opens backfill slots up to this many players; left at its default (-1) it opens none
            // and later Quick Match players can never join this server.
            int capacity = NetSession.Instance != null ? NetSession.Instance.MaxPlayers : 10;
            try
            {
                agent = new ServerAgent<QuickMatchAttributes>(this, config.matchmakerUrl.Trim().TrimEnd('/'), config.matchmakerToken.Trim(),
                    string.IsNullOrEmpty(match.MatchProfile) ? config.matchmakerProfile : match.MatchProfile, new BackfillAttributes(assignment),
                    targetTeamSize: capacity);
                agent.Initialize(match.Tickets, OnMonitor, OnBackfills);
                NetSession.ClientApproved += OnClientApproved;
                NetSession.ClientLeft += OnClientLeft;
            }
            catch (Exception exception)
            {
                agent = null;
                Debug.LogError($"[Spongykill] Backfill disabled: {exception.Message}");
            }
        }

        bool backfillStarted;

        void OnMonitor(Observable<MonitorResponseDTO> monitor, ObservableActionType action, string message)
        {
            if (action != ObservableActionType.Update || message != "healthy" || backfillStarted) return;
            backfillStarted = true;
            agent.AddBackfills();
        }

        void OnBackfills(Observable<Dictionary<string, BackfillResponseDTO<QuickMatchAttributes>>> backfills, ObservableActionType action, string message)
        {
            if (action != ObservableActionType.Update) Debug.LogWarning($"[Spongykill] Backfill {action}: {message}");
        }

        void OnClientApproved(ulong clientId, string ticket)
        {
            if (string.IsNullOrEmpty(ticket)) return;
            tickets[clientId] = ticket;
            agent?.PlayerConnected(ticket);
            // A real player is back, so the placeholder kept for an empty server can go.
            if (agent != null && anchorTicket != null && anchorTicket != ticket)
            {
                string anchor = anchorTicket;
                anchorTicket = null;
                agent.AbandonPlayer(anchor);
            }
        }

        void OnClientLeft(ulong clientId)
        {
            if (!tickets.TryGetValue(clientId, out string ticket)) return;
            tickets.Remove(clientId);
            if (agent == null) return;
            // Backfills are built from the server's current tickets, and the matchmaker can't match
            // anyone into a backfill with none. Keep the last player's ticket as a placeholder so
            // players can keep leaving and rejoining an empty server.
            if (tickets.Count == 0 && agent.Assignments.ContainsKey(ticket))
            {
                if (anchorTicket != null && anchorTicket != ticket) agent.AbandonPlayer(anchorTicket);
                anchorTicket = ticket;
                return;
            }
            agent.AbandonPlayer(ticket);
        }

        string anchorTicket;

        void OnIdleShutdown(Action quit)
        {
            if (agent != null) agent.AbandonAllBackfills(() => StopDeployment(quit));
            else StopDeployment(quit);
        }

        void StopDeployment(Action quit)
        {
            if (deployment == null || string.IsNullOrEmpty(deployment.SelfStopURL))
            {
                quit();
                return;
            }
            http.Delete(deployment.SelfStopURL, deployment.SelfStopToken,
                (string response, UnityWebRequest request) => quit(),
                (string error, UnityWebRequest request) =>
                {
                    Debug.LogError($"[Spongykill] Self-stop failed: {error}");
                    quit();
                },
                new RetryParameters { MaxAttempts = 3, RemainingAttempts = 3 });
        }
    }
}
