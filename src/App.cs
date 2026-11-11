using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace LoopDesk {
internal sealed class Row {
    public Step Step;
    public int Number {get;set;}
    public string Action {get{return (Step.Skip?"제외 · ":"")+Step.Label;}}
    public string Detail {get{return Step.EffectiveRepeats>1?Step.Detail+"  ×"+Step.EffectiveRepeats:Step.Detail;}}
    public string Delay {get{return (Step.Delay/1000m).ToString("0.###",CultureInfo.InvariantCulture)+"초";}}
    public string Repeat {get{return Step.EffectiveRepeats+"회";}}
    public string Summary {get {
        string content;
        if(Step.Kind=="KeyTap")content=Step.Detail.Replace(" · 한 번"," 입력");
        else if(Step.Kind=="Wait")content="기다리기";
        else if(Step.Kind=="Text")content="문장 입력 · "+Step.Value;
        else if(Step.Kind=="Group")content=(String.IsNullOrWhiteSpace(Step.Value)?"동작 묶음":Step.Value)+" · "+Step.Children.Count+"개 동작";
        else if(Step.Kind=="KeyDown"||Step.Kind=="KeyUp")content=Step.Detail+(Step.Kind=="KeyDown"?" 누르기":" 떼기");
        else if(Step.Kind=="MouseTap"||Step.Kind=="Wheel"&&Step.Value=="Manual")content=Step.Detail;
        else content=Step.Label+(String.IsNullOrWhiteSpace(Step.Detail)?"":" · "+Step.Detail);
        return (Step.Skip?"건너뜀 · ":"")+content;
    }}
}
internal sealed partial class Desk : Window {
    readonly Grid root;
    Profile profile;
    Macro current;
    ListBox library; DataGrid grid;
    readonly Dictionary<string,Stack<List<Step>>> undo=new Dictionary<string,Stack<List<Step>>>();
    readonly Dictionary<int,Tuple<Macro,int>> hotkeys=new Dictionary<int,Tuple<Macro,int>>();
    readonly Dictionary<string,string> keyErrors=new Dictionary<string,string>();
    static readonly string[] DefaultKeys={"F9","F11","F6","F7","F10",""};
    static readonly string[] KeyLabels={"실행","실행 정지","녹화 시작","녹화 종료","일시정지 / 이어서","랜덤 딜레이 전환"};
    readonly Dictionary<string,DateTime> scheduled=new Dictionary<string,DateTime>();
    readonly List<string> logs=new List<string>();
    IntPtr hwnd;
    Recorder recorder;
    CancellationTokenSource run;
    ManualResetEventSlim gate;
    Macro active;
    int replaceStart=-1,replaceEnd=-1,serial;
    bool refreshing,headless,pending,paused,closing,capturing,startingRecording;
    volatile bool randomOn;
    long progressTime;
    Forms.NotifyIcon tray;
    DispatcherTimer scheduler;
    string BusyLabel {get{return pending?"시작 대기":recorder!=null?"녹화 중":run!=null?(paused?"일시정지":"실행 중"):"준비됨";}}
    bool Busy {get{return pending || recorder!=null || run!=null;}}
    T F<T>(string n) where T:class {return (T)root.FindName(n);}
    TextBlock Text(string n) {return F<TextBlock>(n);}
    StackPanel Panel(string n) {return F<StackPanel>(n);}
    void On(string n,Action a) {F<Button>(n).Click+=(s,e)=>Safe(a);}
    void Safe(Action a) {try {if(!FlushDrafts())return;a();} catch(Exception e) {Note(e.Message,true);}}
    internal Desk(bool test,bool verifyTray=false) {
        headless=test;
        Title="LoopDesk · 매크로 작업실";
        Width=Math.Min(1220,SystemParameters.WorkArea.Width-24);Height=Math.Min(880,SystemParameters.WorkArea.Height-24);MinWidth=980;MinHeight=680;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        FontFamily=new FontFamily("Malgun Gothic, Segoe UI");FontSize=14;Background=Brushes.White;
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Desk.xaml")) root=(Grid)XamlReader.Load(stream);
        Content=root;
        library=F<ListBox>("Library");grid=F<DataGrid>("Steps");
        root.SizeChanged+=(s,e)=>FitWorkspace(e.NewSize.Width);
        grid.SizeChanged+=(s,e)=>FitColumns(e.NewSize.Width);
        profile=new Profile();
        if(!test) {
            Directory.CreateDirectory(Storage.Home);
            if(File.Exists(Storage.Library)) {
                try {
                    var loaded=Storage.Read<Profile>(Storage.Library);
                    foreach(var m in loaded.Macros)Storage.Validate(m);
                    profile=loaded;
                } catch(Exception e) {Note("저장된 목록을 열 수 없습니다: "+e.Message,true);}
            }
            var lp=Path.Combine(Storage.Home,"run.log");
            if(!profile.LogOff && File.Exists(lp)) logs.AddRange(File.ReadLines(lp).Reverse().Take(80));
        }
        if(profile.Macros.Count==0) profile.Macros.Add(new Macro{Name="첫 번째 매크로"});
        current=profile.Macros.FirstOrDefault(m=>m.Id==profile.SelectedMacro)??profile.Macros[0];
        if(profile.CommonKeys==null || profile.CommonKeys.Length!=6)
            profile.CommonKeys=DefaultKeys.Select(k=>profile.Macros.Any(m=>m.Keys.Contains(k))?"":k).ToArray();
        randomOn=!profile.RandomOff && (ReadGlobalRange()!=null || profile.Macros.Any(m=>m.Steps.Any(s=>s.RandomDelay)));
        library.SelectionChanged+=(s,e)=>{if(!refreshing && library.SelectedItem is Macro){
            var next=(Macro)library.SelectedItem;
            if(!FlushDrafts()){refreshing=true;library.SelectedItem=current;refreshing=false;return;}
            CloseInput();current=next;Save();Refresh();
        }};
        grid.SelectionChanged+=(s,e)=>{if(!refreshing) {
            var next=grid.SelectedItem as Row;
            if(!FlushDrafts()){refreshing=true;grid.SelectedItem=editRow;refreshing=false;return;}
            if(next!=null&&grid.SelectedItem!=next) {
                refreshing=true;grid.SelectedItem=grid.Items.Cast<Row>().FirstOrDefault(r=>r.Step==next.Step);refreshing=false;
            }
            EditPanel();Buttons();F<TabControl>("Tabs").SelectedIndex=1;
        }};
        grid.MouseDoubleClick+=(s,e)=>{if(grid.SelectedItem!=null)F<TabControl>("Tabs").SelectedIndex=1;};
        F<TextBox>("Search").TextChanged+=(s,e)=>RefreshLibrary();
        On("AddMacro",()=>{CloseInput();current=new Macro{Name=Unique("새 매크로")};profile.Macros.Add(current);Changed();});
        On("Duplicate",Duplicate);
        On("DeleteMacro",()=>{
            if(MessageBox.Show(this,current.Name+" 매크로를 삭제할까요?","매크로 삭제",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            profile.Macros.Remove(current);if(profile.Macros.Count==0)profile.Macros.Add(new Macro());
            current=profile.Macros[0];Changed();Register();
        });
        On("Import",Import); On("Export",Export);
        On("Record",()=>StartRecording(current,false,-1,-1));
        On("ReplaceSteps",()=>{var r=Selection();if(r.Length>0)StartRecording(current,false,r[0]-1,r.Last()-1);});
        On("Play",()=>StartPlay(current,false,-1,-1));
        On("Test",()=>{var r=Selection();if(r.Length>0)StartPlay(current,false,r[0]-1,r.Last()-1);});
        On("Preview",Preview);
        grid.ContextMenu=new ContextMenu();
        grid.ContextMenuOpening+=(s,e)=>{if(grid.SelectedItem==null||Busy)e.Handled=true;else OpenRowMenu();};
        On("Pause",TogglePause);On("Stop",()=>Stop(false));
        On("EndRecord",()=>{if(recorder!=null)FinishRecording(false);else if(run==null)Stop(false);});
        On("Random",RandomSettings);
        On("History",ShowHistory);
        On("AddStep",ShowAddMenu);
        On("CopyStep",()=>{var rows=Selection();int index=rows.Last();var copies=rows.Select(n=>current.Steps[n-1].Copy()).ToList();Mutate(()=>current.Steps.InsertRange(index,copies),index);});
        On("DeleteStep",()=>{var selected=Selection();Mutate(()=>{foreach(int n in selected.Reverse()) current.Steps.RemoveAt(n-1);},Math.Min(selected[0]-1,current.Steps.Count-1));});
        On("MoveUp",()=>MoveRow(-1));On("MoveDown",()=>MoveRow(1));On("Undo",Undo);
        On("Help",ShowHelp);InitEase();
        SourceInitialized+=(s,e)=>{
            if(test&&!verifyTray)return;
            var helper=new WindowInteropHelper(this);hwnd=helper.Handle;
            HwndSource.FromHwnd(hwnd).AddHook(HandleMessage);
            Register();Tray();HotkeyPanel();Buttons();
            scheduler=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};scheduler.Tick+=(a,b)=>Safe(Tick);scheduler.Start();
        };
        Closing+=(s,e)=>{
            if(headless&&!verifyTray)return;
            if(!FlushDrafts()){e.Cancel=true;return;}
            CloseInput();
            if(!closing){e.Cancel=true;Hide();Balloon("단축키 대기 중","창을 닫아도 실행 키를 사용할 수 있어요. 완전 종료는 알림 영역 메뉴에서 선택하세요.");return;}
            CloseRunHint();
            if(run!=null){e.Cancel=true;Stop(false);return;}
            serial++;pending=false;
            if(recorder!=null)FinishRecording(false);
            if(scheduler!=null)scheduler.Stop();
            Unregister();
            if(tray!=null)tray.Dispose();
        };
        Refresh();
    }
    void Note(string message,bool error=false) {
        Text("Message").Text=message;
        Text("Message").Foreground=error?Brushes.DarkRed:new SolidColorBrush(Color.FromRgb(52,73,97));
    }
    void FitColumns(double width) {
        grid.Columns[1].Width=new DataGridLength(Math.Max(130,width-168));
    }
    void FitWorkspace(double width) {
        F<Grid>("Workspace").ColumnDefinitions[0].Width=new GridLength(width<1100?145:174);
    }
    void AddLog(string message) {
        if(profile.LogOff)return;
        string line=DateTime.Now.ToString("MM-dd HH:mm:ss")+"  "+message.Replace("\r"," ").Replace("\n"," ");
        logs.Insert(0,line);if(logs.Count>80)logs.RemoveAt(80);
        if(!headless)try {File.WriteAllLines(Path.Combine(Storage.Home,"run.log"),logs.AsEnumerable().Reverse(),Encoding.UTF8);}catch{}
    }
    void Save() {profile.SelectedMacro=current.Id;if(!headless)Storage.Write(Storage.Library,profile);}
    void Changed(int selection=-1) {Save();Refresh(selection);}
    void RefreshLibrary() {
        refreshing=true;
        string q=F<TextBox>("Search").Text.Trim();
        library.ItemsSource=profile.Macros.Where(m=>m.Name.IndexOf(q,StringComparison.CurrentCultureIgnoreCase)>=0).ToList();
        library.SelectedItem=current;refreshing=false;
    }
    void Refresh(int selection=-1) {
        RefreshLibrary();refreshing=true;
        Text("Title").Text=current.Name;Text("Title").ToolTip=current.Name;
        Text("Count").Text=current.Steps.Count==0?"녹화 / 직접 입력 중 편한 방법으로 시작하세요.":current.Steps.Count.ToString("N0")+"개 동작 · 선택하면 오른쪽에서 수정";
        F<StackPanel>("EmptyState").Visibility=current.Steps.Count==0?Visibility.Visible:Visibility.Collapsed;
        grid.ItemsSource=current.Steps.Select((s,i)=>new Row{Step=s,Number=i+1}).ToList();
        if(selection>=0 && grid.Items.Count>0) {grid.SelectedIndex=Math.Min(grid.Items.Count-1,selection);grid.ScrollIntoView(grid.SelectedItem);}
        refreshing=false;SettingsPanel();EditPanel();HotkeyPanel();Buttons();
    }
    void Buttons() {
        var row=grid.SelectedItem as Row;
        var select=row!=null;var editable=!Busy;
        foreach(string name in new[]{"Record","AddMacro","Duplicate","DeleteMacro","Import","Export","AddStep","Search","Library","Tabs"})
            F<Control>(name).IsEnabled=editable;
        F<TabControl>("Tabs").IsEnabled=true;
        F<Button>("DirectInput").IsEnabled=editable;F<Button>("EditTools").IsEnabled=editable;F<Button>("Manage").IsEnabled=editable;
        F<Button>("Pause").Visibility=run!=null?Visibility.Visible:Visibility.Collapsed;
        F<Button>("Stop").Visibility=run!=null||(pending&&!startingRecording)?Visibility.Visible:Visibility.Collapsed;
        F<Button>("EndRecord").Visibility=recorder!=null||(pending&&startingRecording)?Visibility.Visible:Visibility.Collapsed;
        F<Button>("Record").Visibility=recorder!=null?Visibility.Collapsed:Visibility.Visible;
        foreach(string name in new[]{"Settings","Editor","Hotkeys","SettingsFooter","EditorFooter","HotkeysFooter"})Panel(name).IsEnabled=editable;
        F<Button>("Play").IsEnabled=editable&&current.Steps.Count>0;
        foreach(string name in new[]{"Test","Preview","CopyStep","DeleteStep","ReplaceSteps"})F<Button>(name).IsEnabled=editable&&select;
        F<Button>("MoveUp").IsEnabled=editable&&select&&row.Number>1;
        F<Button>("MoveDown").IsEnabled=editable&&select&&row.Number<current.Steps.Count;
        F<Button>("Undo").IsEnabled=editable&&undo.ContainsKey(current.Id)&&undo[current.Id].Count>0;
        F<Button>("Pause").IsEnabled=run!=null;
        F<Button>("Stop").IsEnabled=run!=null || (pending&&!startingRecording);
        F<Button>("EndRecord").IsEnabled=recorder!=null || (pending&&startingRecording);
        ButtonKey("Record","●  녹화",2);
        ButtonKey("EndRecord","녹화 종료",3);
        ButtonKey("Play","▶  실행",0);
        ButtonKey("Pause",paused?"이어서 실행":"일시정지",4);
        ButtonKey("Stop","■  실행 정지",1);
        ButtonKey("Random",randomOn?"딜레이 랜덤 · 켜짐":"딜레이 랜덤 · 꺼짐",5);
        Text("State").Text="●  "+BusyLabel;
        Text("EmergencyLabel").Text="긴급 정지  "+profile.Emergency;
        if(tray!=null)tray.Text="LoopDesk · "+BusyLabel;
    }
    void ButtonKey(string button,string caption,int action) {
        string common=profile.CommonKeys[action];
        string specific=action<4?(active??current).Keys[action]:null;
        var key=String.IsNullOrWhiteSpace(specific)?common:specific;
        F<Button>(button).Content=String.IsNullOrWhiteSpace(key)||keyErrors.ContainsKey(key)?caption:caption+" · "+key;
    }
    static void Label(Panel p,string t,bool small=false) {
        p.Children.Add(new TextBlock{Text=t,FontSize=small?12:13,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,small?4:9,0,4),
            Foreground=small?new SolidColorBrush(Color.FromRgb(82,100,122)):new SolidColorBrush(Color.FromRgb(23,37,59)),FontWeight=small?FontWeights.Normal:FontWeights.SemiBold});
    }
    static TextBox Field(Panel p,string name,string value,int max=256) {
        Label(p,name);
        var b=new TextBox{Text=value??"",MaxLength=max,HorizontalAlignment=HorizontalAlignment.Stretch,Margin=new Thickness(0,0,0,2)};
        System.Windows.Automation.AutomationProperties.SetName(b,name);
        p.Children.Add(b);return b;
    }
    Button ActionButton(Panel p,string caption,Action action,bool primary=false) {
        if(p==Panel("EditorFooter"))advancedCommit=action;
        var b=new Button{Content=caption,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,10,0,6)};
        if(primary)b.Style=(Style)root.Resources["Primary"];
        if(p==Panel("EditorFooter"))b.Click+=(s,e)=>{if(advancedDirty)Safe(()=>{});else Safe(action);};
        else b.Click+=(s,e)=>Safe(action);
        p.Children.Add(b);return b;
    }
    static CheckBox Check(Panel p,string caption,bool value) {
        var c=new CheckBox{Content=new TextBlock{Text=caption,TextWrapping=TextWrapping.Wrap},IsChecked=value};
        p.Children.Add(c);return c;
    }
    static ComboBox Combo(Panel p,string name,string[] options,int selected) {
        Label(p,name);var c=new ComboBox{ItemsSource=options,SelectedIndex=Math.Max(0,selected)};
        System.Windows.Automation.AutomationProperties.SetName(c,name);
        p.Children.Add(c);return c;
    }
    static int Int(TextBox b,int min,int max,string name) {
        int v;if(!int.TryParse(b.Text,out v)||v<min||v>max)throw new InvalidOperationException(name+" 값을 확인하세요 ("+min+"~"+max+").");return v;
    }
    void AdvancedEditPanel() {
        var p=Panel("Editor");p.Children.Clear();Panel("EditorFooter").Children.Clear();
        var row=grid.SelectedItem as Row;
        if(row==null) {Label(p,"동작을 선택하거나 + 동작을 눌러 추가하세요.",true);return;}
        var s=row.Step;Label(p,row.Number+"번 동작");
        var delay=Field(p,"매번 실행 전 대기 (초)",(s.Delay/1000m).ToString("0.000",CultureInfo.InvariantCulture),10);
        var repeats=Field(p,"이 동작 반복 (회)",s.EffectiveRepeats.ToString(),6);
        Label(p,"이 동작만 반복합니다. 매번 실행 전에 위 딜레이를 적용합니다.",true);
        var kinds=new[]{"Wait","Move","Text"}.Concat(new[]{s.Kind}).Distinct().ToArray();
        var kind=Combo(p,"동작 종류",kinds.Select(k=>new Step{Kind=k}.Label).ToArray(),Array.IndexOf(kinds,s.Kind));
        var details=new StackPanel();p.Children.Add(details);
        TextBox x=null,y=null,code=null,value=null,timeout=null,jump=null,otherwise=null;
        CheckBox click=null;
        Action build=()=>{
            details.Children.Clear();x=y=code=value=timeout=jump=otherwise=null;click=null;
            string k=kinds[kind.SelectedIndex];
            if(new[]{"Move","Down","Up","Wheel","HWheel","Click","Pixel"}.Contains(k)) {
                var coordinates=new StackPanel();
                details.Children.Add(new Expander{Header="상세 좌표",Content=coordinates,Margin=new Thickness(0,4,0,0)});
                x=Field(coordinates,"X 위치",s.X.ToString(),8);y=Field(coordinates,"Y 위치",s.Y.ToString(),8);
                var result=new TextBlock{Text="현재 좌표  "+s.X+", "+s.Y,TextWrapping=TextWrapping.Wrap};details.Children.Add(result);
                ActionButton(details,k=="Pixel"?"화면에서 위치와 색 가져오기":"화면에서 위치 지정 · F10",()=>PickPoint(this,(point,window)=>{
                    x.Text=point.X.ToString();y.Text=point.Y.ToString();result.Text="위치 지정 완료  ·  "+point.X+", "+point.Y;
                    var screen=point;
                    if(current.WindowCoords) {var bounds=Native.Client(window);screen.X+=bounds.X;screen.Y+=bounds.Y;}
                    var preview=PointPreview(screen);
                    int previewIndex=details.Children.IndexOf(result)+1;
                    var oldPreview=result.Tag as FrameworkElement;
                    if(oldPreview!=null)details.Children.Remove(oldPreview);
                    if(preview!=null)details.Children.Insert(previewIndex,preview);
                    result.Tag=preview;
                    if(k=="Pixel") {
                        using(var bitmap=Vision.Capture(new System.Drawing.Rectangle(screen.X,screen.Y,1,1))) {
                            var color=bitmap.GetPixel(0,0);value.Text=String.Format("#{0:X2}{1:X2}{2:X2}",color.R,color.G,color.B);
                        }
                    }
                }));
            }
            if(k=="Down"||k=="Up") code=Field(details,"버튼 번호 (1 왼쪽, 2 오른쪽, 3 가운데)",(s.Code==0?1:s.Code).ToString(),3);
            if(k=="Wheel"||k=="HWheel") code=Field(details,"휠 이동량",(s.Code==0?120:s.Code).ToString(),7);
            if(k=="KeyDown"||k=="KeyUp") {
                code=Field(details,"키 (입력란에서 키를 누르세요)",s.Code==0?"":KeyInterop.KeyFromVirtualKey(s.Code).ToString(),30);code.IsReadOnly=true;
                code.PreviewKeyDown+=(a,b)=>{var key=b.Key==Key.System?b.SystemKey:b.Key;if(KeyInterop.VirtualKeyFromKey(key)>0)code.Text=key.ToString();b.Handled=true;};
            }
            string caption=k=="Text"?"입력할 문장":k=="Image"?"찾을 이미지 파일":k=="Pixel"?"찾을 색상 (#RRGGBB)":k=="OCR"?"화면에 나타날 글자":k=="Element"?"요소 이름 또는 자동화 ID":k=="ElementText"?"요소 이름|입력할 문장":k=="Macro"?"실행할 매크로 이름":"";
            if(caption!="") {
                value=Field(details,caption,s.Value,4096);
                if(k=="Text"||k=="ElementText") {value.TextWrapping=TextWrapping.Wrap;value.AcceptsReturn=true;value.MinHeight=70;}
            }
            if(k=="Image") {
                ActionButton(details,"화면에서 버튼 그림 캡처",()=>PickPoint(this,(point,window)=>{
                    if(current.WindowCoords) {var bounds=Native.Client(window);point.X+=bounds.X;point.Y+=bounds.Y;}
                    var path=CaptureTemplate(point);if(path!=null)value.Text=path;
                }));
                ActionButton(details,"이미지 파일 선택",()=>{var d=new OpenFileDialog{Filter="이미지|*.png;*.jpg;*.bmp;*.jpeg"};if(d.ShowDialog(this)==true)value.Text=d.FileName;});
                click=Check(details,"찾으면 버튼 가운데 클릭",s.Kind=="Image"&&s.X==1);
            }
            if(k=="Image"||k=="Pixel")code=Field(details,"색상 오차 허용 (0~255)",(s.Kind==k?s.Code:0).ToString(),3);
            if(new[]{"Pixel","Image","OCR","Element","ElementText"}.Contains(k)) {
                timeout=Field(details,"최대 대기 시간 (초)",(s.Timeout/1000m).ToString(CultureInfo.InvariantCulture),10);
                jump=Field(details,"성공 시 이동할 단계 (0 = 다음)",s.Then.ToString(),6);
                otherwise=Field(details,"실패 시 이동할 단계 (0 = 정지)",s.Else.ToString(),6);
            }
            if(k=="Jump")jump=Field(details,"이동할 단계 번호",Math.Max(1,s.Then).ToString(),6);
            if(k=="Macro") {
                var names=profile.Macros.Where(m=>m!=current).Select(m=>m.Name).ToArray();
                if(names.Length>0) {
                    var target=Combo(details,"저장된 매크로",names,Array.IndexOf(names,s.Value));
                    target.SelectionChanged+=(a,b)=>value.Text=(string)target.SelectedItem;
                }
            }
            if(k=="Element"||k=="ElementText") ActionButton(details,"화면에서 "+(k=="Element"?"버튼":"입력칸")+" 선택",()=>PickPoint(this,(point,window)=>{
                if(current.WindowCoords) {var bounds=Native.Client(window);point.X+=bounds.X;point.Y+=bounds.Y;}
                var element=System.Windows.Automation.AutomationElement.FromPoint(new Point(point.X,point.Y));
                string name=element==null?"":element.Current.AutomationId;
                if(element!=null&&String.IsNullOrWhiteSpace(name))name=element.Current.Name;
                if(String.IsNullOrWhiteSpace(name)||name.Contains("|"))throw new InvalidOperationException("선택한 항목의 이름을 읽을 수 없습니다.");
                int bar=value.Text.IndexOf('|');
                value.Text=k=="Element"?name:name+"|"+(bar<0?"":value.Text.Substring(bar+1));
            }));
        };
        kind.SelectionChanged+=(a,b)=>build();build();
        ActionButton(Panel("EditorFooter"),"변경 저장",()=>{
            string selectedKind=kinds[kind.SelectedIndex];
            var edited=selectedKind==s.Kind?s.Copy():new Step{Kind=selectedKind,Skip=s.Skip,RandomDelay=s.RandomDelay,RandomMin=s.RandomMin,
                RandomMax=s.RandomMax,RandomDirection=s.RandomDirection,RandomWhileHeld=s.RandomWhileHeld};
            edited.Delay=ReadDelay(delay);
            edited.RepeatCount=Int(repeats,1,100000,"이 동작 반복 (회)");
            if(x!=null)edited.X=Int(x,-100000,100000,"X 위치");
            if(y!=null)edited.Y=Int(y,-100000,100000,"Y 위치");
            if(code!=null) {
                if(edited.Kind.StartsWith("Key")) {
                    Key key;if(!Enum.TryParse(code.Text,out key)||KeyInterop.VirtualKeyFromKey(key)==0)throw new InvalidOperationException("녹화하거나 키 입력란에서 키를 누르세요.");
                    edited.Code=KeyInterop.VirtualKeyFromKey(key);
                    if(edited.Code==s.Code) {edited.Scan=s.Scan;edited.Extended=s.Extended;}
                } else edited.Code=Int(code,edited.Kind=="Down"||edited.Kind=="Up"?1:edited.Kind.EndsWith("Wheel")?-32768:0,
                    edited.Kind=="Down"||edited.Kind=="Up"?5:edited.Kind.EndsWith("Wheel")?32767:255,"입력 값");
            }
            if(value!=null)edited.Value=value.Text;
            if(click!=null)edited.X=click.IsChecked==true?1:0;
            if(timeout!=null)edited.Timeout=ReadDelay(timeout);
            if(jump!=null)edited.Then=Int(jump,edited.Kind=="Jump"?1:0,current.Steps.Count,"이동 단계");
            if(otherwise!=null)edited.Else=Int(otherwise,0,current.Steps.Count,"실패 단계");
            Mutate(()=>current.Steps[row.Number-1]=edited,row.Number-1);Note("동작을 저장했습니다.");
        },true);
    }
    static string Seconds(int ms) {return (ms/1000m).ToString("0.000",CultureInfo.InvariantCulture);}
    static int ReadDelay(TextBox field) {
        decimal seconds;
        if((!Decimal.TryParse(field.Text,NumberStyles.AllowDecimalPoint,CultureInfo.CurrentCulture,out seconds) &&
            !Decimal.TryParse(field.Text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out seconds)) || seconds<0 || seconds>86400)
            throw new InvalidOperationException("딜레이를 0~86400초 사이로 입력하세요.");
        return (int)Math.Round(seconds*1000,MidpointRounding.AwayFromZero);
    }
    int[] Selection() {return grid.SelectedItems.Cast<Row>().Select(r=>r.Number).OrderBy(i=>i).ToArray();}
    void MoveRow(int delta) {
        var row=(Row)grid.SelectedItem;int at=row.Number-1,to=at+delta;
        Mutate(()=>{current.Steps.RemoveAt(at);current.Steps.Insert(to,row.Step);},to);
    }
    void Snapshot(Macro m) {
        Stack<List<Step>> stack;
        if(!undo.TryGetValue(m.Id,out stack))undo[m.Id]=stack=new Stack<List<Step>>();
        if(stack.Count>=12)undo[m.Id]=stack=new Stack<List<Step>>(stack.Take(11).Reverse());
        stack.Push(m.Steps.Select(s=>s.Copy()).ToList());
    }
    void Mutate(Action change,int selection) {
        var old=current.Steps.ToArray();Snapshot(current);redo.Remove(current.Id);change();
        var previous=new HashSet<Step>(old);
        var locations=current.Steps.Select((s,i)=>new {s,i}).ToDictionary(item=>item.s,item=>item.i+1);
        foreach(var s in current.Steps) if(previous.Contains(s)) {
            if(s.Then>0 && s.Then<=old.Length)s.Then=Remap(locations,old[s.Then-1],s.Then);
            if(s.Else>0 && s.Else<=old.Length)s.Else=Remap(locations,old[s.Else-1],s.Else);
        }
        Changed(selection);
    }
    int Remap(Dictionary<Step,int> locations,Step step,int number) {int i;return locations.TryGetValue(step,out i)?i:Math.Min(number,current.Steps.Count);}
    void Undo() {
        Stack<List<Step>> stack;if(!redo.TryGetValue(current.Id,out stack))redo[current.Id]=stack=new Stack<List<Step>>();
        stack.Push(current.Steps.Select(s=>s.Copy()).ToList());current.Steps=undo[current.Id].Pop();Changed(grid.SelectedIndex);Note("마지막 동작 편집을 되돌렸습니다.");}
    string Unique(string proposed) {
        if(proposed.Length>88)proposed=proposed.Substring(0,88);
        string name=proposed;int i=2;
        while(profile.Macros.Any(m=>m.Name==name))name=proposed+" "+i++;
        return name;
    }
    void Duplicate() {
        var c=new Macro{Name=Unique(current.Name+" 복사"),Steps=current.Steps.Select(s=>s.Copy()).ToList(),Repeats=current.Repeats,Interval=current.Interval,Speed=current.Speed,
            Input=current.Input,Paths=current.Paths,Process=current.Process,WindowCoords=current.WindowCoords,Csv=current.Csv};
        current=c;profile.Macros.Add(c);Changed();
    }
    void Import() {
        var d=new OpenFileDialog{Filter="LoopDesk 매크로|*.loopdesk;*.json"};
        if(d.ShowDialog(this)!=true)return;
        if(new FileInfo(d.FileName).Length>90000000)throw new InvalidDataException("매크로 파일이 너무 큽니다.");
        var m=Storage.Read<Macro>(d.FileName);Storage.Validate(m);m.Id=Guid.NewGuid().ToString("N");
        m.Name=Unique(m.Name);m.Keys=new string[4];m.Schedule="";profile.Macros.Add(m);current=m;Changed();Note("매크로를 가져왔습니다. 단축키와 예약은 직접 설정하세요.");
    }
    void Export() {
        var d=new SaveFileDialog{Filter="LoopDesk 매크로|*.loopdesk",DefaultExt=".loopdesk",FileName="macro.loopdesk"};
        if(d.ShowDialog(this)==true) {Storage.Write(d.FileName,current);Note("매크로 파일을 저장했습니다.");}
    }
    static bool ParseKey(string text,out uint mods,out int vk) {
        mods=0;vk=0;if(String.IsNullOrWhiteSpace(text))return false;
        var fields=text.Split('+');Key key;
        for(int i=0;i<fields.Length-1;i++) {
            switch(fields[i]) {case "Ctrl":mods|=2;break;case "Alt":mods|=1;break;case "Shift":mods|=4;break;default:return false;}
        }
        string last=fields[fields.Length-1]=="Esc"?"Escape":fields[fields.Length-1];
        if(!Enum.TryParse(last,out key))return false;
        vk=KeyInterop.VirtualKeyFromKey(key);
        return vk>0 && vk!=123 && (mods!=0 || (vk>=112 && vk<=135));
    }
    void HotkeyPanel() {
        var p=Panel("Hotkeys");p.Children.Clear();Panel("HotkeysFooter").Children.Clear();
        Label(p,"공통 키는 선택된 매크로를 실행합니다. 실행 중에는 현재 작업을 조작합니다.",true);
        for(int i=0;i<6;i++) KeyRow(p,KeyLabels[i],profile.CommonKeys,i);
        KeyRow(p,"전체 긴급 정지",null,0);
        Label(p,"선택 매크로 전용 키");
        for(int i=0;i<4;i++) KeyRow(p,KeyLabels[i],current.Keys,i);
    }
    void KeyRow(Panel p,string label,string[] keys,int index) {
        string key=keys==null?profile.Emergency:keys[index];
        string status;
        if(String.IsNullOrWhiteSpace(key))status="미지정";
        else if(keyErrors.TryGetValue(key,out status))status=key+" · "+status;
        else status=key+(hwnd==IntPtr.Zero?"":" · 사용 가능");
        Label(p,label);
        var line=new DockPanel();
        var change=new Button{Content="키 변경",Margin=new Thickness(5,0,0,0)};
        DockPanel.SetDock(change,Dock.Right);line.Children.Add(change);
        line.Children.Add(new TextBlock{Text=status,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center});
        p.Children.Add(line);
        change.Click+=(a,b)=>Safe(()=>ChangeKey(keys,index,label,key));
    }
    void ChangeKey(string[] keys,int index,string label,string previous) {
        CloseInput();
        var instruction=new TextBlock{Text="원하는 키를 누르세요. Backspace는 해제, Esc는 취소입니다.",Margin=new Thickness(18),TextWrapping=TextWrapping.Wrap};
        var dialog=new Window{Owner=this,Title=label+" · 키 변경",Width=410,SizeToContent=SizeToContent.Height,Content=instruction,
            FontFamily=FontFamily,FontSize=FontSize,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false};
        string candidate=null;
        dialog.PreviewKeyDown+=(s,e)=>{
            e.Handled=true;
            if(e.Key==Key.Escape && Keyboard.Modifiers==ModifierKeys.None){dialog.Close();return;}
            if(e.Key==Key.Back && Keyboard.Modifiers==ModifierKeys.None && keys!=null){candidate="";dialog.Close();return;}
            var key=e.Key==Key.System?e.SystemKey:e.Key;
            if(new[]{Key.LeftCtrl,Key.RightCtrl,Key.LeftAlt,Key.RightAlt,Key.LeftShift,Key.RightShift,Key.LWin,Key.RWin}.Contains(key))return;
            var mod=Keyboard.Modifiers;
            string chord=((mod&ModifierKeys.Control)!=0?"Ctrl+":"")+((mod&ModifierKeys.Alt)!=0?"Alt+":"")+((mod&ModifierKeys.Shift)!=0?"Shift+":"")+key;
            uint mask;int vk;
            if((mod&ModifierKeys.Windows)!=0 || !ParseKey(chord,out mask,out vk)) {
                instruction.Text="단독 기능 키 또는 Ctrl·Alt·Shift 조합을 눌러주세요. Esc는 취소입니다.";return;
            }
            candidate=chord;dialog.Close();
        };
        capturing=true;Unregister();
        try {dialog.ShowDialog();}
        finally {capturing=false;if(candidate==null)Register();}
        if(candidate==null){HotkeyPanel();Buttons();return;}
        if(keys==null)profile.Emergency=candidate;else keys[index]=candidate;
        string error=Register(candidate);
        if(error!=null) {
            if(keys==null)profile.Emergency=previous;else keys[index]=previous;
            Register();HotkeyPanel();Buttons();
            MessageBox.Show(this,error,"단축키를 저장할 수 없습니다.",MessageBoxButton.OK,MessageBoxImage.Warning);return;
        }
        Save();HotkeyPanel();Buttons();Note(label+" 단축키를 저장했습니다.");
    }
    void Unregister() {
        foreach(int id in hotkeys.Keys)Native.UnregisterHotKey(hwnd,id);
        hotkeys.Clear();
    }
    string Register(string changedKey=null) {
        var all=new List<Tuple<Macro,int,string>>{Tuple.Create((Macro)null,6,profile.Emergency)};
        for(int i=0;i<6;i++)if(!String.IsNullOrWhiteSpace(profile.CommonKeys[i]))all.Add(Tuple.Create((Macro)null,i,profile.CommonKeys[i]));
        foreach(var m in profile.Macros)for(int i=0;i<4;i++)if(!String.IsNullOrWhiteSpace(m.Keys[i]))all.Add(Tuple.Create(m,i,m.Keys[i]));
        var seen=new Dictionary<long,string>();
        foreach(var a in all) {
            uint md;int vk;if(!ParseKey(a.Item3,out md,out vk))return "사용할 수 없는 단축키입니다: "+a.Item3;
            long chord=((long)md<<32)|(uint)vk;
            string owner=a.Item2==6?"긴급 정지":(a.Item1==null?"공통":a.Item1.Name)+" · "+KeyLabels[a.Item2];
            string other;
            if(seen.TryGetValue(chord,out other))return a.Item3+" 키가 겹칩니다. "+other+" / "+owner;
            seen.Add(chord,owner);
        }
        if(hwnd==IntPtr.Zero)return null;
        Unregister();keyErrors.Clear();
        string error=null;int seq=100;
        foreach(var a in all) {
            uint md;int vk;ParseKey(a.Item3,out md,out vk);int id=seq++;
            if(!Native.RegisterHotKey(hwnd,id,md|0x4000,(uint)vk)) {
                int code=System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                keyErrors[a.Item3]=code==1409?"다른 프로그램이 사용 중":"Windows 등록 실패 ("+code+")";
                if(changedKey==null || a.Item3==changedKey)error=a.Item3+" · "+keyErrors[a.Item3];
                continue;
            }
            hotkeys[id]=Tuple.Create(a.Item1,a.Item2);
        }
        if(error!=null)Note(error,true);
        return error;
    }
    IntPtr HandleMessage(IntPtr h,int msg,IntPtr wp,IntPtr lp,ref bool handled) {
        if(msg==0x312&&pointChoice!=null&&(wp.ToInt32()==80||wp.ToInt32()==81)) {
            handled=true;FinishPoint(wp.ToInt32()==80);return IntPtr.Zero;
        }
        Tuple<Macro,int> action;
        if(msg!=0x312||!hotkeys.TryGetValue(wp.ToInt32(),out action))return IntPtr.Zero;
        handled=true;
        Safe(()=>{
            var m=action.Item1??current;
            if(action.Item2==6)Stop(true);
            else if(action.Item2==5)ToggleRandom();
            else if(action.Item2==4)TogglePause();
            else if(action.Item2==0)StartPlay(m,true,-1,-1);
            else if(action.Item2==1 && (action.Item1==null||active==m) && (run!=null || pending&&!startingRecording))Stop(true);
            else if(action.Item2==2)StartRecording(m,true,-1,-1);
            else if(action.Item2==3 && (action.Item1==null||active==m) && (recorder!=null || pending&&startingRecording))Stop(true);
        });
        return IntPtr.Zero;
    }
    static uint ModState() {
        return (Native.GetAsyncKeyState(18)<0?1u:0u) | (Native.GetAsyncKeyState(17)<0?2u:0u) | (Native.GetAsyncKeyState(16)<0?4u:0u);
    }
    bool ControlKey(int vk) {
        uint mods=ModState();
        foreach(var key in profile.CommonKeys.Concat(active.Keys).Concat(new[]{profile.Emergency})) {
            uint md;int code;
            if(ParseKey(key,out md,out code)&&vk==code&&mods==md)return true;
        }
        return false;
    }
    void StartRecording(Macro m,bool hot,int first,int last) {
        if(Busy||!FlushDrafts())return;
        CloseInput();
        if(first>=0 && !IsContiguous(Selection())) {Note("이어진 범위의 동작을 선택하세요.",true);return;}
        if(!hot && m.Steps.Count>0 && first<0 && MessageBox.Show(this,"새 녹화가 끝나면 녹화 내용을 교체합니다. 편집 화면에서 되돌릴 수 있습니다.","새 녹화",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
        active=m;startingRecording=true;pending=true;Buttons();
        try {
            if(!hot)Hide();
            replaceStart=first;replaceEnd=last;
            recorder=new Recorder(m,hot?Native.GetForegroundWindow():IntPtr.Zero,hwnd,ControlKey);
            pending=false;recordedAt=DateTime.Now;Buttons();Note(m.Name+" 녹화 중");
            ShowRunHint("● 녹화 중 · "+m.Name+"\n종료 "+(String.IsNullOrWhiteSpace(m.Keys[3])?profile.CommonKeys[3]:m.Keys[3]));
            string stopKey=String.IsNullOrWhiteSpace(m.Keys[3])?profile.CommonKeys[3]:m.Keys[3];
            string target=String.IsNullOrWhiteSpace(m.Process)?"":m.Process+" 창에서 입력 · ";
            Balloon("녹화 중",target+(String.IsNullOrWhiteSpace(stopKey)?"알림 영역에서 종료":"종료 "+stopKey));
        } catch(Exception e) {
            pending=false;active=null;Buttons();Note(e.Message,true);
            if(!hot)ShowDesk();
            Balloon("녹화 실패",e.Message);
        }
    }
    void FinishRecording(bool shortcut) {
        CloseRunHint();var m=active;var captured=SimplifyRecording(recorder.Finish(shortcut));
        recorder=null;active=null;pending=false;
        try {
            if(captured.Count>0) {
                current=m;
                if(replaceStart>=0) {
                    int first=replaceStart,last=replaceEnd,number=captured.Count;
                    captured[0].Delay=m.Steps[first].Delay;
                    Snapshot(m);
                    m.Steps.RemoveRange(first,last-first+1);
                    m.Steps.InsertRange(first,captured);
                    foreach(var step in m.Steps) {
                        step.Then=ShiftBranch(step.Then,first,last,number);
                        step.Else=ShiftBranch(step.Else,first,last,number);
                    }
                    Changed(first);
                } else {
                    Snapshot(m);redo.Remove(m.Id);m.Steps=captured;Changed(0);
                }
                AddLog("녹화 완료 · "+m.Name+" · "+captured.Count+"개 동작");Note(captured.Count+"개 동작을 저장했습니다.");
            } else Note("녹화된 동작이 없습니다.");
        } catch(Exception e) {Note(e.Message,true);}
        finally {
            Buttons();
            if(!closing)ShowDesk();
        }
    }
    static int ShiftBranch(int number,int first,int last,int count) {if(number<=first)return number;if(number<=last+1)return first+1;return number+count-(last-first+1);}
    static bool IsContiguous(int[] selection) {return selection.Length>0 && selection.Last()-selection[0]+1==selection.Length;}
    async void StartPlay(Macro m,bool hot,int first,int last) {
        if(Busy||m.Steps.Count==0||!FlushDrafts())return;
        CloseInput();
        if(first>=0&&!IsContiguous(Selection())) {Note("이어진 범위의 동작을 선택하세요.",true);return;}
        int ticket=++serial;active=m;startingRecording=false;pending=true;failedStep=0;lastProgressStep=0;Buttons();
        try {
            Storage.Validate(m);
            if(!hot) {
                WindowState=WindowState.Minimized;
                for(int countdown=3;countdown>0;countdown--) {
                    if(ticket!=serial)return;
                    ShowRunHint(countdown+"초 후 실행 · 대상 창으로 전환하세요\n취소 "+profile.Emergency);
                    await Task.Delay(1000);
                }
            }
            if(ticket!=serial)return;
            if(Native.GetForegroundWindow()==hwnd)throw new InvalidOperationException("대상 창으로 전환한 뒤 실행하세요.");
            run=new CancellationTokenSource();gate=new ManualResetEventSlim(true);pending=false;paused=false;Buttons();
            if(globalRandomRange==null)globalRandomRange=ReadGlobalRange();
            failedStep=0;
            var player=new Player(profile.Macros.ToList(),run.Token,gate,Progress,hwnd,()=>randomOn,globalRandomRange);
            player.Repetition=(done,total)=>{stepRepetition=done;stepTotal=total;};
            AddLog("실행 시작 · "+m.Name);
            Balloon("실행 중",m.Name+" · 정지 "+profile.Emergency);
            await Task.Run(()=>player.Run(m,first,last));
            AddLog("실행 완료 · "+m.Name);Note(m.Name+" 실행 완료");
        } catch(OperationCanceledException) {AddLog("사용자가 정지 · "+m.Name);Note("실행을 정지했습니다.");}
        catch(Exception e) {AddLog("실행 실패 · "+m.Name+" · "+e.Message);Note(e.Message,true);Balloon("실행 실패",e.Message);failedStep=Math.Max(1,lastProgressStep);}
        finally {
            if(ticket==serial) {
                CloseRunHint();
                if(run!=null)run.Dispose();if(gate!=null)gate.Dispose();
                run=null;gate=null;active=null;paused=false;pending=false;Buttons();
                if(closing)Close();else if(failedStep>0){ShowDesk();grid.SelectedIndex=Math.Min(failedStep-1,grid.Items.Count-1);}
            }
        }
    }
    int stepRepetition,stepTotal,lastProgressStep;
    void Progress(int step,int cycle,string name) {
        if(active!=null&&active.Name==name)lastProgressStep=step;
        long now=DateTime.UtcNow.Ticks;
        if(now-Interlocked.Read(ref progressTime)<TimeSpan.TicksPerMillisecond*130)return;
        Interlocked.Exchange(ref progressTime,now);
        int repetition=stepRepetition,total=stepTotal;
        Dispatcher.BeginInvoke(new Action(()=>{
            if(run!=null&&!paused) {
                Text("State").Text="● 실행 중 · 전체 "+cycle+"회차";
                string detail=name+" · "+step+"번 동작"+(total>1?" · 동작 반복 "+repetition+"/"+total:"");
                Note(detail);
                ShowRunHint("▶ "+detail+"\n전체 "+cycle+"회차 · 일시정지 "+profile.CommonKeys[4]+" · 정지 "+profile.CommonKeys[1]);
            }
        }));
    }
    void Stop(bool fromShortcut) {
        if(recorder!=null)FinishRecording(fromShortcut);
        else if(run!=null) {run.Cancel();Note("실행을 정지하는 중입니다.");}
        else if(pending) {CloseRunHint();serial++;pending=false;active=null;Buttons();Note("시작을 취소했습니다.");}
    }
    void TogglePause() {
        if(run==null)return;
        paused=!paused;if(paused)gate.Reset();else gate.Set();Buttons();if(paused)ShowRunHint("Ⅱ 일시정지 · "+profile.CommonKeys[4]+" 다시 누르면 이어서 실행\n정지 "+profile.CommonKeys[1]);Note(paused?"일시정지 · 다시 누르면 계속합니다.":"이어서 실행 중");
    }
    void ToggleRandom() {
        if(ReadGlobalRange()==null && !profile.Macros.Any(m=>m.Steps.Any(s=>s.RandomDelay))) {
            profile.GlobalRandomMin=10;profile.GlobalRandomMax=50;globalRandomRange=new[]{10,50};
        }
        randomOn=!randomOn;profile.RandomOff=!randomOn;Save();Buttons();
        Note(randomOn?"저장한 랜덤 딜레이 설정을 사용합니다.":"기본 딜레이로 실행합니다.");
    }
    const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    static bool AutoStartEnabled() {
        using(var key=Registry.CurrentUser.OpenSubKey(RunKey))
            return key!=null && String.Equals(key.GetValue("LoopDesk") as string,AutoStartCommand(),StringComparison.OrdinalIgnoreCase);
    }
    static string AutoStartCommand() {return "\""+Assembly.GetExecutingAssembly().Location+"\" --background";}
    void SetAutoStart(bool enabled) {
        using(var key=Registry.CurrentUser.CreateSubKey(RunKey)) {
            if(enabled)key.SetValue("LoopDesk",AutoStartCommand());
            else key.DeleteValue("LoopDesk",false);
        }
    }
    void ShowHistory() {
        var panel=new DockPanel{Margin=new Thickness(14)};
        var controls=new WrapPanel();DockPanel.SetDock(controls,Dock.Top);panel.Children.Add(controls);
        var keep=Check(controls,"실행 내역 저장",!profile.LogOff);
        var content=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        Action show=()=>content.Text=logs.Count==0?"저장된 실행 내역이 없습니다.":String.Join(Environment.NewLine+Environment.NewLine,logs);
        keep.Checked+=(a,b)=>Safe(()=>{profile.LogOff=false;Save();});
        keep.Unchecked+=(a,b)=>Safe(()=>{profile.LogOff=true;Save();});
        ActionButton(controls,"내역 지우기",()=>{
            if(!headless)File.Delete(Path.Combine(Storage.Home,"run.log"));
            logs.Clear();show();
        });
        panel.Children.Add(content);show();
        new Window{Owner=this,Title="LoopDesk · 실행 내역",Width=620,Height=440,Content=panel,FontFamily=FontFamily,
            FontSize=FontSize,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false}.ShowDialog();
    }
    void Tray() {
        tray=new Forms.NotifyIcon{Icon=System.Drawing.SystemIcons.Application,Text="LoopDesk · 준비됨",Visible=true};
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("LoopDesk 열기",null,(s,e)=>Dispatcher.Invoke(new Action(ShowDesk)));
        menu.Items.Add("일시정지 / 재개",null,(s,e)=>Dispatcher.Invoke(new Action(TogglePause)));
        menu.Items.Add("녹화 / 실행 정지",null,(s,e)=>Dispatcher.Invoke(new Action(()=>Stop(false))));
        menu.Items.Add("완전히 종료",null,(s,e)=>Dispatcher.Invoke(new Action(()=>{closing=true;Close();})));
        tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>Dispatcher.Invoke(new Action(ShowDesk));
    }
    void Balloon(string title,string detail) {if(tray!=null)tray.ShowBalloonTip(1800,title,detail,Forms.ToolTipIcon.Info);}
    void ShowDesk() {Show();WindowState=WindowState.Normal;Activate();}
    internal void EndSession() {closing=true;Stop(false);}
    static bool IsSchedule(string text) {
        int n;DateTime t;
        return text.StartsWith("+")?int.TryParse(text.Substring(1),out n)&&n>=1&&n<=1440:
            DateTime.TryParseExact(text,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out t);
    }
    void Tick() {
        if(recorder!=null)ShowRunHint("● 녹화 중 · "+(DateTime.Now-recordedAt).ToString(@"mm\:ss")+"\n종료 "+(String.IsNullOrWhiteSpace(active.Keys[3])?profile.CommonKeys[3]:active.Keys[3]));
        if(Busy||capturing)return;
        var now=DateTime.Now;
        foreach(var m in profile.Macros) {
            if(String.IsNullOrWhiteSpace(m.Schedule)||m.Steps.Count==0||m.Repeats==0||String.IsNullOrWhiteSpace(m.Process)||!IsSchedule(m.Schedule))continue;
            DateTime due;bool interval=m.Schedule[0]=='+';
            if(!scheduled.TryGetValue(m.Id,out due)) {
                due=interval?now.AddMinutes(int.Parse(m.Schedule.Substring(1))):now.Date+DateTime.ParseExact(m.Schedule,"HH:mm",CultureInfo.InvariantCulture).TimeOfDay;
                if(!interval && due.AddMinutes(1)<now)due=due.AddDays(1);
                scheduled[m.Id]=due;
            }
            if(due>now)continue;
            scheduled[m.Id]=interval?now.AddMinutes(int.Parse(m.Schedule.Substring(1))):due.AddDays(1);
            if(!String.Equals(Native.App(Native.GetForegroundWindow()),m.Process,StringComparison.OrdinalIgnoreCase)) {
                AddLog("예약 건너뜀 · "+m.Name+" · 대상 앱이 활성화되어 있지 않음");continue;
            }
            StartPlay(m,true,-1,-1);break;
        }
    }
    async void Preview() {
        var selected=grid.SelectedItems.Cast<Row>().Select(r=>r.Step).Where(s=>new[]{"Click","Down","Up","Move","Wheel","Pixel"}.Contains(s.Kind)).Take(16).ToArray();
        if(selected.Length==0) {Note("좌표를 가진 마우스 동작을 선택하세요.");return;}
        var m=current;WindowState=WindowState.Minimized;await Task.Delay(1500);
        var marks=new List<Window>();
        try {
            var area=m.WindowCoords?Native.Client(Native.GetForegroundWindow()):new System.Drawing.Rectangle();
            var transform=PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice;
            for(int i=0;i<selected.Length;i++) {
                var pos=transform.Transform(new Point(selected[i].X+area.X,selected[i].Y+area.Y));
                var mark=new Window{WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=Brushes.Transparent,Width=38,Height=38,
                    WindowStartupLocation=WindowStartupLocation.Manual,Left=pos.X-19,Top=pos.Y-19,Topmost=true,ShowActivated=false,ShowInTaskbar=false,ResizeMode=ResizeMode.NoResize,
                    Content=new Border{CornerRadius=new CornerRadius(19),BorderThickness=new Thickness(3),BorderBrush=Brushes.MediumAquamarine,Background=new SolidColorBrush(Color.FromArgb(212,18,38,51)),
                        Child=new TextBlock{Text=(i+1).ToString(),Foreground=Brushes.White,FontWeight=FontWeights.Bold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}}};
                marks.Add(mark);mark.Show();
            }
            await Task.Delay(2000);
        } catch(Exception e) {Note(e.Message,true);}
        finally {foreach(var mark in marks)mark.Close();ShowDesk();}
    }
    void ShowHelp() {
        MessageBox.Show(this,"1. 단축키 탭에서 공통 키와 매크로 전용 키를 바꿀 수 있습니다.\n\n2. 기본 F6으로 녹화하고 F7로 저장합니다. 현재 화면에서 F9로 실행하고, F10으로 일시정지·재개하거나 F11로 실행을 끝냅니다.\n\n3. 키·마우스 입력을 누르면 아래에 108키 키보드가 펼쳐집니다. 키와 반복 횟수를 정하고 목록에 추가하세요. 동작을 선택하면 오른쪽에서 수정할 수 있으며 자동 저장됩니다.\n\n4. 창을 닫아도 단축키는 계속 작동합니다. 관리 메뉴에서 Windows 로그인 시 자동 대기를 켤 수 있습니다. 완전히 종료하려면 작업 표시줄 알림 영역의 LoopDesk 메뉴를 사용하세요.","LoopDesk 사용법",MessageBoxButton.OK,MessageBoxImage.Information);
    }
    internal async Task CheckTrayControls() {
        // The user's running copy may already own the default shortcuts.
        profile.CommonKeys=new[]{"Ctrl+Alt+Shift+F20","Ctrl+Alt+Shift+F21","","","Ctrl+Alt+Shift+F22",""};
        profile.Emergency="Ctrl+Alt+Shift+F23";
        new WindowInteropHelper(this).EnsureHandle();
        if(IsVisible || hwnd==IntPtr.Zero)throw new InvalidOperationException("Background launch did not create a hidden input window.");
        Show();
        current.Process="LoopDesk.RecordingFixture.NotRunning";
        F<Button>("Record").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(recorder==null || IsVisible)throw new InvalidOperationException("Recording button must start even before a target window is active.");
        Stop(false);
        if(!IsVisible)throw new InvalidOperationException("Ending a recording must restore the editor.");
        Hide();
        current.Process="";
        current.Steps.Add(new Step{Kind="Wait",Delay=2000});
        int playId=hotkeys.First(k=>k.Value.Item2==0).Key;
        int pauseId=hotkeys.First(k=>k.Value.Item2==4).Key;
        int stopId=hotkeys.First(k=>k.Value.Item2==1).Key;
        bool handled=false;
        HandleMessage(hwnd,0x312,(IntPtr)playId,IntPtr.Zero,ref handled);
        if(!handled || run==null || IsVisible)throw new InvalidOperationException("Hidden playback did not start.");
        HandleMessage(hwnd,0x312,(IntPtr)pauseId,IntPtr.Zero,ref handled);
        if(!paused)throw new InvalidOperationException("Pause shortcut was not dispatched.");
        HandleMessage(hwnd,0x312,(IntPtr)pauseId,IntPtr.Zero,ref handled);
        if(paused)throw new InvalidOperationException("Resume shortcut was not dispatched.");
        HandleMessage(hwnd,0x312,(IntPtr)stopId,IntPtr.Zero,ref handled);
        for(int i=0;i<40 && run!=null;i++)await Task.Delay(25);
        if(run!=null)throw new InvalidOperationException("Stop shortcut did not cancel playback.");
        Show();Close();
        if(IsVisible || !hotkeys.ContainsKey(playId))throw new InvalidOperationException("Closing the editor disabled the shortcuts.");
    }
    internal void ExitTest() {closing=true;Close();}
    internal void Render(string path,int w,int h,bool withInput=false) {
        current.Name="Q 입력 반복 예제";
        current.Steps.Add(new Step{Kind="Move",X=260,Y=510});
        current.Steps.Add(new Step{Kind="Click",X=260,Y=510,Delay=120});
        current.Steps.Add(new Step{Kind="KeyTap",Code=81,Delay=100,RepeatCount=10});
        current.Steps.Add(new Step{Kind="Wait",Delay=800});
        profile.Macros.Add(new Macro{Name="여러 파일 이름 바꾸기"});
        profile.Macros.Add(new Macro{Name="일반 프로그램 테스트"});
        Refresh(2);F<TabControl>("Tabs").SelectedIndex=1;
        if(withInput)BeginInput(new Step{Kind="KeyTap",Code=81,RepeatCount=10,Delay=100},step=>{});
        FitWorkspace(w);
        root.Measure(new Size(w,h));root.Arrange(new Rect(0,0,w,h));root.UpdateLayout();
        FitColumns(grid.ActualWidth);root.UpdateLayout();
        var bmp=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);bmp.Render(root);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var file=File.Create(path))png.Save(file);
    }
}
internal static class Program {
    [STAThread] static void Main(string[] args) {
        try {
            if(args.Length>0&&args[0]=="--check"){SelfCheck.Run(args[1]);return;}
            if(args.Length>0&&args[0]=="--desktop-check"){SelfCheck.Desktop(args[1]);return;}
            var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};
            if(args.Length>0&&args[0]=="--ui-check"){new Desk(true).CheckEase(args[1]);return;}
            if(args.Length>0&&args[0]=="--preview-ui"){var preview=new Desk(true);app.MainWindow=preview;preview.PreviewUI();app.Run();return;}
            if(args.Length>0&&args[0]=="--render-input"){new Desk(true).RenderInput(args[1]);return;}
            if(args.Length>0&&args[0]=="--render"){new Desk(true).Render(args[1],int.Parse(args[2]),int.Parse(args[3]));return;}
            if(args.Length>0&&args[0]=="--tray-check") {
                var fixture=new Desk(true,true);app.MainWindow=fixture;
                fixture.Dispatcher.BeginInvoke(new Action(async ()=>{
                    try {await fixture.CheckTrayControls();File.WriteAllText(args[1],"PASS: recording hides the editor and ending recording restores it; background playback and shortcut dispatch also pass.");}
                    catch(Exception e) {File.WriteAllText(args[1]+".error",e.ToString());Environment.ExitCode=1;}
                    finally {fixture.ExitTest();}
                }));
                app.Run();return;
            }
            bool background=args.Contains("--background");
            bool first;
            using(var single=new Mutex(true,"Local\\LoopDesk.Main",out first)) {
                if(!first){if(!background)MessageBox.Show("LoopDesk가 이미 실행 중입니다. 작업 표시줄 알림 영역에서 여세요.","LoopDesk");return;}
                var desk=new Desk(false);
                app.MainWindow=desk;
                app.SessionEnding+=(s,e)=>desk.EndSession();
                if(background)new WindowInteropHelper(desk).EnsureHandle();else desk.Show();
                app.Run();
            }
        } catch(Exception e) {
            if(args.Length>1)File.WriteAllText(args[1]+".error",e.ToString());
            else MessageBox.Show(e.Message,"LoopDesk를 열 수 없습니다.",MessageBoxButton.OK,MessageBoxImage.Error);
            Environment.ExitCode=1;
        }
    }
}
}
