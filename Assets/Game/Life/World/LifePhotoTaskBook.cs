using System.Collections.Generic;
using UnityEngine;
using WeatherHousePrototype;

namespace WeatherHouse.Life
{
    // Place the prefab by hand. Ownership belongs to the existing life save, not the scene instance.
    public sealed class LifePhotoTaskBook : MonoBehaviour
    {
        static readonly List<LifePhotoTaskBook> books = new List<LifePhotoTaskBook>();
        LifeSession life;
        void OnEnable() { if (!books.Contains(this)) books.Add(this); }
        void OnDisable() { books.Remove(this); }
        void Start() { life = FindObjectOfType<LifeSession>(); }
        void Update() { if (life && life.Progress.photoTaskBookCollected) gameObject.SetActive(false); }
        public static LifePhotoTaskBook Nearby(LifeSession session)
        {
            if (!session || session.Progress.photoTaskBookCollected || !session.Gameplay.PlayerTransform) return null;
            LifePhotoTaskBook nearest = null; float distance = 1.8f;
            foreach (var book in books)
            {
                if (!book || book.gameObject.scene != session.gameObject.scene) continue;
                float next = Vector3.Distance(book.transform.position, session.Gameplay.PlayerTransform.position + Vector3.up * .35f);
                if (next < distance) { nearest = book; distance = next; }
            }
            return nearest;
        }
        public bool Collect(LifeSession session)
        {
            if (Nearby(session) != this || session.GetComponent<GameplayHud>().PrimaryInteraction != LifePrimaryAction.PickUpBook) return false;
            if (!session.AcquirePhotoTaskBook()) return false;
            gameObject.SetActive(false); return true;
        }
    }
}
