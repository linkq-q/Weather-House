using System;
using System.Collections.Generic;
using System.Reflection;
using WeatherHouse.Hillside;
using Kind=WeatherHouse.Hillside.AnimalWorkBudget.Kind;

public static class AnimalWorkBudgetChecks
{
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public static string[] Run()
    {
        MethodInfo clock=typeof(AnimalWorkBudget).GetMethod("SetTestClock",BindingFlags.Static|BindingFlags.NonPublic);
        Require(clock!=null,"Editor clock seam missing");
        double now=0;int frame=1;var results=new List<string>();
        void Reset(){now=0;frame++;clock.Invoke(null,new object[]{(Func<double>)(()=>now),(Func<int>)(()=>frame)});}
        try
        {
            Reset();Require(AnimalWorkBudget.TryBegin(Kind.Decision,out var first),"First work denied");using(first)now=.25;
            now=500;Require(AnimalWorkBudget.TryBegin(Kind.Decision,out var later),"Unrelated movement/render time exhausted work budget");using(later)now=500.25;
            now=1000;Require(AnimalWorkBudget.TryBegin(Kind.Decision,out var final),"Second idle gap charged as work");using(final)now=1000.25;
            results.Add("PASS 1000ms between work scopes does not consume either lane's 1ms budget");

            Reset();Require(AnimalWorkBudget.TryBegin(Kind.Decision,out var a),"First timed unit denied");using(a)now=.625;
            now=100;Require(AnimalWorkBudget.TryBegin(Kind.Decision,out var b),"Idle gap counted before second timed unit");using(b)now=100.5;
            Require(!AnimalWorkBudget.TryBegin(Kind.Decision,out _),"1.125ms of decision work did not stop decision starts");
            Require(AnimalWorkBudget.TryBegin(Kind.Planning,out var separate),"Decision time consumed planning reservation");using(separate)now=101.625;
            Require(!AnimalWorkBudget.TryBegin(Kind.Planning,out _),"1.125ms of planning work did not stop planning starts");
            frame++;Require(AnimalWorkBudget.TryBegin(Kind.Decision,out var fresh),"Next frame did not reset time budget");fresh.Dispose();
            results.Add("PASS independent 1ms soft time caps protect each lane and reset next frame");

            Reset();int starts=0;
            foreach(Kind kind in new[]{Kind.Decision,Kind.Planning})
            {
                for(int i=0;i<4;i++){Require(AnimalWorkBudget.TryBegin(kind,out var unit),"Fewer than 4 lane starts accepted");unit.Dispose();starts++;}
                Require(!AnimalWorkBudget.TryBegin(kind,out _),"More than 4 lane starts accepted");
            }
            Require(starts==8,"Combined start allowance changed");results.Add("PASS maximum 4+4=8 independent work starts per frame");

            Reset();Require(AnimalWorkBudget.TryBegin(Kind.Decision,out var outer),"Outer scope denied");
            using(outer)
            {
                now=.15;Require(AnimalWorkBudget.TryBegin(Kind.Planning,out var inner),"Nested work denied");using(inner)now=.55;
                now=.75;
            }
            Require(AnimalWorkBudget.TryBegin(Kind.Decision,out var remaining),"Nested time counted twice");using(remaining)now=.8;
            Reset();Require(AnimalWorkBudget.TryBegin(Kind.Decision,out outer),"Outer scope denied");
            using(outer)for(int i=0;i<16;i++){Require(AnimalWorkBudget.TryBegin(i%2==0?Kind.Decision:Kind.Planning,out var inner),"Nested work incorrectly counted as a new start");using(inner)now+=.01;}
            for(int i=0;i<3;i++){Require(AnimalWorkBudget.TryBegin(Kind.Decision,out var unit),"Nested work consumed independent starts");unit.Dispose();}
            Require(!AnimalWorkBudget.TryBegin(Kind.Decision,out _),"Outer start was not counted");
            for(int i=0;i<4;i++){Require(AnimalWorkBudget.TryBegin(Kind.Planning,out var unit),"Cross-lane nesting consumed planning reservation");unit.Dispose();}
            results.Add("PASS nested scopes charge time and starts only once to the outer lane");

            Reset();try
            {
                Require(AnimalWorkBudget.TryBegin(Kind.Planning,out var failed),"Exception fixture denied");
                using(failed){now=1.1;throw new ApplicationException("expected fixture exception");}
            }
            catch(ApplicationException){}
            Require(!AnimalWorkBudget.TryBegin(Kind.Planning,out _),"Exception bypassed scope accounting");
            Reset();void ReturnEarly(){Require(AnimalWorkBudget.TryBegin(Kind.Decision,out var early),"Return fixture denied");using(early){now=1.1;return;}}
            ReturnEarly();Require(!AnimalWorkBudget.TryBegin(Kind.Decision,out _),"Early return bypassed scope accounting");
            results.Add("PASS exceptions and early returns dispose and charge their work");

            Reset();Require(AnimalWorkBudget.TryBegin(Kind.Decision,out var oldFrame),"Old frame fixture denied");frame++;
            Require(AnimalWorkBudget.TryBegin(Kind.Planning,out var current),"New frame fixture denied");oldFrame.Dispose();now=1.1;current.Dispose();
            Require(!AnimalWorkBudget.TryBegin(Kind.Planning,out _),"Stale scope corrupted the new frame's accounting");
            results.Add("PASS a stale previous-frame scope cannot alter the current frame");

            Reset();var visits=new int[50];int cursor=0,totalPlans=0;
            for(int f=0;f<25;f++)
            {
                frame++;now=f*100;int decisions=0,plans=0;
                for(int i=0;i<50;i++)
                {
                    if(!AnimalWorkBudget.TryBegin(Kind.Decision,out var decision))break;
                    using(decision){now+=.15;visits[cursor]++;cursor=(cursor+1)%visits.Length;decisions++;}
                }
                now+=85; // Low-FPS movement/animation after population decisions, before autonomy Update.
                for(int i=0;i<50;i++)
                {
                    if(!AnimalWorkBudget.TryBegin(Kind.Planning,out var plan))break;
                    using(plan){now+=.1;plans++;totalPlans++;}
                }
                Require(decisions==4 && plans==4,"50 overdue animals starved later planning in frame "+f);
                Require(decisions+plans<=8,"Fair scheduling increased total starts");
            }
            foreach(int count in visits)Require(count==2,"Population round-robin lost an animal");
            Require(totalPlans==100,"Planner failed to make continuous progress under low FPS");
            Reset();int costly=0;
            while(AnimalWorkBudget.TryBegin(Kind.Decision,out var decision))using(decision){now+=.5;costly++;}
            Require(costly==2,"Decision time cap failed before start cap");
            now+=100;for(int i=0;i<4;i++){Require(AnimalWorkBudget.TryBegin(Kind.Planning,out var plan),"Decision time saturation starved planner");using(plan)now+=.2;}
            results.Add("PASS 50 overdue animals over 25 low-FPS frames: every animal advances twice, planning advances every frame; both count and time saturation stay fair");
        }
        finally{clock.Invoke(null,new object[]{null,null});}
        return results.ToArray();
    }
}
