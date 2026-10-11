using System;
using System.Linq;
using MurderMystery.Core;
using MurderMystery.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace MurderMystery.Network
{
    /// <summary>
    /// IMGUI lobby: offline solo test, online room (Sessions + Relay) by code, LAN host/join for local tests,
    /// then character selection. Hidden once the local player holds a seat.
    /// </summary>
    public class LobbyMenu : MonoBehaviour
    {
        private string playerName = "플레이어";
        private string joinCode = string.Empty;
        private string lanAddress = "127.0.0.1";
        private string status = string.Empty;
        private bool busy;
        private GUIStyle wrap;

        private static bool Connected => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        private void OnGUI()
        {
            wrap ??= new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 15 };
            if (GameClient.Current != null && GameClient.Current.IsOffline) return;

            var net = NetGame.Instance;
            if (Connected && net != null && net.HasSeat) return;

            FirstPersonController.SetCursorLocked(false);
            var rect = new Rect(Screen.width / 2f - 240, 60, 480, 420);
            for (var i = 0; i < 3; i++) GUI.Box(rect, i == 0 ? "윤 대감 댁의 마지막 밤" : string.Empty);
            GUILayout.BeginArea(new Rect(rect.x + 16, rect.y + 30, rect.width - 32, rect.height - 46));

            if (!Connected) DrawConnect();
            else if (net == null) GUILayout.Label("접속 중…", wrap);
            else DrawSeats(net);

            if (!string.IsNullOrEmpty(status)) GUILayout.Label(status, wrap);
            GUILayout.EndArea();
        }

        private void DrawConnect()
        {
            GUILayout.Label("이름", wrap);
            playerName = GUILayout.TextField(playerName, 16);
            GUI.enabled = !busy;

            GUILayout.Space(8);
            if (GUILayout.Button("방 만들기 (온라인)")) Run(async () =>
            {
                var s = await SessionService.HostOnlineAsync();
                status = $"방 코드: {s.Code}  — 친구에게 알려 주세요.";
            });
            GUILayout.BeginHorizontal();
            joinCode = GUILayout.TextField(joinCode, 12, GUILayout.Width(180));
            if (GUILayout.Button("코드로 참가")) Run(async () =>
            {
                await SessionService.JoinOnlineAsync(joinCode);
                status = "참가했습니다.";
            });
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("로컬 테스트", wrap);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("LAN 호스트")) status = SessionService.HostLan() ? "LAN 호스트 시작" : "LAN 호스트 실패";
            lanAddress = GUILayout.TextField(lanAddress, 40, GUILayout.Width(140));
            if (GUILayout.Button("LAN 참가")) status = SessionService.JoinLan(lanAddress) ? "LAN 접속 중…" : "LAN 접속 실패";
            GUILayout.EndHorizontal();
            if (GUILayout.Button("혼자 테스트 (오프라인)")) PrototypeBootstrap.SpawnOffline();
            GUI.enabled = true;
        }

        private void DrawSeats(NetGame net)
        {
            if (!string.IsNullOrEmpty(SessionService.JoinCode)) GUILayout.Label($"방 코드: {SessionService.JoinCode}", wrap);
            GUILayout.Label($"인물을 고르세요 · {VoiceService.Status}", wrap);
            foreach (var seat in net.Seats)
            {
                var ch = net.Data.CharacterById[seat.characterId];
                var taken = !string.IsNullOrEmpty(seat.playerName);
                GUI.enabled = !taken;
                if (GUILayout.Button(taken ? $"{ch.name} — {seat.playerName}" : ch.name)) net.ClaimSeat(seat.characterId, playerName);
                GUI.enabled = true;
            }
            if (net.LobbyPhase != nameof(GamePhase.CharacterSelect))
                GUILayout.Label("이미 게임이 진행 중입니다. 원래 맡았던 인물을 고르면 다시 들어갈 수 있습니다.", wrap);
            if (NetworkManager.Singleton.IsHost && GUILayout.Button("호스트: 다음 단계로 (배역 선택 마감)")) net.HostAdvance();
            if (GUILayout.Button("나가기")) Run(SessionService.LeaveAsync);
        }

        private async void Run(Func<System.Threading.Tasks.Task> action)
        {
            busy = true;
            status = "처리 중…";
            try
            {
                await action();
            }
            catch (Exception e)
            {
                status = $"실패: {e.Message}";
                Debug.LogWarning($"[Lobby] {e}");
            }
            finally
            {
                busy = false;
            }
        }
    }
}
