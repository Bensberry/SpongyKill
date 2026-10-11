using System;
using System.Collections.Generic;
using Edgegap;
using Edgegap.Matchmaking;
using Newtonsoft.Json;
using UnityEngine;

namespace Spongykill.Net
{
    /// <summary>Ticket attributes for the "spongykill-tdm" matchmaker profile.</summary>
    public class QuickMatchAttributes
    {
        [JsonProperty("backfill_group_size")]
        public string[] BackfillGroupSize;
    }

    public class QuickMatchRequest : GroupUpRequestDTO<QuickMatchAttributes>
    {
        public QuickMatchRequest(string profile, string[] backfillGroupSize)
            : base(profile, true)
        {
            Attributes = new QuickMatchAttributes { BackfillGroupSize = backfillGroupSize };
        }
    }

    /// <summary>
    /// Quick Match through Edgegap's matchmaker: files a ticket, waits for a server (a new
    /// deployment, or a running match with a free slot through backfill) and reports where to connect.
    /// </summary>
    public class EdgegapQuickMatch : MonoBehaviour
    {
        GroupClient<QuickMatchRequest, QuickMatchAttributes> client;
        OnlineConfig config;
        Action<string> status;
        Action<string, ushort, string> assigned;
        Action<string> failed;
        bool searching;
        bool finished;

        /// <param name="onStatus">Progress text for the menu.</param>
        /// <param name="onAssigned">Server address, port and the ticket to present when connecting.</param>
        /// <param name="onFailed">Why matchmaking stopped.</param>
        public static EdgegapQuickMatch Begin(GameObject host, Action<string> onStatus, Action<string, ushort, string> onAssigned, Action<string> onFailed)
        {
            OnlineConfig settings = OnlineConfig.Instance;
            if (settings == null || !settings.MatchmakingConfigured)
            {
                onFailed?.Invoke("Online play isn't set up yet: add the Edgegap matchmaker URL and token to OnlineConfig.");
                return null;
            }
            var quickMatch = host.AddComponent<EdgegapQuickMatch>();
            quickMatch.config = settings;
            quickMatch.status = onStatus;
            quickMatch.assigned = onAssigned;
            quickMatch.failed = onFailed;
            return quickMatch;
        }

        void Start()
        {
            status?.Invoke("Contacting matchmaker...");
            try
            {
                client = new GroupClient<QuickMatchRequest, QuickMatchAttributes>(this, config.matchmakerUrl.Trim().TrimEnd('/'), config.matchmakerToken.Trim(),
                    requestTimeoutSeconds: 5, pollingBackoffSeconds: 1f, maxConsecutivePollingErrors: 10, removeGroupSeconds: 30f,
                    logGroupUpdates: true, logPollingUpdates: false);
                client.Initialize(OnMonitor, OnGroup);
            }
            catch (Exception exception)
            {
                Fail($"Matchmaking error: {exception.Message}");
            }
        }

        public void Cancel()
        {
            if (finished) return;
            finished = true;
            StopThenDestroy();
        }

        /// <summary>Withdraws the ticket (a web request that needs this component alive), then cleans up.</summary>
        void StopThenDestroy()
        {
            if (searching && client != null) client.StopMatchmaking(() => Destroy(this));
            else Destroy(this);
        }

        void OnApplicationQuit()
        {
            if (searching && !finished) client?.StopMatchmaking();
        }

        void OnMonitor(Observable<MonitorResponseDTO> monitor, ObservableActionType action, string message)
        {
            if (finished || action != ObservableActionType.Update) return;
            if (message == "healthy")
            {
                if (searching) return;
                searching = true;
                status?.Invoke("Searching for a match...");
                client.CreateGroup(new QuickMatchRequest(config.matchmakerProfile, config.backfillGroupSize), true);
            }
            else
            {
                Fail("The matchmaker is offline right now. Try again in a minute.");
            }
        }

        void OnGroup(Observable<GroupUpResponseDTO> group, ObservableActionType action, string message)
        {
            if (finished || group.Current == null) return;
            if (action == ObservableActionType.Error)
            {
                Fail($"Matchmaking error: {message}");
                return;
            }
            if (action != ObservableActionType.Update) return;

            string state = group.Current.Status ?? "";
            if (state.Contains("SEARCHING")) status?.Invoke("Searching for a match...");
            else if (state.Contains("MATCH_FOUND") || state.Contains("TEAM_FOUND")) status?.Invoke("Match found! Starting a server...");
            else if (state.Contains("CANCELLED") || state.Contains("EXPIRED")) Fail("Matchmaking timed out. Try again.");
            else if (state.Contains("HOST_ASSIGNED")) OnHostAssigned(group.Current);
        }

        void OnHostAssigned(GroupUpResponseDTO group)
        {
            DeploymentDTO assignment = group.Assignment;
            if (assignment == null || assignment.Ports == null)
            {
                Fail("The matchmaker didn't return a server address.");
                return;
            }

            PortMappingDTO port = FindGamePort(assignment.Ports);
            if (port == null || !ushort.TryParse(port.External, out ushort externalPort))
            {
                Fail("The matchmaker's server has no game port.");
                return;
            }
            string address = !string.IsNullOrEmpty(assignment.PublicIP) ? assignment.PublicIP : assignment.Fqdn;
            finished = true;
            status?.Invoke("Joining server...");
            assigned?.Invoke(address, externalPort, group.TicketID);
            // The ticket stays with the server now; this component's job is done.
            Destroy(this);
        }

        PortMappingDTO FindGamePort(Dictionary<string, PortMappingDTO> ports)
        {
            if (ports.TryGetValue(config.gamePortName, out PortMappingDTO named)) return named;
            foreach (KeyValuePair<string, PortMappingDTO> entry in ports)
                if (string.Equals(entry.Value.Protocol, "UDP", StringComparison.OrdinalIgnoreCase)) return entry.Value;
            foreach (PortMappingDTO first in ports.Values) return first;
            return null;
        }

        void Fail(string reason)
        {
            if (finished) return;
            finished = true;
            failed?.Invoke(reason);
            StopThenDestroy();
        }
    }
}
