using System.Linq;
using MurderMystery.Core;
using UnityEngine;

namespace MurderMystery.Gameplay
{
    /// <summary>
    /// IMGUI HUD driven only by the local client's snapshot, so the same screen works offline and online:
    /// phase/timer, focus prompt, log, notebook (items, board, secret talks, ready), NPC menu, vote, ending.
    /// </summary>
    public class PrototypeHud : MonoBehaviour
    {
        [SerializeField] private PlayerInteractor interactor;

        private Vector2 notebookScroll;
        private Vector2 topicScroll;
        private Vector2 endingScroll;
        private int bonusChoice = -1;
        private GUIStyle wrap;

        public void Init(PlayerInteractor source) => interactor = source;

        private string Name(IGameClient c, string id) =>
            c.Data.CharacterById.TryGetValue(id ?? string.Empty, out var ch) ? ch.name : id;

        private void OnGUI()
        {
            var c = GameClient.Current;
            var snap = c?.Snapshot;
            if (snap == null || interactor == null) return;
            wrap ??= new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 15 };
            var phase = snap.Phase;

            if (phase == GamePhase.Ending)
            {
                DrawEnding(c, snap);
                return;
            }

            GUI.Box(new Rect(10, 10, 440, 100), GUIContent.none);
            var timer = snap.timed ? $"  {Mathf.Max(0, (int)snap.remainingSec) / 60}:{Mathf.Max(0, (int)snap.remainingSec) % 60:00}" : "";
            GUI.Label(new Rect(20, 16, 420, 22), $"{Name(c, snap.characterId)}  |  {GameSessionHost.PhaseLabel(phase)}{timer}", wrap);
            GUI.Label(new Rect(20, 40, 420, 22), $"조사 점수 {snap.investigationPoints}  ·  질문 {snap.questionsLeft}  ·  밀담 {snap.secretTalksLeft}  ·  소지품 {snap.inventory.Length}", wrap);
            var help = c.IsOffline ? "E 살펴보기/대화  R 숨긴 물건 회수  Tab 수첩\nSpace 점프  C 앉기  N 다음 단계  1~5 인물 전환" : "E 살펴보기/대화  R 숨긴 물건 회수  Tab 수첩·밀담·준비\nSpace 점프  C 앉기/일어서기";
            GUI.Label(new Rect(20, 64, 420, 44), help, wrap);

            if (!string.IsNullOrEmpty(snap.talkPartner))
                GUI.Label(new Rect(Screen.width / 2f - 200, 14, 400, 24), $"밀담 중: {Name(c, snap.talkPartner)}  ({(int)snap.talkRemainingSec}초)", wrap);
            else if (snap.incomingTalks.Length > 0)
                GUI.Label(new Rect(Screen.width / 2f - 200, 14, 400, 24), $"{Name(c, snap.incomingTalks[0].from)}이(가) 밀담을 청합니다 (Tab)", wrap);

            // Crosshair and prompt.
            GUI.Label(new Rect(Screen.width / 2f - 5, Screen.height / 2f - 12, 20, 24), "+", wrap);
            string prompt = null;
            if (interactor.NpcFocused) prompt = "E: 도 검률에게 묻기";
            else if (interactor.Focused != null)
            {
                var spot = interactor.Focused;
                var lockData = c.Data.LockOfSpot(spot.SpotId);
                if (lockData != null && !PlayerInteractor.IsUnlocked(c, lockData.id))
                {
                    prompt = $"{lockData.lockedText}  E: 열쇠를 써 본다";
                }
                else
                {
                    var deep = spot.Layer == Investigation.SpotLayer.Deep ? " (2차 조사부터)" : "";
                    prompt = $"E: {spot.DisplayName} 살펴보기 ({GameRules.SpotCost}점){deep}";
                    if (snap.retrievableSpots.Contains(spot.SpotId)) prompt += $"   R: 숨긴 물건 회수 ({GameRules.RetrieveCost}점)";
                }
            }
            else if (interactor.FocusedDoor != null && c.Data.LockById.TryGetValue(interactor.FocusedDoor.LockId, out var door))
            {
                prompt = door.Openable ? $"{door.lockedText}  E: 열쇠를 써 본다" : door.lockedText;
            }
            if (prompt != null)
                GUI.Label(new Rect(Screen.width / 2f - 220, Screen.height / 2f + 20, 440, 30), prompt, wrap);

            var y = Screen.height - 24 * c.Log.Count - 12;
            foreach (var line in c.Log)
            {
                GUI.Label(new Rect(14, y, 700, 24), line, wrap);
                y += 24;
            }

            // The reading phase opens the book; it can be reopened from the notebook at any time.
            if (phase != lastPhase)
            {
                if (phase == GamePhase.Reading)
                {
                    showBook = true;
                    interactor.SetNotebook(true);
                }
                lastPhase = phase;
            }
            if (phase == GamePhase.Vote) DrawVote(c, snap);
            if (interactor.NotebookOpen) DrawNotebook(c, snap);
            if (interactor.NpcMenuOpen) DrawNpcMenu(c, snap);
        }

        private void DrawVote(IGameClient c, PlayerSnapshot snap)
        {
            if (string.IsNullOrEmpty(snap.voteStage)) return;
            FirstPersonController.SetCursorLocked(false);
            var rect = new Rect(Screen.width / 2f - 260, 120, 520, 360);
            GUI.Box(rect, snap.voteStage == nameof(VoteStage.Runoff) ? "결선 투표 — 검시 보고서에 올릴 이름" : "투표 — 검시 보고서에 올릴 이름");
            GUILayout.BeginArea(new Rect(rect.x + 12, rect.y + 28, rect.width - 24, rect.height - 40));
            if (snap.hasVoted)
            {
                GUILayout.Label("투표했습니다. 다른 사람을 기다리는 중…", wrap);
            }
            else
            {
                var me = snap.characterId;
                if (snap.voteStage == nameof(VoteStage.Main) && !c.Data.CharacterById[me].isCulprit)
                {
                    var q = c.Data.Characters.bonusQuestion;
                    GUILayout.Label($"보너스: {q.text}", wrap);
                    bonusChoice = GUILayout.SelectionGrid(bonusChoice, q.choices, 1);
                }
                foreach (var target in snap.voteCandidates.Where(t => t != me))
                    if (GUILayout.Button(Name(c, target))) c.Vote(target, bonusChoice);
            }
            GUILayout.EndArea();
        }

        private void DrawEnding(IGameClient c, PlayerSnapshot snap)
        {
            var data = c.Data;
            FirstPersonController.SetCursorLocked(false);
            var rect = new Rect(Screen.width / 2f - 330, 30, 660, Screen.height - 60);
            // IMGUI boxes are translucent; stack them for an opaque reading panel.
            for (var i = 0; i < 4; i++) GUI.Box(rect, i == 0 ? "엔딩" : string.Empty);
            GUILayout.BeginArea(new Rect(rect.x + 14, rect.y + 28, rect.width - 28, rect.height - 40));
            endingScroll = GUILayout.BeginScrollView(endingScroll);
            var ending = data.Endings.endings.FirstOrDefault(e => e.key == (snap.endingKey ?? "C"));
            if (ending != null)
            {
                GUILayout.Label($"【{ending.title}】", wrap);
                foreach (var line in ending.lines) GUILayout.Label(line, wrap);
            }
            GUILayout.Space(10);
            GUILayout.Label("— 간밤의 진실 —", wrap);
            foreach (var stop in data.Endings.epilogue.replayStops) GUILayout.Label($"{stop.time}  {stop.text}", wrap);
            GUILayout.Space(10);
            GUILayout.Label("— 다섯 통의 문서 —", wrap);
            foreach (var id in data.Endings.epilogue.documentOrder)
                GUILayout.Label($"{data.ClueById[id].title}\n{data.ClueById[id].sealedText}", wrap);
            GUILayout.Label($"\"{data.Endings.epilogue.finalLine}\"", wrap);
            GUILayout.Space(10);
            GUILayout.Label("— 점수 —", wrap);
            foreach (var s in snap.scores) GUILayout.Label($"{Name(c, s.characterId)}: {s.total}점  ({s.goals})", wrap);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private bool showBook;
        private Vector2 bookScroll;
        private GamePhase lastPhase = GamePhase.CharacterSelect;
        private GUIStyle bookStyle;

        private void DrawBook(IGameClient c, PlayerSnapshot snap, Rect rect)
        {
            bookStyle ??= new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 15, richText = true };
            GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 56, rect.width - 20, rect.height - 66));
            bookScroll = GUILayout.BeginScrollView(bookScroll);
            var mine = c.Data.Book(snap.characterId);
            var common = c.Data.Book("common");
            if (mine == null && common == null) GUILayout.Label("시나리오북이 없습니다.", wrap);
            if (mine != null) GUILayout.Label(mine, bookStyle);
            if (common != null)
            {
                GUILayout.Space(16);
                GUILayout.Label(common, bookStyle);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawNotebook(IGameClient c, PlayerSnapshot snap)
        {
            var rect = new Rect(Screen.width - 520, 10, 510, Screen.height - 20);
            GUI.Box(rect, showBook ? "시나리오북 (나만 본다)" : "수첩");
            if (GUI.Button(new Rect(rect.x + 10, rect.y + 24, 120, 26), showBook ? "수첩 보기" : "시나리오북")) showBook = !showBook;
            if (showBook)
            {
                DrawBook(c, snap, rect);
                return;
            }
            GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 56, rect.width - 20, rect.height - 66));
            notebookScroll = GUILayout.BeginScrollView(notebookScroll);

            if (snap.timed && GUILayout.Button(snap.ready ? "준비 완료 취소" : "준비 완료 (모두 누르면 다음 단계)")) c.SetReady(!snap.ready);

            GUILayout.Label("밀담", wrap);
            if (!string.IsNullOrEmpty(snap.talkPartner))
            {
                GUILayout.Label($"{Name(c, snap.talkPartner)}와(과) 밀담 중 · {(int)snap.talkRemainingSec}초 남음", wrap);
                if (GUILayout.Button("밀담 끝내기")) c.EndTalk();
            }
            foreach (var req in snap.incomingTalks)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{Name(c, req.from)}의 요청", wrap, GUILayout.Width(200));
                if (GUILayout.Button("수락")) c.RespondTalk(req.id, true);
                if (GUILayout.Button("거절")) c.RespondTalk(req.id, false);
                GUILayout.EndHorizontal();
            }
            if (string.IsNullOrEmpty(snap.talkPartner) && snap.secretTalksLeft > 0)
            {
                GUILayout.BeginHorizontal();
                foreach (var seat in snap.seats.Where(s => s.characterId != snap.characterId))
                    if (GUILayout.Button(Name(c, seat.characterId))) c.RequestTalk(seat.characterId);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.Label("소지품", wrap);
            foreach (var card in snap.inventory)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(card.title, wrap, GUILayout.Width(200));
                if (card.sealedClosed && GUILayout.Button("뜯기")) c.Open(card.id);
                if (GUILayout.Button("공개")) c.Disclose(card.id);
                if (!string.IsNullOrEmpty(snap.talkPartner))
                {
                    if (GUILayout.Button("보여 주기")) c.Show(snap.talkPartner, card.id);
                    if (GUILayout.Button("건네기")) c.Give(snap.talkPartner, card.id);
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.Label("공개 단서 게시판", wrap);
            foreach (var card in snap.publicBoard) GUILayout.Label($"◆ {card.title}: {card.text}", wrap);

            GUILayout.Space(8);
            GUILayout.Label("기록", wrap);
            foreach (var line in snap.notebook) GUILayout.Label($"• {line}", wrap);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawNpcMenu(IGameClient c, PlayerSnapshot snap)
        {
            var rect = new Rect(Screen.width / 2f - 260, Screen.height / 2f - 220, 520, 440);
            GUI.Box(rect, "도 검률에게 무엇을 묻겠소?");
            GUILayout.BeginArea(new Rect(rect.x + 12, rect.y + 28, rect.width - 24, rect.height - 40));
            topicScroll = GUILayout.BeginScrollView(topicScroll, GUILayout.Height(250));
            foreach (var topic in snap.topics)
                if (GUILayout.Button($"[{topic.category}] {topic.label}")) c.Ask(topic.id);
            GUILayout.EndScrollView();
            GUILayout.Label($"남은 질문: {snap.questionsLeft}", wrap);
            var last = c.Log.LastOrDefault(l => l.StartsWith("도 검률:"));
            if (!string.IsNullOrEmpty(last)) GUILayout.Label(last, wrap);
            if (GUILayout.Button("닫기"))
            {
                interactor.NpcMenuOpen = false;
                FirstPersonController.SetCursorLocked(!interactor.NotebookOpen);
            }
            GUILayout.EndArea();
        }
    }
}
