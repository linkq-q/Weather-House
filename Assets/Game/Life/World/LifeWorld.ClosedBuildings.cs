using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeatherHouse.Life
{
    public sealed partial class LifeWorld
    {
        sealed class ClosedBuilding
        {
            public Transform root;
            public Renderer[] renderers;
        }
        readonly List<ClosedBuilding> closedBuildings=new List<ClosedBuilding>();
        bool closedBuildingsFound;

        string ClosedBuildingHint()
        {
            if(!Player)return "";
            if(!closedBuildingsFound)
            {
                closedBuildingsFound=true;
                foreach(Transform part in FindObjectsOfType<Transform>())
                    if(part.name=="A01 · 湖畔集雨温室" ||
                       part.name=="A04 · 潮湾船坞风铃楼" ||
                       part.name=="A08 · 白桦林缘树籽库")
                        closedBuildings.Add(new ClosedBuilding{root=part,renderers=part.GetComponentsInChildren<Renderer>(true)});
            }
            foreach(ClosedBuilding building in closedBuildings)
            {
                if(!building.root || !building.root.gameObject.activeInHierarchy)continue;
                bool found=false;Bounds bounds=default;
                foreach(Renderer renderer in building.renderers)
                    if(renderer && renderer.enabled && renderer.gameObject.activeInHierarchy)
                    {
                        if(found)bounds.Encapsulate(renderer.bounds);
                        else {bounds=renderer.bounds;found=true;}
                    }
                if(!found)continue;
                Vector3 at=Player.position;at.y=bounds.center.y;
                if((bounds.ClosestPoint(at)-at).sqrMagnitude<=2.2f*2.2f)
                    return "该房屋暂未开放";
            }
            return "";
        }
    }
}
