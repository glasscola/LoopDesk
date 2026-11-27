using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;
using System.Threading.Tasks;

namespace LoopDesk {
public sealed class Step {
    public string Kind {get;set;}
    public int X {get;set;} public int Y {get;set;}
    public int Code {get;set;} public int Scan {get;set;} public bool Extended {get;set;}
    public int Delay {get;set;} public int Timeout {get;set;}
    public int RepeatCount {get;set;}
    public List<Step> Children {get;set;}
    public int EffectiveRepeats {get{return Math.Max(1,RepeatCount);}}
    public bool RandomDelay {get;set;}
    public int RandomMin {get;set;} public int RandomMax {get;set;}
    public int RandomDirection {get;set;}
    public bool RandomWhileHeld {get;set;}
    public int[] Modifiers {get;set;}
    public int HoldMs {get;set;}
    public bool CurrentPointer {get;set;}
    public bool DoubleClick {get;set;}
    public int SearchLeft {get;set;} public int SearchTop {get;set;}
    public int SearchWidth {get;set;} public int SearchHeight {get;set;}
    public int SearchDpi {get;set;}
    public string WindowTitle {get;set;}
    public int Then {get;set;} public int Else {get;set;}
    public string Value {get;set;} public bool Skip {get;set;}
    public bool Breakpoint {get;set;}
    public Step() { Kind="Wait"; Value=""; Timeout=5000; }
    internal string KeySlot {get{return "K"+Code+(Extended?"E":"");}}
    internal bool Removed {get{return Then!=0||Else!=0||RemovedKinds.Contains(Kind);}}
    static readonly string[] RemovedKinds={"Image","Pixel","OCR","Element","ElementText","Macro","Jump","WaitWindow"};
    public Step Copy() {var copy=(Step)MemberwiseClone();if(Children!=null)copy.Children=Children.Select(c=>c.Copy()).ToList();return copy;}
    public string Label { get { string[] names={"대기","마우스 이동","버튼 누름","버튼 뗌","휠 스크롤","가로 스크롤","키 누름","키 뗌","문장 입력","지정한 곳 클릭","버튼 그림으로 찾기","색상 기다리기","글자 기다리기","버튼 선택","다른 매크로 실행","조건에 따라 이동","입력칸 선택","키 입력","마우스 클릭","동작 묶음","다음 창 기다리기"};
        int i=Array.IndexOf(Kinds,Kind); return i<0?Kind:names[i]; } }
    public static readonly string[] Kinds={"Wait","Move","Down","Up","Wheel","HWheel","KeyDown","KeyUp","Text","Click","Image","Pixel","OCR","Element","Macro","Jump","ElementText","KeyTap","MouseTap","Group","WaitWindow"};
    public string Detail {get {
        if (Kind=="Group")return (Children==null?0:Children.Count)+"개 동작";
        if (Kind=="KeyTap") return String.Join("+",(Modifiers??new int[0]).Select(k=>InputPicker.KeyName(k,false)).Concat(new[]{InputPicker.KeyName(Code,Extended)}))+
            (HoldMs>0?" · "+(HoldMs/1000m)+"초":" · 한 번");
        if (Kind=="MouseTap") return new[]{"","왼쪽","오른쪽","휠","뒤로","앞으로"}[Math.Max(0,Math.Min(5,Code))]+(DoubleClick?" 더블클릭":" 클릭")+
            (CurrentPointer?" · 현재 커서":" · ("+X+", "+Y+")");
        if (Kind=="Wheel" && CurrentPointer) return "휠 "+(Code>0?"위로 ":"아래로 ")+Math.Abs((long)Code)/120+"칸";
        if (Kind=="KeyDown" || Kind=="KeyUp") return System.Windows.Input.KeyInterop.KeyFromVirtualKey(Code).ToString();
        if (Kind=="Move" || Kind=="Down" || Kind=="Up" || Kind=="Wheel" || Kind=="HWheel" || Kind=="Click" || Kind=="Pixel") return "("+X+", "+Y+")"+(Code==0?"":"  ·  "+Code)+(String.IsNullOrEmpty(Value)?"":"  ·  "+Value);
        if (Kind=="Jump") return Then+"번으로 이동";
        return Value;
    }}
}
public sealed class Macro {
    public string Id {get;set;} public string Name {get;set;}
    public List<Step> Steps {get;set;}
    public string[] Keys {get;set;}
    public int Repeats {get;set;} public int Interval {get;set;} public double Speed {get;set;}
    public string Input {get;set;} public bool Paths {get;set;}
    public bool WindowCoords {get;set;} public string Process {get;set;}
    public bool FollowFocus {get;set;}
    public bool WeekdaysOnly {get;set;}
    public int ScheduleGrace {get;set;}
    public string LastReservation {get;set;}
    public string Schedule {get;set;} public string Csv {get;set;}
    public bool CsvWholeCycle {get;set;} public int CsvRowInterval {get;set;}
    public string Display {get {return Name;}}
    public Macro() {Id=Guid.NewGuid().ToString("N");Name="새 매크로";Steps=new List<Step>();Keys=new string[4];Repeats=1;Speed=1;Input="Both";Paths=true;Process="";Schedule="";Csv="";}
}
internal static class Native {
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int L,T,R,B; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseHook { public Point Pos; public uint Data,Flags,Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyHook { public uint Vk,Scan,Flags,Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput {public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
    [StructLayout(LayoutKind.Sequential)] internal struct KeyInput {public ushort Vk,Scan;public uint Flags,Time;public UIntPtr Extra;}
    [StructLayout(LayoutKind.Explicit)] internal struct Union { [FieldOffset(0)] public MouseInput M; [FieldOffset(0)] public KeyInput K; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint Type;public Union Data; }
    [DllImport("user32")] internal static extern int GetWindowLong(IntPtr h,int index);
    [DllImport("user32")] internal static extern int SetWindowLong(IntPtr h,int index,int value);
    internal delegate IntPtr Hook(int code,IntPtr message,IntPtr data);
    [DllImport("user32",SetLastError=true)] internal static extern IntPtr SetWindowsHookEx(int id,Hook cb,IntPtr module,uint thread);
    [DllImport("user32")] internal static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32")] internal static extern IntPtr CallNextHookEx(IntPtr h,int code,IntPtr w,IntPtr l);
    [DllImport("kernel32",CharSet=CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string n);
    [DllImport("user32",SetLastError=true)] internal static extern bool RegisterHotKey(IntPtr h,int id,uint mod,uint vk);
    [DllImport("user32")] internal static extern bool UnregisterHotKey(IntPtr h,int id);
    [DllImport("user32")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] internal static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32")] internal static extern IntPtr GetAncestor(IntPtr h,uint flags);
    [DllImport("user32")] internal static extern bool IsIconic(IntPtr h);
    [DllImport("user32")] internal static extern bool GetClientRect(IntPtr h,out Rect r);
    [DllImport("user32")] internal static extern bool ClientToScreen(IntPtr h,ref Point p);
    [DllImport("user32")] internal static extern bool GetCursorPos(out Point p);
    [DllImport("user32")] internal static extern int GetSystemMetrics(int id);
    [DllImport("user32")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32")] internal static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    [DllImport("user32",SetLastError=true)] static extern uint SendInput(uint n,Input[] input,int size);
    internal static string App(IntPtr h) {
        uint pid; GetWindowThreadProcessId(h,out pid);
        try { using(var p=Process.GetProcessById((int)pid)) return p.ProcessName; } catch {return "";}
    }
    internal static Rectangle Screen {get {return new Rectangle(GetSystemMetrics(76),GetSystemMetrics(77),GetSystemMetrics(78),GetSystemMetrics(79));}}
    internal static Rectangle Client(IntPtr h) {
        Rect r; var p=new Point();
        if(!GetClientRect(h,out r) || !ClientToScreen(h,ref p) || IsIconic(h) || r.R<=0 || r.B<=0) throw new InvalidOperationException("대상 창의 위치를 확인할 수 없습니다.");
        return new Rectangle(p.X,p.Y,r.R,r.B);
    }
    internal static void Mouse(uint f,int d,int x,int y) {
        var u=new Input(); u.Data.M.Flags=f;u.Data.M.Data=unchecked((uint)d);u.Data.M.X=x;u.Data.M.Y=y; Send(u);
    }
    internal static void Move(int x,int y) {
        var r=Screen;
        if(!r.Contains(x,y)) throw new InvalidOperationException("클릭 위치가 현재 화면 밖에 있습니다.");
        Mouse(0xC001,0,(int)(((long)(x-r.X)*65536+32768)/r.Width),(int)(((long)(y-r.Y)*65536+32768)/r.Height));
    }
    internal static void Key(Step s,bool up) {
        var u=new Input();u.Type=1;
        u.Data.K.Vk=(ushort)(s.Scan>0?0:s.Code);u.Data.K.Scan=(ushort)s.Scan;
        u.Data.K.Flags=(uint)((s.Scan>0?8:0)|(s.Extended?1:0)|(up?2:0));Send(u);
    }
    internal static void Char(char c) {
        var u=new Input();u.Type=1;u.Data.K.Scan=c;u.Data.K.Flags=4; Send(u);u.Data.K.Flags=6;Send(u);
    }
    static void Send(Input u) {
        if(SendInput(1,new[]{u},Marshal.SizeOf(typeof(Input)))!=1) throw new InvalidOperationException("Windows에서 입력을 전달하지 못했습니다. 대상 앱의 권한이나 상태를 확인하세요.");
    }
    internal static uint MouseFlag(int code,bool up) { return (uint)((code==1?2:code==2?8:code==3?32:128) << (up?1:0)); }
    internal static int MouseData(int code) {return code>=4?code-3:0;}
}
internal sealed class Recorder : IDisposable {
    readonly Native.Hook mouse,key;
    IntPtr mh,kh; readonly Stopwatch clock=new Stopwatch(); long last;
    readonly HashSet<int> buttons=new HashSet<int>();
    readonly HashSet<string> keys=new HashSet<string>();
    readonly Macro macro; readonly IntPtr own; readonly Func<int,bool> isStop;
    IntPtr window;
    readonly bool scoped;
    internal readonly List<Step> Steps=new List<Step>();
    internal Recorder(Macro m,IntPtr target,IntPtr ownWindow,Func<int,bool> stop) {
        macro=m;own=ownWindow;isStop=stop;mouse=Mouse;key=Key;
        scoped=m.WindowCoords || !String.IsNullOrWhiteSpace(m.Process);
        if(scoped)AcceptWindow(target,true);
        try {
            if(m.Input!="Keyboard") mh=Install(14,mouse);
            // Observe the stop shortcut even for mouse-only recordings.
            kh=Install(13,key);
            clock.Start();
        } catch {Dispose();throw;}
    }
    bool AcceptWindow(IntPtr candidate,bool canBind) {
        if(candidate==own && own!=IntPtr.Zero)return false;
        if(!scoped)return true;
        if(candidate==IntPtr.Zero)return false;
        if(window!=IntPtr.Zero)return candidate==window;
        if(!canBind || (!String.IsNullOrWhiteSpace(macro.Process) &&
            !String.Equals(Native.App(candidate),macro.Process,StringComparison.OrdinalIgnoreCase)))return false;
        if(macro.WindowCoords) {
            try {Native.Client(candidate);}
            catch(InvalidOperationException) {return false;}
        }
        window=candidate;
        return true;
    }
    static IntPtr Install(int id,Native.Hook callback) {
        var h=Native.SetWindowsHookEx(id,callback,Native.GetModuleHandle(null),0);
        if(h==IntPtr.Zero) throw new Win32Exception();return h;
    }
    void Add(Step s) {
        if(Steps.Count>=200000) return;
        long now=clock.ElapsedMilliseconds;
        s.Delay=Steps.Count==0?0:(int)Math.Min(86400000,now-last);last=now;Steps.Add(s);
    }
    IntPtr Mouse(int n,IntPtr w,IntPtr l) {
        if(n==0) try {
            var p=(Native.MouseHook)Marshal.PtrToStructure(l,typeof(Native.MouseHook)); int msg=w.ToInt32();
            if((p.Flags&1)==0 && AcceptWindow(Native.GetAncestor(Native.WindowFromPoint(p.Pos),2),msg!=512)) {
                var s=new Step {X=p.Pos.X,Y=p.Pos.Y};
                if(macro.WindowCoords) {var r=Native.Client(window);s.X-=r.X;s.Y-=r.Y;}
                if(msg==512) {
                    if((macro.Paths || buttons.Count>0) && clock.ElapsedMilliseconds-last>=12) {s.Kind="Move";Add(s);}
                } else if(msg==522 || msg==526) {s.Kind=msg==522?"Wheel":"HWheel";s.Code=(short)(p.Data>>16);Add(s);}
                else {
                    int code=msg==513||msg==514?1:msg==516||msg==517?2:msg==519||msg==520?3:msg==523||msg==524?(int)(3+(p.Data>>16)):0;
                    if(code!=0) {
                        bool down=msg==513||msg==516||msg==519||msg==523;
                        if(down?buttons.Add(code):buttons.Remove(code)) {s.Kind=down?"Down":"Up";s.Code=code;Add(s);}
                    }
                }
            }
        } catch {}
        return Native.CallNextHookEx(mh,n,w,l);
    }
    IntPtr Key(int n,IntPtr w,IntPtr l) {
        if(n==0) try {
            var p=(Native.KeyHook)Marshal.PtrToStructure(l,typeof(Native.KeyHook));
            int vk=(int)p.Vk;bool up=(p.Flags&128)!=0;
            if((p.Flags&16)==0 && macro.Input!="Mouse" && !(!up && isStop(vk)) &&
                AcceptWindow(Native.GetForegroundWindow(),!up)) {
                bool modifier=vk>=160&&vk<=165 || vk==91 || vk==92;
                var step=new Step{Kind=up?"KeyUp":"KeyDown",Code=vk,Scan=(int)p.Scan,Extended=(p.Flags&1)!=0};
                if(up?keys.Remove(step.KeySlot):keys.Add(step.KeySlot)||!modifier)Add(step);
            }
        } catch {}
        return Native.CallNextHookEx(kh,n,w,l);
    }
    internal List<Step> Finish(bool fromShortcut) {
        Dispose();
        if(fromShortcut) {
            while(Steps.Count>0) {
                var s=Steps[Steps.Count-1];
                if(s.Kind!="KeyDown" || !(s.Code>=160 && s.Code<=165 || s.Code==91 || s.Code==92)) break;
                Steps.RemoveAt(Steps.Count-1);
            }
        }
        var held=new Dictionary<string,Step>();
        foreach(var s in Steps) {
            string id=s.Kind.StartsWith("Key")?s.KeySlot:"M"+s.Code;
            if(s.Kind=="Down" || s.Kind=="KeyDown") held[id]=s;
            if(s.Kind=="Up" || s.Kind=="KeyUp") held.Remove(id);
        }
        foreach(var s in held.Values) {var release=s.Copy();release.Kind=s.Kind=="Down"?"Up":"KeyUp";release.Delay=0;Steps.Add(release);}
        return Steps;
    }
    public void Dispose() {
        if(mh!=IntPtr.Zero) {Native.UnhookWindowsHookEx(mh);mh=IntPtr.Zero;}
        if(kh!=IntPtr.Zero) {Native.UnhookWindowsHookEx(kh);kh=IntPtr.Zero;}
    }
}
internal sealed class Player {
    readonly CancellationToken cancel;
    readonly ManualResetEventSlim gate;
    readonly Action<int,int,string> progress;
    readonly Dictionary<string,Step> held=new Dictionary<string,Step>();
    internal Action<int,int> Repetition;
    int cycle,lastStep;
    readonly List<int> actionPath=new List<int>();
    readonly List<string> callPath=new List<string>();
    internal int[] FailurePath {get;private set;}
    internal string FailureDetail {get;private set;}
    internal Action BeforeInput;
    internal Action<Step,int,int,string> BeforeStep;
    internal readonly Dictionary<string,List<Dictionary<string,string>>> CsvRows=new Dictionary<string,List<Dictionary<string,string>>>(StringComparer.OrdinalIgnoreCase);
    internal void SetPaused(bool paused) {if(paused)gate.Reset();else gate.Set();}
    long lastWheel = -1;
    readonly Stopwatch clock = Stopwatch.StartNew();
    string lastMacro;
    readonly IntPtr own;
    readonly Random random=new Random();
    readonly Func<bool> randomEnabled;
    readonly int[] randomRange;
    internal Player(List<Macro> list,CancellationToken token,ManualResetEventSlim pause,Action<int,int,string> update,IntPtr ownWindow=default(IntPtr),Func<bool> randomSwitch=null,int[] range=null) {
        cancel=token;gate=pause;progress=update;own=ownWindow;
        randomEnabled=randomSwitch;
        randomRange=range;
    }
    internal static int DelayMs(int delay,double speed,int percent) {
        return (int)Math.Round(delay/Math.Max(.1,speed)*(100+percent)/100.0,MidpointRounding.AwayFromZero);
    }
    internal void Run(Macro m,int start,int end) {
        try {Execute(m,start,end,0,null);}
        catch(OperationCanceledException) {throw;}
        catch(Exception e) {
            if(lastStep==0)throw;
            throw new InvalidOperationException(lastMacro+" · "+lastStep+"번 동작: "+e.Message,e);
        }
        finally {Release();}
    }
    void Release() {
        foreach(var s in held.Values.ToArray()) try {
            if(s.Kind=="KeyDown") Native.Key(s,true); else Native.Mouse(Native.MouseFlag(s.Code,true),Native.MouseData(s.Code),0,0);
        } catch {}
        held.Clear();
    }
    void Wait(int ms) {
        do {
            cancel.ThrowIfCancellationRequested();
            if(!gate.IsSet) {
                var previous=held.ToArray();var focus=Native.GetForegroundWindow();
                Release();
                gate.Wait(cancel);
                if(previous.Length>0 && Native.GetForegroundWindow()!=focus)
                    throw new InvalidOperationException("이어서 실행하기 전에 대상 창을 활성화하세요.");
                foreach(var item in previous) {
                    if(item.Value.Kind=="KeyDown")Native.Key(item.Value,false);
                    else Native.Mouse(Native.MouseFlag(item.Value.Code,false),Native.MouseData(item.Value.Code),0,0);
                    held.Add(item.Key,item.Value);
                }
            }
            int chunk=Math.Min(40,ms);
            if(chunk>0 && cancel.WaitHandle.WaitOne(chunk)) cancel.ThrowIfCancellationRequested();
            ms-=chunk;
        } while(ms>0);
    }
    IntPtr Target(Macro m,IntPtr expected) {
        var h=Native.GetForegroundWindow();
        if(own!=IntPtr.Zero && h==own)throw new InvalidOperationException("오토 매크로 창에 포커스가 돌아와 실행을 멈췄습니다.");
        if(expected!=IntPtr.Zero && expected!=h)throw new InvalidOperationException("실행 중 대상 창이 바뀌었습니다.");
        if(h==IntPtr.Zero&&own!=IntPtr.Zero)throw new InvalidOperationException("활성 창이 없습니다.");
        if(!String.IsNullOrWhiteSpace(m.Process) && !String.Equals(Native.App(h),m.Process,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("대상 창을 먼저 활성화하세요: "+m.Process);
        return h;
    }
    static void CheckPoint(Macro m,IntPtr h,int x,int y) {
        if(!m.FollowFocus || !String.IsNullOrWhiteSpace(m.Process) || m.WindowCoords) {
            var over=Native.GetAncestor(Native.WindowFromPoint(new Native.Point{X=x,Y=y}),2);
            if(over!=h) throw new InvalidOperationException("지정된 위치가 대상 창에 가려져 있거나 창 밖에 있습니다.");
        }
    }
    internal static IEnumerable<Tuple<Dictionary<string,string>,int,bool>> Visits(Macro macro,List<Dictionary<string,string>> rows,bool selection) {
        if(selection) {
            if(rows.Count>0)yield return Tuple.Create(rows[0],0,true);
            yield break;
        }
        if(rows.Count==0)yield break;
        bool whole=rows.Count>1&&(macro.CsvWholeCycle||macro.Repeats==0);
        if(whole) {
            for(int pass=0;macro.Repeats==0||pass<macro.Repeats;pass++)
                for(int i=0;i<rows.Count;i++)
                    yield return Tuple.Create(rows[i],i>0?macro.CsvRowInterval:pass>0?macro.Interval:0,i==0);
        } else {
            for(int i=0;i<rows.Count;i++)
                for(int pass=0;macro.Repeats==0||pass<macro.Repeats;pass++)
                    yield return Tuple.Create(rows[i],pass>0?macro.Interval:i>0?macro.CsvRowInterval:0,true);
        }
    }
    IntPtr Execute(Macro m,int start,int end,int depth,Dictionary<string,string> inherited) {
        if(depth>8) throw new InvalidOperationException("동작 묶음이 너무 깊습니다.");
        if(m.Steps.Count==0)return IntPtr.Zero;
        List<Dictionary<string,string>> rows;
        if(String.IsNullOrWhiteSpace(m.Csv))rows=new List<Dictionary<string,string>>{inherited};
        else if(!CsvRows.TryGetValue(m.Csv,out rows))rows=Storage.ReadCsv(m.Csv);
        int lower=Math.Max(0,start),upper=end<0?m.Steps.Count-1:Math.Min(end,m.Steps.Count-1);
        var expected=(!m.FollowFocus||m.WindowCoords||!String.IsNullOrWhiteSpace(m.Process))
            ?Target(m,IntPtr.Zero):IntPtr.Zero;
        foreach(var visit in Visits(m,rows,start>=0)) {
            Wait(visit.Item2);
            var row=visit.Item1;
            if(depth==0&&visit.Item3)cycle++;
            int operations=0;
            for(int i=lower;i<=upper;i++) {
                Wait(0);var s=m.Steps[i];
                if(s.Skip)continue;
                if(s.Removed)throw new InvalidOperationException("제거된 동작입니다. 이 줄을 삭제하고 다시 실행하세요.");
                if(BeforeStep!=null){BeforeStep(s,i+1,cycle,m.Name);Wait(0);}
                for(int repetition=0;repetition<s.EffectiveRepeats;repetition++) {
                    Wait(0);
                    lastStep=i+1;lastMacro=m.Name;
                    if(operations==ExecutionLimit(m.Steps.Count)) {
                        FailurePath=actionPath.Concat(new[]{i}).ToArray();
                        throw new InvalidOperationException("반복 횟수 한도를 넘었습니다. 실행한 동작: "+operations.ToString("N0"));
                    }
                    operations++;
                    FailurePath=actionPath.Concat(new[]{i}).ToArray();
                    string location=(i+1)+"번 "+s.Label+" ("+(repetition+1)+"/"+s.EffectiveRepeats+"회)";
                    FailureDetail="전체 "+cycle+"회차 · "+String.Join(" → ",callPath.Concat(new[]{location}));
                    string value=Expand(s.Value,row);
                    if(Repetition!=null)Repetition(repetition+1,s.EffectiveRepeats);progress(lastStep,cycle,m.Name);
                    int variation=0;
                    if((randomRange!=null || s.RandomDelay) && (randomEnabled==null || randomEnabled()) &&
                        (held.Count==0 || randomRange==null && s.RandomWhileHeld)) {
                        variation=randomRange==null?random.Next(s.RandomMin,s.RandomMax+1):random.Next(randomRange[0],randomRange[1]+1);
                        int direction=randomRange==null?s.RandomDirection:0;
                        if(direction==1 || (direction==0 && random.Next(2)==0)) variation=-variation;
                    }
                    Wait(DelayMs(s.Delay,m.Speed,variation));
                    if(BeforeInput!=null)BeforeInput();
                    var h=Target(m,expected);
                    if(s.Kind=="Group") {
                        if(held.Count>0)throw new InvalidOperationException("키나 마우스를 누른 상태에서는 묶음 반복을 시작할 수 없습니다.");
                        var child=new Macro{Name=m.Name+" / 묶음",Steps=s.Children,Repeats=1,Input=m.Input,Speed=m.Speed,Process=m.Process,WindowCoords=m.WindowCoords,FollowFocus=m.FollowFocus};
                        actionPath.Add(i);callPath.Add(location);
                        try {Execute(child,-1,-1,depth+1,row);}
                        finally {actionPath.RemoveAt(actionPath.Count-1);callPath.RemoveAt(callPath.Count-1);}
                    } else if(s.Kind=="KeyTap") {
                        if(m.Input!="Mouse") TapKeys(s);
                    } else if(s.Kind=="KeyDown" || s.Kind=="KeyUp") {
                        if(m.Input!="Mouse") {
                            bool up=s.Kind=="KeyUp";string id=s.KeySlot;
                            if(!up) {Native.Key(s,false);held[id]=s;}
                            else if(held.ContainsKey(id)) {Native.Key(s,true);held.Remove(id);}
                        }
                    } else if(s.Kind=="Text") {
                        if(m.Input!="Mouse") foreach(char c in value) {Wait(0);Target(m,expected);Native.Char(c);}
                    } else if(s.Kind!="Wait" && m.Input!="Keyboard") {
                        int x=s.X,y=s.Y;
                        if(s.CurrentPointer) {
                            Native.Point point;
                            if(!Native.GetCursorPos(out point))throw new InvalidOperationException("커서 위치를 읽을 수 없습니다.");
                            x=point.X;y=point.Y;
                        } else if(m.WindowCoords) {var r=Native.Client(h);x+=r.X;y+=r.Y;}
                        if(s.Kind!="Move") CheckPoint(m,h,x,y);
                        if(s.Kind=="Up" && !held.ContainsKey("M"+s.Code))break;
                        if(!s.CurrentPointer)Native.Move(x,y);
                        if(s.Kind=="Click"||s.Kind=="MouseTap"||s.Kind=="Down")CheckPoint(m,Target(m,expected),x,y);
                        if(s.Kind=="Click") Click();
                        if(s.Kind=="MouseTap") {
                            Click(s.Code);
                            if(s.DoubleClick) {
                                Wait(70);CheckPoint(m,Target(m,expected),x,y);
                                Native.Move(x,y);CheckPoint(m,Target(m,expected),x,y);Click(s.Code);
                            }
                        }
                        if(s.Kind=="Down" || s.Kind=="Up") {
                            bool up=s.Kind=="Up";Native.Mouse(Native.MouseFlag(s.Code,up),Native.MouseData(s.Code),0,0);
                            if(up) held.Remove("M"+s.Code);else held["M"+s.Code]=s;
                        }
                        if(s.Kind=="Wheel" || s.Kind=="HWheel") {
                            if(lastWheel>=0)Wait((int)Math.Max(0,100-(clock.ElapsedMilliseconds-lastWheel)));
                            Target(m,expected);CheckPoint(m,h,x,y);
                            Native.Mouse(s.Kind=="Wheel"?2048u:4096u,s.Code,0,0);
                            lastWheel=clock.ElapsedMilliseconds;
                        }
                    }
                }
            }
            Release();
        }
        return expected;
    }
    void TapKeys(Step s) {
        var keys=(s.Modifiers??new int[0]).Select(code=>new Step{Kind="KeyDown",Code=code,Extended=InputPicker.Extended(code)}).ToList();
        var main=s.Copy();main.Kind="KeyDown";keys.Add(main);
        var pressed=new List<Step>();
        foreach(var key in keys) {
            if(held.ContainsKey(key.KeySlot))continue;
            Native.Key(key,false);held[key.KeySlot]=key;pressed.Add(key);
        }
        Wait(s.HoldMs);
        foreach(var key in pressed.AsEnumerable().Reverse()) {
            Native.Key(key,true);held.Remove(key.KeySlot);
        }
    }
    void Click(int code=1) {
        string id="M"+code;
        if(held.ContainsKey(id))return;
        var s=new Step{Kind="Down",Code=code};Native.Mouse(Native.MouseFlag(code,false),Native.MouseData(code),0,0);held[id]=s;
        Native.Mouse(Native.MouseFlag(code,true),Native.MouseData(code),0,0);held.Remove(id);
    }
    internal static int ExecutionLimit(int steps) {return Math.Max(1000000,steps*2);}
    internal static string Expand(string value,Dictionary<string,string> row) {
        value=value??"";
        if(row==null)return value;
        return System.Text.RegularExpressions.Regex.Replace(value,@"\{\{([^{}]+)\}\}",match=>{
            string field;
            if(!row.TryGetValue(match.Groups[1].Value,out field))
                throw new InvalidDataException("CSV 열을 찾을 수 없습니다: "+match.Groups[1].Value+" · 사용 가능: "+String.Join(", ",row.Keys));
            return field;
        });
    }
}
}



