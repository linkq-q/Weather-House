using UnityEngine;

namespace WeatherHouse.DrawerPrototype
{
    public sealed class DrawerLetterContent : ScriptableObject
    {
        public string salutation = "Nana：";
        [TextArea(3,10)] public string message = "我们出门去参加魔法协会的\n年度大会了。\n这一个月，岛上的小动物\n就麻烦你照顾啦。\n记得去看看豆豆，\n也要照顾好自己。";
        public string signature = "";
    }
}
