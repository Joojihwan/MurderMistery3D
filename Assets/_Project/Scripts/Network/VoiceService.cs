using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MurderMystery.Core;
using MurderMystery.Gameplay;
using Unity.Services.Vivox;
using UnityEngine;

namespace MurderMystery.Network
{
    /// <summary>
    /// Vivox voice (docs/design/tech_decisions.md §4): one room channel used in discussion-type phases,
    /// and a two-person channel per secret talk. Outside those, the mic transmits nowhere.
    /// Voice is optional — if Vivox is not enabled for the project the game continues text-only.
    /// </summary>
    public static class VoiceService
    {
        public static bool Available { get; private set; }
        public static string Status { get; private set; } = "음성 꺼짐";

        private static string roomChannel;
        private static string talkChannel;
        private static string transmittingTo;
        private static bool busy;
        private static string room;
        private static (GamePhase phase, int talkId)? wanted;

        public static async Task StartAsync(string roomKey)
        {
            if (Available || busy) return;
            if (!GameSettings.VoiceEnabled)
            {
                SetStatus("음성 꺼짐 (설정)");
                return;
            }
            busy = true;
            try
            {
                if (!SessionService.SignedIn) await SessionService.EnsureSignedInAsync();
                await VivoxService.Instance.InitializeAsync();
                if (!VivoxService.Instance.IsLoggedIn) await VivoxService.Instance.LoginAsync();
                room = roomKey;
                roomChannel = Channel($"mm{roomKey}all");
                await VivoxService.Instance.JoinGroupChannelAsync(roomChannel, ChatCapability.AudioOnly);
                await TransmitAsync(null);
                Available = true;
                SetStatus("음성 연결됨");
            }
            catch (Exception e)
            {
                Available = false;
                SetStatus("음성 사용 불가 (텍스트로 진행)");
                Debug.LogWarning($"[Voice] Vivox unavailable: {e.Message}");
            }
            finally
            {
                busy = false;
            }
            _ = PumpAsync();
        }

        /// <summary>
        /// Called whenever the local snapshot changes. Requests that arrive while Vivox is still starting or
        /// switching are kept (latest wins) and applied once it is free, so no phase/talk change is lost.
        /// </summary>
        public static void Apply(GamePhase phase, int talkId)
        {
            wanted = (phase, talkId);
            _ = PumpAsync();
        }

        private static async Task PumpAsync()
        {
            if (!Available || busy) return;
            busy = true;
            try
            {
                while (wanted.HasValue)
                {
                    var (phase, talkId) = wanted.Value;
                    wanted = null;
                    await SwitchAsync(phase, talkId);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Voice] apply failed: {e.Message}");
            }
            finally
            {
                busy = false;
            }
        }

        private static async Task SwitchAsync(GamePhase phase, int talkId)
        {
            var wantTalk = talkId > 0 ? Channel($"mm{room}t{talkId}") : null;
            if (talkChannel != null && talkChannel != wantTalk)
            {
                await VivoxService.Instance.LeaveChannelAsync(talkChannel);
                talkChannel = null;
            }
            if (wantTalk != null && talkChannel == null)
            {
                await VivoxService.Instance.JoinGroupChannelAsync(wantTalk, ChatCapability.AudioOnly);
                talkChannel = wantTalk;
            }

            var target = talkChannel ?? (IsOpenFloor(phase) ? roomChannel : null);
            await TransmitAsync(target);
            SetStatus(target == null ? "음성: 말할 수 없는 단계" : target == talkChannel ? "음성: 밀담" : "음성: 전체");
        }

        private static void SetStatus(string status)
        {
            if (Status == status) return;
            Status = status;
            Debug.Log($"[Voice] {status}");
        }

        public static async Task StopAsync()
        {
            if (!Available) return;
            try
            {
                if (talkChannel != null) await VivoxService.Instance.LeaveChannelAsync(talkChannel);
                if (roomChannel != null) await VivoxService.Instance.LeaveChannelAsync(roomChannel);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Voice] stop failed: {e.Message}");
            }
            talkChannel = roomChannel = transmittingTo = null;
            Available = false;
            wanted = null;
            SetStatus("음성 꺼짐");
        }

        // Everyone can speak to the room only while gathered in the yard.
        private static bool IsOpenFloor(GamePhase phase) =>
            phase == GamePhase.CharacterSelect || phase == GamePhase.Introductions || phase == GamePhase.AutopsyBriefing ||
            phase == GamePhase.Discussion1 || phase == GamePhase.Discussion2 || phase == GamePhase.FinalStatements ||
            phase == GamePhase.Ending;

        private static async Task TransmitAsync(string channel)
        {
            if (channel == transmittingTo && channel != null) return;
            if (channel == null) await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.None, roomChannel);
            else await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.Single, channel);
            transmittingTo = channel;
        }

        // Vivox channel names allow a limited character set; keep them short and alphanumeric.
        private static string Channel(string raw)
        {
            var clean = Regex.Replace(raw, "[^A-Za-z0-9]", string.Empty);
            return clean.Length > 60 ? clean.Substring(0, 60) : clean;
        }
    }
}
