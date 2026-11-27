using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace LoopDesk {
internal static class V6Checks {
    static void Require(bool condition,string message) {if(!condition)throw new InvalidOperationException(message);}

    internal static void Run() {
        var rows=new[]{"A","B","C"}.Select(value=>new Dictionary<string,string>{{"row",value}}).ToList();
        var macro=new Macro{Repeats=2,Interval=200,CsvRowInterval=50};
        Func<string> order=()=>String.Concat(Player.Visits(macro,rows,false).Select(visit=>visit.Item1["row"]));
        Require(order()=="AABBCC","Legacy CSV repetition changed.");
        macro.CsvWholeCycle=true;
        Require(order()=="ABCABC","Full CSV repetition is not row-major.");
        Require(Player.Visits(macro,rows,false).Select(visit=>visit.Item2).SequenceEqual(new[]{0,50,50,200,50,50}),
            "Row and cycle intervals are not distinct.");
        Require(Player.Visits(macro,rows,false).Select(visit=>visit.Item3).SequenceEqual(new[]{true,false,false,true,false,false}),
            "Full CSV cycles were counted by row.");
        Require(Player.Visits(macro,rows,true).Count()==1,"Selection did not limit CSV to one row.");
        Require(!Player.Visits(macro,new List<Dictionary<string,string>>(),false).Any(),"An empty table produced a visit.");
        macro.CsvWholeCycle=false;
        macro.Repeats=0;
        Require(String.Concat(Player.Visits(macro,rows,false).Take(7).Select(visit=>visit.Item1["row"]))=="ABCABCA",
            "Infinite CSV playback did not reach all rows.");

        var large=new Macro{Steps=Enumerable.Range(0,11).Select(index=>new Step{RepeatCount=100000}).ToList()};
        AssertBlocked(large,10,"1,100,000");
        foreach(string kind in new[]{"Image","Pixel","OCR","Element","ElementText","Macro","Jump","WaitWindow"})
            AssertBlocked(new Macro{Steps=new List<Step>{new Step(),new Step{Kind=kind}}},1,"제거된");
        AssertBlocked(new Macro{Steps=new List<Step>{new Step{Then=1}}},0,"제거된");
        AssertBlocked(new Macro{Steps=new List<Step>{new Step{Kind="Group",Children=new List<Step>{new Step{Kind="OCR"}}}}},0,"묶음");
        Require(new Step{Code=13}.KeySlot!=new Step{Code=13,Extended=true}.KeySlot,"Enter key slots collided.");

    }

    static void AssertBlocked(Macro macro,int index,string expected) {
        try {Preflight.Check(macro,-1,-1,CancellationToken.None);}
        catch(PreflightException e) {
            Require(e.StepIndex==index&&e.Message.Contains(expected),"Incorrect preflight error: "+e.Message);
            return;
        }
        throw new InvalidOperationException("An invalid macro passed preflight.");
    }
}
}

