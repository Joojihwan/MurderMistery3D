using System.Linq;
using MurderMystery.Gameplay;
using MurderMystery.Level;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace MurderMystery.Network
{
    /// <summary>
    /// Networked body. The owner gets the first-person rig (movement replicates through an owner-authority
    /// NetworkTransform on the prefab); everyone else sees the character in hanbok (Resources/Characters,
    /// built from the KHS garments at each character's height), or a capsule of that build if the outfit
    /// is missing. Build matters for the "tall man without a gat" sighting.
    /// </summary>
    public class NetPlayer : NetworkBehaviour
    {
        private readonly NetworkVariable<FixedString32Bytes> seat = new NetworkVariable<FixedString32Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Owner-written so the sit pose shows on everyone's screen (jumps show through the moving transform).
        private readonly NetworkVariable<bool> sitting = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private Transform body;
        private FirstPersonController localController;
        private string bodySeat;

        public string SeatId => seat.Value.ToString();

        public void SetSeat(string characterId)
        {
            if (IsServer) seat.Value = characterId;
        }

        public override void OnNetworkSpawn()
        {
            seat.OnValueChanged += (_, __) => RefreshBody();
            sitting.OnValueChanged += (_, now) => ApplySitting(now);
            if (IsOwner)
            {
                var spawns = FindObjectsByType<GameplayMarker>(FindObjectsSortMode.None)
                    .Where(m => m.Type == MarkerType.PlayerSpawn).OrderBy(m => m.MarkerId).ToList();
                if (spawns.Count > 0)
                {
                    var cc = GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;
                    transform.SetPositionAndRotation(
                        spawns[(int)(OwnerClientId % (ulong)spawns.Count)].transform.position + Vector3.up * 0.05f,
                        Quaternion.identity);
                    if (cc != null) cc.enabled = true;
                }
                LocalPlayerRig.Attach(gameObject);
                localController = GetComponent<FirstPersonController>();
            }
            else
            {
                // Remote bodies are moved by the NetworkTransform only.
                var cc = GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                RefreshBody();
            }
        }

        private void Update()
        {
            if (IsOwner && localController != null && sitting.Value != localController.IsSitting)
                sitting.Value = localController.IsSitting;
        }

        private void ApplySitting(bool now)
        {
            if (body != null && body.TryGetComponent<CharacterAnimator>(out var animator)) animator.Sitting = now;
        }

        private void RefreshBody()
        {
            if (IsOwner || bodySeat == SeatId) return;
            if (body != null) Destroy(body.gameObject);
            body = null;
            bodySeat = SeatId;

            var outfit = string.IsNullOrEmpty(SeatId) ? null : Resources.Load<GameObject>($"Characters/{SeatId}");
            if (outfit != null)
            {
                body = Instantiate(outfit, transform, false).transform;
                body.name = "Body";
                ApplySitting(sitting.Value);
                return;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(go.GetComponent<Collider>());
            go.name = "Body";
            body = go.transform;
            body.SetParent(transform, false);

            // Heights from docs/design/timeline.md §0 (build matters for sightings).
            var (height, width) = SeatId switch
            {
                "jaeha" => (1.80f, 0.45f),
                "heo" => (1.55f, 0.50f),
                "byeon" => (1.70f, 0.65f),
                "jeong" => (1.58f, 0.42f),
                "buni" => (1.52f, 0.40f),
                _ => (1.65f, 0.45f)
            };
            body.localScale = new Vector3(width, height / 2f, width);
            body.localPosition = new Vector3(0f, height / 2f, 0f);
        }
    }
}
