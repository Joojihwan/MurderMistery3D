using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace MurderMystery.Network
{
    /// <summary>
    /// Room creation and joining. Online rooms use Multiplayer Services Sessions over Relay (the SDK starts
    /// the Netcode host/client itself). LAN mode bypasses UGS entirely for local testing.
    /// </summary>
    public static class SessionService
    {
        public const int MaxPlayers = 5;
        public const ushort LanPort = 7777;

        public static ISession Current { get; private set; }
        private static bool online;
        private static bool leaving;

        /// <summary>
        /// Stable id for the room, used to name voice channels. LAN rooms use a fixed id. Null while an online
        /// session is still being created or joined (the Netcode host can already be running by then).
        /// </summary>
        public static string RoomKey => Current?.Id ?? (online ? null : "lan");
        public static string JoinCode => Current?.Code;

        public static bool SignedIn =>
            UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn;

        public static string PlayerKey => SignedIn ? AuthenticationService.Instance.PlayerId : $"local-{SystemInfo.deviceUniqueIdentifier}-{Application.dataPath.GetHashCode()}";

        public static async Task EnsureSignedInAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                // A distinct profile per process lets several editor/player instances sign in on one machine.
                var options = new InitializationOptions();
                options.SetProfile($"p{System.Diagnostics.Process.GetCurrentProcess().Id}");
                await UnityServices.InitializeAsync(options);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        public const string ScenarioProperty = "scenario";

        public static async Task<ISession> HostOnlineAsync(string scenarioId = null)
        {
            online = true;
            await EnsureSignedInAsync();
            var options = new SessionOptions { MaxPlayers = MaxPlayers, IsPrivate = true }.WithRelayNetwork();
            if (!string.IsNullOrEmpty(scenarioId))
                options.SessionProperties = new System.Collections.Generic.Dictionary<string, SessionProperty>
                {
                    [ScenarioProperty] = new SessionProperty(scenarioId, VisibilityPropertyOptions.Member, PropertyIndex.None)
                };
            Current = await MultiplayerService.Instance.CreateSessionAsync(options);
            WatchNetworkStop();
            return Current;
        }

        public static async Task<ISession> JoinOnlineAsync(string code)
        {
            online = true;
            await EnsureSignedInAsync();
            Current = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant());
            WatchNetworkStop();
            return Current;
        }

        public static bool HostLan()
        {
            online = false;
            Transport().SetConnectionData("127.0.0.1", LanPort, "0.0.0.0");
            return NetworkManager.Singleton.StartHost();
        }

        public static bool JoinLan(string address)
        {
            online = false;
            Transport().SetConnectionData(string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim(), LanPort);
            return NetworkManager.Singleton.StartClient();
        }

        public static async Task LeaveAsync()
        {
            if (leaving) return;
            leaving = true;
            try
            {
                if (Current != null) await Current.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Session] leave failed: {e.Message}");
            }
            finally
            {
                Current = null;
                online = false;
                leaving = false;
            }
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) NetworkManager.Singleton.Shutdown();
        }

        /// <summary>When the connection drops (host left, network lost) leave the online session too.</summary>
        private static void WatchNetworkStop()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            nm.OnClientStopped -= OnNetworkStopped;
            nm.OnClientStopped += OnNetworkStopped;
        }

        private static void OnNetworkStopped(bool wasHost)
        {
            NetworkManager.Singleton.OnClientStopped -= OnNetworkStopped;
            if (Current == null || leaving) return;
            Debug.Log("[Session] connection ended; leaving the online session");
            _ = LeaveAsync();
        }

        private static UnityTransport Transport() => NetworkManager.Singleton.GetComponent<UnityTransport>();
    }
}
