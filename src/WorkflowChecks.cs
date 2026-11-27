using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;

namespace LoopDesk {
internal sealed partial class Desk {
    void CheckV4(string path) {
        var folder=path+".assets";
        Directory.CreateDirectory(folder);
        string csv=Path.Combine(folder,"sample.csv");
        File.WriteAllText(csv,"이름\r\n민지\r\n다음");
        var macro=new Macro{Csv=csv,Steps=new List<Step>{new Step{Kind="Group",RepeatCount=7,Children=new List<Step>{
            new Step{Kind="Wait"},new Step{Kind="Text",Value="{{이름}}님"}}}}};
        var source=new Profile();source.Macros.Add(macro);
        string backup=Path.Combine(folder,"backup.loopdesk-backup");
        Storage.Write(backup,BackupBundle.Create(source,true));
        File.Delete(csv);
        var restored=Storage.Read<BackupBundle>(backup);
        restored.Extract(Path.Combine(folder,"restored"));
        var copy=restored.Profile.Macros[0];
        Require(File.Exists(copy.Csv)&&copy.Steps[0].EffectiveRepeats==7,"CSV or repeat was lost in restore.");
        var row=Storage.ReadCsv(copy.Csv).First();
        Require(Player.Expand(copy.Steps[0].Children[1].Value,row)=="민지님","CSV asset could not be restored.");
        try {Player.Expand("{{없는 열}}",row);throw new InvalidOperationException("A missing column was accepted.");}
        catch(InvalidDataException) {}
        var bad=new Macro{Csv=copy.Csv,Steps=new List<Step>{new Step{Kind="Group",Children=new List<Step>{
            new Step{Kind="Wait"},new Step{Kind="Text",Value="{{missing}}"}}}}};
        using(var gate=new ManualResetEventSlim(true)) {
            var player=new Player(new List<Macro>{bad},CancellationToken.None,gate,(a,b,c)=>{});
            try {player.Run(bad,-1,-1);}
            catch(InvalidOperationException) {}
            Require(player.FailurePath.SequenceEqual(new[]{0,1}),"Nested failure did not identify the inner step.");
        }
        current.Steps=new List<Step>{new Step{Kind="Move",Delay=10},new Step{Kind="Move",Delay=20},new Step{Kind="Wait"}};
        Refresh(0);
        Require(grid.Items.Count==2&&Selection().SequenceEqual(new[]{1,2}),"Movement folding lost selection range.");
        TogglePath((Row)grid.SelectedItem);
        Require(grid.Items.Count==3&&current.Steps[1].Delay==20,"Expanding movement edited captured data.");
        Require(HasFixedPoint(new Step{Kind="MouseTap",DoubleClick=true})&&!HasFixedPoint(new Step{Kind="Wheel",CurrentPointer=true}),
            "Fixed and pointer-relative previews were not distinguished.");
        var friday=new DateTime(2026,11,27,10,0,0);
        Require(NextReservation(new Macro{Schedule="09:30",WeekdaysOnly=true},friday,false)==new DateTime(2026,11,30,9,30,0),
            "Weekdays-only reservation did not skip the weekend.");
        string bands=RandomPreview(100,1,10,40);
        Require(bands.Contains("0.06")&&bands.Contains("0.09")&&bands.Contains("0.11")&&bands.Contains("0.14"),"Random delay preview does not match timing.");
    }
}
}

