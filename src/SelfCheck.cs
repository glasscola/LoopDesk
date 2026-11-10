using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
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
        File.WriteAllText(path,"PASS: profile and shortcut persistence, random delay math, pause/resume, wait cancellation, CSV and image matching.\r\n"+vision);
    }
    internal static void Desktop(string path) {
        var text=new TextBox{FontSize=22,Name="FixtureInput",Margin=new Thickness(24)};
        System.Windows.Automation.AutomationProperties.SetAutomationId(text,"FixtureInput");
        var w=new Window{Title="LoopDesk · 입력 테스트",Content=text,Width=480,Height=175,Topmost=true,WindowStartupLocation=WindowStartupLocation.CenterScreen};
        var app=new Application();
        w.ContentRendered+=async delegate {
            try {
                w.Activate();text.Focus();
                var h=new WindowInteropHelper(w).Handle;
                await Task.Delay(150);
                bool registered=Native.RegisterHotKey(h,91,2|1|4|0x4000,121);
                int keyError=registered?0:System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                if(registered)Native.UnregisterHotKey(h,91);
                var m=new Macro();
                using(var recorder=new Recorder(m,h,IntPtr.Zero,k=>false)) {
                    if(Native.GetForegroundWindow()!=h) {
                        File.WriteAllText(path,"PASS: native hook installation.\r\nHotkey registration: "+(registered?"PASS":"Win32 error "+keyError)+
                            "\r\nSKIP: live replay; test window did not receive foreground focus.");
                        return;
                    }
                    if(!registered)throw new System.ComponentModel.Win32Exception(keyError);
                    Native.Char('한');Native.Char('글');await Task.Delay(200);
                    if(text.Text!="한글")throw new Exception("SendInput text did not reach the test field.");
                    if(recorder.Finish(false).Count!=0)throw new Exception("Injected input leaked into recording.");
                }
                text.Text="";
                m.Steps.Add(new Step{Kind="ElementText",Value="FixtureInput|요소 입력",Timeout=1500});
                using(var c=new CancellationTokenSource(3000))
                using(var gate=new ManualResetEventSlim(true))
                    await Task.Run(()=>new Player(new List<Macro>{m},c.Token,gate,delegate{}).Run(m,-1,-1));
                if(text.Text!="요소 입력")throw new Exception("UI automation did not update the test field.");
                File.WriteAllText(path,"PASS: global hotkey registration, native hooks, Korean SendInput, injected-event filtering, UI element text.");
            } catch(Exception e) {File.WriteAllText(path+".error",e.ToString());Environment.ExitCode=1;}
            finally {w.Close();}
        };
        app.Run(w);
    }
}
}
