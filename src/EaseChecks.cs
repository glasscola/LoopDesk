using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace LoopDesk {
internal sealed partial class Desk {
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    TextBox EditField(string label){return Inputs(Panel("Editor")).OfType<TextBox>().Single(b=>AutomationProperties.GetName(b)==label);}
    internal async Task CheckActiveReservation(IntPtr target) {
        Require(Native.GetForegroundWindow()==target,"The reservation test window is not active.");
        current.Process=Native.App(target);
        current.Schedule="+5";
        current.Steps=new List<Step>{new Step{Kind="Text",Value="예약 실행"}};
        scheduled[current.Id]=DateTime.Now.AddMinutes(-1);
        Tick();
        for(int i=0;i<60&&Busy;i++)await Task.Delay(50);
        Require(!Busy&&logs.Any(line=>line.Contains("실행 완료")),"The active-window reservation did not finish.");
    }
    internal void CheckEase(string path) {
        current.Steps=new List<Step>{new Step{Kind="KeyTap",Code=81},new Step{Kind="Wait",Delay=500}};
        Refresh(0);
        EditField("매번 실행 전 대기 (초)").Text="0.2";
        EditField("이 동작 반복 (회)").Text="10";
        grid.SelectedIndex=1;
        Require(current.Steps[0].Delay==200&&current.Steps[0].RepeatCount==10&&grid.SelectedIndex==1,"Unsaved fields were lost on row change.");
        grid.SelectedIndex=0;EditField("이 동작 반복 (회)").Text="0";grid.SelectedIndex=1;
        Require(grid.SelectedIndex==0&&current.Steps[0].RepeatCount==10,"Invalid edits did not keep the original selection and value.");
        EditField("이 동작 반복 (회)").Text="5";Require(FlushDrafts(),"Valid edits did not save.");
        Undo();Require(current.Steps[0].RepeatCount==10,"Undo lost previous repeat count.");
        Redo();Require(current.Steps[0].RepeatCount==5,"Redo did not restore repeat count.");
        grid.SelectedItems.Clear();grid.SelectedItems.Add(grid.Items[0]);grid.SelectedItems.Add(grid.Items[1]);GroupSelection();
        Require(current.Steps.Count==1&&current.Steps[0].Kind=="Group"&&current.Steps[0].Children.Count==2,"Grouping failed.");
        Storage.Validate(current);var copy=current.Steps[0].Copy();copy.Children[0].Code=65;
        Require(current.Steps[0].Children[0].Code==81,"Group copies share child steps.");
        Ungroup();Require(current.Steps.Count==2&&current.Steps[0].Code==81,"Ungroup lost child input.");
        BeginInput(null,null);
        Require(F<Border>("InputDock").Visibility==Visibility.Visible&&inputPicker!=null,"Input dock did not open.");
        inputPicker.CheckPick(65,10,100);
        Require(current.Steps.Any(s=>s.Kind=="KeyTap"&&s.Code==65&&s.RepeatCount==10),"Picker did not add repeated input.");
        Require(F<Border>("InputDock").Visibility==Visibility.Visible,"Adding input must leave the dock open for more inputs.");
        var mouseActions=new[]{"왼쪽 클릭","오른쪽 클릭","휠 클릭","휠 위로","휠 아래로"};
        var mouseCodes=new[]{1,2,3,120,-120};
        for(int i=0;i<mouseActions.Length;i++) {
            inputPicker.CheckMouse(mouseActions[i]);
            var added=current.Steps[grid.SelectedIndex];
            Require(added.Kind==(i<3?"MouseTap":"Wheel")&&added.Code==mouseCodes[i]&&added.CurrentPointer,
                "The mouse control added the wrong input: "+mouseActions[i]);
        }
        CloseInput();Require(!capturing&&F<Border>("InputDock").Visibility==Visibility.Collapsed,"Closing input did not resume shortcut mode.");
        var small=new Size(530,365);
        FitWorkspace(small.Width);
        root.Measure(small);root.Arrange(new Rect(small));root.UpdateLayout();
        Require(compact&&F<Border>("StepsPane").Visibility==Visibility.Visible&&F<Border>("MacroPane").Visibility==Visibility.Collapsed,
            "Compact layout did not show the steps as its main view.");
        ShowCompactPane("MacroPane");
        Require(F<Border>("MacroPane").Visibility==Visibility.Visible&&F<Border>("StepsPane").Visibility==Visibility.Collapsed,
            "The macro picker is inaccessible at the minimum window size.");
        ShowCompactPane("EditorPane");
        Require(F<Border>("EditorPane").Visibility==Visibility.Visible,"Compact settings are inaccessible.");
        BeginInput(null,null);
        Require(Grid.GetRow(F<Border>("InputDock"))==2&&F<Grid>("Workspace").Visibility==Visibility.Collapsed,
            "The small window clipped the input picker instead of opening it in the workspace.");
        CloseInput();
        Require(F<Border>("EditorPane").Visibility==Visibility.Visible&&F<Grid>("Workspace").Visibility==Visibility.Visible,
            "Closing input did not return to the compact editor.");
        FitWorkspace(1100);
        Require(!compact&&new[]{"MacroPane","StepsPane","EditorPane"}.All(name=>F<Border>(name).Visibility==Visibility.Visible),
            "Widening the window did not restore the three-pane layout.");
        current.Steps=new List<Step>{new Step{Kind="Pixel",Code=0,Timeout=5000},new Step{Kind="Wait"}};Refresh(0);
        var timeout=Inputs(Panel("Editor")).OfType<TextBox>().Single(b=>AutomationProperties.GetName(b)=="최대 대기 시간 (초)");
        timeout.Text="1.5";grid.SelectedIndex=1;
        Require(current.Steps[0].Timeout==1500&&grid.SelectedIndex==1,"Advanced timing was not saved or selection was lost.");
        var pairs=SimplifyRecording(new List<Step>{new Step{Kind="KeyDown",Code=81,Delay=70},new Step{Kind="KeyUp",Code=81,Delay=40}});
        Require(pairs.Count==1&&pairs[0].Kind=="KeyTap"&&pairs[0].Delay==70&&pairs[0].HoldMs==40,"Recording simplification changed key timing.");
        var chord=SimplifyRecording(new List<Step>{new Step{Kind="KeyDown",Code=162},new Step{Kind="KeyDown",Code=81},new Step{Kind="KeyUp",Code=81},new Step{Kind="KeyUp",Code=162}});
        Require(chord.Count==4,"Recording simplification changed a held modifier.");
        using(var gate=new ManualResetEventSlim(true)) {
            var group=new Macro{Name="group",Steps=new List<Step>{
                new Step{Kind="Group",RepeatCount=3,Children=new List<Step>{new Step{Kind="Wait"},new Step{Kind="Wait"}}},new Step{Kind="Wait"}}};
            var visits=new List<string>();
            new Player(new List<Macro>{group},CancellationToken.None,gate,(step,cycle,name)=>visits.Add(name+":"+step)).Run(group,-1,-1);
            Require(visits.Count==10&&visits.Count(v=>v.Contains("/ 묶음"))==6,"Grouped actions did not execute in order on each repeat.");
        }
        current.Schedule="+5";
        current.Process="LoopDesk.ScheduleFixture.NotRunning";
        scheduled[current.Id]=DateTime.Now.AddMinutes(-1);
        Tick();
        Require(!Busy&&scheduled[current.Id]>DateTime.Now&&logs.Any(line=>line.Contains("예약 건너뜀")),
            "A scheduled macro without its target window was not skipped.");
        profile.CommonKeys[0]=profile.Emergency;
        Require(Register().Contains("겹칩니다"),"Duplicate shortcuts were not detected.");
        File.WriteAllText(path,"PASS: editor persistence, undo/redo, grouped repeats, small-window navigation, input picker and wide layout restoration, all five mouse controls, recording conversion, reservation skip, and shortcut conflict detection.");
    }
    internal void PreviewUI() {
        current.Name="Q 입력 반복 예제";
        current.Steps=new List<Step>{new Step{Kind="KeyTap",Code=81,RepeatCount=10,Delay=100},new Step{Kind="Wait",Delay=500}};
        Refresh(0);Show();
    }
}
}
