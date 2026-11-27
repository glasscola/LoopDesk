using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace LoopDesk {
internal sealed partial class Desk {
    bool recordingReview;
    string reservationRunning;
    TextBlock reservationCaption;
    Step inputOriginal;
    int inputIndex;
    readonly HashSet<Step> expandedPaths=new HashSet<Step>();

    Macro NewRecordedMacro(Macro source) {
        var macro=new Macro {Name=Unique(source.Name+" 녹화"),Process=source.Process,WindowCoords=source.WindowCoords,
            Paths=source.Paths,Input=source.Input,FollowFocus=source.FollowFocus};
        profile.Macros.Add(macro);
        return macro;
    }

    Window WorkflowDialog(string title, Panel panel, double width) {
        return new Window {Owner=this, Title=title, Content=panel, Width=width, MaxHeight=SystemParameters.WorkArea.Height-24,
            SizeToContent=SizeToContent.Height, WindowStartupLocation=WindowStartupLocation.CenterOwner,
            ShowInTaskbar=false, FontFamily=FontFamily, FontSize=FontSize};
    }

    int ChooseRecording(Macro target,int count) {
        var panel=new StackPanel {Margin=new Thickness(18)};
        var dialog=WorkflowDialog("녹화 저장",panel,460);
        Label(panel,count+"개 동작을 녹화했습니다. 기존 ‘"+target.Name+"’에 "+target.Steps.Count+"개 동작이 있습니다.",true);
        int choice=0;
        var save=new Button {Content="새 매크로로 저장",IsDefault=true,Padding=new Thickness(10,7,10,7),Margin=new Thickness(0,8,0,6)};
        save.Click+=(s,e)=>{choice=1;dialog.DialogResult=true;};
        panel.Children.Add(save);
        ActionButton(panel,"현재 매크로 교체",()=>{choice=2;dialog.DialogResult=true;});
        panel.Children.Add(new Button {Content="저장하지 않기",IsCancel=true,Margin=new Thickness(0,8,0,0)});
        recordingReview=true;
        try {dialog.ShowDialog();} finally {recordingReview=false;}
        return choice;
    }

    static bool HasFixedPoint(Step step) {
        return !step.CurrentPointer && new[]{"Click","MouseTap","Down","Up","Move","Wheel","HWheel"}.Contains(step.Kind);
    }

    static string ShortStep(Step step) {
        return new Row{Step=step}.Summary.Replace("\r"," ").Replace("\n"," ")+" · "+step.EffectiveRepeats+"회";
    }

    void UpdateInputContext() {
        if(inputPicker==null||inputPicker.IsFavoriteKey)return;
        var steps=current.Steps;
        var row=grid.SelectedItem as Row;
        int index=inputOriginal==null?(row==null?steps.Count:row.Number+row.PathCount-1):inputIndex;
        index=Math.Max(0,Math.Min(index,steps.Count));
        var before=index>0?(index)+"번 "+ShortStep(steps[index-1]):"목록 시작";
        int next=inputOriginal==null?index:index+1;
        var after=next<steps.Count?(next+1)+"번 "+ShortStep(steps[next]):"목록 끝";
        inputPicker.SetContext(inputOriginal==null?(index+1)+"번에 추가":(index+1)+"번 수정 · "+ShortStep(inputOriginal),before+"  →  "+after);
    }

    void RefreshStepRows(int selection) {
        var steps=current.Steps;
        expandedPaths.RemoveWhere(step=>!steps.Contains(step));
        var rows=new List<Row>();
        for(int index=0;index<steps.Count;) {
            int end=index+1;
            var step=steps[index];
            if(step.Kind=="Move"&&!expandedPaths.Contains(step))
                while(end<steps.Count&&steps[end].Kind=="Move"&&!expandedPaths.Contains(steps[end]))end++;
            rows.Add(new Row{Step=step,Number=index+1,PathCount=end-index});
            index=end;
        }
        grid.ItemsSource=rows;
        if(selection<0||rows.Count==0)return;
        grid.SelectedItem=rows.Last(row=>row.Number<=Math.Min(selection+1,steps.Count));
        grid.ScrollIntoView(grid.SelectedItem);
    }

    void TogglePath(Row row) {
        if(row.Step.Kind!="Move")return;
        bool expand=row.PathCount>1;
        int start=row.Number-1,end=start+1;
        if(!expand)while(start>0&&current.Steps[start-1].Kind=="Move")start--;
        while(end<current.Steps.Count&&current.Steps[end].Kind=="Move")end++;
        for(int index=start;index<end;index++) {
            if(expand)expandedPaths.Add(current.Steps[index]);else expandedPaths.Remove(current.Steps[index]);
        }
        Refresh(start);
    }

    void UpdateTargetCaption() {
        if(current==null)return;
        var label=Text("TargetCaption");
        label.Text="입력 대상 · "+(!String.IsNullOrWhiteSpace(current.Process)?current.Process:
            current.FollowFocus?"실행 중 활성 창을 따라감":"실행을 시작한 창");
        label.ToolTip=label.Text;
    }

    static DateTime NextReservation(Macro macro,DateTime now,bool initial) {
        DateTime due=macro.Schedule.StartsWith("+")?now.AddMinutes(int.Parse(macro.Schedule.Substring(1))):
            now.Date+DateTime.ParseExact(macro.Schedule,"HH:mm",CultureInfo.InvariantCulture).TimeOfDay;
        if(!macro.Schedule.StartsWith("+")&&(initial?due.AddMinutes(Math.Max(1,macro.ScheduleGrace))<now:due<=now))due=due.AddDays(1);
        while(macro.WeekdaysOnly&&(due.DayOfWeek==DayOfWeek.Saturday||due.DayOfWeek==DayOfWeek.Sunday))due=due.Date.AddDays(1).Add(due.TimeOfDay);
        return due;
    }

    void UpdateReservationCaption() {
        if(reservationCaption==null||current==null)return;
        DateTime next;
        reservationCaption.Text=!scheduled.TryGetValue(current.Id,out next)?"예약 미지정":
            "다음 실행 · "+next.ToString("ddd HH:mm",CultureInfo.GetCultureInfo("ko-KR"))+" · "+current.Process;
        if(!String.IsNullOrEmpty(current.LastReservation))reservationCaption.Text+="\n지난 예약 · "+current.LastReservation;
    }

    void SetReservationResult(Macro macro,string result) {
        macro.LastReservation=DateTime.Now.ToString("MM-dd HH:mm")+" · "+result;
        UpdateReservationCaption();
        Save();
    }

    void Practice() {
        var panel=new StackPanel{Margin=new Thickness(18)};
        var dialog=WorkflowDialog("Q 10회 연습",panel,420);
        Label(panel,"연습칸에서 수신한 입력을 확인합니다.",true);
        var receiver=new TextBox{Margin=new Thickness(0,10,0,10),MinHeight=34,MaxLength=1000};
        panel.Children.Add(receiver);
        var result=new TextBlock{Text="예상 · Q 10회",TextWrapping=TextWrapping.Wrap};
        panel.Children.Add(result);
        var test=new Button{Content="Q 10회 시험",Padding=new Thickness(10,6,10,6),Margin=new Thickness(0,12,0,0)};
        panel.Children.Add(test);
        bool testing=false;
        dialog.Closing+=(s,e)=>{if(testing)e.Cancel=true;};
        test.Click+=async (s,e)=>{
            test.IsEnabled=false;testing=true;
            var example=new Macro{Steps=new List<Step>{new Step{Kind="KeyTap",Code=81,RepeatCount=10,Delay=100}}};
            receiver.Clear();receiver.Focus();
            try {
                var expected=new System.Windows.Interop.WindowInteropHelper(dialog).Handle;
                using(var gate=new ManualResetEventSlim(true)) {
                    var player=new Player(new List<Macro>{example},CancellationToken.None,gate,(a,b,c)=>{});
                    player.BeforeInput=()=>{
                        if(Native.GetForegroundWindow()!=expected)throw new InvalidOperationException("연습 창을 활성화한 뒤 다시 시험하세요.");
                    };
                    await Task.Run(()=>player.Run(example,-1,-1));
                }
                for(int attempt=0;attempt<15&&receiver.Text.Count(ch=>Char.ToUpperInvariant(ch)=='Q')<10;attempt++)
                    await Task.Delay(20);
                int count=receiver.Text.Count(ch=>Char.ToUpperInvariant(ch)=='Q');
                result.Text="예상 10회 · 실제 수신 "+count+"회"+(count==10?"":" · 입력 언어와 연습칸 포커스를 확인하세요.");
            } catch(Exception error) {result.Text=error.Message;}
            finally {testing=false;test.IsEnabled=true;}
        };
        CaptureDialog(dialog);
    }

    void ChooseCsvColumn(TextBox value) {
        try {
            var menu=new ContextMenu{PlacementTarget=value};
            foreach(var column in Storage.ReadCsv(current.Csv).First().Keys) {
                string token="{{"+column+"}}";
                AddMenuItem(menu,column,()=>{value.SelectedText=token;value.Focus();});
            }
            menu.IsOpen=true;
        } catch(Exception error) {Note(error.Message,true);}
    }

    void ShowCsvText(TextBox source,TextBlock result) {
        try {result.Text="첫 행 · "+Player.Expand(source.Text,Storage.ReadCsv(current.Csv).First());}
        catch(Exception error){result.Text=error.Message;}
    }

    void CsvPreview(string path,TextBlock result,Macro schedule=null) {
        try {
            var rows=Storage.ReadCsv(path);
            var row=rows[0];
            var text=current.Steps.SelectMany(BackupBundle.Walk).FirstOrDefault(step=>step.Kind=="Text"&&step.Value.Contains("{{"));
            result.Text="첫 행 · "+String.Join(" · ",row.Select(pair=>pair.Key+" = "+pair.Value));
            if(text!=null)result.Text+="\n입력 예정 · "+Player.Expand(text.Value,row);
            if(schedule!=null) {
                var numbers=rows.Select((entry,index)=>new{entry,index}).ToDictionary(item=>item.entry,item=>item.index+1);
                result.Text+="\n처음 실행할 CSV 행 · "+String.Join(" → ",Player.Visits(schedule,rows,false).Take(4).Select(visit=>numbers[visit.Item1]));
            }
        } catch(Exception error) {result.Text=error.Message;}
    }
}
}

