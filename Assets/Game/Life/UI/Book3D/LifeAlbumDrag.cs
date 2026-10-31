using UnityEngine;
using UnityEngine.EventSystems;
using WeatherHousePrototype;

namespace WeatherHouse.Life
{
    // Lives on the persistent HUD canvas so a page rebuild cannot destroy an ongoing drag.
    public sealed class LifeAlbumDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public GameplayHud hud;
        Vector2 start;
        bool candidate, active;
        int direction;
        float progress;
        public void OnBeginDrag(PointerEventData e)
        {
            candidate = e.button == PointerEventData.InputButton.Left && hud && hud.CanDragAlbumAt(e.pressPosition);
            start = e.pressPosition; active = false; progress = 0;
        }
        public void OnDrag(PointerEventData e)
        {
            if (!candidate) return;
            float dx = e.position.x - start.x;
            if (!active)
            {
                if (Mathf.Abs(dx) < 6 || Mathf.Abs(dx) < Mathf.Abs(e.position.y - start.y)) return;
                direction = dx < 0 ? 1 : -1; active = hud.BeginAlbumDrag(direction);
                if (!active) { candidate = false; return; }
            }
            e.eligibleForClick = false;
            progress = Mathf.Clamp01(-dx * direction / Mathf.Max(160, Screen.width * .35f));
            hud.DragAlbum(progress);
        }
        public void OnEndDrag(PointerEventData e)
        { if (active && hud) hud.EndAlbumDrag(progress >= .32f); candidate = active = false; }
        void OnDisable() { if (active && hud) hud.EndAlbumDrag(false); candidate = active = false; }
    }
}
