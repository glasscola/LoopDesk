using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace LoopDesk {
internal sealed partial class Desk {
    Window debugWindow;
    TextBlock debugCaption;
    volatile bool stepMode;

    void ConfigureDebug(Player player,int ticket) {
        stepMode=true;
        player.BeforeStep=(step,number,cycle,macro)=>{
            if(!stepMode&&!step.Breakpoint)return;
            Dispatcher.Invoke(new Action(()=>{
                if(ticket!=serial||run==null||run.IsCancellationRequested)return;
                paused=true;
                player.SetPaused(true);
                Buttons();
                OpenDebug();
                debugCaption.Text=macro+" · "+cycle+"회차\n다음 "+number+"번 · "+step.Label;
                Note("한 단계씩 실행 · 다음 "+number+"번 "+step.Label);
            }));
        };
    }

    void OpenDebug() {
        if(debugWindow!=null)return;
        var panel=new StackPanel{Margin=new Thickness(12)};
        debugCaption=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};
        panel.Children.Add(debugCaption);
        var controls=new WrapPanel();panel.Children.Add(controls);
        ActionButton(controls,"다음 한 단계",()=>ResumeDebug(true),true).Focusable=false;
        ActionButton(controls,"계속 실행",()=>ResumeDebug(false)).Focusable=false;
        ActionButton(controls,"정지",()=>Stop(false)).Focusable=false;
        var area=SystemParameters.WorkArea;
        debugWindow=new Window{Title="오토 매크로 · 한 단계씩 실행",Left=Math.Max(area.Left,area.Right-370),Top=area.Top+24,
            Width=350,SizeToContent=SizeToContent.Height,Content=panel,ResizeMode=ResizeMode.NoResize,
            ShowActivated=false,ShowInTaskbar=false,Topmost=true,FontFamily=FontFamily};
        debugWindow.SourceInitialized+=(s,e)=>{
            var handle=new WindowInteropHelper(debugWindow).Handle;
            Native.SetWindowLong(handle,-20,Native.GetWindowLong(handle,-20)|0x08000000|0x80);
            HwndSource.FromHwnd(handle).AddHook(DebugWindowMessage);
        };
        debugWindow.Closing+=(s,e)=>{if(run!=null&&debugWindow!=null){e.Cancel=true;Stop(false);}};
        debugWindow.Show();
    }

    static IntPtr DebugWindowMessage(IntPtr handle,int message,IntPtr wParam,IntPtr lParam,ref bool handled) {
        if(message!=0x21)return IntPtr.Zero;
        handled=true;
        return new IntPtr(3);
    }

    void ResumeDebug(bool single) {
        if(run==null||!paused)return;
        stepMode=single;
        paused=false;
        activePlayer.SetPaused(false);
        if(debugCaption!=null)debugCaption.Text=single?"현재 동작을 실행 중입니다.":"계속 실행 중입니다.";
        Buttons();
    }

    void CloseDebug() {
        var window=debugWindow;
        debugWindow=null;debugCaption=null;
        if(window!=null)window.Close();
    }
}
}
