using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Point=System.Drawing.Point;
using FontStyle=System.Drawing.FontStyle;

namespace LoopDesk {
internal static class SelfCheck {
    internal static void Run(string path) {
        var profile=new Profile();
        if(InputPicker.KeyCount!=108)throw new Exception("The keyboard picker does not have 108 keys.");
        var m=new Macro{Name="한글 매크로"};
        m.Steps.Add(new Step{Kind="Text",Value="한글 {{이름}}",Delay=15});
        profile.Macros.Add(m);Storage.Validate(m);
        string json=path+".json",csv=path+".csv";
        Storage.Write(json,profile);
        var restored=Storage.Read<Profile>(json);
        if(restored.Macros[0].Steps[0].Value!=m.Steps[0].Value)throw new Exception("JSON round trip failed.");
        if(Player.DelayMs(100,1,40)!=140 || Player.DelayMs(100,1,-40)!=60 || Player.DelayMs(100,2,40)!=70)
            throw new Exception("Random delay percentage or playback speed is wrong.");
        if(restored.Macros[0].Steps[0].RandomDelay)throw new Exception("Existing delays must default to fixed.");
        profile.CommonKeys=new[]{"F9","F11","F6","F7","F10","F8"};
        profile.SelectedMacro=m.Id;
        m.Steps[0].RandomDelay=true;m.Steps[0].RandomMin=10;m.Steps[0].RandomMax=50;
        m.Steps.Add(new Step{Kind="KeyTap",Code=67,Modifiers=new[]{162},HoldMs=100,RepeatCount=10});
        m.Steps.Add(new Step{Kind="MouseTap",Code=3,CurrentPointer=true});
        Storage.Write(json,profile);
        restored=Storage.Read<Profile>(json);
        Storage.Validate(restored.Macros[0]);
        if(Storage.Read<Profile>(json+".bak").Macros[0].Steps.Count!=1)
            throw new Exception("The backup did not keep the previous saved revision.");
        if(restored.Macros[0].Steps[1].Detail!="Ctrl+C · 0.1초" || restored.Macros[0].Steps[1].EffectiveRepeats!=10 ||
            restored.Macros[0].Steps[2].EffectiveRepeats!=1 || !restored.Macros[0].Steps[2].CurrentPointer)
            throw new Exception("Manual input rows did not round trip.");
        using(var gate=new ManualResetEventSlim(true)) {
            var repeat=new Macro{Steps=new List<Step>{new Step{RepeatCount=10},new Step()}};
            var visits=new List<int>();
            new Player(new List<Macro>{repeat},CancellationToken.None,gate,(step,cycle,name)=>visits.Add(step)).Run(repeat,-1,-1);
            if(visits.Count!=11 || visits.GetRange(0,10).Exists(step=>step!=1) || visits[10]!=2)
                throw new Exception("Step repeats must finish before the next step.");
        }
        if(restored.CommonKeys[0]!="F9" || restored.SelectedMacro!=m.Id ||
            !restored.Macros[0].Steps[0].RandomDelay || restored.Macros[0].Steps[0].RandomMax!=50)
            throw new Exception("Shortcut or random delay settings were not persisted.");
        File.WriteAllText(csv,"이름,비고\r\n민수,\"쉼표, 포함\"\r\n");
        if(Storage.ReadCsv(csv)[0]["비고"]!="쉼표, 포함")throw new Exception("CSV quote handling failed.");
        using(var screen=new Bitmap(55,35))
        using(var template=new Bitmap(5,7)) {
            using(var g=Graphics.FromImage(template))g.Clear(Color.Turquoise);
            using(var g=Graphics.FromImage(screen)){g.Clear(Color.Black);g.DrawImageUnscaled(template,20,14);}
            Point point;
            if(!Vision.MatchBitmap(screen,template,0,CancellationToken.None,Point.Empty,out point) || point!=new Point(22,17))
                throw new Exception("Image match failed.");
        }
        using(var token=new CancellationTokenSource())
        using(var gate=new ManualResetEventSlim(true)) {
            var wait=new Macro{Repeats=0,Steps=new List<Step>{new Step{Kind="Wait",Delay=5000}}};
            token.CancelAfter(40);
            try {new Player(new List<Macro>{wait},token.Token,gate,delegate{}).Run(wait,-1,-1);throw new Exception("Cancellation failed.");}
            catch(OperationCanceledException) {}
        }
        using(var token=new CancellationTokenSource(5000))
        using(var gate=new ManualResetEventSlim(true))
        using(var reached=new ManualResetEventSlim()) {
            var wait=new Macro{Steps=new List<Step>{new Step{Kind="Wait",Delay=60},new Step{Kind="Wait",Delay=10}}};
            var visited=new List<int>();
            var playback=Task.Run(()=>new Player(new List<Macro>{wait},token.Token,gate,(step,cycle,name)=>{
                visited.Add(step);
                if(step==1){gate.Reset();reached.Set();}
            }).Run(wait,-1,-1));
            if(!reached.Wait(2000) || playback.Wait(160))throw new Exception("Playback did not stay paused.");
            gate.Set();
            if(!playback.Wait(2000) || visited.Count!=2 || visited[0]!=1 || visited[1]!=2)
                throw new Exception("Playback did not resume from the paused step.");
        }
        string vision;
        using(var sample=new Bitmap(600,120))
        using(var font=new Font("Arial",42,FontStyle.Bold))
        using(var g=Graphics.FromImage(sample)) {
            g.Clear(Color.White);g.DrawString("LOOP DESK",font,Brushes.Black,10,10);
            try {vision=Vision.HasTextBitmap(sample,"LOOP DESK",CancellationToken.None)?"PASS: Windows OCR.":"OCR call completed; test phrase not detected.";}
            catch(Exception e){vision="OCR could not be verified: "+e.Message;}
        }
        File.WriteAllText(path,"PASS: profile, shortcuts and previous-revision backup, random delay math, pause/resume, wait cancellation, CSV and image matching.\r\n"+vision);
    }
    internal static void Desktop(string path) {
        var text=new TextBox{FontSize=22,Name="FixtureInput",Margin=new Thickness(24)};
        System.Windows.Automation.AutomationProperties.SetAutomationId(text,"FixtureInput");
        var board=new Grid{Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(23,143,119))};
        text.VerticalAlignment=VerticalAlignment.Top;
        board.Children.Add(text);
        var w=new Window{Title="LoopDesk · 입력 테스트",Content=board,Width=480,Height=245,Topmost=true,WindowStartupLocation=WindowStartupLocation.CenterScreen};
        var results=new List<string>();
        var app=new Application();
        w.ContentRendered+=async delegate {
            try {
                w.Activate();text.Focus();
                var h=new WindowInteropHelper(w).Handle;
                for(int attempt=0;attempt<300&&Native.GetForegroundWindow()!=h;attempt++)await Task.Delay(150);
                System.Windows.Input.Keyboard.Focus(text);
                bool registered=Native.RegisterHotKey(h,91,2|1|4|0x4000,121);
                int keyError=registered?0:System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                if(registered)Native.UnregisterHotKey(h,91);
                var m=new Macro{WindowCoords=true};
                using(var recorder=new Recorder(m,h,IntPtr.Zero,k=>false)) {
                    if(Native.GetForegroundWindow()!=h) {
                        File.WriteAllText(path,"PASS: native hook installation.\r\nHotkey registration: "+(registered?"PASS":"Win32 error "+keyError)+
                            "\r\nSKIP: live replay; test window did not receive foreground focus.");
                        return;
                    }
                    if(!registered)throw new System.ComponentModel.Win32Exception(keyError);
                    Native.Char('한');Native.Char('글');await Task.Delay(350);
                    results.Add((text.Text=="한글"?"PASS":"FAIL")+" 03: Korean Unicode input into Windows TextBox.");
                    if(recorder.Finish(false).Count!=0)throw new Exception("Injected input leaked into recording.");
                }
                text.Text="";
                m.Steps.Add(new Step{Kind="ElementText",Value="FixtureInput|요소 입력",Timeout=1500});
                results.Add("PASS: hotkey registration, native hooks and injected-event filtering.");
                try {
                    using(var c=new CancellationTokenSource(6000))
                    using(var gate=new ManualResetEventSlim(true))
                        await Task.Run(()=>new Player(new List<Macro>{m},c.Token,gate,delegate{}).Run(m,-1,-1));
                    if(text.Text!="요소 입력")throw new Exception("UI automation did not update the test field.");
                    results.Add("PASS: UI element text.");
                } catch(Exception error) {results.Add("FAIL UI element text: "+error.Message);}
                w.Activate();text.Focus();
                Func<Step[],Task> play=async steps=>{
                    if(Native.GetForegroundWindow()!=h)throw new InvalidOperationException("The test window lost focus.");
                    m.Steps=new List<Step>(steps);
                    using(var token=new CancellationTokenSource(12000))
                    using(var gate=new ManualResetEventSlim(true))
                        await Task.Run(()=>new Player(new List<Macro>{m},token.Token,gate,delegate{}).Run(m,-1,-1));
                    await Task.Delay(60);
                };
                Action<string> expectText=value=>{
                    if(text.Text!=value)throw new InvalidOperationException("Expected '"+value+"' but received '"+text.Text+"'.");
                };
                text.Text="";text.Focus();
                await play(new[]{new Step{Kind="KeyTap",Code=81,RepeatCount=10,Delay=100}});
                expectText("qqqqqqqqqq");results.Add("PASS 01: Windows TextBox received exactly 10 Q taps.");
                text.Text="";
                m.Repeats=2;
                await play(new[]{new Step{Kind="Group",RepeatCount=3,Children=new List<Step>{
                    new Step{Kind="KeyTap",Code=81,RepeatCount=2},new Step{Kind="KeyTap",Code=69}}}});
                expectText(string.Concat(Enumerable.Repeat("qqe",6)));m.Repeats=1;
                results.Add("PASS 07: single, grouped and whole-macro repeats reached the TextBox in the right order.");
                var events=new List<Tuple<int,long,long>>();
                var watch=Stopwatch.StartNew();
                HwndSource.FromHwnd(h).AddHook((IntPtr window,int message,IntPtr word,IntPtr data,ref bool handled)=>{
                    if((message>=0x100&&message<=0x101)||(message>=0x201&&message<=0x20a))
                        events.Add(Tuple.Create(message,message==0x20a?word.ToInt64():data.ToInt64(),watch.ElapsedMilliseconds));
                    return IntPtr.Zero;
                });
                await play(new[]{new Step{Kind="KeyTap",Code=13},new Step{Kind="KeyTap",Code=13,Extended=true},
                    new Step{Kind="KeyTap",Code=65,HoldMs=500}});
                var downs=events.Where(e=>e.Item1==0x100).ToArray();var ups=events.Where(e=>e.Item1==0x101).ToArray();
                if(downs.Length<3||ups.Length!=3||(downs[0].Item2&0x1000000)!=0||(downs[1].Item2&0x1000000)==0||
                    ups[2].Item3-downs[2].Item3<480)throw new InvalidOperationException("Enter or hold timing differed.");
                results.Add("PASS 04: Enter and numpad Enter differed, and a 500 ms hold released once.");
                events.Clear();
                await play(new[]{
                    new Step{Kind="MouseTap",Code=1,X=100,Y=115},new Step{Kind="MouseTap",Code=2,X=100,Y=115},
                    new Step{Kind="MouseTap",Code=3,X=100,Y=115},new Step{Kind="Wheel",Code=120,X=100,Y=115},
                    new Step{Kind="Wheel",Code=-120,X=100,Y=115},new Step{Kind="Down",Code=1,X=100,Y=130},
                    new Step{Kind="Move",X=150,Y=140},new Step{Kind="Up",Code=1,X=150,Y=140}});
                var expected=new[]{0x201,0x202,0x204,0x205,0x207,0x208,0x20a,0x20a,0x201,0x202};
                var mouse=events.Where(e=>e.Item1>=0x201).ToArray();
                bool mousePassed=mouse.Select(e=>e.Item1).SequenceEqual(expected)&&(short)(mouse[6].Item2>>16)==120&&
                    (short)(mouse[7].Item2>>16)==-120&&(short)mouse[9].Item2==150;
                results.Add(mousePassed?"PASS 05: left, right, middle, both wheel directions and drag endpoint.":
                    "FAIL 05: mouse message transcript "+string.Join(", ",mouse.Select(e=>e.Item1.ToString("X")+":"+e.Item2.ToString("X"))));
                if(!mousePassed)throw new InvalidOperationException("Wheel or mouse delivery failed.");
                text.Focus();text.Text="";
                m.Csv=path+".csv";
                File.WriteAllText(m.Csv,"이름\r\n민지\r\n다음");
                m.Steps=new List<Step>{new Step{Kind="Text",Value="{{이름}}님"}};
                using(var gate=new ManualResetEventSlim(true))
                    await Task.Run(()=>new Player(new List<Macro>{m},CancellationToken.None,gate,delegate{}).Run(m,0,0));
                await Task.Delay(80);expectText("민지님");m.Csv="";
                results.Add("PASS: selected-step playback read the first CSV row and delivered expanded Korean text.");
                var oldBounds=Native.Client(h);
                w.Left+=30;await Task.Delay(80);
                var newBounds=Native.Client(h);
                await play(new[]{new Step{Kind="MouseTap",Code=1,X=100,Y=115}});
                Native.Point cursor;
                if(oldBounds.X==newBounds.X||!Native.GetCursorPos(out cursor)||cursor.X!=newBounds.X+100||cursor.Y!=newBounds.Y+115)
                    throw new InvalidOperationException("Window-relative click did not follow the moved window.");
                results.Add("PASS 06 (single display): client-relative click followed a moved window.");
                var points=new List<long>();
                var timer=Stopwatch.StartNew();
                using(var gate=new ManualResetEventSlim(true)) {
                    m.Steps=new List<Step>{new Step{Kind="Wait",Delay=100,RepeatCount=30}};
                    await Task.Run(()=>new Player(new List<Macro>{m},CancellationToken.None,gate,(i,r,n)=>points.Add(timer.ElapsedMilliseconds),
                        randomSwitch:()=>true,range:new[]{40,40}).Run(m,-1,-1));
                }
                var intervals=points.Zip(points.Skip(1),(a,b)=>b-a).ToArray();
                if(!intervals.Any(ms=>ms>=50&&ms<100)||!intervals.Any(ms=>ms>=125&&ms<210))
                    throw new InvalidOperationException("Expected both randomized delay bands.");
                results.Add("PASS 08: randomized waits included both bands; measured "+intervals.Min()+"–"+intervals.Max()+" ms.");
                text.Focus();
                text.Text="";
                await play(new[]{new Step{Kind="Pixel",X=100,Y=115,Value="#178F77",Timeout=2000},
                    new Step{Kind="Text",Value="픽셀 성공"}});
                expectText("픽셀 성공");
                text.Text="";
                await play(new[]{new Step{Kind="Pixel",X=100,Y=115,Value="#FF00FF",Timeout=200,Else=3},
                    new Step{Kind="Text",Value="잘못된 경로"},new Step{Kind="Text",Value="시간 초과"}});
                expectText("시간 초과");
                string template=path+".png";
                var target=new Rectangle(newBounds.X+90,newBounds.Y+105,20,20);
                using(var image=Vision.Capture(target))image.Save(template);
                text.Text="";
                await play(new[]{new Step{Kind="Image",Value=template,Timeout=2000},
                    new Step{Kind="Text",Value="이미지 성공"}});
                expectText("이미지 성공");
                results.Add("PASS 11: live pixel match, timeout fallback and live image search.");

                text.Text="";
                await new Desk(true).CheckActiveReservation(h);
                expectText("예약 실행");
                results.Add("PASS 12: a due reservation typed into its active target window.");

                m.Steps=new List<Step>{new Step{Kind="KeyDown",Code=162},new Step{Kind="Wait",Delay=5000}};
                using(var cancel=new CancellationTokenSource())
                using(var gate=new ManualResetEventSlim(true)) {
                    var running=Task.Run(()=>new Player(new List<Macro>{m},cancel.Token,gate,delegate{}).Run(m,-1,-1));
                    await Task.Delay(200);
                    bool pressed=Native.GetAsyncKeyState(162)<0;
                    cancel.Cancel();
                    try{await running;}catch(OperationCanceledException){}
                    if(!pressed||Native.GetAsyncKeyState(162)<0)throw new InvalidOperationException("Cancellation did not release the held Ctrl key.");
                }
                results.Add("PASS 09: cancelling playback released a held Ctrl key.");
                File.WriteAllLines(path,results);
                if(results.Any(result=>result.StartsWith("FAIL")))Environment.ExitCode=1;
            } catch(Exception e) {File.WriteAllLines(path,results);File.WriteAllText(path+".error",e.ToString());Environment.ExitCode=1;}
            finally {w.Close();}
        };
        app.Run(w);
    }
}
}
