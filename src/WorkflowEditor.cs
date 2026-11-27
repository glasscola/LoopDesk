using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Rectangle=System.Drawing.Rectangle;
using DrawingPoint=System.Drawing.Point;

namespace LoopDesk {
internal sealed partial class Desk {
    static string RandomPreview(int delay,double speed,int min,int max) {
        Func<int,string> seconds=percent=>(Player.DelayMs(delay,speed,percent)/1000m).ToString("0.###",CultureInfo.InvariantCulture)+"초";
        return "선택 동작 기준 · "+seconds(0)+"\n빠르게 "+seconds(-max)+"–"+seconds(-min)+" · 느리게 "+seconds(min)+"–"+seconds(max)+
            "\n예: "+max+"% 느리게 → "+seconds(max)+"\n키나 버튼을 누른 구간의 기존 타이밍은 유지합니다.";
    }

    void EditGroup(Step group,int[] failurePath) {
        var outer=current;
        int original=outer.Steps.IndexOf(group);
        if(original<0)return;
        var draft=group.Copy();
        var body=new DockPanel {Margin=new Thickness(14)};
        var dialog=WorkflowDialog("묶음 편집 · "+group.EffectiveRepeats+"회",body,650);
        dialog.Height=500;dialog.SizeToContent=SizeToContent.Manual;
        var footer=new WrapPanel {Margin=new Thickness(0,9,0,0)};
        DockPanel.SetDock(footer,Dock.Bottom);body.Children.Add(footer);
        var error=new TextBlock {TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Firebrick};
        DockPanel.SetDock(error,Dock.Bottom);body.Children.Add(error);
        var split=new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(210)});
        split.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        body.Children.Add(split);
        var tree=new TreeView{Margin=new Thickness(0,0,12,0)};
        split.Children.Add(tree);
        var fields=new StackPanel();
        var scroll=new ScrollViewer{Content=fields,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        Grid.SetColumn(scroll,1);split.Children.Add(scroll);

        Action saveFields=null;
        TreeViewItem lastItem=null,failedItem=null;
        bool changing=false;
        tree.SelectedItemChanged+=(s,e)=>{
            if(changing)return;
            try {if(saveFields!=null)saveFields();error.Text="";}
            catch(Exception ex) {
                error.Text=ex.Message;
                changing=true;if(lastItem!=null)lastItem.IsSelected=true;changing=false;
                return;
            }
            fields.Children.Clear();saveFields=null;
            lastItem=e.NewValue as TreeViewItem;
            if(lastItem==null)return;
            var item=lastItem;
            var step=(Step)item.Tag;
            if(step.Removed){Label(fields,"제거된 동작입니다. 묶음을 풀고 이 줄을 삭제하세요.",true);return;}
            var delay=Field(fields,"실행 전 대기 (초)",(step.Delay/1000m).ToString(CultureInfo.InvariantCulture),10);
            var repeat=Field(fields,"반복 횟수",step.EffectiveRepeats.ToString(),6);
            var skip=Check(fields,"이 동작 건너뛰기",step.Skip);
            TextBox value=null,code=null,x=null,y=null;
            if(step.Kind=="Text")value=Field(fields,"입력할 문장",step.Value,4096);
            if(new[]{"KeyTap","KeyDown","KeyUp","MouseTap","Down","Up","Wheel","HWheel"}.Contains(step.Kind))
                code=Field(fields,step.Kind.StartsWith("Key")?"키 번호":"버튼·휠 값",step.Code.ToString(),8);
            if(HasFixedPoint(step)) {
                x=Field(fields,"X 위치",step.X.ToString(),8);y=Field(fields,"Y 위치",step.Y.ToString(),8);
                ActionButton(fields,"화면에서 위치 지정 · F10",()=>PickPoint(dialog,(p,h)=>{x.Text=p.X.ToString();y.Text=p.Y.ToString();}));
            }
            if(step.Kind=="KeyTap"||step.Kind=="MouseTap"||step.Kind=="Wheel"&&step.Value=="Manual") {
                ActionButton(fields,"키·마우스 입력 변경",()=>{
                    try {saveFields();}catch(Exception ex){error.Text=ex.Message;return;}
                    var inputPanel=new StackPanel{Margin=new Thickness(12)};
                    var inputWindow=WorkflowDialog("묶음 안의 입력 변경",inputPanel,1060);
                    var picker=new InputPicker(inputWindow,step,PickPoint,result=>{
                        step.Kind=result.Kind;step.Code=result.Code;step.Scan=result.Scan;step.Extended=result.Extended;
                        step.Modifiers=result.Modifiers;step.HoldMs=result.HoldMs;step.Value=result.Value;
                        step.X=result.X;step.Y=result.Y;step.DoubleClick=result.DoubleClick;step.CurrentPointer=result.CurrentPointer;
                        step.Delay=result.Delay;step.RepeatCount=result.RepeatCount;item.Header=ShortStep(step);
                        saveFields=null;inputWindow.DialogResult=true;
                    },()=>inputWindow.Close());
                    inputPanel.Children.Add(picker);
                    try {inputWindow.ShowDialog();}finally{picker.Detach();}
                    if(saveFields==null){changing=true;item.IsSelected=false;changing=false;item.IsSelected=true;}
                });
            }
            var caption=step.Kind=="Group"?"묶음 · ":"";
            saveFields=()=>{
                var updated=step.Copy();
                updated.Delay=ReadDelay(delay);updated.RepeatCount=Int(repeat,1,100000,"반복 횟수");updated.Skip=skip.IsChecked==true;
                if(value!=null)updated.Value=value.Text;
                if(code!=null)updated.Code=Int(code,-32768,32767,"입력 값");
                if(x!=null){updated.X=Int(x,-100000,100000,"X");updated.Y=Int(y,-100000,100000,"Y");}
                Storage.ValidateStep(updated);
                if(step.Kind.StartsWith("Key")&&step.Code!=updated.Code){step.Scan=0;step.Extended=InputPicker.Extended(updated.Code);}
                step.Delay=updated.Delay;step.RepeatCount=updated.RepeatCount;step.Skip=updated.Skip;
                step.Value=updated.Value;step.Code=updated.Code;step.X=updated.X;step.Y=updated.Y;
                item.Header=caption+ShortStep(step);
            };
        };

        Action<ItemsControl,List<Step>,int> add=null;
        add=(parent,children,depth)=>{
            for(int i=0;i<children.Count;i++) {
                var step=children[i];
                var node=new TreeViewItem{Header=ShortStep(step),Tag=step,IsExpanded=true};
                parent.Items.Add(node);
                bool match=depth<failurePath.Length&&i==failurePath[depth];
                if(match&&depth==failurePath.Length-1)failedItem=node;
                if(step.Children!=null)add(node,step.Children,match?depth+1:failurePath.Length);
            }
        };
        add(tree,draft.Children,0);
        (failedItem??(TreeViewItem)tree.Items[0]).IsSelected=true;
        ActionButton(footer,"묶음의 변경 저장",()=>{
            try {
                if(saveFields!=null)saveFields();
                Storage.ValidateStep(draft);
                Mutate(()=>outer.Steps[original]=draft,original);
                dialog.DialogResult=true;
            } catch(Exception ex){error.Text=ex.Message;}
        },true);
        footer.Children.Add(new Button{Content="취소",IsCancel=true,Margin=new Thickness(8,0,0,0)});
        CaptureDialog(dialog);
    }

    void CaptureDialog(Window dialog) {
        bool previous=capturing;
        capturing=true;Unregister();
        try {dialog.ShowDialog();} finally {capturing=previous;if(!capturing)Register();}
    }

}
}
