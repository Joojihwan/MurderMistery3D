using System;
using System.Collections.Generic;
using System.Linq;
using MurderMystery.Core;
using MurderMystery.Gameplay;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace MurderMystery.Network
{
    [Serializable]
    internal class SeatTable
    {
        public string phase;
        public SeatView[] seats = Array.Empty<SeatView>();
    }

    /// <summary>
    /// Host-authoritative game (docs/design/tech_decisions.md §3). The server owns the LocalSessionGateway;
    /// clients send intents by RPC and receive only their own snapshot. Also the local IGameClient for UI.
    /// </summary>
    public class NetGame : NetworkBehaviour, IGameClient
    {
        private const float PushInterval = 0.5f;
        private const float GiveDistance = 2.5f;

        public static NetGame Instance { get; private set; }

        // Room key published by the host so every peer names the same voice channels.
        private readonly NetworkVariable<FixedString64Bytes> room = new NetworkVariable<FixedString64Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private string RoomKey => room.Value.ToString();

        // The host's scenario, so every peer loads the same rule data and shows the same cards.
        private readonly NetworkVariable<FixedString64Bytes> scenarioId = new NetworkVariable<FixedString64Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public ScenarioManifest Scenario => ScenarioCatalog.Find(scenarioId.Value.ToString()) ?? ScenarioCatalog.Default;
        private bool endingRecorded;

        // ---------------- server state
        private LocalSessionGateway gateway;
        private readonly Dictionary<ulong, string> seatOfClient = new Dictionary<ulong, string>();
        private readonly Dictionary<string, string> playerKeyOfSeat = new Dictionary<string, string>();
        private readonly Dictionary<string, string> nameOfSeat = new Dictionary<string, string>();
        private float pushTimer;

        // ---------------- client state
        private readonly List<string> log = new List<string>();
        private GameData data;
        private string lastVoiceRoom;
        private int lastNotice;
        private string lastVoicePhase;

        public string LocalId => Snapshot?.characterId;
        public GameData Data => data ??= GameData.LoadFromResources(Scenario?.dataPath ?? "Data");
        public PlayerSnapshot Snapshot { get; private set; }
        public IReadOnlyList<string> Log => log;
        public bool IsOffline => false;
        public event Action Changed;

        /// <summary>Public seat table and phase, available before the local player has a seat.</summary>
        public SeatView[] Seats { get; private set; } = Array.Empty<SeatView>();
        public string LobbyPhase { get; private set; } = nameof(GamePhase.CharacterSelect);
        public bool HasSeat => Snapshot != null;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            GameClient.Current = this;
            if (IsServer) scenarioId.Value = (GameLauncher.Scenario ?? ScenarioCatalog.Default)?.id ?? string.Empty;
            if (!IsServer && GameLauncher.Scenario == null) GameLauncher.Scenario = Scenario;
            if (IsServer)
            {
                gateway = new LocalSessionGateway(Data, Environment.TickCount);
                gateway.ActiveSeats = () => seatOfClient.Values.Distinct().ToList();
                gateway.AreNearby = Near;
                gateway.Announcement += line => AnnounceRpc(line);
                gateway.Rooms.Changed += PushAll;
                gateway.Notice += (_, __) => PushAll();
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
                PublishRoom();
            }
            room.OnValueChanged += (_, __) => StartVoice();
            StartVoice();
        }

        private void PublishRoom()
        {
            var key = SessionService.RoomKey;
            if (!string.IsNullOrEmpty(key) && RoomKey != key) room.Value = key;
        }

        private void StartVoice()
        {
            if (!string.IsNullOrEmpty(RoomKey)) _ = VoiceService.StartAsync(RoomKey);
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            if (ReferenceEquals(GameClient.Current, this)) GameClient.Current = null;
            if (Instance == this) Instance = null;
            _ = VoiceService.StopAsync();
        }

        private void Update()
        {
            if (!IsServer || gateway == null) return;
            gateway.Tick(Time.deltaTime);
            pushTimer -= Time.deltaTime;
            if (pushTimer > 0f) return;
            PublishRoom();
            PushAll();
        }

        // =========================================================== server side

        private void OnClientDisconnected(ulong clientId)
        {
            // The seat stays reserved for the same player key so a reconnect gets the same character back.
            // A dropped player leaves their small room (the council room keeps everyone).
            if (seatOfClient.TryGetValue(clientId, out var seat)) gateway.LeaveRoom(seat);
            seatOfClient.Remove(clientId);
            PushAll();
        }

        private bool Near(string a, string b)
        {
            NetPlayer Find(string seat) => FindObjectsByType<NetPlayer>(FindObjectsSortMode.None).FirstOrDefault(p => p.SeatId == seat);
            var pa = Find(a);
            var pb = Find(b);
            return pa != null && pb != null && Vector3.Distance(pa.transform.position, pb.transform.position) <= GiveDistance;
        }

        private SeatView[] SeatViews() => Data.Characters.characters.Select(c => new SeatView
        {
            characterId = c.id,
            playerName = seatOfClient.ContainsValue(c.id) && nameOfSeat.TryGetValue(c.id, out var n) ? n : string.Empty
        }).ToArray();

        private void PushAll()
        {
            pushTimer = PushInterval;
            var seats = SeatViews();
            SeatsRpc(JsonUtility.ToJson(new SeatTable { phase = gateway.Session.Phase.ToString(), seats = seats }));
            foreach (var pair in seatOfClient)
            {
                var snap = PlayerSnapshot.Build(gateway, pair.Value, seats);
                SnapshotRpc(JsonUtility.ToJson(snap), RpcTarget.Single(pair.Key, RpcTargetUse.Temp));
            }
        }

        private void Handle(RpcParams p, Func<string, ActionResult> action)
        {
            var sender = p.Receive.SenderClientId;
            if (!seatOfClient.TryGetValue(sender, out var seat)) return;
            var result = action(seat);
            ResultRpc(string.Join("\n", GameSessionHost.Describe(Data, result)), RpcTarget.Single(sender, RpcTargetUse.Temp));
            PushAll();
        }

        [Rpc(SendTo.Server)]
        private void ClaimSeatRpc(string characterId, string playerName, string playerKey, RpcParams p = default)
        {
            var sender = p.Receive.SenderClientId;
            var reply = TryClaim(sender, characterId, playerName, playerKey);
            ResultRpc(reply, RpcTarget.Single(sender, RpcTargetUse.Temp));
            PushAll();
        }

        private string TryClaim(ulong client, string characterId, string playerName, string playerKey)
        {
            if (!Data.CharacterById.ContainsKey(characterId)) return "없는 인물입니다.";
            var takenByOther = seatOfClient.Any(kv => kv.Value == characterId && kv.Key != client);
            var reserved = playerKeyOfSeat.TryGetValue(characterId, out var owner) && owner != playerKey && !takenByOther &&
                           gateway.Session.Phase != GamePhase.CharacterSelect;
            if (takenByOther || reserved) return "이미 다른 사람이 고른 인물입니다.";
            if (gateway.Session.Phase != GamePhase.CharacterSelect && owner != playerKey)
                return "배역 선택이 끝났습니다.";

            // Changing seat during character select frees the previous one.
            if (seatOfClient.TryGetValue(client, out var previous) && previous != characterId)
            {
                playerKeyOfSeat.Remove(previous);
                nameOfSeat.Remove(previous);
            }
            seatOfClient[client] = characterId;
            playerKeyOfSeat[characterId] = playerKey;
            nameOfSeat[characterId] = string.IsNullOrWhiteSpace(playerName) ? $"플레이어{client}" : playerName.Trim();

            foreach (var np in FindObjectsByType<NetPlayer>(FindObjectsSortMode.None).Where(x => x.OwnerClientId == client))
                np.SetSeat(characterId);
            return $"{Data.CharacterById[characterId].name}(으)로 참여합니다.";
        }

        [Rpc(SendTo.Server)]
        private void HostAdvanceRpc(RpcParams p = default)
        {
            if (p.Receive.SenderClientId != NetworkManager.ServerClientId) return;
            gateway.Flow.Advance();
            PushAll();
        }

        [Rpc(SendTo.Server)] private void InvestigateRpc(string spotId, RpcParams p = default) => Handle(p, id => gateway.Investigate(id, spotId));
        [Rpc(SendTo.Server)] private void RetrieveRpc(string spotId, RpcParams p = default) => Handle(p, id => gateway.RetrieveHidden(id, spotId));
        [Rpc(SendTo.Server)] private void UnlockRpc(string lockId, RpcParams p = default) => Handle(p, id => gateway.TryUnlock(id, lockId));
        [Rpc(SendTo.Server)] private void OpenRpc(string clueId, RpcParams p = default) => Handle(p, id => gateway.OpenEnvelope(id, clueId));
        [Rpc(SendTo.Server)] private void DiscloseRpc(string clueId, RpcParams p = default) => Handle(p, id => gateway.Disclose(id, clueId));
        [Rpc(SendTo.Server)] private void GiveRpc(string toId, string itemId, RpcParams p = default) => Handle(p, id => gateway.Give(id, toId, itemId));
        [Rpc(SendTo.Server)] private void ShowRpc(string toId, string clueId, RpcParams p = default) => Handle(p, id => gateway.Show(id, toId, clueId));
        [Rpc(SendTo.Server)] private void RequestRoomRpc(string roomId, RpcParams p = default) => Handle(p, id => gateway.RequestRoom(id, roomId));
        [Rpc(SendTo.Server)] private void RespondRoomRpc(int requestId, bool accept, RpcParams p = default) => Handle(p, id => gateway.RespondRoom(id, requestId, accept));
        [Rpc(SendTo.Server)] private void LeaveRoomRpc(RpcParams p = default) => Handle(p, id => gateway.LeaveRoom(id));
        [Rpc(SendTo.Server)] private void ReadyRpc(bool ready, RpcParams p = default) => Handle(p, id => gateway.SetReady(id, ready));
        [Rpc(SendTo.Server)] private void VoteRpc(string targetId, int bonus, RpcParams p = default) => Handle(p, id => gateway.CastVote(id, targetId, bonus));

        [Rpc(SendTo.Server)]
        private void AskRpc(string topicId, RpcParams p = default)
        {
            var sender = p.Receive.SenderClientId;
            if (!seatOfClient.TryGetValue(sender, out var seat)) return;
            var answer = gateway.AskNpc(seat, topicId);
            ResultRpc($"도 검률: {answer.Text}", RpcTarget.Single(sender, RpcTargetUse.Temp));
            PushAll();
        }

        // =========================================================== client side

        [Rpc(SendTo.SpecifiedInParams)]
        private void SnapshotRpc(string json, RpcParams p)
        {
            Snapshot = JsonUtility.FromJson<PlayerSnapshot>(json);
            // Messages addressed to this player (room requests answered, someone came in...).
            foreach (var n in Snapshot.notices.Where(n => n.seq > lastNotice))
            {
                Push(n.text);
                lastNotice = n.seq;
            }
            if (!endingRecorded && Snapshot.Phase == GamePhase.Ending)
            {
                endingRecorded = true;
                ScenarioCatalog.MarkPlayed(Scenario?.id);
            }
            UpdateVoice();
            Changed?.Invoke();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SeatsRpc(string json)
        {
            var table = JsonUtility.FromJson<SeatTable>(json);
            Seats = table.seats;
            LobbyPhase = table.phase;
            Changed?.Invoke();
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void ResultRpc(string text, RpcParams p)
        {
            foreach (var line in text.Split('\n').Where(l => l.Length > 0)) Push(line);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void AnnounceRpc(string line) => Push($"도 검률: {line}");

        private void Push(string line)
        {
            log.Add(line);
            if (log.Count > 8) log.RemoveAt(0);
            Changed?.Invoke();
        }

        private void UpdateVoice()
        {
            if (Snapshot == null) return;
            if (Snapshot.phase == lastVoicePhase && Snapshot.room == lastVoiceRoom) return;
            lastVoicePhase = Snapshot.phase;
            lastVoiceRoom = Snapshot.room;
            VoiceService.Apply(Snapshot.Phase, Snapshot.room);
        }

        // =========================================================== IGameClient (local UI → host)

        public void ClaimSeat(string characterId, string playerName) =>
            ClaimSeatRpc(characterId, playerName ?? string.Empty, SessionService.PlayerKey);

        public void HostAdvance() => HostAdvanceRpc();

        public void Investigate(string spotId) => InvestigateRpc(spotId);
        public void Retrieve(string spotId) => RetrieveRpc(spotId);
        public void TryUnlock(string lockId) => UnlockRpc(lockId);
        public void Open(string clueId) => OpenRpc(clueId);
        public void Disclose(string clueId) => DiscloseRpc(clueId);
        public void Give(string toId, string itemId) => GiveRpc(toId, itemId);
        public void Show(string toId, string clueId) => ShowRpc(toId, clueId);
        public void Ask(string topicId) => AskRpc(topicId);
        public void RequestRoom(string roomId) => RequestRoomRpc(roomId);
        public void RespondRoom(int requestId, bool accept) => RespondRoomRpc(requestId, accept);
        public void LeaveRoom() => LeaveRoomRpc();
        public void SetReady(bool ready) => ReadyRpc(ready);
        public void Vote(string targetId, int bonusAnswer) => VoteRpc(targetId, bonusAnswer);
    }
}
