using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path=System.IO.Path;

namespace LoopDesk {
internal sealed partial class Desk {
    internal void RenderInput(string path) {
        new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        Render(path,1220,880,true);
    }
    Action<Native.Point,IntPtr> pointChoice;
    Window pointHost,pointHint;
    WindowState pointHostState;
    bool pointWasCapturing,pointMainWasVisible;
    int[] globalRandomRange;

    void ShowAddMenu() {
        var menu=new ContextMenu{PlacementTarget=F<Button>("AddStep"),Placement=PlacementMode.Bottom,FontFamily=FontFamily,FontSize=FontSize};
        
        foreach(var kind in new[]{"Text","Move","Wait"})AddMenuItem(menu,new Step{Kind=kind}.Label,()=>InsertStep(new Step{Kind=kind,Delay=kind=="Wait"?500:0}));
        var advanced=new MenuItem{Header="고급 기능"};menu.Items.Add(advanced);
        foreach(var kind in new[]{"Click","Image","Pixel","OCR","Element","ElementText","Macro","Jump"})
            AddMenuItem(advanced,new Step{Kind=kind}.Label,()=>InsertStep(new Step{Kind=kind}));
        menu.IsOpen=true;
    }
    void AddMenuItem(ItemsControl menu,string name,Action action) {
        var item=new MenuItem{Header=name};item.Click+=(s,e)=>Safe(action);menu.Items.Add(item);
    }
    void InsertStep(Step step) {
        var row=grid.SelectedItem as Row;
        int index=row==null?current.Steps.Count:row.Number;
        Mutate(()=>current.Steps.Insert(index,step),index);
        F<TabControl>("Tabs").SelectedIndex=1;
    }
    void OpenRowMenu() {
        var menu=grid.ContextMenu;
        if(menu==null)return;
        menu.Items.Clear();
        FillEditMenu(menu);menu.Items.Add(new Separator());
        var row=grid.SelectedItem as Row;
        if(row==null||Busy)return;
        AddMenuItem(menu,row.Step.Skip?"다시 사용":"잠시 사용 안 함",()=>Mutate(()=>row.Step.Skip=!row.Step.Skip,row.Number-1));
    }
    int[] ReadGlobalRange() {
        int? min=profile.GlobalRandomMin,max=profile.GlobalRandomMax;
        return min.HasValue && max.HasValue && min.Value>=0 && max.Value>=min.Value && max.Value<=100?new[]{min.Value,max.Value}:null;
    }
    void RandomSettings() {
        if(Busy) {Note("실행을 마친 뒤 딜레이 랜덤 설정을 열어주세요.");return;}
        CloseInput();
        var panel=new StackPanel{Margin=new Thickness(18)};
        var dialog=new Window{Owner=this,Title="딜레이 랜덤",Width=400,SizeToContent=SizeToContent.Height,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false,Content=panel,FontFamily=FontFamily,FontSize=FontSize};
        var saved=ReadGlobalRange()??new[]{10,50};
        var enabled=Check(panel,"모든 매크로에 랜덤 대기 적용",randomOn);
        var min=Field(panel,"최소 변동 (%)",saved[0].ToString(),3);
        var max=Field(panel,"최대 변동 (%)",saved[1].ToString(),3);
        var preview=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,4)};panel.Children.Add(preview);
        Action update=()=>{
            int low,high;
            preview.Text=int.TryParse(min.Text,out low)&&int.TryParse(max.Text,out high)&&low>=0&&high>=low&&high<=100?
                "예: 0.1초 → "+(0.1*(100-high)/100).ToString("0.###")+"~"+(0.1*(100+high)/100).ToString("0.###")+"초 범위\n매번 "+low+"~"+high+"% 빠르게 또는 느리게 적용":
                "변동률을 0~100% 사이로 입력하세요.";
        };
        min.TextChanged+=(a,b)=>update();max.TextChanged+=(a,b)=>update();update();
        var message=new TextBlock{Foreground=Brushes.DarkRed,TextWrapping=TextWrapping.Wrap};panel.Children.Add(message);
        var save=new Button{Content="저장",Padding=new Thickness(12,6,12,6),HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0)};
        panel.Children.Add(save);
        save.Click+=(a,b)=>{
            try {
                int low=Int(min,0,100,"최소 변동"),high=Int(max,low,100,"최대 변동");
                profile.GlobalRandomMin=low;profile.GlobalRandomMax=high;globalRandomRange=new[]{low,high};
                randomOn=enabled.IsChecked==true;profile.RandomOff=!randomOn;Save();dialog.DialogResult=true;SettingsPanel();
            } catch(Exception e){message.Text=e.Message;}
        };
        capturing=true;Unregister();
        try {dialog.ShowDialog();} finally {capturing=false;Register();Buttons();}
    }
    void PickPoint(Window host,Action<Native.Point,IntPtr> apply) {
        if(Busy||pointChoice!=null)return;
        pointWasCapturing=capturing;capturing=true;Unregister();
        if(!Native.RegisterHotKey(hwnd,80,0x4000,121)||!Native.RegisterHotKey(hwnd,81,0x4000,27)) {
            Native.UnregisterHotKey(hwnd,80);Native.UnregisterHotKey(hwnd,81);
            capturing=pointWasCapturing;if(!capturing)Register();
            MessageBox.Show(host,"F10 또는 Esc를 다른 프로그램이 사용 중입니다.","위치 지정");return;
        }
        pointChoice=apply;pointHost=host;pointHostState=host.WindowState;pointMainWasVisible=IsVisible;
        host.WindowState=WindowState.Minimized;
        if(host!=this)WindowState=WindowState.Minimized;
        var border=new Border{Background=new SolidColorBrush(Color.FromRgb(20,37,56)),Padding=new Thickness(16,10,16,10),
            Child=new TextBlock{Text="원하는 위치에서 F10  ·  Esc 취소",Foreground=Brushes.White,FontSize=15}};
        var area=SystemParameters.WorkArea;
        pointHint=new Window{Content=border,SizeToContent=SizeToContent.WidthAndHeight,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,
            ShowActivated=false,ShowInTaskbar=false,Topmost=true,Left=area.Left+18,Top=area.Top+18,IsHitTestVisible=false};
        pointHint.Show();
    }
    void FinishPoint(bool accept) {
        var apply=pointChoice;var host=pointHost;
        pointChoice=null;pointHost=null;
        Native.UnregisterHotKey(hwnd,80);Native.UnregisterHotKey(hwnd,81);
        if(pointHint!=null){pointHint.Close();pointHint=null;}
        string error=null;
        try {
            if(accept) {
                Native.Point point;
                if(!Native.GetCursorPos(out point))throw new InvalidOperationException("마우스 위치를 읽을 수 없습니다.");
                var window=Native.GetAncestor(Native.WindowFromPoint(point),2);
                if(!String.IsNullOrWhiteSpace(current.Process)&&!String.Equals(Native.App(window),current.Process,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("설정한 대상 앱 위에서 위치를 지정하세요.");
                if(current.WindowCoords) {var rect=Native.Client(window);point.X-=rect.X;point.Y-=rect.Y;}
                apply(point,window);
            }
        } catch(Exception e){error=e.Message;}
        finally {
            capturing=pointWasCapturing;
            if(!capturing)Register();
            if(pointMainWasVisible&&host!=this)WindowState=WindowState.Normal;
            host.WindowState=pointHostState;host.Activate();
        }
        if(error!=null)MessageBox.Show(host,error,"위치 지정");
    }
    static BitmapSource BitmapImage(System.Drawing.Bitmap bitmap) {
        using(var stream=new MemoryStream()) {
            bitmap.Save(stream,System.Drawing.Imaging.ImageFormat.Png);stream.Position=0;
            return BitmapFrame.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
        }
    }
    static FrameworkElement PointPreview(Native.Point point) {
        var area=System.Drawing.Rectangle.Intersect(Native.Screen,new System.Drawing.Rectangle(point.X-60,point.Y-32,121,65));
        if(!area.Contains(point.X,point.Y))return null;
        using(var bitmap=Vision.Capture(area)) {
            var preview=new Canvas{Width=area.Width,Height=area.Height,Margin=new Thickness(0,5,0,3),HorizontalAlignment=HorizontalAlignment.Left};
            preview.Children.Add(new Image{Source=BitmapImage(bitmap),Width=area.Width,Height=area.Height,Stretch=Stretch.Fill});
            var mark=new Ellipse{Width=15,Height=15,Stroke=Brushes.Turquoise,StrokeThickness=2};
            Canvas.SetLeft(mark,point.X-area.X-7);Canvas.SetTop(mark,point.Y-area.Y-7);preview.Children.Add(mark);
            return preview;
        }
    }
    string CaptureTemplate(Native.Point point) {
        var area=System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(point.X,point.Y)).Bounds;
        using(var bitmap=Vision.Capture(area)) {
            var work=SystemParameters.WorkArea;
            double scale=Math.Min(1,Math.Min((work.Width-60)/area.Width,(work.Height-120)/area.Height));
            var canvas=new Canvas{Width=area.Width*scale,Height=area.Height*scale,ClipToBounds=true};
            canvas.Children.Add(new Image{Source=BitmapImage(bitmap),Width=canvas.Width,Height=canvas.Height,Stretch=Stretch.Fill});
            var outline=new Rectangle{Stroke=Brushes.Turquoise,StrokeThickness=2,Fill=new SolidColorBrush(Color.FromArgb(50,0,180,160)),
                IsHitTestVisible=false,Visibility=Visibility.Collapsed};
            canvas.Children.Add(outline);
            var panel=new StackPanel();
            panel.Children.Add(new TextBlock{Text="찾을 버튼 영역을 드래그하세요 · Esc 취소",Margin=new Thickness(10),FontSize=15});
            panel.Children.Add(canvas);
            var dialog=new Window{Title="버튼 그림 캡처",Content=panel,SizeToContent=SizeToContent.WidthAndHeight,ResizeMode=ResizeMode.NoResize,
                WindowStartupLocation=WindowStartupLocation.CenterScreen,Topmost=true,ShowInTaskbar=false};
            Point? start=null;string path=null;
            dialog.PreviewKeyDown+=(a,b)=>{if(b.Key==Key.Escape)dialog.Close();};
            canvas.MouseLeftButtonDown+=(a,b)=>{start=b.GetPosition(canvas);canvas.CaptureMouse();};
            canvas.MouseMove+=(a,b)=>{
                if(!start.HasValue)return;
                var rect=new Rect(start.Value,b.GetPosition(canvas));outline.Visibility=Visibility.Visible;
                Canvas.SetLeft(outline,rect.Left);Canvas.SetTop(outline,rect.Top);outline.Width=rect.Width;outline.Height=rect.Height;
            };
            canvas.MouseLeftButtonUp+=(a,b)=>{
                if(!start.HasValue)return;
                var rect=new Rect(start.Value,b.GetPosition(canvas));start=null;canvas.ReleaseMouseCapture();
                var pixels=System.Drawing.Rectangle.Intersect(new System.Drawing.Rectangle(0,0,area.Width,area.Height),
                    new System.Drawing.Rectangle((int)(rect.X/scale),(int)(rect.Y/scale),(int)(rect.Width/scale),(int)(rect.Height/scale)));
                if(pixels.Width<3||pixels.Height<3){outline.Visibility=Visibility.Collapsed;return;}
                try {
                    string folder=Path.Combine(Storage.Home,"templates");Directory.CreateDirectory(folder);
                    var file=Path.Combine(folder,Guid.NewGuid().ToString("N")+".png");
                    using(var cropped=bitmap.Clone(pixels,System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                        cropped.Save(file,System.Drawing.Imaging.ImageFormat.Png);
                    path=file;dialog.DialogResult=true;
                } catch(Exception e){MessageBox.Show(dialog,e.Message,"그림 저장 실패");}
            };
            dialog.ShowDialog();return path;
        }
    }
}
}
