using System;
using System.Threading.Tasks;
using MurderMystery.Core;
using MurderMystery.Gameplay;
using MurderMystery.Network;
using UnityEngine;
using UnityEngine.UIElements;

namespace MurderMystery.FrontEnd
{
    public partial class FrontEndController
    {
        private VisualElement OpenDialog(string title)
        {
            modalLayer.Clear();
            modalLayer.RemoveFromClassList("off");
            var box = Add(modalLayer, "modal");
            Serif(AddLabel(box, "modal__title", title));
            sound.Stamp();
            return box;
        }

        private void CloseDialog()
        {
            modalLayer.AddToClassList("off");
            modalLayer.Clear();
        }

        private static VisualElement Row(VisualElement box, string label)
        {
            var row = Add(box, "modal__row");
            AddLabel(row, "modal__label", label);
            return row;
        }

        // ---------------------------------------------------------------- open a room

        private void OpenRoomDialog(ScenarioManifest s)
        {
            var box = OpenDialog($"방 열기 — {s.title}");
            AddLabel(box, "modal__text", "방을 열면 초대 코드가 나옵니다. 친구들이 코드로 들어오면 고택에서 함께 인물을 고릅니다.");

            var online = true;
            var seg = Add(Row(box, "연결"), "segment");
            Label onlineItem = null, lanItem = null;
            void Paint()
            {
                onlineItem.EnableInClassList("segment__item--on", online);
                lanItem.EnableInClassList("segment__item--on", !online);
            }
            onlineItem = AddLabel(seg, "segment__item", "인터넷 (초대 코드)");
            lanItem = AddLabel(seg, "segment__item", "같은 네트워크 (LAN)");
            onlineItem.RegisterCallback<ClickEvent>(_ => { online = true; sound.Click(); Paint(); });
            lanItem.RegisterCallback<ClickEvent>(_ => { online = false; sound.Click(); Paint(); });
            Paint();

            var name = new TextField { value = GameSettings.PlayerName, maxLength = 12 };
            name.AddToClassList("field");
            Row(box, "내 이름").Add(name);

            var voice = new Toggle { value = GameSettings.VoiceEnabled };
            Row(box, "음성 대화").Add(voice);
            AddLabel(box, "small-note", "음성 대화: 토론 때는 모두와, 조사 때는 밀담 상대와만 이야기합니다. 마이크가 필요합니다.");

            var buttons = Add(box, "modal__buttons");
            AddButton(buttons, "취소", CloseDialog);
            AddButton(buttons, "방 열기", () =>
            {
                GameSettings.PlayerName = name.value;
                GameSettings.VoiceEnabled = voice.value;
                GameSettings.Save();
                namePlate.text = GameSettings.PlayerName;
                CloseDialog();
                _ = HostAsync(s, online);
            }, "btn--primary");
        }

        private async Task HostAsync(ScenarioManifest s, bool online)
        {
            ShowLoading("방을 여는 중");
            try
            {
                if (online)
                {
                    await GameLauncher.HostOnlineAsync(s);
                }
                else if (!GameLauncher.HostLan(s))
                {
                    throw new Exception("LAN 호스트를 시작하지 못했습니다 (포트 7777 사용 중?)");
                }
                sound.Bell();
                fadeTitle.text = "고택으로 가는 중";
            }
            catch (Exception e)
            {
                HideLoading();
                Toast($"방을 열지 못했습니다: {e.Message}", 5f);
                Debug.LogWarning($"[FrontEnd] host failed: {e}");
            }
        }

        // ---------------------------------------------------------------- join

        private void OpenJoinDialog()
        {
            var box = OpenDialog("코드로 참가");
            AddLabel(box, "modal__text", "방을 연 친구에게 받은 6자리 코드를 입력하세요.");
            var code = new TextField { maxLength = 6 };
            code.AddToClassList("code-field");
            box.Add(code);
            code.RegisterValueChangedCallback(e =>
            {
                var up = e.newValue.ToUpperInvariant();
                if (up != e.newValue) code.SetValueWithoutNotify(up);
            });

            var name = new TextField { value = GameSettings.PlayerName, maxLength = 12 };
            name.AddToClassList("field");
            Row(box, "내 이름").Add(name);

            var lan = new Foldout { text = "같은 네트워크(LAN)로 참가", value = false };
            var address = new TextField { value = "127.0.0.1" };
            address.AddToClassList("field");
            lan.Add(address);
            box.Add(lan);

            var buttons = Add(box, "modal__buttons");
            AddButton(buttons, "취소", CloseDialog);
            AddButton(buttons, "LAN 참가", () =>
            {
                GameSettings.PlayerName = name.value;
                GameSettings.Save();
                CloseDialog();
                ShowLoading("고택으로 가는 중");
                if (!GameLauncher.JoinLan(address.value))
                {
                    HideLoading();
                    Toast("LAN 접속을 시작하지 못했습니다.");
                }
                else
                {
                    WatchJoinTimeout();
                }
            });
            AddButton(buttons, "참가", () =>
            {
                if (code.value.Trim().Length < 6)
                {
                    Toast("코드 6자리를 입력하세요.");
                    return;
                }
                GameSettings.PlayerName = name.value;
                GameSettings.Save();
                CloseDialog();
                _ = JoinAsync(code.value);
            }, "btn--primary");
            code.Focus();
        }

        private async Task JoinAsync(string code)
        {
            ShowLoading("방을 찾는 중");
            try
            {
                await GameLauncher.JoinOnlineAsync(code);
                fadeTitle.text = "고택으로 가는 중";
                WatchJoinTimeout();
            }
            catch (Exception e)
            {
                HideLoading();
                Toast($"참가하지 못했습니다: {e.Message}", 5f);
                Debug.LogWarning($"[FrontEnd] join failed: {e}");
            }
        }

        /// <summary>If the map never arrives (wrong address, host gone), come back to the menu.</summary>
        private void WatchJoinTimeout()
        {
            schedule(() =>
            {
                if (!InTitleScene) return;
                HideLoading();
                Toast("방에 들어가지 못했습니다. 코드나 주소를 확인하세요.", 5f);
                _ = GameLauncher.ReturnToTitleAsync();
            }, 20000);
        }

        // ---------------------------------------------------------------- settings

        private void OpenSettingsDialog()
        {
            var box = OpenDialog("설정");
            var name = new TextField { value = GameSettings.PlayerName, maxLength = 12 };
            name.AddToClassList("field");
            Row(box, "이름").Add(name);

            var mouse = new Slider(0.03f, 0.4f) { value = GameSettings.MouseSensitivity };
            mouse.AddToClassList("slider");
            Row(box, "마우스 감도").Add(mouse);

            var volume = new Slider(0f, 1f) { value = GameSettings.Volume };
            volume.AddToClassList("slider");
            volume.RegisterValueChangedCallback(e => AudioListener.volume = e.newValue);
            Row(box, "소리 크기").Add(volume);

            var voice = new Toggle { value = GameSettings.VoiceEnabled };
            Row(box, "음성 대화").Add(voice);

            var buttons = Add(box, "modal__buttons");
            AddButton(buttons, "닫기", () =>
            {
                GameSettings.PlayerName = name.value;
                GameSettings.MouseSensitivity = mouse.value;
                GameSettings.Volume = volume.value;
                GameSettings.VoiceEnabled = voice.value;
                GameSettings.Save();
                if (namePlate != null) namePlate.text = GameSettings.PlayerName;
                CloseDialog();
            }, "btn--primary");
        }

        private void Confirm(string title, string text, string ok, Action onOk)
        {
            var box = OpenDialog(title);
            AddLabel(box, "modal__text", text);
            var buttons = Add(box, "modal__buttons");
            AddButton(buttons, "취소", CloseDialog);
            AddButton(buttons, ok, () =>
            {
                CloseDialog();
                onOk();
            }, "btn--primary");
        }
    }
}
