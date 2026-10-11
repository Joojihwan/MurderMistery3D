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

        /// <summary>Stable id for the room, used to name voice channels. LAN rooms use a fixed id.</summary>
        public static string RoomKey => Current?.Id ?? "lan";
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

        public static async Task<ISession> HostOnlineAsync()
        {
            await EnsureSignedInAsync();
            var options = new SessionOptions { MaxPlayers = MaxPlayers, IsPrivate = true }.WithRelayNetwork();
            Current = await MultiplayerService.Instance.CreateSessionAsync(options);
            return Current;
        }

        public static async Task<ISession> JoinOnlineAsync(string code)
        {
            await EnsureSignedInAsync();
            Current = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant());
            return Current;
        }

        public static bool HostLan()
        {
            Transport().SetConnectionData("127.0.0.1", LanPort, "0.0.0.0");
            return NetworkManager.Singleton.StartHost();
        }

        public static bool JoinLan(string address)
        {
            Transport().SetConnectionData(string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim(), LanPort);
            return NetworkManager.Singleton.StartClient();
        }

        public static async Task LeaveAsync()
        {
            try
            {
                if (Current != null) await Current.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Session] leave failed: {e.Message}");
            }
            Current = null;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) NetworkManager.Singleton.Shutdown();
        }

        private static UnityTransport Transport() => NetworkManager.Singleton.GetComponent<UnityTransport>();
    }
}
