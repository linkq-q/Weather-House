using System;
using System.Collections.Generic;

namespace WeatherHouse.Life
{
    // One authored identity per role. The bindings keep the ecology animal's persisted ID.
    public static class LifeContent
    {
        public static LifeRequest[] Build()
        {
            var r = new List<LifeRequest>();
            Add(r,"dog.hello","E0","dog","you,hello,want","它向刚走近的玩家问好，想交流。","听听院门的问候","conversation","dog.here",true);
            Add(r,"dog.bye","E0","dog","bye","它留在院门向准备离开的玩家道别。","","","dog.here");
            Add(r,"fox.hello","E0","fox","you,hello","狐狸从林间走来，向仍站在原地的玩家问好。","","","fox.here");
            Add(r,"fox.bye","E0","fox","bye","狐狸准备去地面住处休息，向留在这里的玩家道别。","","","fox.home");
            Add(r,"dog.rain.lesson","E0","dog","want,rain","它想让玩家在身边画出雨手势，观察落下的雨。","在院门的圈内画雨手势","rain_seen","dog.bowl",true);
            Add(r,"dog.water","E1","dog","want,water","它想要身边容器里的水；请下雨接水，然后停雨，水仍留在容器中。","在狗身边用容器接水并停雨","water_retained","dog.bowl",true);
            Add(r,"dog.rain","E1","dog","rain,reject","它不想继续淋雨；落下的是雨，碗里留下的是水。","","","dog.bowl");
            Add(r,"dog.sun","E1","dog","sun,want","它想待在明亮的日照里，树荫与明亮地面形成对照。","","","dog.sun");
            Add(r,"fox.water","E1","fox","water,want","晴天也能喝到固定水碗中留下的水。","","","fox.bowl");
            Add(r,"fox.rain","E1","fox","rain,reject","狐狸看着雨滴落进碗里；它不想继续淋雨。","","","fox.bowl");
            Add(r,"fox.sun","E1","fox","sun,reject","狐狸不想待在直射日照里，选择旁边的树荫。","","","fox.sun");
            Add(r,"squirrel.mat","E2","squirrel","want,grass,dry,reject,wet","松鼠想要用草编成的干窝垫，不想要湿窝垫。","把一张干草垫铺到松鼠身边","dry_mat","squirrel.nest",true);
            Add(r,"squirrel.grass","E2","squirrel","grass","它指向既能做散草束、又能编成窝垫的同一种草。","","","squirrel.grass");
            Add(r,"squirrel.wet","E2","squirrel","wet,reject","草束和窝垫都可能湿；当前湿窝垫不适合它休息。","","","squirrel.wet");
            Add(r,"squirrel.dry","E2","squirrel","dry,want","窝垫在阴影里也能是干的，干不等于有日照。","","","squirrel.dry");
            Add(r,"sheep.grass","E2","sheep","want,grass","绵羊想吃同一种草，湿草和干草都可以。","把一束草递给绵羊","fed","sheep.feed");
            Add(r,"sheep.wet","E2","sheep","wet,water","晴天水槽溅水让垫子变湿；没有雨也能湿。","","","sheep.wet");
            Add(r,"sheep.dry","E2","sheep","dry","棚下同款垫子是干的；干不等于想要，湿不等于拒绝。","","","sheep.dry");
            Add(r,"squirrel.tree","E3","squirrel","tree,home","松鼠指向自己的树根窝，再看向另一棵没有窝的树；树不一定是住处。","走到松鼠指向的树旁","arrive","squirrel.tree",true);
            Add(r,"squirrel.home","E3","squirrel","home,tree","松鼠的住处在树根，临时站的地方不是住处。","","","squirrel.nest");
            Add(r,"fox.tree","E3","fox","tree","狐狸把树当作路标，它的住处在地面。","走到狐狸指向的路标树旁","arrive","fox.tree");
            Add(r,"fox.home","E3","fox","home","狐狸离开住处后仍指向属于它的地面窝。","请狐狸去附近的地面住处休息","animal_arrived","fox.home");
            Add(r,"sheep.wind","E4","sheep","wind,strong,reject,weak,want","绵羊不想要强风，想让这里保持弱风；弱风仍在吹，不能完全关闭。","把绵羊附近的风调成弱风","wind_weak","sheep.ribbon",true);
            Add(r,"sheep.strong","E4","sheep","wind,strong","同一块布条在同样范围、同样时长的强风下摆幅更大。","","","sheep.ribbon");
            Add(r,"sheep.weak","E4","sheep","wind,weak","弱风仍吹动草穗和布条，停风则不再摆动。","","","sheep.ribbon");
            Add(r,"deer.wind","E4","deer","wind","鹿观察芦苇处风停与风起，雨和日照保持原样。","","","deer.ribbon");
            Add(r,"deer.weak","E4","deer","rain,weak,want","鹿想要一场小雨；弱雨仍然有雨滴，不是停雨。","给鹿附近下一场小雨","rain_weak","deer.bowl");
            Add(r,"deer.strong","E4","deer","rain,strong,reject","鹿不想要强雨，强说的是雨的强度，面积和时长保持相同。","","","deer.bowl");
            Add(r,"sheep.warm","E5","sheep","cold,reject,warm,want,dry","风口地面明亮而干，但仍冷；绵羊想在背风暖处的干垫上休息。","在绵羊身边的暖处铺干垫并邀请它","warm_rest","sheep.warm",true);
            Add(r,"sheep.cold","E5","sheep","cold,sun,dry","暴露的风口明亮而干燥，但这里仍冷。","","","sheep.cold");
            Add(r,"fox.cold","E5","fox","cold","狐狸指向树荫里的冷石面，说的是石头温度，并不是自己。","","","fox.cold");
            Add(r,"fox.warm","E5","fox","warm,reject,sun","狐狸在遮阴的保温休息位感到暖；暖不等于有日照。","在狐狸身边的暖处铺干垫并邀请它","warm_rest","fox.warm");
            Add(r,"sheep.self.player","E6a","sheep","self,you,hello","说话者是绵羊，听者是玩家；我指绵羊，你指玩家。","辨明这一话轮的说话者与听者","conversation","sheep.here",true,"player");
            Add(r,"sheep.self.deer","E6a","sheep","self,you","说话者是绵羊，听者是鹿；我指绵羊，你指鹿。","","","sheep.here",false,"deer");
            Add(r,"deer.self.sheep","E6a","deer","self,you","鹿接话对绵羊说；我指鹿，你指绵羊。","","","deer.here",false,"sheep");
            Add(r,"deer.self.player","E6a","deer","self,you","说话者换成鹿，听者是玩家；我随说话者改变。","","","deer.here",false,"player");
            Add(r,"sheep.here","E6b","sheep","here,come,want","绵羊指向脚边的安全站位，邀请玩家来这里。","走到绵羊指向的近处站位","arrive","sheep.here",true);
            Add(r,"sheep.go","E6b","sheep","go,here","绵羊指向附近另一块安全石台，请玩家离开当前点去那里。","步行到绵羊指向的另一处干岸站位","arrive","sheep.away");
            Add(r,"fox.here","E6b","fox","here,come,want","狐狸在林间另一方向指向脚边，邀请玩家来这里。","走到狐狸指向的林间站位","arrive","fox.here");
            Add(r,"fox.go","E6b","fox","go,tree","狐狸请玩家从当前交谈点去它指向的树旁。","步行去狐狸指向的树旁","arrive","fox.tree");
            Add(r,"squirrel.sequence","E7","squirrel","first,rain,then,sun,water,dry","松鼠想先用雨接水，再用日照把垫子晾干，最后水碗有水、窝垫干燥，请附近同伴一起歇脚。","让共同歇脚点有水和干垫，并邀请附近两只动物","common_rest","squirrel.common",true);
            Add(r,"fox.sequence","E7","fox","first,come,here,then,go,tree","狐狸邀请玩家先来它指的这里，再去树旁；第二步的终点是树旁。","先到狐狸的近处站位，再步行到路标树","walk_sequence","fox.tree");
            Add(r,"fox.reverse","E7","fox","first,go,tree,then,come,here","这次计划倒过来，先去树旁，再来狐狸这里，最后停在狐狸身边。","先去树旁，再到狐狸的近处站位","walk_reverse","fox.here");
            foreach(LifeRequest request in r)
            {
                request.openForPhotography=request.eventId=="E6a" || request.eventId=="E6b" ||
                    request.id=="fox.home" || request.id=="sheep.warm" || request.id=="fox.warm" ||
                    request.id=="squirrel.sequence" || request.id=="fox.sequence" || request.id=="fox.reverse";
                if(request.eventId=="E6a")
                {
                    request.speakerKey=request.speakerKey=="sheep"?"harborSheep":"harborDeer";
                    if(request.listenerKey=="sheep")request.listenerKey="harborSheep";
                    else if(request.listenerKey=="deer")request.listenerKey="harborDeer";
                    request.targetId=request.speakerKey=="harborSheep"?"harbor.sheep.here":"harbor.deer.here";
                }
                if(request.eventId=="E6b" && request.speakerKey=="sheep")
                {
                    request.speakerKey="harborSheep";
                    request.targetId=request.id=="sheep.here"?"harbor.sheep.here":"harbor.tide";
                    request.requiredForEvent=true;
                    request.meaning=request.id=="sheep.here"?"渡口绵羊指向身旁的干岸站位，邀请玩家来这里。":"渡口绵羊从交谈点指向潮池旁的另一处干岸，请玩家去那里。";
                    request.objective=request.id=="sheep.here"?"走到渡口绵羊指向的近处站位":"步行到绵羊指向的潮池干岸";
                }
            }
            return r.ToArray();
        }

        static void Add(List<LifeRequest> all,string id,string ev,string speaker,string words,string meaning,
            string objective,string condition,string target,bool required=false,string listener="player")
        {
            string[] glyphs = words.Split(',');
            for (int i=0;i<glyphs.Length;i++)
                glyphs[i] = glyphs[i] == "sun" ? "sunlight" : glyphs[i] == "bye" ? "goodbye" : glyphs[i];
            all.Add(new LifeRequest { id=id,eventId=ev,speakerKey=speaker,listenerKey=listener,
                glyphIds=glyphs,meaning=meaning,objective=objective,condition=condition,
                targetId=target,requiredForEvent=required,
                context="当前话轮与对象状态须结合现场及原照片。" });
        }
    }
}
