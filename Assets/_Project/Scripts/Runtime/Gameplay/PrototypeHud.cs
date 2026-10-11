using System.Linq;
using MurderMystery.Core;
using UnityEngine;

namespace MurderMystery.Gameplay
{
    /// <summary>
    /// IMGUI HUD for the prototype: phase, points, focus prompt, log, notebook, NPC topic menu.
    /// Replace with real UI later; kept dependency-free so it works in any scene.
    /// </summary>
    public class PrototypeHud : MonoBehaviour
    {
        [SerializeField] private PlayerInteractor interactor;

        private Vector2 notebookScroll;
        private Vector2 topicScroll;
        private string lastAnswer;
        private GUIStyle wrap;

        public void Init(PlayerInteractor source) => interactor = source;

        private void OnGUI()
        {
            var host = GameSessionHost.Instance;
            if (host == null || interactor == null) return;
            wrap ??= new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 15 };
            var s = host.Session;
            var me = host.Local;
            var myName = s.Data.CharacterById[host.LocalId].name;

            GUI.Box(new Rect(10, 10, 360, 92), GUIContent.none);
            var flow = host.Gateway.Flow;
            var timer = flow.Current != null && flow.Current.durationSec > 0
                ? $"  {Mathf.Max(0, (int)flow.Remaining) / 60}:{Mathf.Max(0, (int)flow.Remaining) % 60:00}"
                : "";
            GUI.Label(new Rect(20, 16, 340, 22), $"{myName}  |  {GameSessionHost.PhaseLabel(s.Phase)}{timer}", wrap);
            GUI.Label(new Rect(20, 40, 340, 22), $"조사 점수 {me.InvestigationPoints}  ·  질문 {me.QuestionsLeft}  ·  소지품 {me.Inventory.Count}", wrap);
            GUI.Label(new Rect(20, 64, 340, 34), "E 살펴보기/대화  R 숨긴 물건 회수  Tab 수첩  N 다음 단계  1~5 인물", wrap);

            // Crosshair and prompt.
            GUI.Label(new Rect(Screen.width / 2f - 5, Screen.height / 2f - 12, 20, 24), "+", wrap);
            string prompt = null;
            if (interactor.NpcFocused) prompt = "E: 도 검률에게 묻기";
            else if (interactor.Focused != null)
            {
                var spot = interactor.Focused;
                var deep = spot.Layer == Investigation.SpotLayer.Deep ? " (2차 조사부터)" : "";
                prompt = $"E: {spot.DisplayName} 살펴보기 ({GameRules.SpotCost}점){deep}";
                var owns = s.Spots.TryGetValue(spot.SpotId, out var st) &&
                           st.RemainingItems.Any(id => s.Data.ClueById[id].owner == host.LocalId);
                if (owns) prompt += $"   R: 숨긴 물건 회수 ({GameRules.RetrieveCost}점)";
            }
            if (prompt != null)
                GUI.Label(new Rect(Screen.width / 2f - 220, Screen.height / 2f + 20, 440, 30), prompt, wrap);

            // Log.
            var y = Screen.height - 24 * host.Log.Count - 12;
            foreach (var line in host.Log)
            {
                GUI.Label(new Rect(14, y, 700, 24), line, wrap);
                y += 24;
            }

            if (s.Phase == GamePhase.Vote) DrawVote(host);
            if (s.Phase == GamePhase.Ending) DrawEnding(host);
            if (interactor.NotebookOpen) DrawNotebook(host, me);
            if (interactor.NpcMenuOpen) DrawNpcMenu(host);
        }

        private int bonusChoice = -1;
        private Vector2 endingScroll;

        // Solo prototype: the local seat votes; the idle seats vote at random so the flow can finish.
        private void DrawVote(GameSessionHost host)
        {
            var g = host.Gateway;
            if (g.Vote == null) return;
            FirstPersonController.SetCursorLocked(false);
            var rect = new Rect(Screen.width / 2f - 260, 120, 520, 360);
            GUI.Box(rect, g.Vote.Stage == VoteStage.Runoff ? "결선 투표 — 검시 보고서에 올릴 이름" : "투표 — 검시 보고서에 올릴 이름");
            GUILayout.BeginArea(new Rect(rect.x + 12, rect.y + 28, rect.width - 24, rect.height - 40));
            var me = host.LocalId;
            if (g.Vote.Stage == VoteStage.Main && !host.Session.Data.CharacterById[me].isCulprit)
            {
                var q = host.Session.Data.Characters.bonusQuestion;
                GUILayout.Label($"보너스: {q.text}", wrap);
                bonusChoice = GUILayout.SelectionGrid(bonusChoice, q.choices, 1);
            }
            foreach (var c in g.Vote.Candidates.Where(c => c != me))
            {
                if (!GUILayout.Button(host.Session.Data.CharacterById[c].name)) continue;
                host.Report(g.CastVote(me, c, bonusChoice));
                var rng = new System.Random();
                foreach (var seat in host.Session.Players.Keys.Where(k => k != me).ToList())
                {
                    if (g.Vote == null || g.Vote.Stage == VoteStage.Done) break;
                    var options = g.Vote.Candidates.Where(x => x != seat).ToList();
                    if (options.Count > 0) g.CastVote(seat, options[rng.Next(options.Count)], rng.Next(4));
                }
            }
            GUILayout.EndArea();
        }

        private void DrawEnding(GameSessionHost host)
        {
            var g = host.Gateway;
            var data = host.Session.Data;
            FirstPersonController.SetCursorLocked(false);
            var rect = new Rect(Screen.width / 2f - 330, 60, 660, Screen.height - 120);
            GUI.Box(rect, "엔딩");
            GUILayout.BeginArea(new Rect(rect.x + 14, rect.y + 28, rect.width - 28, rect.height - 40));
            endingScroll = GUILayout.BeginScrollView(endingScroll);
            var key = g.Vote != null && g.Vote.Stage == VoteStage.Done ? g.Vote.EndingKey : "C";
            var ending = data.Endings.endings.FirstOrDefault(e => e.key == key);
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
            foreach (var sheet in host.Session.Score(g.Vote?.Accused).Values.OrderByDescending(x => x.Total))
            {
                var goals = string.Join(", ", sheet.Goals.Select(x => $"{(x.Achieved ? "○" : "×")}{x.Type}"));
                GUILayout.Label($"{data.CharacterById[sheet.CharacterId].name}: {sheet.Total}점  ({goals})", wrap);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawNotebook(GameSessionHost host, PlayerState me)
        {
            var s = host.Session;
            var rect = new Rect(Screen.width - 470, 10, 460, Screen.height - 20);
            GUI.Box(rect, "수첩");
            GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 26, rect.width - 20, rect.height - 36));
            notebookScroll = GUILayout.BeginScrollView(notebookScroll);

            GUILayout.Label("소지품", wrap);
            foreach (var id in me.Inventory.ToList())
            {
                var clue = s.Data.ClueById[id];
                GUILayout.BeginHorizontal();
                GUILayout.Label(clue.title, wrap, GUILayout.Width(250));
                if (clue.@sealed && !s.IsOpened(id) && GUILayout.Button("뜯기")) host.Report(s.OpenEnvelope(host.LocalId, id));
                if (GUILayout.Button("공개")) host.Report(s.Disclose(host.LocalId, id));
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.Label("공개 단서 게시판", wrap);
            foreach (var id in s.PublicBoard) GUILayout.Label($"◆ {s.Data.ClueById[id].title}", wrap);

            GUILayout.Space(8);
            GUILayout.Label("기록", wrap);
            foreach (var e in me.Notebook) GUILayout.Label($"• {e.Text}", wrap);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawNpcMenu(GameSessionHost host)
        {
            var rect = new Rect(Screen.width / 2f - 260, Screen.height / 2f - 220, 520, 440);
            GUI.Box(rect, "도 검률에게 무엇을 묻겠소?");
            GUILayout.BeginArea(new Rect(rect.x + 12, rect.y + 28, rect.width - 24, rect.height - 40));
            topicScroll = GUILayout.BeginScrollView(topicScroll, GUILayout.Height(250));
            foreach (var topic in host.Session.VisibleTopics(host.LocalId))
            {
                if (GUILayout.Button($"[{topic.category}] {topic.label}"))
                {
                    var answer = host.Session.AskNpc(host.LocalId, topic.id);
                    lastAnswer = answer.Text;
                    host.Push($"도 검률: {answer.Text}");
                }
            }
            GUILayout.EndScrollView();
            GUILayout.Label($"남은 질문: {host.Local.QuestionsLeft}", wrap);
            if (!string.IsNullOrEmpty(lastAnswer)) GUILayout.Label(lastAnswer, wrap);
            if (GUILayout.Button("닫기"))
            {
                interactor.NpcMenuOpen = false;
                FirstPersonController.SetCursorLocked(true);
            }
            GUILayout.EndArea();
        }
    }
}
