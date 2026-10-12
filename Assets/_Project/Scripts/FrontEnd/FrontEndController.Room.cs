using System.Linq;
using MurderMystery.Core;
using MurderMystery.Gameplay;
using MurderMystery.Network;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace MurderMystery.FrontEnd
{
    /// <summary>
    /// Inside the map: the room overlay where everyone picks a character while standing in the yard, then a
    /// compact bar (code · ready · host start) until the game begins, plus a corner button to leave.
    /// </summary>
    public partial class FrontEndController
    {
        private VisualElement room;
        private Label roomScenario;
        private Label roomCode;
        private Button copyCode;
        private Label roomHint;
        private VisualElement seatRow;
        private Label roomVoice;
        private Button readyButton;
        private Button startButton;
        private Button repickButton;
        private Button leaveCorner;

        private string seatSignature;
        private bool expanded = true;
        private bool wasInRoom;
        private bool leaving;
        private float roomTimer;
        private bool lastFull;
        private string lastSeat;

        private void BuildRoom()
        {
            room = Add(root, "room");
            room.AddToClassList("off");

            var banner = Add(room, "room-banner");
            roomScenario = Serif(AddLabel(banner, "room-banner__scenario", string.Empty));
            AddLabel(banner, "room-banner__code-label", "초대 코드");
            roomCode = AddLabel(banner, "room-banner__code", string.Empty);
            copyCode = AddButton(banner, "복사", () =>
            {
                GUIUtility.systemCopyBuffer = SessionService.JoinCode ?? string.Empty;
                Toast("초대 코드를 복사했습니다.");
            });

            roomHint = AddLabel(room, "room-hint", string.Empty);
            seatRow = Add(room, "seat-row");

            var buttons = Add(room, "room-buttons");
            roomVoice = AddLabel(buttons, "room-voice", string.Empty);
            repickButton = AddButton(buttons, "인물 다시 고르기", () =>
            {
                expanded = true;
                seatSignature = null;
            });
            readyButton = AddButton(buttons, "준비", () =>
            {
                var net = NetGame.Instance;
                if (net?.Snapshot == null) return;
                net.SetReady(!net.Snapshot.ready);
                sound.Stamp();
            });
            startButton = AddButton(buttons, "배역 마감하고 시작", () =>
                Confirm("시작할까요?", "인물을 고르지 않은 자리는 비워 둔 채 시작합니다. 시작하면 각자 시나리오북을 읽는 시간이 됩니다.", "시작",
                    () => NetGame.Instance?.HostAdvance()), "btn--primary");
            AddButton(buttons, "방 나가기", AskLeave);

            leaveCorner = AddButton(root, "메뉴로 나가기", AskLeave);
            leaveCorner.AddToClassList("leave-corner");
            leaveCorner.AddToClassList("off");
        }

        private void AskLeave() =>
            Confirm("나갈까요?", GameLauncher.InRoom && NetworkManager.Singleton.IsHost
                    ? "방을 연 사람이 나가면 방이 닫히고 모두 처음 화면으로 돌아갑니다."
                    : "처음 화면으로 돌아갑니다. 같은 방에 다시 들어오면 맡았던 인물을 다시 고를 수 있습니다.",
                "나가기", Leave);

        private void Leave()
        {
            leaving = true;
            ShowLoading("처음 화면으로");
            schedule(() => _ = GameLauncher.ReturnToTitleAsync(), 400);
        }

        private void UpdateRoom()
        {
            var inMap = !InTitleScene;
            var inRoom = GameLauncher.InRoom;

            // Lost the room while in a map: host left or the connection dropped.
            if (inMap && wasInRoom && !inRoom && !leaving)
            {
                leaving = true;
                Toast("방과의 연결이 끊겼습니다. 처음 화면으로 돌아갑니다.", 4f);
                schedule(() => _ = GameLauncher.ReturnToTitleAsync(), 2500);
            }
            wasInRoom = inMap && inRoom;
            if (!inMap)
            {
                leaving = false;
                expanded = true;
                lastSeat = null;
            }

            var net = NetGame.Instance;
            var show = inMap && inRoom && net != null && net.Seats.Length > 0 &&
                       (net.LobbyPhase == nameof(GamePhase.CharacterSelect) || !net.HasSeat);
            room.EnableInClassList("off", !show);

            // A new seat (picked here or restored on reconnect) folds the overlay so the player can look round.
            var seatNow = net?.Snapshot?.characterId;
            if (seatNow != lastSeat)
            {
                if (seatNow != null && lastSeat != seatNow) expanded = false;
                lastSeat = seatNow;
            }
            var full = show && (!net.HasSeat || expanded);
            var cursorFree = UnityEngine.Cursor.lockState != CursorLockMode.Locked;
            leaveCorner.EnableInClassList("off", !inMap || full || !cursorFree);

            // Only on change: the notebook also toggles InputEnabled.
            if (full != lastFull)
            {
                lastFull = full;
                var fpc = FindFirstObjectByType<FirstPersonController>();
                if (fpc != null) fpc.InputEnabled = !full;
            }
            if (!show) return;

            if (full && UnityEngine.Cursor.lockState == CursorLockMode.Locked) FirstPersonController.SetCursorLocked(false);
            room.EnableInClassList("room--compact", !full);
            seatRow.EnableInClassList("off", !full);
            roomHint.EnableInClassList("off", !full);

            roomTimer -= Time.unscaledDeltaTime;
            if (roomTimer > 0f) return;
            roomTimer = 0.25f;

            var scenario = net.Scenario;
            roomScenario.text = scenario != null ? scenario.title : string.Empty;
            var code = SessionService.JoinCode;
            roomCode.text = string.IsNullOrEmpty(code) ? "LAN" : code;
            copyCode.EnableInClassList("off", string.IsNullOrEmpty(code));
            roomVoice.text = VoiceService.Status;

            var selecting = net.LobbyPhase == nameof(GamePhase.CharacterSelect);
            var taken = net.Seats.Count(s => !string.IsNullOrEmpty(s.playerName));
            roomHint.text = selecting
                ? $"맡을 인물을 고르세요 · {taken}/{net.Seats.Length}명 선택 · 고르면 마당을 둘러볼 수 있습니다"
                : "이미 진행 중인 방입니다. 원래 맡았던 인물을 고르면 다시 들어갈 수 있습니다.";

            var mine = net.Snapshot?.characterId;
            readyButton.EnableInClassList("off", !selecting || mine == null);
            readyButton.text = net.Snapshot != null && net.Snapshot.ready ? "준비 취소" : "준비";
            startButton.EnableInClassList("off", !selecting || !NetworkManager.Singleton.IsHost);
            repickButton.EnableInClassList("off", full || !selecting);

            var signature = mine + "|" + string.Join(",", net.Seats.Select(s => s.characterId + ":" + s.playerName));
            if (full && signature != seatSignature)
            {
                seatSignature = signature;
                BuildSeats(net, scenario, mine);
            }
        }

        private void BuildSeats(NetGame net, ScenarioManifest scenario, string mine)
        {
            seatRow.Clear();
            foreach (var seat in net.Seats)
            {
                var cast = scenario?.cast.FirstOrDefault(c => c.id == seat.characterId);
                var card = Add(seatRow, "seat-card");
                var isMine = seat.characterId == mine;
                var taken = !string.IsNullOrEmpty(seat.playerName) && !isMine;
                card.EnableInClassList("seat-card--mine", isMine);
                card.EnableInClassList("seat-card--taken", taken);

                var portrait = Add(card, "seat-card__portrait");
                var tex = Portrait(seat.characterId);
                if (tex != null) portrait.style.backgroundImage = new StyleBackground(tex);
                var body = Add(card, "seat-card__body");
                Serif(AddLabel(body, "seat-card__name", cast?.name ?? net.Data.CharacterById[seat.characterId].name));
                if (cast != null)
                {
                    AddLabel(body, "seat-card__status", $"{cast.status} · {cast.age}세");
                    AddLabel(body, "seat-card__blurb", cast.blurb);
                }
                AddLabel(body, "seat-card__taker", isMine ? "내 인물" : taken ? $"{seat.playerName} 님" : string.Empty);
                if (!taken && !isMine)
                {
                    var id = seat.characterId;
                    var pick = AddButton(body, "이 인물로", () =>
                    {
                        net.ClaimSeat(id, GameSettings.PlayerName);
                        sound.Stamp();
                        expanded = false;
                        schedule(() => FirstPersonController.SetCursorLocked(true), 200);
                    }, "btn--primary", "seat-card__button");
                    pick.style.marginRight = 0;
                }
                card.RegisterCallback<PointerEnterEvent>(_ => sound.Hover());
            }
        }
    }
}
