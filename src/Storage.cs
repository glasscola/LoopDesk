using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using Microsoft.VisualBasic.FileIO;

namespace LoopDesk {
public sealed class Profile {
    public List<Macro> Macros {get;set;}
    public string Emergency {get;set;}
    public string[] CommonKeys {get;set;}
    public string SelectedMacro {get;set;}
    public List<Step> FavoriteKeys {get;set;}
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
        if(m.ScheduleGrace<0||m.ScheduleGrace>120||m.Repeats<0 || m.Repeats>100000 || m.Interval<0 || m.Interval>86400000 || m.CsvRowInterval<0 || m.CsvRowInterval>86400000 || Double.IsNaN(m.Speed) || m.Speed<.1 || m.Speed>8 || !new[]{"Both","Mouse","Keyboard"}.Contains(m.Input))
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
        if(s.SearchDpi<0||s.SearchDpi>960||(s.WindowTitle??"").Length>250)throw new InvalidDataException("검색 범위 배율이나 창 제목을 확인하세요.");
        if(s.SearchWidth<0||s.SearchWidth>200000||s.SearchHeight<0||s.SearchHeight>200000||
            s.SearchLeft< -200000||s.SearchLeft>200000||s.SearchTop< -200000||s.SearchTop>200000)
            throw new InvalidDataException("검색 범위를 확인하세요.");
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
    internal static List<Dictionary<string,string>> ReadCsv(string path,CancellationToken cancel=default(CancellationToken)) {
        var rows=new List<Dictionary<string,string>>();
        using(var parser=new TextFieldParser(path,Encoding.UTF8,true)) {
            parser.SetDelimiters(",");parser.HasFieldsEnclosedInQuotes=true;
            var names=parser.ReadFields();if(names==null || names.Any(String.IsNullOrWhiteSpace) || names.Distinct().Count()!=names.Length)
                throw new InvalidDataException("CSV 첫 줄에 중복 없는 열 이름을 넣어주세요.");
            while(!parser.EndOfData) {
                cancel.ThrowIfCancellationRequested();
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
internal static class ScreenCapture {
    internal static Bitmap Capture(Rectangle region) {
        var bitmap=new Bitmap(region.Width,region.Height,PixelFormat.Format32bppArgb);
        try {
            using(var graphics=Graphics.FromImage(bitmap))graphics.CopyFromScreen(region.Location,Point.Empty,region.Size);
            return bitmap;
        } catch {bitmap.Dispose();throw;}
    }

}
}

