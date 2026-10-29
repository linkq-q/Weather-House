using UnityEngine;

namespace WeatherHouse.DrawerPrototype
{
    public sealed class DrawerLetterContent : ScriptableObject
    {
        public string salutation = "亲爱的：";
        [TextArea(3,10)] public string message = "出门后，拜托去看看小狗。\n照片里的就是它。";
        public string signature = "奶奶";
    }
}
