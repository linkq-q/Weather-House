// Pure route contracts: callable from Unity batch checks or a managed console without starting Unity.
using System;
using System.Collections.Generic;
using UnityEngine;
using WeatherHouse.Hillside;

public static class LongShelterRouteChecks
{
    struct Box
    {
        public float left,right,bottom,top;
        public Box(float left,float right,float bottom,float top)
        {this.left=left;this.right=right;this.bottom=bottom;this.top=top;}
    }
    sealed class Fixture
    {
        readonly float radius;
        readonly Box[] obstacles;
        public int calls;
        public float longest;
        public Fixture(float radius,params Box[] obstacles){this.radius=radius;this.obstacles=obstacles;}
        public bool Clear(Vector3 from,Vector3 to,out Vector3 foot)
        {
            calls++;longest=Math.Max(longest,Vector3.Distance(from,to));foot=to;
            foreach(Box obstacle in obstacles)
                if(Intersects(from,to,obstacle,radius))return false;
            return true;
        }
        public bool Valid(Vector3 from,Vector3 to)
        {foreach(Box obstacle in obstacles)if(Intersects(from,to,obstacle,radius))return false;return true;}
    }
    static bool Intersects(Vector3 from,Vector3 to,Box box,float radius)
    {
        float enter=0,leave=1;
        return Clip(from.x,to.x-from.x,box.left-radius,box.right+radius,ref enter,ref leave) &&
            Clip(from.z,to.z-from.z,box.bottom-radius,box.top+radius,ref enter,ref leave);
    }
    static bool Clip(float origin,float direction,float min,float max,ref float enter,ref float leave)
    {
        if(Math.Abs(direction)<.000001f)return origin>=min && origin<=max;
        float a=(min-origin)/direction,b=(max-origin)/direction;
        if(a>b){float swap=a;a=b;b=swap;}
        enter=Math.Max(enter,a);leave=Math.Min(leave,b);return enter<=leave;
    }
    static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    static AnimalLocalRoute Solve(Fixture fixture,Vector3 goal,float radius)
    {
        var route=new AnimalLocalRoute(Vector3.zero,goal,radius,fixture.Clear);
        for(int work=0;!route.Finished && work<20000;work++)
        {
            int before=fixture.calls;route.Advance();
            Require(fixture.calls-before<=1,"An Advance consumed more than one real traverse call");
        }
        Require(route.Finished,"Route did not terminate within bounded work");
        Require(route.Queries==fixture.calls,"Route query accounting differs from traverse calls");
        Require(fixture.longest<=6.001f,"Route bypassed the 6m checked segment limit");
        if(route.Result!=null)
        {
            Vector3 last=Vector3.zero;
            foreach(Vector3 point in route.Result)
            {
                Require(Vector3.Distance(last,point)<=6.001f && fixture.Valid(last,point),"Result skips a checked body-safe route segment");
                last=point;
            }
            Require(Vector3.Distance(last,goal)<.01f,"Result does not reach requested goal");
        }
        return route;
    }
    public static int MeasureNearObstacle()
    {
        var fixture=new Fixture(.3f,new Box(1.2f,1.8f,-.45f,.45f));
        AnimalLocalRoute route=Solve(fixture,Vector3.right*28,.3f);
        Require(route.Result!=null,"A small obstacle made a reachable 28m shelter unreachable");
        return route.Queries;
    }
    public static IEnumerable<string> Run()
    {
        int queries=MeasureNearObstacle();
        // Twenty 4-query frames is already generous for one small detour and five open-ground segments.
        Require(queries<=80,"28m shelter with a small initial obstacle exceeded 80 checked edges: "+queries);
        yield return "PASS 28m route sidesteps a small initial obstacle then reconnects; queries="+queries+" (limit80)";
        foreach(float distance in new[]{25f,30f})
        {
            var fixture=new Fixture(.3f);AnimalLocalRoute route=Solve(fixture,Vector3.right*distance,.3f);
            Require(route.Result!=null && route.Queries<=5,"Open long route performed unnecessary A* expansion");
            yield return "PASS open "+distance+"m route uses checked segments; queries="+route.Queries;
        }
        var narrowWalls=new[]{new Box(-1,31,.35f,3),new Box(-1,31,-3,-.35f)};
        AnimalLocalRoute small=Solve(new Fixture(.2f,narrowWalls),Vector3.right*28,.2f);
        AnimalLocalRoute wide=Solve(new Fixture(.4f,narrowWalls),Vector3.right*28,.4f);
        Require(small.Result!=null && wide.Result==null,"Narrow corridor lost body-radius rejection");
        yield return "PASS 0.7m corridor admits radius0.2 and rejects radius0.4 without body clipping";
        AnimalLocalRoute enclosed=Solve(new Fixture(.3f,new Box(25.5f,25.8f,-2.5f,2.5f),
            new Box(30.2f,30.5f,-2.5f,2.5f),new Box(25.5f,30.5f,2.2f,2.5f),new Box(25.5f,30.5f,-2.5f,-2.2f)),Vector3.right*28,.3f);
        Require(enclosed.Result==null,"Direct reconnect crossed a closed wall");
        yield return "PASS enclosed 28m goal remains unreachable; queries="+enclosed.Queries;
        AnimalLocalRoute blocked=Solve(new Fixture(.3f,new Box(-1,1,-1,1)),Vector3.right*28,.3f);
        Require(blocked.Result==null,"Blocked start unexpectedly escaped through an obstacle");
        yield return "PASS blocked start terminates with no route; queries="+blocked.Queries;
    }
}
