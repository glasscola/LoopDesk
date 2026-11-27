using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace LoopDesk {
internal sealed partial class Desk {
    void InitQuickActions() {
        var popup=F<Popup>("LibraryPopup");
        popup.PlacementTarget=F<Button>("OpenLibrary");
        popup.Opened+=(s,e)=>F<TextBox>("Search").Focus();
        On("OpenLibrary",()=>{
            CloseInput();
            popup.IsOpen=true;
        });
        popup.Child.PreviewKeyDown+=(s,e)=>{
            if(e.Key==Key.Escape){popup.IsOpen=false;popup.PlacementTarget.Focus();e.Handled=true;}
        };
        BuildQuickActions();
    }

    void BuildQuickActions() {
        var panel=Panel("QuickActions");
        panel.Children.Clear();
        if(profile.FavoriteKeys==null)
            profile.FavoriteKeys=new List<Step>{
                new Step{Kind="KeyTap",Code=13,RepeatCount=1},
                new Step{Kind="KeyTap",Code=9,RepeatCount=1}
            };

        QuickHeading(panel,"자주 쓰는 동작",0);
        QuickButton(panel,"왼쪽 클릭",()=>AddQuickStep(MouseClick(1,false)));
        QuickButton(panel,"오른쪽 클릭",()=>AddQuickStep(MouseClick(2,false)));
        QuickButton(panel,"더블클릭",()=>AddQuickStep(MouseClick(1,true)));
        QuickButton(panel,"휠 위",()=>AddQuickStep(new Step{Kind="Wheel",Value="Manual",Code=120,CurrentPointer=true}));
        QuickButton(panel,"휠 아래",()=>AddQuickStep(new Step{Kind="Wheel",Value="Manual",Code=-120,CurrentPointer=true}));
        QuickButton(panel,"마우스 이동",()=>{CloseInput();PickPoint(this,(point,window)=>AddQuickStep(new Step{Kind="Move",X=point.X,Y=point.Y},true));});
        QuickButton(panel,"문장 입력",()=>AddQuickStep(new Step{Kind="Text",Value=""},true));
        QuickButton(panel,"대기 0.5초",()=>AddQuickStep(new Step{Kind="Wait",Delay=500}));

        QuickHeading(panel,"내 키",12);
        foreach(var key in profile.FavoriteKeys) {
            if(key==null||key.Kind!="KeyTap"||key.Code<1||key.Code>254)continue;
            var line=new DockPanel();
            panel.Children.Add(line);
            string label=key.Detail.Split(new[]{" · "},StringSplitOptions.None)[0];
            if(key.EffectiveRepeats>1)label+=" × "+key.EffectiveRepeats;
            var options=new Button{Content="⋯",MinWidth=28,MinHeight=28,Padding=new Thickness(3),Margin=new Thickness(4,0,0,4),
                ToolTip=label+" 버튼 설정"};
            System.Windows.Automation.AutomationProperties.SetName(options,label+" 버튼 설정");
            DockPanel.SetDock(options,Dock.Right);line.Children.Add(options);
            options.Click+=(s,e)=>Safe(()=>{
                var menu=new ContextMenu{PlacementTarget=options,Placement=PlacementMode.Bottom};
                AddMenuItem(menu,"키 설정 변경",()=>EditFavoriteKey(key));
                AddMenuItem(menu,"내 키에서 삭제",()=>ChangeFavoriteKey(key,null));
                menu.IsOpen=true;
            });
            QuickButton(line,label,()=>AddQuickStep(key.Copy())).ToolTip=key.Detail+" · "+key.EffectiveRepeats+"회";
        }
        QuickButton(panel,"＋ 내 키 추가",()=>EditFavoriteKey(null));
    }

    static Step MouseClick(int button,bool twice) {
        return new Step{Kind="MouseTap",Code=button,DoubleClick=twice,Value="Manual",CurrentPointer=true};
    }

    static void QuickHeading(Panel panel,string title,int top) {
        panel.Children.Add(new TextBlock{Text=title,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,top,0,7)});
    }

    Button QuickButton(Panel panel,string label,Action click) {
        var button=new Button{Content=new TextBlock{Text=label,TextWrapping=TextWrapping.NoWrap,
            TextTrimming=TextTrimming.CharacterEllipsis},ToolTip=label,MinHeight=28,Padding=new Thickness(5,4,5,4),Margin=new Thickness(0,0,0,4)};
        System.Windows.Automation.AutomationProperties.SetName(button,label);
        button.Click+=(s,e)=>Safe(click);
        panel.Children.Add(button);
        return button;
    }

    void AddQuickStep(Step step,bool edit=false) {
        if(Busy)return;
        CloseInput();
        InsertStep(step);
        if(edit)ShowCompactPane("EditorPane");
        Note("추가됨 · "+new Row{Step=step}.Summary);
    }

    void EditFavoriteKey(Step original) {
        if(Busy)return;
        CloseInput();capturing=true;Unregister();
        inputOriginal=null;inputIndex=-1;
        inputPicker=new InputPicker(this,original,PickPoint,result=>ChangeFavoriteKey(original,result),CloseInput,true);
        inputPicker.SetContext("내 키 설정","키를 선택한 뒤 저장하면 왼쪽에 버튼이 생깁니다.");
        F<ContentControl>("InputContent").Content=inputPicker;
        F<Border>("InputDock").Visibility=Visibility.Visible;
        FitInputDock();
    }

    void ChangeFavoriteKey(Step original,Step replacement) {
        if(Busy)return;
        var previous=profile.FavoriteKeys;
        var keys=previous.ToList();
        int index=keys.IndexOf(original);
        if(original==null)keys.Add(replacement);
        else if(index<0)return;
        else if(replacement==null)keys.RemoveAt(index);
        else keys[index]=replacement;
        profile.FavoriteKeys=keys;
        try {Save();}
        catch {profile.FavoriteKeys=previous;throw;}
        BuildQuickActions();
        Note(replacement==null?"내 키 버튼을 삭제했습니다.":"내 키 버튼을 저장했습니다.");
    }
}
}



