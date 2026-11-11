using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LoopDesk {
internal sealed partial class Desk {
    InputPicker inputPicker;
    Func<Step> readEdit;
    Row editRow;
    Macro editMacro;
    Action readSettings;
    bool editDirty,settingsDirty,savingDraft;
    TextBlock editStatus,settingsStatus;
    DispatcherTimer draftTimer;
    readonly Dictionary<string,Stack<List<Step>>> redo=new Dictionary<string,Stack<List<Step>>>();
    Window runHint;
    TextBlock runHintText;
    DateTime recordedAt;
    int failedStep;
    Point? dragOrigin;
    Action advancedCommit;
    bool advancedDirty;
    // Collapse only isolated key pairs. Chords, auto-repeat and mouse paths retain their timing.
    internal static List<Step> SimplifyRecording(List<Step> steps) {
        var result=new List<Step>();var held=new HashSet<int>();
        for(int i=0;i<steps.Count;i++) {
            var s=steps[i];
            if(held.Count==0&&s.Kind=="KeyDown"&&!InputPicker.IsModifier(s.Code)&&i+1<steps.Count) {
                var next=steps[i+1];
                if(next.Kind=="KeyUp"&&next.Code==s.Code) {
                    result.Add(new Step{Kind="KeyTap",Code=s.Code,Scan=s.Scan,Extended=s.Extended,Delay=s.Delay,HoldMs=next.Delay});
                    i++;continue;
                }
            }
            if(s.Kind=="KeyDown")held.Add(s.Code);else if(s.Kind=="KeyUp")held.Remove(s.Code);
            result.Add(s);
        }
        return result;
    }

    void InitEase() {
        On("DirectInput",()=>BeginInput(null,null));
        On("EditTools",()=>EditMenu(F<Button>("EditTools")));
        On("Manage",ManageMenu);
        On("Example",()=>{
            current=new Macro{Name=Unique("Q 10번 입력")};
            current.Steps.Add(new Step{Kind="KeyTap",Code=81,RepeatCount=10,Delay=100});
            profile.Macros.Add(current);Changed(0);F<TabControl>("Tabs").SelectedIndex=1;
        });
        draftTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(650)};
        draftTimer.Tick+=(a,b)=>{draftTimer.Stop();FlushDrafts();};
        grid.PreviewMouseLeftButtonDown+=(a,b)=>{
            var element=b.OriginalSource as DependencyObject;
            while(element!=null&&!(element is DataGridRow))element=VisualTreeHelper.GetParent(element);
            dragOrigin=element is DataGridRow?(Point?)b.GetPosition(grid):null;
        };
        grid.MouseMove+=(a,b)=>{
            if(Busy||!dragOrigin.HasValue||b.LeftButton!=MouseButtonState.Pressed||grid.SelectedItems.Count!=1)return;
            var pos=b.GetPosition(grid);
            if(Math.Abs(pos.X-dragOrigin.Value.X)<8&&Math.Abs(pos.Y-dragOrigin.Value.Y)<8)return;
            var row=grid.SelectedItem as Row;dragOrigin=null;
            if(row!=null&&FlushDrafts())DragDrop.DoDragDrop(grid,new DataObject("LoopDesk.Row",row),DragDropEffects.Move);
        };
        grid.AllowDrop=true;
        grid.Drop+=(a,b)=>Safe(()=>{
            var source=b.Data.GetData("LoopDesk.Row") as Row;
            var visual=b.OriginalSource as DependencyObject;
            while(visual!=null&&!(visual is DataGridRow))visual=VisualTreeHelper.GetParent(visual);
            var target=visual is DataGridRow?((DataGridRow)visual).Item as Row:null;
            if(source==null||target==null||source==target||Busy)return;
            int from=current.Steps.IndexOf(source.Step),to=current.Steps.IndexOf(target.Step);
            if(from<0||to<0)return;
            Mutate(()=>{current.Steps.RemoveAt(from);current.Steps.Insert(to,source.Step);},to);
        });
        PreviewKeyDown+=(a,b)=>{
            if(capturing||Busy||b.OriginalSource is TextBox)return;
            if(Keyboard.Modifiers==ModifierKeys.Control&&b.Key==Key.Z){Safe(()=>{if(undo.ContainsKey(current.Id)&&undo[current.Id].Count>0)Undo();});b.Handled=true;}
            if(Keyboard.Modifiers==ModifierKeys.Control&&b.Key==Key.Y){Safe(Redo);b.Handled=true;}
        };
    }
    static IEnumerable<FrameworkElement> Inputs(DependencyObject parent) {
        foreach(var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>()) {
            var element=child as FrameworkElement;
            if(element is TextBox||element is ComboBox||element is CheckBox)yield return element;
            foreach(var nested in Inputs(child))yield return nested;
        }
    }
    void Watch(Panel panel,Action dirty) {
        foreach(var e in Inputs(panel)) {
            var text=e as TextBox;var combo=e as ComboBox;var check=e as CheckBox;
            if(text!=null)text.TextChanged+=(a,b)=>dirty();
            if(combo!=null)combo.SelectionChanged+=(a,b)=>dirty();
            if(check!=null){check.Checked+=(a,b)=>dirty();check.Unchecked+=(a,b)=>dirty();}
        }
    }
    void DraftChanged(bool settings) {
        if(savingDraft||refreshing)return;
        if(settings)settingsDirty=true;else editDirty=true;
        var status=settings?settingsStatus:editStatus;
        if(status!=null){status.Text="수정 중…";status.Foreground=Brushes.SlateGray;}
        if(draftTimer!=null){draftTimer.Stop();draftTimer.Start();}
    }
    bool FlushDrafts() {
        if(savingDraft)return true;
        savingDraft=true;
        try {
            if(editDirty&&readEdit!=null&&editRow!=null&&editMacro!=null) {
                var changed=readEdit();
                int index=editMacro.Steps.IndexOf(editRow.Step);
                if(index<0)throw new InvalidOperationException("편집 중인 동작을 찾을 수 없습니다.");
                Storage.ValidateStep(changed);
                Snapshot(editMacro);redo.Remove(editMacro.Id);
                editMacro.Steps[index]=changed;editRow.Step=changed;Save();
                refreshing=true;try{grid.Items.Refresh();}finally{refreshing=false;}
                editDirty=false;
                if(editStatus!=null){editStatus.Text="✓ 자동 저장됨";editStatus.Foreground=new SolidColorBrush(Color.FromRgb(23,107,99));}
            }
            if(settingsDirty&&readSettings!=null) {
                readSettings();Save();settingsDirty=false;RefreshLibrary();Text("Title").Text=current.Name;
                if(settingsStatus!=null){settingsStatus.Text="✓ 자동 저장됨";settingsStatus.Foreground=new SolidColorBrush(Color.FromRgb(23,107,99));}
            }
            if(advancedDirty&&advancedCommit!=null) {
                advancedDirty=false;
                try{advancedCommit();}catch{advancedDirty=true;throw;}
            }
            return true;
        } catch(Exception e) {
            var status=editDirty?editStatus:settingsStatus;
            if(status!=null){status.Text=e.Message;status.Foreground=Brushes.Firebrick;}
            Note(e.Message,true);return false;
        } finally {savingDraft=false;}
    }
    void EditPanel() {
        readEdit=null;editDirty=false;advancedCommit=null;advancedDirty=false;
        editRow=grid.SelectedItem as Row;editMacro=current;
        var p=Panel("Editor");p.Children.Clear();Panel("EditorFooter").Children.Clear();
        if(editRow==null){Label(p,"동작을 선택하면 여기에서 수정할 수 있어요.",true);return;}
        var s=editRow.Step;
        bool simple=s.Kind=="KeyTap"||s.Kind=="MouseTap"||s.Kind=="Wait"||s.Kind=="Text"||s.Kind=="Move"||s.Kind=="Group"||s.Kind=="Wheel"&&s.Value=="Manual";
        if(!simple){
            AdvancedEditPanel();
            Watch(p,()=>{advancedDirty=true;Note("수정 중 · 변경 저장 또는 다른 동작 선택 시 저장됩니다.");});
            return;
        }
        p.Children.Add(new TextBlock{Text=s.Kind=="Group"?"묶음 반복":editRow.Summary,FontSize=18,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,8)});
        var delay=Field(p,s.Kind=="Wait"?"기다릴 시간 (초)":"매번 실행 전 대기 (초)",(s.Delay/1000m).ToString("0.###",CultureInfo.InvariantCulture),10);
        var count=Field(p,s.Kind=="Group"?"이 묶음 반복 (회)":"이 동작 반복 (회)",s.EffectiveRepeats.ToString(),6);
        TextBox value=null;
        if(s.Kind=="Text"){value=Field(p,"입력할 문장",s.Value,4096);value.AcceptsReturn=true;value.TextWrapping=TextWrapping.Wrap;value.MinHeight=80;}
        if(s.Kind=="KeyTap"||s.Kind=="MouseTap"||s.Kind=="Wheel") {
            ActionButton(p,"키·마우스 바꾸기",()=>{
                var target=editRow;var macro=current;
                BeginInput(target.Step,input=>{
                    if(current!=macro)throw new InvalidOperationException("원래 매크로로 돌아와 변경하세요.");
                    int index=macro.Steps.IndexOf(target.Step);
                    if(index<0)throw new InvalidOperationException("변경할 동작이 삭제되었습니다.");
                    Mutate(()=>macro.Steps[index]=input,index);
                });
            });
        }
        if(s.Kind=="Move")ActionButton(p,"화면에서 위치 지정 · F10",()=>PickPoint(this,(point,window)=>{
            int index=current.Steps.IndexOf(editRow.Step);
            Mutate(()=>{current.Steps[index].X=point.X;current.Steps[index].Y=point.Y;},index);
        }));
        if(s.Kind=="Group") {
            foreach(var child in s.Children)Label(p,"• "+new Row{Step=child}.Summary+" · "+child.EffectiveRepeats+"회",true);
            ActionButton(p,"묶음 풀고 각각 편집",Ungroup);
        }
        var tools=new WrapPanel();p.Children.Add(tools);
        ActionButton(tools,"이 동작 시험",()=>F<Button>("Test").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        var more=new Expander{Header="추가 설정",Margin=new Thickness(0,12,0,0)};var extra=new StackPanel();more.Content=extra;p.Children.Add(more);
        var skip=Check(extra,"이 동작 건너뛰기",s.Skip);
        readEdit=()=>{
            var copy=editRow.Step.Copy();copy.Delay=ReadDelay(delay);copy.RepeatCount=Int(count,1,100000,"반복 횟수");
            if(value!=null)copy.Value=value.Text;
            copy.Skip=skip.IsChecked==true;return copy;
        };
        editStatus=new TextBlock{Text="✓ 자동 저장",FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray,Margin=new Thickness(0,10,0,0)};
        Panel("EditorFooter").Children.Add(editStatus);Watch(p,()=>DraftChanged(false));
    }
    void SettingsPanel() {
        readSettings=null;settingsDirty=false;
        var p=Panel("Settings");p.Children.Clear();Panel("SettingsFooter").Children.Clear();
        var macro=current;
        var name=Field(p,"매크로 이름",macro.Name,100);
        var forever=Check(p,"멈출 때까지 계속 반복",macro.Repeats==0);
        var repeat=Field(p,"전체 작업 반복 (회)",Math.Max(1,macro.Repeats).ToString(),6);
        repeat.IsEnabled=forever.IsChecked!=true;
        forever.Checked+=(a,b)=>repeat.IsEnabled=false;forever.Unchecked+=(a,b)=>repeat.IsEnabled=true;
        var gap=Field(p,"전체 반복 사이 대기 (초)",(macro.Interval/1000m).ToString("0.###",CultureInfo.InvariantCulture),10);
        ActionButton(p,randomOn?"딜레이 랜덤 · 켜짐":"딜레이 랜덤 · 꺼짐",RandomSettings);
        var advanced=new StackPanel();
        p.Children.Add(new Expander{Header="고급 설정",Content=advanced,Margin=new Thickness(0,14,0,4)});
        var modes=new[]{"Both","Mouse","Keyboard"};
        var mode=Combo(advanced,"녹화·실행 입력 범위",new[]{"마우스 + 키보드","마우스만","키보드만"},Array.IndexOf(modes,macro.Input));
        var paths=Check(advanced,"마우스 이동 경로도 녹화",macro.Paths);
        var speed=Field(advanced,"실행 속도 (배)",macro.Speed.ToString(CultureInfo.InvariantCulture),8);
        var process=Field(advanced,"대상 앱 이름 (비우면 모든 앱)",macro.Process,100);
        ActionButton(advanced,"화면에서 대상 앱 선택",()=>PickPoint(this,(point,window)=>process.Text=Native.App(window)));
        var relative=Check(advanced,"대상 창 기준으로 좌표 기록",macro.WindowCoords);
        relative.IsEnabled=macro.Steps.Count==0;
        relative.ToolTip="좌표 기준은 녹화 전에 정합니다. 기존 매크로에서는 좌표가 바뀌지 않도록 고정합니다.";
        var csv=Field(advanced,"반복 데이터 CSV (선택)",macro.Csv,2048);
        ActionButton(advanced,"CSV 선택",()=>{var d=new OpenFileDialog{Filter="CSV|*.csv"};if(d.ShowDialog(this)==true)csv.Text=d.FileName;});
        var schedule=Field(advanced,"예약 (09:30 또는 +15분은 +15)",macro.Schedule,12);
        Label(advanced,"예약은 LoopDesk가 대기 중이고 대상 앱이 앞에 열려 있을 때 실행됩니다.",true);
        readSettings=()=>{
            string n=name.Text.Trim();
            if(n.Length==0)throw new InvalidOperationException("매크로 이름을 입력하세요.");
            if(profile.Macros.Any(m=>m!=macro&&m.Name==n))throw new InvalidOperationException("같은 매크로 이름이 있습니다.");
            int count=forever.IsChecked==true?0:Int(repeat,1,100000,"전체 반복");
            int interval=ReadDelay(gap);double sp;
            if(!double.TryParse(speed.Text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out sp)||sp<.1||sp>8)throw new InvalidOperationException("실행 속도는 0.1~8배입니다.");
            string sc=schedule.Text.Trim();
            if(sc!=""&&(!IsSchedule(sc)||process.Text.Trim()==""||count==0))throw new InvalidOperationException("예약 형식, 대상 앱, 유한 반복 횟수를 확인하세요.");
            macro.Name=n;macro.Repeats=count;macro.Interval=interval;macro.Speed=sp;macro.Input=modes[mode.SelectedIndex];
            macro.Paths=paths.IsChecked==true;macro.Process=process.Text.Trim();macro.WindowCoords=relative.IsChecked==true;macro.Csv=csv.Text.Trim();
            if(macro.Schedule!=sc){macro.Schedule=sc;scheduled.Remove(macro.Id);}
        };
        settingsStatus=new TextBlock{Text="✓ 자동 저장",FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray};
        Panel("SettingsFooter").Children.Add(settingsStatus);Watch(p,()=>DraftChanged(true));
    }
    void BeginInput(Step existing,Action<Step> apply) {
        if(Busy||!FlushDrafts())return;
        CloseInput();capturing=true;Unregister();
        inputPicker=new InputPicker(this,existing,PickPoint,step=>{
            if(!FlushDrafts())throw new InvalidOperationException("오른쪽 입력값을 먼저 확인하세요.");
            if(apply!=null)apply(step);else InsertStep(step);
        },CloseInput);
        F<ContentControl>("InputContent").Content=inputPicker;F<Border>("InputDock").Visibility=Visibility.Visible;
        Note("키를 선택하고 ‘목록에 추가’를 누르세요. 입력 선택 중에는 실행 단축키가 잠시 대기합니다.");
    }
    void CloseInput() {
        if(inputPicker==null)return;
        inputPicker.Detach();inputPicker=null;F<ContentControl>("InputContent").Content=null;F<Border>("InputDock").Visibility=Visibility.Collapsed;
        capturing=false;Register();Buttons();
    }
    void MenuCommand(ItemsControl menu,string title,string control) {
        var b=F<Button>(control);var item=new MenuItem{Header=title,IsEnabled=b.IsEnabled};
        item.Click+=(a,e)=>b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));menu.Items.Add(item);
    }
    void EditMenu(Button anchor) {
        var menu=new ContextMenu{PlacementTarget=anchor,Placement=PlacementMode.Bottom,FontFamily=FontFamily,FontSize=14};
        FillEditMenu(menu);menu.IsOpen=true;
    }
    void FillEditMenu(ItemsControl menu) {
        MenuCommand(menu,"선택한 동작 복제","CopyStep");MenuCommand(menu,"선택한 동작 삭제","DeleteStep");
        AddMenuItem(menu,"선택한 동작 묶어서 반복…",()=>Safe(GroupSelection));
        MenuCommand(menu,"위로 이동","MoveUp");MenuCommand(menu,"아래로 이동","MoveDown");
        menu.Items.Add(new Separator());
        MenuCommand(menu,"되돌리기 · Ctrl+Z","Undo");
        var redoItem=new MenuItem{Header="다시 실행 · Ctrl+Y",IsEnabled=!Busy&&redo.ContainsKey(current.Id)&&redo[current.Id].Count>0};
        redoItem.Click+=(a,b)=>Safe(Redo);menu.Items.Add(redoItem);
        menu.Items.Add(new Separator());
        MenuCommand(menu,"선택한 동작만 시험","Test");MenuCommand(menu,"클릭 위치 확인","Preview");
        MenuCommand(menu,"선택한 구간 다시 녹화","ReplaceSteps");
    }
    void ManageMenu() {
        var menu=new ContextMenu{PlacementTarget=F<Button>("Manage"),Placement=PlacementMode.Bottom,FontFamily=FontFamily,FontSize=14};
        MenuCommand(menu,"현재 매크로 복제","Duplicate");MenuCommand(menu,"현재 매크로 삭제","DeleteMacro");
        MenuCommand(menu,"매크로 가져오기","Import");MenuCommand(menu,"현재 매크로 내보내기","Export");
        menu.Items.Add(new Separator());
        AddMenuItem(menu,"모든 매크로 백업",()=>Safe(Backup));
        AddMenuItem(menu,"백업에서 복원…",()=>Safe(Restore));
        AddMenuItem(menu,"실행 내역 / 오류 확인",ShowHistory);
        var startup=new MenuItem{Header="Windows 로그인 시 자동 대기",IsCheckable=true,IsChecked=!headless&&AutoStartEnabled()};
        startup.Click+=(a,b)=>Safe(()=>SetAutoStart(startup.IsChecked));menu.Items.Add(startup);
        AddMenuItem(menu,"완전히 종료",()=>{closing=true;Close();});
        menu.IsOpen=true;
    }
    void Backup() {
        var d=new SaveFileDialog{Filter="LoopDesk 전체 백업|*.loopdesk-backup",FileName="LoopDesk-"+DateTime.Now.ToString("yyyyMMdd-HHmm")};
        if(d.ShowDialog(this)==true){Storage.Write(d.FileName,profile);Note("모든 매크로와 설정을 백업했습니다.");}
    }
    void Restore() {
        if(Busy)return;
        var d=new OpenFileDialog{Filter="LoopDesk 전체 백업|*.loopdesk-backup;*.bak"};
        if(d.ShowDialog(this)!=true)return;
        var restored=Storage.Read<Profile>(d.FileName);if(restored.Macros==null||restored.Macros.Count==0)throw new InvalidDataException("비어 있는 백업입니다.");
        foreach(var m in restored.Macros)Storage.Validate(m);
        if(MessageBox.Show(this,"현재 목록을 자동 백업한 뒤 선택한 백업으로 복원할까요?","백업 복원",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
        Storage.Write(Storage.Library+".before-restore.bak",profile);
        CloseInput();profile=restored;current=profile.Macros.FirstOrDefault(m=>m.Id==profile.SelectedMacro)??profile.Macros[0];
        if(profile.CommonKeys==null||profile.CommonKeys.Length!=6)profile.CommonKeys=DefaultKeys.ToArray();
        undo.Clear();redo.Clear();randomOn=!profile.RandomOff;globalRandomRange=ReadGlobalRange();Save();Refresh();Register();Note("백업을 복원했습니다.");
    }
    void Redo() {
        Stack<List<Step>> stack;if(!redo.TryGetValue(current.Id,out stack)||stack.Count==0)return;
        Snapshot(current);current.Steps=stack.Pop();Changed();Note("다시 적용했습니다.");
    }
    void GroupSelection() {
        var selection=Selection();
        if(selection.Length<2||!IsContiguous(selection))throw new InvalidOperationException("이어진 동작을 두 개 이상 선택하세요. Shift를 누른 채 마지막 동작을 선택하면 됩니다.");
        if(current.Steps.Any(s=>s.Then>0||s.Else>0))throw new InvalidOperationException("조건 이동이 있는 매크로는 묶기 전에 조건을 해제하세요.");
        int first=selection[0]-1;var children=selection.Select(n=>current.Steps[n-1].Copy()).ToList();
        var group=new Step{Kind="Group",Value="동작 묶음",Children=children,RepeatCount=10};
        Mutate(()=>{current.Steps.RemoveRange(first,children.Count);current.Steps.Insert(first,group);},first);
        F<TabControl>("Tabs").SelectedIndex=1;Note("선택한 동작을 순서대로 10회 반복합니다. 오른쪽에서 횟수를 바꿀 수 있어요.");
    }
    void Ungroup() {
        var row=grid.SelectedItem as Row;if(row==null||row.Step.Kind!="Group")return;
        if(row.Step.Then>0||row.Step.Else>0)throw new InvalidOperationException("조건 이동을 먼저 해제하세요.");
        var children=row.Step.Children.Select(s=>s.Copy()).ToList();
        // Ungrouping edits the body once, rather than expanding potentially 100,000 repeats.
        Mutate(()=>{current.Steps.RemoveAt(row.Number-1);current.Steps.InsertRange(row.Number-1,children);},row.Number-1);
        Note("묶음 반복을 해제했습니다. 안의 동작을 각각 수정할 수 있습니다.");
    }
    void ShowRunHint(string text) {
        if(headless)return;
        if(runHint==null) {
            runHintText=new TextBlock{Foreground=Brushes.White,FontSize=14,TextWrapping=TextWrapping.Wrap,MaxWidth=450};
            runHint=new Window{WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,SizeToContent=SizeToContent.WidthAndHeight,
                ShowActivated=false,ShowInTaskbar=false,Topmost=true,IsHitTestVisible=false,Left=SystemParameters.WorkArea.Left+18,Top=SystemParameters.WorkArea.Top+18,
                Content=new Border{Background=new SolidColorBrush(Color.FromRgb(23,47,61)),Padding=new Thickness(16,10,16,10),Child=runHintText}};
            runHint.SourceInitialized+=(a,b)=>{
                var handle=new System.Windows.Interop.WindowInteropHelper(runHint).Handle;
                Native.SetWindowLong(handle,-20,Native.GetWindowLong(handle,-20)|0x08000000|0x20|0x80);
            };
            runHintText.Text=text;runHint.Show();
        } else runHintText.Text=text;
    }
    void CloseRunHint(){if(runHint!=null){runHint.Close();runHint=null;runHintText=null;}}
}
}
