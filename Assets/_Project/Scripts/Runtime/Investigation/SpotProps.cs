using System;
using MurderMystery.Gameplay;
using UnityEngine;

namespace MurderMystery.Investigation
{
    /// <summary>
    /// The objects showing a spot's items (a letter, a key...). Each disappears once its item has been
    /// taken by anyone (snapshot goneItems), so the spot looks searched.
    /// </summary>
    public class SpotProps : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            public string clueId;
            public GameObject root;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();
        private float nextCheck;

        public void Configure(Entry[] items) => entries = items;

        private void Update()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.5f;
            var gone = GameClient.Current?.Snapshot?.goneItems;
            if (gone == null) return;
            foreach (var e in entries)
                if (e.root != null) e.root.SetActive(Array.IndexOf(gone, e.clueId) < 0);
        }
    }
}
