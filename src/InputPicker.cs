using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LoopDesk {
internal sealed class InputPicker : UserControl {
    sealed class KeyCap {
        internal int Code;
        internal bool Extended;
        internal string Text;
        internal double X,Y,W,H;
    }
    static readonly Brush Teal=new SolidColorBrush(Color.FromRgb(23,107,99));
    static readonly Brush Ink=new SolidColorBrush(Color.FromRgb(20,37,56));
    static readonly Brush Pale=new SolidColorBrush(Color.FromRgb(239,243,247));
    static readonly List<KeyCap> Layout=MakeLayout();
    readonly Dictionary<Button,KeyCap> buttons=new Dictionary<Button,KeyCap>();
    readonly List<int> modifiers=new List<int>();
    readonly TextBlock summary;
    readonly TextBox hold,notches,repeats,delay;
    readonly ComboBox pressMode;
    readonly Window owner;
    readonly Action<Step> apply;
    readonly Action dismiss;
    readonly bool editing;
    readonly Step originalStep;
    WindowInteropHelperForPicker keySource;
    readonly StackPanel wheelFields;
    readonly CheckBox twice,currentPointer;
    readonly Action<Window,Action<Native.Point,IntPtr>> pickPoint;
    readonly StackPanel keyboardOptions,mouseOptions;
    readonly Dictionary<Button,string> mouseButtons=new Dictionary<Button,string>();
    static readonly Brush MouseOutline=new SolidColorBrush(Color.FromRgb(101,117,138));
    int key,mouseCode,wheelDirection;
    bool extended,hasPosition;
    Native.Point position;
    internal Step Result {get;private set;}
    internal static int KeyCount {get{return Layout.Count;}}
    internal static bool IsModifier(int code) {return code>=160&&code<=165 || code==91 || code==92;}
    internal static bool Extended(int code) {return code==163||code==165||code==91||code==92||code==93||code==111||
        code>=33&&code<=40||code==45||code==46||code==144||code==174||code==175;}
    internal static string KeyName(int code,bool extended) {
        var cap=Layout.FirstOrDefault(k=>k.Code==code && (code!=13 || k.Extended==extended));
        if(cap==null)return KeyInterop.KeyFromVirtualKey(code).ToString();
        if(code==13 && extended)return "숫자 Enter";
        if(code>=96 && code<=105)return "숫자 "+cap.Text;
        return cap.Text.Replace("\n"," ");
    }
    static List<KeyCap> MakeLayout() {
        var caps=new List<KeyCap>();
        Action<int,string,double,double,double,double,bool> add=(code,text,x,y,w,h,ext)=>
            caps.Add(new KeyCap{Code=code,Text=text,X=x,Y=y,W=w,H=h,Extended=ext||Extended(code)});
        Action<double,double,int[],string[],double[]> row=(x,y,codes,names,widths)=>{
            for(int i=0;i<codes.Length;i++) {
                double w=widths==null?1:widths[i];add(codes[i],names[i],x,y,w,1,false);x+=w;
            }
        };
        add(27,"Esc",0,0,1,1,false);
        for(int i=0;i<12;i++)add(112+i,"F"+(i+1),1.55+i+i/4*.38,0,1,1,false);
        row(15.45,0,new[]{44,145,19},new[]{"PrtSc","ScrLk","Pause"},null);
        row(19.05,0,new[]{21,25,174,175},new[]{"한/영","한자","Vol −","Vol +"},null);
        row(0,1.45,new[]{192,49,50,51,52,53,54,55,56,57,48,189,187,8},
            new[]{"`","1","2","3","4","5","6","7","8","9","0","−","=","Backspace"},
            new double[]{1,1,1,1,1,1,1,1,1,1,1,1,1,2});
        row(0,2.45,new[]{9,81,87,69,82,84,89,85,73,79,80,219,221,220},
            new[]{"Tab","Q","W","E","R","T","Y","U","I","O","P","[","]","\\"},
            new double[]{1.5,1,1,1,1,1,1,1,1,1,1,1,1,1.5});
        row(0,3.45,new[]{20,65,83,68,70,71,72,74,75,76,186,222,13},
            new[]{"Caps","A","S","D","F","G","H","J","K","L",";","'","Enter"},
            new double[]{1.75,1,1,1,1,1,1,1,1,1,1,1,2.25});
        row(0,4.45,new[]{160,90,88,67,86,66,78,77,188,190,191,161},
            new[]{"Shift","Z","X","C","V","B","N","M",",",".","/","Shift"},
            new double[]{2.25,1,1,1,1,1,1,1,1,1,1,2.75});
        row(0,5.45,new[]{162,91,164,32,165,92,93,163},new[]{"Ctrl","Win","Alt","Space","Alt","Win","Menu","Ctrl"},
            new double[]{1.25,1.25,1.25,6.25,1.25,1.25,1.25,1.25});
        row(15.45,1.45,new[]{45,36,33},new[]{"Ins","Home","PgUp"},null);
        row(15.45,2.45,new[]{46,35,34},new[]{"Del","End","PgDn"},null);
        add(38,"↑",16.45,4.45,1,1,false);
        row(15.45,5.45,new[]{37,40,39},new[]{"←","↓","→"},null);
        row(19.05,1.45,new[]{144,111,106,109},new[]{"Num","/","*","−"},null);
        row(19.05,2.45,new[]{103,104,105},new[]{"7","8","9"},null);
        row(19.05,3.45,new[]{100,101,102},new[]{"4","5","6"},null);
        row(19.05,4.45,new[]{97,98,99},new[]{"1","2","3"},null);
        add(107,"+",22.05,2.45,1,2,false);
        add(13,"Enter",22.05,4.45,1,2,true);
        add(96,"0",19.05,5.45,2,1,false);
        add(110,".",21.05,5.45,1,1,false);
        return caps;
    }
    TextBlock inputContext,neighbors;
    internal void SetContext(string target,string adjacent) {
        inputContext.Text=target;inputContext.ToolTip=target;
        neighbors.Text=adjacent;neighbors.ToolTip=adjacent;
    }
    internal InputPicker(Window host,Step original,Action<Window,Action<Native.Point,IntPtr>> picker,Action<Step> onApply,Action onDismiss) {
        owner=host;pickPoint=picker;apply=onApply;dismiss=onDismiss;editing=original!=null;originalStep=original;
        FontFamily=host.FontFamily;FontSize=13;Background=Brushes.White;Focusable=true;
        var body=new Grid();
        body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        deviceRow=body.RowDefinitions[1];
        Content=body;
        var heading=new DockPanel{Margin=new Thickness(0,0,0,8)};
        var close=new Button{Content="닫기 ×",Padding=new Thickness(10,4,10,4),MinHeight=28,Margin=new Thickness(8,0,0,0)};
        DockPanel.SetDock(close,Dock.Right);heading.Children.Add(close);close.Click+=(a,e)=>dismiss();
        summary=new TextBlock{TextWrapping=TextWrapping.Wrap,FontWeight=FontWeights.SemiBold,FontSize=16,Foreground=Ink,VerticalAlignment=VerticalAlignment.Center};
                var context=new StackPanel();
        inputContext=new TextBlock{Foreground=Ink,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis};
        neighbors=new TextBlock{Foreground=Brushes.SlateGray,FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis};
        context.Children.Add(inputContext);context.Children.Add(summary);context.Children.Add(neighbors);
        heading.Children.Add(context);body.Children.Add(heading);
        var area=new Grid();Grid.SetRow(area,1);body.Children.Add(area);
        area.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        optionColumn=new ColumnDefinition{Width=new GridLength(232)};
        area.ColumnDefinitions.Add(optionColumn);
        var left=new DockPanel{Margin=new Thickness(0,0,12,0)};
        area.Children.Add(left);
        var canvas=new Canvas{Width=23.05*43,Height=6.45*43};
        keyboardView=new Viewbox{Child=canvas,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,HorizontalAlignment=HorizontalAlignment.Stretch,MaxHeight=230};
        foreach(var cap in Layout) {
            var b=new Button{Content=cap.Text,Width=cap.W*43-4,Height=cap.H*43-4,Padding=new Thickness(2),Margin=new Thickness(0),MinHeight=0,
                FontSize=cap.Text.Length>=4?12:15,Focusable=false,Foreground=Ink,Background=Brushes.White,ToolTip=KeyName(cap.Code,cap.Extended)};
            System.Windows.Automation.AutomationProperties.SetName(b,KeyName(cap.Code,cap.Extended));
            Canvas.SetLeft(b,cap.X*43);Canvas.SetTop(b,cap.Y*43);canvas.Children.Add(b);buttons.Add(b,cap);
            b.Click+=(a,e)=>{Focus();SelectKey(cap.Code,cap.Extended,null);};
        }
        deviceSwitch=new WrapPanel{Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,0,6)};
        DockPanel.SetDock(deviceSwitch,Dock.Top);
        left.Children.Add(deviceSwitch);
        var keyboardTab=new Button{Content="키보드",MinHeight=29,Padding=new Thickness(8,3,8,3)};
        var mouseTab=new Button{Content="마우스",MinHeight=29,Padding=new Thickness(8,3,8,3)};
        keyboardTab.Click+=(a,e)=>{ShowDevice(false);Focus();};mouseTab.Click+=(a,e)=>{ShowDevice(true);Focus();};
        deviceSwitch.Children.Add(keyboardTab);deviceSwitch.Children.Add(mouseTab);
        deviceHint=new TextBlock{Text="108키 · 화면의 키를 클릭하거나 실제 키보드로 선택",FontSize=12,Foreground=Brushes.SlateGray,Margin=new Thickness(0,3,0,0)};
        DockPanel.SetDock(deviceHint,Dock.Bottom);left.Children.Add(deviceHint);
        var devices=new DockPanel();left.Children.Add(devices);
        mouseView=CreateMouse();DockPanel.SetDock(mouseView,Dock.Right);devices.Children.Add(mouseView);
        keyboardScroller=new ScrollViewer{Content=keyboardView,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled};
        devices.Children.Add(keyboardScroller);
        var options=new StackPanel();
        var actions=new DockPanel();
        Grid.SetColumn(actions,1);area.Children.Add(actions);
        var commit=new Button{Content=editing?"입력 변경 적용":"목록에 추가",Background=Teal,Foreground=Brushes.White,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,6,0,0)};
        DockPanel.SetDock(commit,Dock.Bottom);actions.Children.Add(commit);
        commit.Click+=(a,e)=>Commit();
        optionScroller=new ScrollViewer{Content=options,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        actions.Children.Add(optionScroller);
        var repeatRow=new WrapPanel();options.Children.Add(repeatRow);
        repeats=Number(repeatRow,"반복 (회)",original==null?"1":original.EffectiveRepeats.ToString());
        var delayRow=new WrapPanel{Margin=new Thickness(0,6,0,0)};options.Children.Add(delayRow);
        delay=Number(delayRow,"입력 전 대기 (초)",original==null?"0.1":(original.Delay/1000m).ToString(CultureInfo.InvariantCulture));
        keyboardOptions=new StackPanel{Margin=new Thickness(0,6,0,0)};options.Children.Add(keyboardOptions);
        pressMode=new ComboBox{ItemsSource=new[]{"한 번 누르기","길게 누르기"},SelectedIndex=0,MinHeight=30,Margin=new Thickness(0,0,0,4)};
        keyboardOptions.Children.Add(pressMode);
        var holdRow=new WrapPanel();keyboardOptions.Children.Add(holdRow);
        hold=Number(holdRow,"누르는 시간 (초)","0.5");holdRow.Visibility=Visibility.Collapsed;
        pressMode.SelectionChanged+=(a,e)=>holdRow.Visibility=pressMode.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;
        mouseOptions=new StackPanel();options.Children.Add(mouseOptions);
        wheelFields=new StackPanel{Orientation=Orientation.Horizontal};mouseOptions.Children.Add(wheelFields);
        notches=Number(wheelFields,"휠 칸 수","1");
        twice=new CheckBox{Content="더블클릭",Margin=new Thickness(0,5,0,5)};mouseOptions.Children.Add(twice);
        currentPointer=new CheckBox{Content="현재 커서 위치 사용",IsChecked=true,Margin=new Thickness(0,5,0,5)};mouseOptions.Children.Add(currentPointer);
        var pick=new Button{Content="화면에서 위치 지정 · F10",Padding=new Thickness(6,4,6,4),MinHeight=30,Focusable=false};mouseOptions.Children.Add(pick);
        pick.Click+=(a,e)=>pickPoint(owner,(point,window)=>{position=point;hasPosition=true;currentPointer.IsChecked=false;RefreshSelection();});
        currentPointer.Checked+=(a,e)=>RefreshSelection();currentPointer.Unchecked+=(a,e)=>RefreshSelection();
        if(original!=null) {
            if(original.Kind=="KeyTap") {
                key=original.Code;extended=original.Extended;modifiers.AddRange(original.Modifiers??new int[0]);
                if(original.HoldMs>0){pressMode.SelectedIndex=1;hold.Text=(original.HoldMs/1000m).ToString(CultureInfo.InvariantCulture);}
            } else {
                mouseCode=original.Kind=="Wheel"?0:original.Code;
                wheelDirection=original.Kind=="Wheel"?Math.Sign(original.Code):0;
                notches.Text=Math.Max(1,Math.Abs(original.Code/120)).ToString();
                twice.IsChecked=original.DoubleClick;position=new Native.Point{X=original.X,Y=original.Y};
                hasPosition=!original.CurrentPointer;currentPointer.IsChecked=original.CurrentPointer;
            }
        }
        keySource=new WindowInteropHelperForPicker(owner);
        PreviewKeyDown+=(a,e)=>{
            if(e.OriginalSource is TextBox || e.OriginalSource is ComboBox)return;
            var focusedButton=e.OriginalSource as Button;
            if(focusedButton!=null&&mouseButtons.ContainsKey(focusedButton)&&(e.Key==Key.Enter||e.Key==Key.Space))return;
            // Tab keeps normal keyboard navigation; it can still be selected on screen.
            if(e.Key==Key.Tab)return;
            var pressed=e.Key==Key.System?e.SystemKey:e.Key==Key.ImeProcessed?e.ImeProcessedKey:e.Key;
            int code=KeyInterop.VirtualKeyFromKey(pressed);
            if(code<=0 || code>254)return;
            e.Handled=true;if(e.IsRepeat)return;
            var physical=new List<int>();
            foreach(var vk in new[]{162,163,164,165,160,161,91,92})
                if(vk!=code && Native.GetAsyncKeyState(vk)<0)physical.Add(vk);
            SelectKey(code,Extended(code)||code==13&&keySource.ExtendedEnter,physical);
        };
        RefreshSelection();
        Loaded+=(a,e)=>Focus();
    }
    ColumnDefinition optionColumn;
    RowDefinition deviceRow;
    WrapPanel deviceSwitch;
    TextBlock deviceHint;
    Viewbox keyboardView;
    ScrollViewer keyboardScroller;
    ScrollViewer optionScroller;
    FrameworkElement mouseView;
    bool compactLayout;

    internal void UseCompactLayout(bool compact) {
        if(compactLayout==compact)return;
        compactLayout=compact;
        optionColumn.Width=new GridLength(compact?176:232);
        deviceRow.Height=compact?new GridLength(1,GridUnitType.Star):GridLength.Auto;
        optionScroller.VerticalScrollBarVisibility=compact?ScrollBarVisibility.Auto:ScrollBarVisibility.Disabled;
        deviceSwitch.Visibility=compact?Visibility.Visible:Visibility.Collapsed;
        deviceHint.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
        DockPanel.SetDock(mouseView,compact?Dock.Left:Dock.Right);
        keyboardView.Width=compact?580:double.NaN;
        keyboardScroller.HorizontalScrollBarVisibility=compact?ScrollBarVisibility.Auto:ScrollBarVisibility.Disabled;
        keyboardScroller.VerticalScrollBarVisibility=compact?ScrollBarVisibility.Auto:ScrollBarVisibility.Disabled;
        ShowDevice(mouseCode!=0||wheelDirection!=0);
    }
    void ShowDevice(bool mouse) {
        keyboardScroller.Visibility=!compactLayout||!mouse?Visibility.Visible:Visibility.Collapsed;
        mouseView.Visibility=!compactLayout||mouse?Visibility.Visible:Visibility.Collapsed;
    }
    internal void Detach() {if(keySource!=null)keySource.Detach();}
    internal void CheckPick(int code,int count,int wait) {
        SelectKey(code,Extended(code),null);repeats.Text=count.ToString();delay.Text=(wait/1000m).ToString(CultureInfo.InvariantCulture);Commit();
    }
    internal void CheckMouse(string label) {
        mouseButtons.Single(pair=>pair.Value==label).Key.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Commit();
    }
    // WPF exposes both Enter keys as the same Key. The native message carries the keypad flag.
    sealed class WindowInteropHelperForPicker {
        internal bool ExtendedEnter {get;private set;}
        System.Windows.Interop.HwndSource source;
        internal WindowInteropHelperForPicker(Window owner) {
            var handle=new System.Windows.Interop.WindowInteropHelper(owner).Handle;
            if(handle!=IntPtr.Zero){source=System.Windows.Interop.HwndSource.FromHwnd(handle);source.AddHook(Message);}
        }
        internal void Detach(){if(source!=null){source.RemoveHook(Message);source=null;}}
        IntPtr Message(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled) {
            if((msg==0x100||msg==0x104)&&w.ToInt64()==13)ExtendedEnter=(l.ToInt64()&0x1000000)!=0;
            return IntPtr.Zero;
        }
    }
    static TextBox Number(Panel row,string label,string initial) {
        row.Children.Add(new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)});
        var b=new TextBox{Text=initial,Width=65,MinHeight=30,Padding=new Thickness(4),Margin=new Thickness(0,0,10,0)};
        System.Windows.Automation.AutomationProperties.SetName(b,label);
        row.Children.Add(b);return b;
    }
    FrameworkElement CreateMouse() {
        var panel=new StackPanel{Width=144,Margin=new Thickness(12,0,0,0),VerticalAlignment=VerticalAlignment.Center};
        panel.Children.Add(new TextBlock{Text="마우스",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,5)});
        var body=new Canvas{Width=136,Height=140,HorizontalAlignment=HorizontalAlignment.Left};
        panel.Children.Add(body);
        body.Children.Add(new System.Windows.Shapes.Path{
            Data=Geometry.Parse("M 44,3 C 20,3 5,20 5,45 L 5,92 C 5,121 21,137 44,137 C 67,137 83,121 83,92 L 83,45 C 83,20 68,3 44,3 Z"),
            Fill=new SolidColorBrush(Color.FromRgb(220,231,240)),Stroke=Ink,StrokeThickness=1.5
        });
        MouseChoice(body,"왼쪽 클릭",1,0,8,6,35,54,"M 35,0 C 12,0 0,17 0,38 L 0,54 L 35,54 Z");
        MouseChoice(body,"오른쪽 클릭",2,0,45,6,35,54,"M 0,0 C 23,0 35,17 35,38 L 35,54 L 0,54 Z");
        MouseChoice(body,"휠 클릭",3,0,35,19,18,29,"M 9,1 C 4,1 1,4 1,9 L 1,20 C 1,25 4,28 9,28 C 14,28 17,25 17,20 L 17,9 C 17,4 14,1 9,1 Z M 5,10 L 13,10 M 5,15 L 13,15 M 5,20 L 13,20");
        MouseChoice(body,"휠 위로",0,1,93,20,40,32,null,"↑");
        MouseChoice(body,"휠 아래로",0,-1,93,58,40,32,null,"↓");
        panel.Children.Add(new TextBlock{Text="버튼 또는 휠 선택",FontSize=11,Foreground=Brushes.SlateGray});
        return panel;
    }
    void MouseChoice(Canvas panel,string label,int code,int direction,double x,double y,double width,double height,string shape,string glyph=null) {
        var b=new Button{Content=glyph,Width=width,Height=height,FontSize=16,MinHeight=0,Padding=new Thickness(0),Margin=new Thickness(0),ToolTip=label};
        System.Windows.Automation.AutomationProperties.SetName(b,label);
        if(shape!=null) {
            var path=new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            path.SetValue(System.Windows.Shapes.Path.DataProperty,Geometry.Parse(shape));
            path.SetBinding(System.Windows.Shapes.Path.FillProperty,new System.Windows.Data.Binding("Background"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
            path.SetBinding(System.Windows.Shapes.Path.StrokeProperty,new System.Windows.Data.Binding("BorderBrush"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
            path.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty,1d);
            var template=new ControlTemplate(typeof(Button)){VisualTree=path};
            var hover=new Trigger{Property=IsMouseOverProperty,Value=true};
            hover.Setters.Add(new Setter(BorderBrushProperty,Teal));template.Triggers.Add(hover);
            b.Template=template;
        }
        Canvas.SetLeft(b,x);Canvas.SetTop(b,y);panel.Children.Add(b);mouseButtons.Add(b,label);
        b.Tag=Tuple.Create(code,direction);
        b.Click+=(a,e)=>{Focus();mouseCode=code;wheelDirection=direction;key=0;modifiers.Clear();RefreshSelection();};
    }
    void SelectKey(int code,bool ext,List<int> physical) {
        mouseCode=0;wheelDirection=0;
        if(IsModifier(code) && physical==null) {
            if(!modifiers.Remove(code))modifiers.Add(code);
        } else {
            key=code;extended=ext;
            if(physical!=null) {modifiers.Clear();modifiers.AddRange(physical);}
        }
        RefreshSelection();
    }
    void RefreshSelection() {
        bool mouse=mouseCode!=0||wheelDirection!=0;
        keyboardOptions.Visibility=mouse?Visibility.Collapsed:Visibility.Visible;
        mouseOptions.Visibility=mouse?Visibility.Visible:Visibility.Collapsed;
        wheelFields.Visibility=wheelDirection==0?Visibility.Collapsed:Visibility.Visible;
        twice.Visibility=wheelDirection==0?Visibility.Visible:Visibility.Collapsed;
        foreach(var pair in buttons) {
            var cap=pair.Value;
            bool selected=!mouse&&(modifiers.Contains(cap.Code)||cap.Code==key&&(key!=13||extended==cap.Extended));
            pair.Key.Background=selected?Teal:Brushes.White;pair.Key.Foreground=selected?Brushes.White:Ink;
        }
        string mouseName="";
        foreach(var pair in mouseButtons) {
            var value=(Tuple<int,int>)pair.Key.Tag;
            bool selected=mouse&&value.Item1==mouseCode&&value.Item2==wheelDirection;
            pair.Key.Background=selected?Teal:Brushes.White;pair.Key.Foreground=selected?Brushes.White:Ink;
            pair.Key.BorderBrush=selected?Ink:MouseOutline;
            if(selected)mouseName=pair.Value;
        }
        var codes=modifiers.Select(c=>KeyName(c,false)).Concat(key==0?new string[0]:new[]{KeyName(key,extended)});
        summary.Text="선택한 입력   "+(mouse?mouseName+(currentPointer.IsChecked==true?" · 현재 커서":hasPosition?" · 위치 지정 완료":" · 위치를 지정하세요"):
            key==0&&modifiers.Count==0?"키를 선택하세요":String.Join("+",codes));
    }
    void Commit() {
        try {
            if(mouseCode!=0||wheelDirection!=0) {
                bool cursor=currentPointer.IsChecked==true;
                if(!cursor&&!hasPosition)throw new InvalidOperationException("화면에서 마우스 위치를 지정하세요.");
                int amount=0;
                if(wheelDirection!=0 && (!Int32.TryParse(notches.Text,out amount)||amount<1||amount>100))
                    throw new InvalidOperationException("휠 칸 수를 1~100 사이로 입력하세요.");
                Result=new Step{Kind=wheelDirection==0?"MouseTap":"Wheel",Value="Manual",Code=wheelDirection==0?mouseCode:amount*120*wheelDirection,
                    CurrentPointer=cursor,X=position.X,Y=position.Y,DoubleClick=twice.IsChecked==true&&wheelDirection==0};
            } else {
                int main=key==0&&modifiers.Count>0?modifiers.Last():key;
                if(main==0)throw new InvalidOperationException("입력할 키를 선택하세요.");
                decimal seconds=0;
                if(pressMode.SelectedIndex==1 && ((!Decimal.TryParse(hold.Text,NumberStyles.AllowDecimalPoint,CultureInfo.CurrentCulture,out seconds)&&
                    !Decimal.TryParse(hold.Text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out seconds))||seconds<=0||seconds>86400))
                    throw new InvalidOperationException("누르는 시간을 0~86400초 사이로 입력하세요.");
                Result=new Step{Kind="KeyTap",Code=main,Extended=main==key?extended:Extended(main),
                    Modifiers=modifiers.Where(c=>c!=main).ToArray(),HoldMs=(int)Math.Round(seconds*1000,MidpointRounding.AwayFromZero)};
            }
            int count;decimal wait;
            if(!int.TryParse(repeats.Text,out count)||count<1||count>100000)throw new InvalidOperationException("반복 횟수를 1~100000 사이로 입력하세요.");
            if(!decimal.TryParse(delay.Text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out wait)||wait<0||wait>86400)
                throw new InvalidOperationException("대기 시간을 0~86400초 사이로 입력하세요.");
            Result.RepeatCount=count;Result.Delay=(int)Math.Round(wait*1000);
            if(originalStep!=null){Result.Skip=originalStep.Skip;Result.Then=originalStep.Then;Result.Else=originalStep.Else;}
            apply(Result);
            if(editing)dismiss();else {summary.Text="추가됨 · "+new Row{Step=Result}.Summary+" · "+count+"회";Focus();}
        } catch(Exception e) {summary.Text=e.Message;}
    }
}
}
