using System;
using System.Collections;
using System.Linq;
using MurderMystery.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace MurderMystery.Network
{
    /// <summary>
    /// Test hook for multi-instance runs. With command-line arguments a built player joins a LAN host,
    /// (or an online session by code) claims a seat and optionally investigates a spot, logging what it receives:
    ///   -mmJoin 127.0.0.1 -mmSeat buni [-mmName 분이] [-mmInvestigate 02] [-mmAcceptTalk] [-mmVote heo] [-mmQuitAfter 60]
    ///   -mmCode ABC123 ... joins an online session (Sessions + Relay) instead of -mmJoin.
    /// Does nothing when neither -mmJoin nor -mmCode is given.
    /// </summary>
    public class CommandLineAutoPlay : MonoBehaviour
    {
        private string address;
        private string code;
        private string seat;
        private string playerName;
        private string spot;
        private float quitAfter;
        private bool acceptTalk;
        private string lastLine;
        private string voteFor;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Arg("-mmJoin") == null && Arg("-mmCode") == null) return;
            DontDestroyOnLoad(new GameObject(nameof(CommandLineAutoPlay)).AddComponent<CommandLineAutoPlay>().gameObject);
        }

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private void Awake()
        {
            address = Arg("-mmJoin");
            code = Arg("-mmCode");
            seat = Arg("-mmSeat");
            playerName = Arg("-mmName") ?? seat;
            spot = Arg("-mmInvestigate");
            quitAfter = float.TryParse(Arg("-mmQuitAfter"), out var q) ? q : 0f;
            acceptTalk = Array.IndexOf(Environment.GetCommandLineArgs(), "-mmAcceptTalk") >= 0;
            voteFor = Arg("-mmVote");
        }

        private IEnumerator Start()
        {
            Application.runInBackground = true;
            yield return null;
            if (!string.IsNullOrEmpty(code))
            {
                var join = SessionService.JoinOnlineAsync(code);
                yield return new WaitUntil(() => join.IsCompleted);
                Debug.Log(join.IsFaulted
                    ? $"[AutoPlay] online join failed: {join.Exception?.GetBaseException()}"
                    : $"[AutoPlay] joined online session {join.Result.Id} as {seat}");
                if (join.IsFaulted) yield break;
            }
            else
            {
                Debug.Log($"[AutoPlay] joining {address} as {seat}: {SessionService.JoinLan(address)}");
            }

            yield return WaitFor(() => NetGame.Instance != null && NetGame.Instance.Seats.Length > 0, 30f);
            if (NetGame.Instance == null)
            {
                Debug.LogError("[AutoPlay] NetGame never arrived");
                yield break;
            }
            var net = NetGame.Instance;
            net.Changed += () => OnChanged(net);

            if (!string.IsNullOrEmpty(seat))
            {
                net.ClaimSeat(seat, playerName);
                yield return WaitFor(() => net.HasSeat, 10f);
                Debug.Log($"[AutoPlay] seat={net.Snapshot?.characterId} phase={net.Snapshot?.phase}");
            }

            if (!string.IsNullOrEmpty(spot))
            {
                yield return WaitFor(() => net.Snapshot != null && net.Snapshot.phase.StartsWith("Investigation"), 120f);
                net.Investigate(spot);
                Debug.Log($"[AutoPlay] investigate {spot} sent");
            }

            if (quitAfter > 0f)
            {
                yield return new WaitForSecondsRealtime(quitAfter);
                Debug.Log($"[AutoPlay] quitting; known={string.Join(",", net.Snapshot?.knownClueIds ?? Array.Empty<string>())}");
                NetworkManager.Singleton.Shutdown();
                Application.Quit();
            }
        }

        private void OnChanged(NetGame net)
        {
            var snap = net.Snapshot;
            var line = $"voice={VoiceService.Status} phase={snap?.phase} talk={snap?.talkPartner}#{snap?.talkId} vote={snap?.voteStage} end={snap?.endingKey} log={net.Log.LastOrDefault()}";
            if (line != lastLine) Debug.Log($"[AutoPlay] {lastLine = line}");
            if (acceptTalk && snap != null && snap.incomingTalks.Length > 0 && string.IsNullOrEmpty(snap.talkPartner))
            {
                Debug.Log($"[AutoPlay] accepting talk #{snap.incomingTalks[0].id} from {snap.incomingTalks[0].from}");
                net.RespondTalk(snap.incomingTalks[0].id, true);
                acceptTalk = false;
            }
            if (!string.IsNullOrEmpty(voteFor) && snap != null && !string.IsNullOrEmpty(snap.voteStage) && !snap.hasVoted &&
                snap.voteCandidates.Contains(voteFor))
            {
                Debug.Log($"[AutoPlay] voting {voteFor} in {snap.voteStage}");
                net.Vote(voteFor, 0);
                voteFor = null;
            }
        }

        private static IEnumerator WaitFor(Func<bool> condition, float timeout)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
