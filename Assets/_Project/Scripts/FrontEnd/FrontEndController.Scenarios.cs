using System.Collections.Generic;
using System.Linq;
using MurderMystery.Core;
using MurderMystery.Network;
using UnityEngine;
using UnityEngine.UIElements;

namespace MurderMystery.FrontEnd
{
    public partial class FrontEndController
    {
        private VisualElement scenariosScreen;
        private VisualElement cardRow;
        private VisualElement detail;
        private readonly Dictionary<string, VisualElement> cards = new Dictionary<string, VisualElement>();
        private ScenarioManifest selected;

        // Typewriter for the synopsis.
        private Label typewriterLabel;
        private string typewriterText = string.Empty;
        private float typewriterChars;

        private void BuildScenarios()
        {
            scenariosScreen = NewScreen("scenarios-screen");
            var header = Add(scenariosScreen, "screen-header");
            var back = new Button(() =>
            {
                sound.Click();
                ShowScreen(menuScreen);
            }) { text = "← 돌아가기" };
            back.AddToClassList("back-button");
            back.RegisterCallback<PointerEnterEvent>(_ => sound.Hover());
            header.Add(back);
            Serif(AddLabel(header, "screen-title", "사건첩"));
            AddLabel(header, "screen-title-sub", "풀어 볼 사건을 고르세요");

            var body = Add(scenariosScreen, "scenarios-body");
            cardRow = Add(body, "card-row");
            detail = Add(body, "detail");
            detail.AddToClassList("detail--hidden");
        }

        private void RefreshScenarios()
        {
            cardRow.Clear();
            cards.Clear();
            foreach (var s in ScenarioCatalog.All) cardRow.Add(cards[s.id] = BuildCard(s));
            var keep = selected != null ? ScenarioCatalog.Find(selected.id) : null;
            if (keep != null) Select(keep, false);
            else detail.AddToClassList("detail--hidden");
        }

        private VisualElement BuildCard(ScenarioManifest s)
        {
            var card = new VisualElement();
            card.AddToClassList("case-card");
            if (s.locked)
            {
                card.AddToClassList("case-card--sealed");
                var body = Add(card, "case-card__body");
                AddLabel(body, "case-card__subtitle", s.subtitle);
                var mark = Add(body, "sealed-mark");
                Serif(AddLabel(mark, "sealed-mark__text", "封"));
                AddLabel(body, "sealed-caption", s.status);
                card.RegisterCallback<PointerEnterEvent>(_ => sound.Hover());
                card.RegisterCallback<ClickEvent>(_ =>
                {
                    sound.Click();
                    Toast("아직 봉인된 사건입니다. 다음 이야기를 기다려 주세요.");
                });
                return card;
            }

            var cover = Add(card, "case-card__cover");
            var tex = !string.IsNullOrEmpty(s.cover) ? Resources.Load<Texture2D>(s.cover) : null;
            if (tex != null) cover.style.backgroundImage = new StyleBackground(tex);
            var ribbon = AddLabel(card, "case-card__ribbon", s.subtitle);
            ribbon.pickingMode = PickingMode.Ignore;
            var info = Add(card, "case-card__body");
            AddLabel(info, "case-card__subtitle", $"{s.players}인 · 약 {s.durationMin}분");
            Serif(AddLabel(info, "case-card__title", s.title));
            AddLabel(info, "case-card__tagline", s.tagline);
            AddLabel(info, "case-card__meta", "난이도 " + Dots(s.difficulty));
            var chips = Add(info, "chip-row");
            foreach (var t in s.tags) AddLabel(chips, "chip", t);
            if (ScenarioCatalog.IsPlayed(s.id)) Serif(AddLabel(card, "played-stamp", "해 봄"));

            card.RegisterCallback<PointerEnterEvent>(_ => sound.Hover());
            card.RegisterCallback<ClickEvent>(_ => Select(s, true));
            return card;
        }

        private static string Dots(int n) => new string('●', Mathf.Clamp(n, 0, 3)) + new string('○', 3 - Mathf.Clamp(n, 0, 3));

        private void Select(ScenarioManifest s, bool withSound)
        {
            if (withSound) sound.Stamp();
            selected = s;
            foreach (var pair in cards) pair.Value.EnableInClassList("case-card--selected", pair.Key == s.id);
            BuildDetail(s);
            detail.RemoveFromClassList("detail--hidden");
        }

        private void BuildDetail(ScenarioManifest s)
        {
            detail.Clear();
            var cover = Add(detail, "detail__cover");
            var tex = !string.IsNullOrEmpty(s.cover) ? Resources.Load<Texture2D>(s.cover) : null;
            if (tex != null) cover.style.backgroundImage = new StyleBackground(tex);
            Serif(AddLabel(detail, "detail__title", s.title));
            AddLabel(detail, "detail__meta",
                $"{s.subtitle} · {s.players}인 · 약 {s.durationMin}분 · 난이도 {Dots(s.difficulty)}" +
                (ScenarioCatalog.IsPlayed(s.id) ? " · 해 본 사건" : string.Empty));
            typewriterLabel = AddLabel(detail, "detail__synopsis", string.Empty);
            typewriterText = s.synopsis ?? string.Empty;
            typewriterChars = 0f;

            if (s.cast.Length > 0)
            {
                AddLabel(detail, "detail__cast-title", "등장인물");
                foreach (var c in s.cast)
                {
                    var line = Add(detail, "cast-line");
                    var p = Add(line, "cast-line__portrait");
                    var pt = Portrait(c.id);
                    if (pt != null) p.style.backgroundImage = new StyleBackground(pt);
                    AddLabel(line, "cast-line__name", $"{c.name} ({c.age})");
                    AddLabel(line, "cast-line__blurb", $"{c.status}. {c.blurb}");
                }
            }

            var buttons = Add(detail, "detail__buttons");
            AddButton(buttons, "방 열기", () => OpenRoomDialog(s), "btn--primary");
            AddButton(buttons, "혼자 둘러보기", () =>
            {
                ShowLoading("고택으로 가는 중");
                schedule(() => GameLauncher.PractiseAlone(s), 600);
            });
            if (!string.IsNullOrEmpty(s.credit)) AddLabel(detail, "detail__credit", s.credit);
        }

        private void UpdateTypewriter()
        {
            if (typewriterLabel == null || typewriterChars >= typewriterText.Length) return;
            typewriterChars += Time.unscaledDeltaTime * 45f;
            typewriterLabel.text = typewriterText.Substring(0, Mathf.Min(typewriterText.Length, (int)typewriterChars));
        }
    }
}
