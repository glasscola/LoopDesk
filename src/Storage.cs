using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using Microsoft.VisualBasic.FileIO;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace LoopDesk {
public sealed class Profile {
    public List<Macro> Macros {get;set;}
    public string Emergency {get;set;}
    public string[] CommonKeys {get;set;}
    public string SelectedMacro {get;set;}
    public bool RandomOff {get;set;}
    public int? GlobalRandomMin {get;set;}
    public int? GlobalRandomMax {get;set;}
    public bool LogOff {get;set;}
    public Profile() {Macros=new List<Macro>();Emergency="Ctrl+Alt+Esc";}
}
internal static class Storage {
    internal static readonly string Home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LoopDesk");
    internal static readonly string Library=Path.Combine(Home,"library.json");
    internal static T Read<T>(string path) {
        using(var f=File.OpenRead(path)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(f);
    }
    internal static void Write<T>(string path,T value) {
        string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            using(var f=File.Create(tmp)) new DataContractJsonSerializer(typeof(T)).WriteObject(f,value);
            if(File.Exists(path)) File.Replace(tmp,path,path+".bak");else File.Move(tmp,path);
        } finally {if(File.Exists(tmp))File.Delete(tmp);}
    }
    internal static void Validate(Macro m) {
        if(m==null || String.IsNullOrWhiteSpace(m.Name) || m.Name.Length>100 || m.Steps==null || m.Steps.Count>200100 || m.Keys==null || m.Keys.Length!=4)
            throw new InvalidDataException("매크로 파일 형식을 확인하세요.");
        if(m.Repeats<0 || m.Repeats>100000 || m.Interval<0 || m.Interval>86400000 || Double.IsNaN(m.Speed) || m.Speed<.1 || m.Speed>8 || !new[]{"Both","Mouse","Keyboard"}.Contains(m.Input))
            throw new InvalidDataException("반복, 속도 또는 입력 범위가 올바르지 않습니다.");
        foreach(var s in m.Steps) {
            ValidateStep(s);
            if(s==null || !Step.Kinds.Contains(s.Kind) || s.Delay<0 || s.Delay>86400000 || s.Timeout<0 || s.Timeout>86400000 ||
                s.RepeatCount<0 || s.RepeatCount>100000 ||
                s.RandomMin<0 || s.RandomMax<s.RandomMin || s.RandomMax>100 || s.RandomDirection<0 || s.RandomDirection>2 ||
                s.Then<0 || s.Else<0 || s.Then>m.Steps.Count || s.Else>m.Steps.Count)
                throw new InvalidDataException("동작 종류, 시간 또는 분기 단계 번호를 확인하세요.");
            if((s.Kind=="Down" || s.Kind=="Up") && (s.Code<1 || s.Code>5))throw new InvalidDataException("마우스 버튼 번호는 1~5입니다.");
            if((s.Kind=="KeyDown" || s.Kind=="KeyUp") && (s.Code<1 || s.Code>254 || s.Scan<0 || s.Scan>255))throw new InvalidDataException("키 번호가 올바르지 않습니다.");
            if(s.Kind=="KeyTap" && (s.Code<1 || s.Code>254 || s.Scan<0 || s.Scan>255 || s.HoldMs<0 || s.HoldMs>86400000 ||
                (s.Modifiers!=null && (s.Modifiers.Length>8 || s.Modifiers.Any(k=>!InputPicker.IsModifier(k))))))
                throw new InvalidDataException("키 입력 설정을 확인하세요.");
            if(s.Kind=="MouseTap" && (s.Code<1 || s.Code>5))throw new InvalidDataException("마우스 버튼을 확인하세요.");
        }
    }
    internal static void ValidateStep(Step s,int depth=0) {
        if(s==null||depth>8||!Step.Kinds.Contains(s.Kind)||s.Delay<0||s.Delay>86400000||s.Timeout<0||s.Timeout>86400000||
            s.RepeatCount<0||s.RepeatCount>100000||s.HoldMs<0||s.HoldMs>86400000||
            s.RandomMin<0||s.RandomMax<s.RandomMin||s.RandomMax>100||s.RandomDirection<0||s.RandomDirection>2)
            throw new InvalidDataException("동작의 시간이나 반복 횟수가 올바르지 않습니다.");
        if(s.Kind=="Group") {
            if(s.Children==null||s.Children.Count<1||s.Children.Count>200100)throw new InvalidDataException("묶음 안의 동작을 확인하세요.");
            foreach(var child in s.Children) {
                ValidateStep(child,depth+1);
                if(child.Then!=0||child.Else!=0||child.Kind=="Jump")throw new InvalidDataException("묶음 안에는 단계 이동을 넣을 수 없습니다.");
            }
        }
        if(s.Kind=="KeyTap"||s.Kind=="KeyDown"||s.Kind=="KeyUp") {
            if(s.Code<1||s.Code>254||s.Scan<0||s.Scan>255||s.Modifiers!=null&&s.Modifiers.Any(k=>!InputPicker.IsModifier(k)))
                throw new InvalidDataException("입력할 키를 확인하세요.");
        }
        if((s.Kind=="MouseTap"||s.Kind=="Down"||s.Kind=="Up")&&(s.Code<1||s.Code>5))
            throw new InvalidDataException("마우스 버튼을 확인하세요.");
    }
    internal static List<Dictionary<string,string>> ReadCsv(string path) {
        var rows=new List<Dictionary<string,string>>();
        using(var parser=new TextFieldParser(path,Encoding.UTF8,true)) {
            parser.SetDelimiters(",");parser.HasFieldsEnclosedInQuotes=true;
            var names=parser.ReadFields();if(names==null || names.Any(String.IsNullOrWhiteSpace) || names.Distinct().Count()!=names.Length)
                throw new InvalidDataException("CSV 첫 줄에 중복 없는 열 이름을 넣어주세요.");
            while(!parser.EndOfData) {
                var fields=parser.ReadFields();
                if(fields.Length!=names.Length)throw new InvalidDataException("CSV 열 개수가 맞지 않습니다.");
                var row=new Dictionary<string,string>();
                for(int i=0;i<names.Length;i++)row.Add(names[i],fields[i]);
                rows.Add(row);
            }
        }
        if(rows.Count==0)throw new InvalidDataException("CSV에 실행할 데이터가 없습니다.");
        return rows;
    }
}
internal static class Vision {
    internal static Bitmap Capture(Rectangle region) {
        if(region.Width<=0 || region.Height<=0)throw new InvalidOperationException("검색 영역이 화면에 표시되지 않습니다.");
        var bitmap=new Bitmap(region.Width,region.Height,PixelFormat.Format32bppArgb);
        try {using(var g=Graphics.FromImage(bitmap))g.CopyFromScreen(region.Location,Point.Empty,region.Size);return bitmap;}
        catch {bitmap.Dispose();throw;}
    }
    internal static bool Pixel(Rectangle region,int x,int y,string hex,int tolerance) {
        if(!region.Contains(x,y))throw new InvalidOperationException("색상 좌표가 검색 영역 밖입니다.");
        if(hex.Length!=7 || hex[0]!='#')throw new InvalidOperationException("색상을 #RRGGBB 형식으로 입력하세요.");
        var expected=ColorTranslator.FromHtml(hex);
        using(var b=Capture(new Rectangle(x,y,1,1))) {
            var p=b.GetPixel(0,0);
            return Math.Abs(p.R-expected.R)<=tolerance && Math.Abs(p.G-expected.G)<=tolerance && Math.Abs(p.B-expected.B)<=tolerance;
        }
    }
    static byte[] Pixels(Bitmap b) {
        var rect=new Rectangle(0,0,b.Width,b.Height);
        var data=b.LockBits(rect,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try {
            byte[] result=new byte[b.Width*b.Height*4];
            for(int y=0;y<b.Height;y++)Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),result,y*b.Width*4,b.Width*4);
            return result;
        } finally {b.UnlockBits(data);}
    }
    internal static bool Match(Rectangle region,Bitmap template,int tolerance,CancellationToken token,out Point p) {
        using(var b=Capture(region))return MatchBitmap(b,template,tolerance,token,region.Location,out p);
    }
    internal static bool MatchBitmap(Bitmap screen,Bitmap template,int tolerance,CancellationToken token,Point offset,out Point p) {
        p=Point.Empty;
        int w=screen.Width,h=screen.Height,tw=template.Width,th=template.Height;
        if(tw>w || th>h)return false;
        var a=Pixels(screen);var b=Pixels(template);
        int[] samples={0,(tw-1)*4,(th/2*tw+tw/2)*4,(tw*th-1)*4};
        for(int y=0;y<=h-th;y++) {
            token.ThrowIfCancellationRequested();
            for(int x=0;x<=w-tw;x++) {
                int idx=(y*w+x)*4;bool ok=true;
                foreach(int i in samples) if(!Similar(a,idx+i/(tw*4)*w*4+i%(tw*4),b,i,tolerance)) {ok=false;break;}
                if(!ok)continue;
                for(int j=0;j<th && ok;j++)for(int k=0;k<tw*4;k+=4)
                    if(!Similar(a,idx+j*w*4+k,b,j*tw*4+k,tolerance)) {ok=false;break;}
                if(ok) {p=new Point(offset.X+x+tw/2,offset.Y+y+th/2);return true;}
            }
        }
        return false;
    }
    static bool Similar(byte[] a,int ai,byte[] b,int bi,int tolerance) {
        return Math.Abs(a[ai]-b[bi])<=tolerance && Math.Abs(a[ai+1]-b[bi+1])<=tolerance && Math.Abs(a[ai+2]-b[bi+2])<=tolerance;
    }
    internal static bool HasText(Rectangle area,string text,CancellationToken token) {
        using(var b=Capture(area))return HasTextBitmap(b,text,token);
    }
    internal static bool HasTextBitmap(Bitmap b,string text,CancellationToken token) {
        if(String.IsNullOrWhiteSpace(text))throw new InvalidOperationException("찾을 글자를 입력하세요.");
        var engine=OcrEngine.TryCreateFromUserProfileLanguages();
        if(engine==null)throw new InvalidOperationException("Windows 설정에서 사용할 OCR 언어를 설치하세요.");
        {
            double scale=Math.Min(1, (double)OcrEngine.MaxImageDimension/Math.Max(b.Width,b.Height));
            using(var resized=new Bitmap(b,Math.Max(1,(int)(b.Width*scale)),Math.Max(1,(int)(b.Height*scale))))
            using(var writer=new DataWriter()) {
                writer.WriteBytes(Pixels(resized));
                using(var bitmap=SoftwareBitmap.CreateCopyFromBuffer(writer.DetachBuffer(),BitmapPixelFormat.Bgra8,resized.Width,resized.Height,BitmapAlphaMode.Ignore)) {
                    var op=engine.RecognizeAsync(bitmap);
                    try {
                        while(op.Status==Windows.Foundation.AsyncStatus.Started) {
                            if(token.WaitHandle.WaitOne(20)){op.Cancel();token.ThrowIfCancellationRequested();}
                        }
                        var result=op.GetResults();
                        return result.Text.IndexOf(text,StringComparison.CurrentCultureIgnoreCase)>=0 ||
                            result.Text.Replace(" ","").IndexOf(text.Replace(" ",""),StringComparison.CurrentCultureIgnoreCase)>=0;
                    } finally {if(op.Status!=Windows.Foundation.AsyncStatus.Started)op.Close();}
                }
            }
        }
    }
}
}
