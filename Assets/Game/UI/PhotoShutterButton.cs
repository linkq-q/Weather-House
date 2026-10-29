using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WeatherHousePrototype
{
    // Space is also mapped to Unity UI Submit. The shutter accepts pointer clicks only.
    public sealed class PhotoShutterButton : Button
    {
        public override void OnSubmit(BaseEventData eventData) { }
    }
}
