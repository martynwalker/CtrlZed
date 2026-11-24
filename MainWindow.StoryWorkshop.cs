using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CtrlZed;

public partial class MainWindow
{
    internal Window? storyWorkshopWindow;

    void InstallStoryWorkshop()
    {
        void AddItem()
        {
            var review=MainMenu.Items.OfType<MenuItem>().FirstOrDefault(item=>(item.Tag as string)=="CtrlZedReviewMenu");
            if(review==null || review.Items.OfType<MenuItem>().Any(item=>(item.Tag as string)=="CtrlZedStoryWorkshop"))return;
            var item=new MenuItem{Header="Story _Workshop…",Tag="CtrlZedStoryWorkshop"};
            item.Click+=(_,__)=>ShowStoryWorkshop();review.Items.Add(item);
        }
        Loaded+=(_,__)=>Dispatcher.BeginInvoke(new Action(AddItem),DispatcherPriority.ApplicationIdle);
        ContentRendered+=(_,__)=>Dispatcher.BeginInvoke(new Action(AddItem),DispatcherPriority.ApplicationIdle);
        Closed+=(_,__)=>storyWorkshopWindow?.Close();
    }

    /// <summary>Opens this document's planning conversation without requesting AI or changing the document.</summary>
    public void ShowStoryWorkshop()
    {
        if(storyWorkshopWindow!=null)
        {
            if(storyWorkshopWindow.WindowState==WindowState.Minimized)storyWorkshopWindow.WindowState=WindowState.Normal;
            storyWorkshopWindow.Activate();return;
        }
        var state=SnapshotStoryWorkshop();
        var document=Editor.Document;
        var width=Math.Max(320,SystemParameters.WorkArea.Width-32);
        var height=Math.Max(320,SystemParameters.WorkArea.Height-32);
        var w=new Window{Title="Story Workshop",Owner=this,Width=Math.Min(1090,width),Height=Math.Min(840,height),
            MinWidth=Math.Min(880,width),MinHeight=Math.Min(620,height),WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Icon=Branding.CreateIconImageSource()};
        storyWorkshopWindow=w;NameScope.SetNameScope(w,new NameScope());
        var assigning=true;var committing=false;var closed=false;var busy=false;
        long generation=0;CancellationTokenSource? cancellation=null;string configuration="";string? unavailable=null;
        var undo=new List<StoryWorkshopState>();
        var fieldPanels=new List<WorkshopFieldPanel>();
        var editableControls=new List<Control>();
        T Named<T>(string name,T control) where T:FrameworkElement
        {
            control.Name=name;w.RegisterName(name,control);AutomationProperties.SetName(control,name.Replace("Workshop",""));return control;
        }
        static Button Button(string text)=>new(){Content=text,Padding=new Thickness(9,4,9,4),Margin=new Thickness(0,4,7,4),MinWidth=72};
        static TextBlock Label(string text)=>new(){Text=text,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,5,0,5)};
        static TextBox Text(int maximum)=>new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MaxLength=maximum,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(6)};
        var root=new Grid{Margin=new Thickness(16)};
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var heading=new DockPanel();
        var modePanel=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        modePanel.Children.Add(new TextBlock{Text="Workshop writing mode",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,8,0)});
        var mode=Named("WorkshopMode",new ComboBox{ItemsSource=WritingModeProfiles.All,DisplayMemberPath="DisplayName",MinWidth=170,
            SelectedItem=WritingModeProfiles.Get(state.WritingMode),VerticalAlignment=VerticalAlignment.Center});
        editableControls.Add(mode);modePanel.Children.Add(mode);DockPanel.SetDock(modePanel,Dock.Right);heading.Children.Add(modePanel);
        heading.Children.Add(new TextBlock{Text="Story Workshop",FontSize=23,FontWeight=FontWeights.SemiBold});root.Children.Add(heading);
        var information=new TextBlock{Text="Plan the story through conversation, then review its cast, arcs and storyboard. Notes and conversation are saved with this .czd document; they are separate from the manuscript.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,8,0,10)};
        Grid.SetRow(information,1);root.Children.Add(information);
        var tabs=Named("WorkshopTabs",new TabControl());Grid.SetRow(tabs,2);root.Children.Add(tabs);
        TabItem Tab(TabControl parent,string title,object content)
        {var item=new TabItem{Header=title,Content=content};parent.Items.Add(item);return item;}

        var conversationPage=new Grid{Margin=new Thickness(10)};
        conversationPage.RowDefinitions.Add(new RowDefinition());
        conversationPage.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        conversationPage.RowDefinitions.Add(new RowDefinition{Height=new GridLength(110),MinHeight=75});
        conversationPage.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var conversation=Named("WorkshopConversation",new StackPanel());
        var conversationScroll=new ScrollViewer{Content=conversation,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        conversationPage.Children.Add(conversationScroll);
        conversationPage.Children.Add(WithRow(Label("Tell me about the story, or answer the latest question"),1));
        var input=Named("WorkshopInput",Text(StoryWorkshopState.MaximumTurnLength));Grid.SetRow(input,2);conversationPage.Children.Add(input);
        var conversationActions=new DockPanel();
        var clearConversation=Named("WorkshopClearConversation",Button("Clear conversation"));DockPanel.SetDock(clearConversation,Dock.Left);conversationActions.Children.Add(clearConversation);
        var sendActions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};
        var cancel=Named("WorkshopCancel",Button("Cancel request"));var send=Named("WorkshopSend",Button("Send"));
        send.ToolTip="Send this message and the planning record to the configured AI provider. Ctrl+Enter also sends.";
        sendActions.Children.Add(cancel);sendActions.Children.Add(send);conversationActions.Children.Add(sendActions);
        Grid.SetRow(conversationActions,3);conversationPage.Children.Add(conversationActions);Tab(tabs,"Conversation",conversationPage);

        var characters=Named("WorkshopCharacters",new ListBox{DisplayMemberPath="Name",HorizontalContentAlignment=HorizontalAlignment.Stretch});
        var beats=Named("WorkshopBeats",new ListBox{DisplayMemberPath="Name"});
        var clues=Named("WorkshopClues",new ListBox{DisplayMemberPath="Name"});
        var characterName=Named("WorkshopCharacterName",new TextBox{MaxLength=160,Padding=new Thickness(4),MinWidth=150});
        var beatName=Named("WorkshopBeatName",new TextBox{MaxLength=160,Padding=new Thickness(4),MinWidth=150});
        var clueName=Named("WorkshopClueName",new TextBox{MaxLength=160,Padding=new Thickness(4),MinWidth=150});
        var characterSaveName=Named("WorkshopSaveCharacterName",Button("Confirm name"));
        var beatSaveName=Named("WorkshopSaveBeatName",Button("Confirm name"));
        var clueSaveName=Named("WorkshopSaveClueName",Button("Confirm name"));
        var addCharacter=Named("WorkshopAddCharacter",Button("Add character"));var deleteCharacter=Named("WorkshopDeleteCharacter",Button("Remove"));
        var addBeat=Named("WorkshopAddBeat",Button("Add beat"));var deleteBeat=Named("WorkshopDeleteBeat",Button("Remove"));
        var addClue=Named("WorkshopAddClue",Button("Add clue"));var deleteClue=Named("WorkshopDeleteClue",Button("Remove"));
        var characterVoice=Named("WorkshopCharacterVoice",Button("Use in Character Voice…"));
        var beatLinks=Named("WorkshopBeatCharacters",new ListBox{DisplayMemberPath="Name",SelectionMode=SelectionMode.Multiple,MaxHeight=75});
        var clueLinks=Named("WorkshopClueCharacters",new ListBox{DisplayMemberPath="Name",SelectionMode=SelectionMode.Multiple,MaxHeight=75});
        var overview=Named("WorkshopOverview",Text(int.MaxValue));overview.IsReadOnly=true;overview.FontSize=14;
        var questions=Named("WorkshopQuestions",Text(64000));var contradictions=Named("WorkshopContradictions",Text(64000));
        var saveQuestions=Named("WorkshopSaveQuestions",Button("Save questions"));var saveContradictions=Named("WorkshopSaveContradictions",Button("Save continuity notes"));
        var footer=new StackPanel{Margin=new Thickness(0,10,0,0)};
        var status=Named("WorkshopStatus",new TextBlock{TextWrapping=TextWrapping.Wrap,MinHeight=32,Foreground=Brushes.DimGray});footer.Children.Add(status);
        var footerActions=new DockPanel();
        var settings=new WrapPanel();var languageSetup=Button("Language settings…");var aiSetup=Button("AI settings…");
        settings.Children.Add(languageSetup);settings.Children.Add(aiSetup);DockPanel.SetDock(settings,Dock.Left);footerActions.Children.Add(settings);
        var remaining=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};
        var undoPlanning=Named("WorkshopUndo",Button("Undo planning edit"));var close=Button("Close");close.IsCancel=true;
        remaining.Children.Add(undoPlanning);remaining.Children.Add(close);footerActions.Children.Add(remaining);footer.Children.Add(footerActions);
        Grid.SetRow(footer,3);root.Children.Add(footer);w.Content=root;

        bool Editable()=>!closed && ReferenceEquals(Editor.Document,document) && !Editor.IsReadOnly && Editor.IsEnabled;
        List<StoryWorkshopEntry> Entries(StoryWorkshopState value,StoryWorkshopEntryKind kind)=>kind switch
        {StoryWorkshopEntryKind.Character=>value.Characters,StoryWorkshopEntryKind.Beat=>value.Beats,_=>value.Clues};
        ListBox EntryList(StoryWorkshopEntryKind kind)=>kind switch
        {StoryWorkshopEntryKind.Character=>characters,StoryWorkshopEntryKind.Beat=>beats,_=>clues};
        TextBox EntryName(StoryWorkshopEntryKind kind)=>kind switch
        {StoryWorkshopEntryKind.Character=>characterName,StoryWorkshopEntryKind.Beat=>beatName,_=>clueName};
        StoryWorkshopEntry? Selected(StoryWorkshopEntryKind kind)=>EntryList(kind).SelectedItem as StoryWorkshopEntry;
        StoryWorkshopEntry? Resolve(StoryWorkshopState value,StoryWorkshopEntryKind kind)
        {var id=Selected(kind)?.Id;return Entries(value,kind).FirstOrDefault(entry=>entry.Id==id);}

        void CancelRequest(string? message=null)
        {
            generation++;var old=cancellation;cancellation=null;old?.Cancel();old?.Dispose();busy=false;
            if(message!=null)status.Text=message;
        }
        void UpdateButtons()
        {
            var edit=Editable();
            mode.IsEnabled=edit;input.IsEnabled=edit && !busy;
            send.IsEnabled=edit && !busy && unavailable==null && !string.IsNullOrWhiteSpace(input.Text);
            cancel.IsEnabled=busy;clearConversation.IsEnabled=edit && state.Conversation.Count>0;
            undoPlanning.IsEnabled=edit && undo.Count>0;
            foreach(var control in editableControls.Where(control=>!ReferenceEquals(control,mode)))control.IsEnabled=edit;
            deleteCharacter.IsEnabled=edit && Selected(StoryWorkshopEntryKind.Character)!=null;
            deleteBeat.IsEnabled=edit && Selected(StoryWorkshopEntryKind.Beat)!=null;deleteClue.IsEnabled=edit && Selected(StoryWorkshopEntryKind.Clue)!=null;
            characterVoice.IsEnabled=Selected(StoryWorkshopEntryKind.Character)!=null;
            foreach(var kind in Enum.GetValues<StoryWorkshopEntryKind>())EntryName(kind).IsEnabled=edit && Selected(kind)!=null;
            beatLinks.IsEnabled=edit && Selected(StoryWorkshopEntryKind.Beat)!=null;clueLinks.IsEnabled=edit && Selected(StoryWorkshopEntryKind.Clue)!=null;
            foreach(var panel in fieldPanels)panel.UpdateButtons();
        }
        void RefreshOverview()
        {
            var text=new StringBuilder();text.AppendLine("STORY WORKSHOP").AppendLine(WritingModeProfiles.Get(state.WritingMode).DisplayName).AppendLine();
            void Values(Dictionary<string,StoryWorkshopValue> values,IReadOnlyList<StoryWorkshopField> fields)
            {
                string? group=null;
                foreach(var field in fields)
                {
                    if(!values.TryGetValue(field.Key,out var value) || string.IsNullOrWhiteSpace(value.Text))continue;
                    if(group!=field.Group){group=field.Group;text.AppendLine(group);}
                    text.Append(field.Label).Append(" [").Append(value.Status).AppendLine("]").AppendLine(value.Text).AppendLine();
                }
            }
            Values(state.Story,StoryWorkshopSchema.StoryFields);
            foreach(var (title,entries,fields) in new[]{("CHARACTERS",state.Characters,StoryWorkshopSchema.CharacterFields),
                ("ORDERED PLOT BEATS",state.Beats,StoryWorkshopSchema.BeatFields),("FAIR-PLAY CLUE LEDGER",state.Clues,StoryWorkshopSchema.ClueFields)})
            {
                if(entries.Count==0)continue;text.AppendLine(title).AppendLine();var number=0;
                foreach(var entry in entries)
                {
                    text.Append(++number).Append(". ").Append(entry.Name).Append(" [").Append(entry.Status).AppendLine("]");
                    var names=entry.CharacterIds.Select(id=>state.Characters.FirstOrDefault(character=>character.Id==id)?.Name).Where(name=>!string.IsNullOrWhiteSpace(name));
                    if(names.Any())text.Append("Characters: ").AppendLine(string.Join(", ",names));
                    Values(entry.Fields,fields);
                }
            }
            if(state.OpenQuestions.Count>0){text.AppendLine("OPEN QUESTIONS");foreach(var question in state.OpenQuestions)text.Append("• ").AppendLine(question);text.AppendLine();}
            if(state.Contradictions.Count>0){text.AppendLine("CONTINUITY TO RESOLVE");foreach(var question in state.Contradictions)text.Append("• ").AppendLine(question);}
            if(!state.HasContent)text.AppendLine("Begin in Conversation, or add your own character and story notes. The storyboard will grow here.");
            overview.Text=text.ToString();
        }
        void RenderConversation()
        {
            conversation.Children.Clear();
            void Add(string name,string text,bool assistant)
            {
                var content=new StackPanel();content.Children.Add(new TextBlock{Text=name,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,4)});
                content.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,FontSize=14});
                conversation.Children.Add(new Border{Child=content,Background=assistant?new SolidColorBrush(Color.FromRgb(237,243,250)):Brushes.WhiteSmoke,
                    Padding=new Thickness(10),CornerRadius=new CornerRadius(5),Margin=new Thickness(0,0,5,10)});
            }
            if(state.Conversation.Count==0)Add("Story Workshop","How can I help you?",true);
            foreach(var turn in state.Conversation)Add(turn.Role==StoryWorkshopTurnRole.User?"You":"Story Workshop",turn.Text,turn.Role==StoryWorkshopTurnRole.Assistant);
            conversationScroll.ScrollToEnd();
        }
        void RefreshEntries(StoryWorkshopEntryKind kind)
        {
            var list=EntryList(kind);var id=Selected(kind)?.Id;list.ItemsSource=Entries(state,kind);
            list.SelectedItem=Entries(state,kind).FirstOrDefault(entry=>entry.Id==id)??Entries(state,kind).FirstOrDefault();
            EntryName(kind).Text=Selected(kind)?.Name??"";
        }
        void RefreshLinks(ListBox list,StoryWorkshopEntryKind kind)
        {
            list.ItemsSource=state.Characters;list.SelectedItems.Clear();var entry=Selected(kind);
            if(entry!=null)foreach(var character in state.Characters.Where(character=>entry.CharacterIds.Contains(character.Id,StringComparer.Ordinal)))list.SelectedItems.Add(character);
        }
        void RefreshAll()
        {
            var wasAssigning=assigning;assigning=true;
            try
            {
                state=SnapshotStoryWorkshop();mode.SelectedItem=WritingModeProfiles.Get(state.WritingMode);
                foreach(var kind in Enum.GetValues<StoryWorkshopEntryKind>())RefreshEntries(kind);
                foreach(var panel in fieldPanels)panel.Refresh();
                RefreshLinks(beatLinks,StoryWorkshopEntryKind.Beat);RefreshLinks(clueLinks,StoryWorkshopEntryKind.Clue);
                questions.Text=string.Join(Environment.NewLine,state.OpenQuestions);contradictions.Text=string.Join(Environment.NewLine,state.Contradictions);
                RenderConversation();RefreshOverview();
            }
            finally{assigning=wasAssigning;}UpdateButtons();
        }
        bool CommitLocal(StoryWorkshopState next,string message,bool refresh=true,bool remember=true)
        {
            if(!Editable())return false;
            try
            {
                next.Validate();CancelRequest();var before=SnapshotStoryWorkshop();
                committing=true;CommitStoryWorkshop(next,message);state=SnapshotStoryWorkshop();
                if(state.Revision!=before.Revision && remember){undo.Add(before);while(undo.Count>20)undo.RemoveAt(0);}
                if(refresh)RefreshAll();else RefreshOverview();
                status.Text=message+" Saved in this document's planning record; save the document to keep it on disk.";UpdateButtons();return true;
            }
            catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or InvalidDataException)
            {status.Text=ex.Message;return false;}
            finally{committing=false;}
        }

        WorkshopFieldPanel MakeFields(string prefix,IReadOnlyList<StoryWorkshopField> schema,StoryWorkshopEntryKind? kind)
        {
            var grid=new Grid{Margin=new Thickness(0,6,0,0)};grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition());
            var group=Named(prefix+"Groups",new ComboBox{ItemsSource=schema.Select(field=>field.Group).Distinct().ToArray(),Margin=new Thickness(0,0,0,8)});
            grid.Children.Add(group);var body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(175)});body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(10)});body.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetRow(body,1);grid.Children.Add(body);
            var fields=Named(prefix+"Fields",new ListBox{DisplayMemberPath="Label"});body.Children.Add(fields);
            var right=new Grid();right.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});right.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});right.RowDefinitions.Add(new RowDefinition());right.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            Grid.SetColumn(right,2);body.Children.Add(right);
            var hint=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,6)};right.Children.Add(hint);
            var valueStatus=Named(prefix+"Status",new TextBlock{FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6)});Grid.SetRow(valueStatus,1);right.Children.Add(valueStatus);
            var value=Named(prefix+"Value",Text(StoryWorkshopState.MaximumFieldLength));Grid.SetRow(value,2);right.Children.Add(value);
            var actions=new WrapPanel();var save=Named(prefix+"Save",Button("Save field"));var confirm=Named(prefix+"Confirm",Button("Confirm proposal"));var remove=Named(prefix+"Remove",Button("Remove field"));
            actions.Children.Add(save);actions.Children.Add(confirm);actions.Children.Add(remove);Grid.SetRow(actions,3);right.Children.Add(actions);
            Dictionary<string,StoryWorkshopValue>? Values(StoryWorkshopState current)=>kind==null?current.Story:Resolve(current,kind.Value)?.Fields;
            StoryWorkshopField? Field()=>fields.SelectedItem as StoryWorkshopField;
            void SetValue()
            {
                var wasAssigning=assigning;assigning=true;
                try
                {
                    var field=Field();var values=Values(state);var item=field!=null && values!=null && values.TryGetValue(field.Key,out var found)?found:null;
                    value.MaxLength=field?.MaximumLength??StoryWorkshopState.MaximumFieldLength;value.Text=item?.Text??"";hint.Text=field?.Hint??"Choose a field.";
                    valueStatus.Text=item==null?"Not set":item.Status==StoryWorkshopValueStatus.Proposed?"Proposed — review, edit or confirm":"Confirmed by the author";
                }
                finally{assigning=wasAssigning;}
            }
            void Buttons()
            {
                var values=Values(state);var field=Field();var edit=Editable() && field!=null && values!=null;
                value.IsReadOnly=!edit;save.IsEnabled=edit;remove.IsEnabled=edit && values!.ContainsKey(field!.Key);
                confirm.IsEnabled=edit && values!.TryGetValue(field!.Key,out var current) && current.Status==StoryWorkshopValueStatus.Proposed;
            }
            void Filter()
            {
                var key=Field()?.Key;var currentGroup=group.SelectedItem as string;
                fields.ItemsSource=schema.Where(field=>field.Group==currentGroup).ToArray();
                fields.SelectedItem=fields.Items.Cast<StoryWorkshopField>().FirstOrDefault(field=>field.Key==key)??fields.Items.Cast<StoryWorkshopField>().FirstOrDefault();
                SetValue();Buttons();
            }
            void Save()
            {
                if(assigning || !Editable() || Field()==null)return;var next=SnapshotStoryWorkshop();var values=Values(next);if(values==null)return;
                values[Field()!.Key]=new StoryWorkshopValue{Text=value.Text,Status=StoryWorkshopValueStatus.Confirmed};
                if(CommitLocal(next,Field()!.Label+" updated.",false)){valueStatus.Text="Confirmed by the author";Buttons();}
            }
            group.SelectionChanged+=(_,__)=>{if(!assigning)Filter();};fields.SelectionChanged+=(_,__)=>{if(!assigning){SetValue();Buttons();}};
            value.TextChanged+=(_,__)=>{if(!assigning){CancelRequest("Planning note changed; any pending reply was cancelled.");Save();UpdateButtons();}};
            save.Click+=(_,__)=>Save();
            confirm.Click+=(_,__)=>
            {
                if(!Editable() || Field()==null)return;var next=SnapshotStoryWorkshop();var values=Values(next);if(values==null || !values.TryGetValue(Field()!.Key,out var item))return;
                item.Status=StoryWorkshopValueStatus.Confirmed;if(CommitLocal(next,"Proposal confirmed.",false)){SetValue();Buttons();}
            };
            remove.Click+=(_,__)=>
            {
                if(!Editable() || Field()==null)return;var next=SnapshotStoryWorkshop();var values=Values(next);if(values==null)return;
                values.Remove(Field()!.Key);if(CommitLocal(next,"Planning field removed.",false)){SetValue();Buttons();}
            };
            var panel=new WorkshopFieldPanel(grid,()=>
            {
                if(group.SelectedItem==null)group.SelectedIndex=0;Filter();
            },Buttons);fieldPanels.Add(panel);return panel;
        }

        var characterFields=MakeFields("WorkshopCharacter",StoryWorkshopSchema.CharacterFields,StoryWorkshopEntryKind.Character);
        var storyFields=MakeFields("WorkshopStory",StoryWorkshopSchema.StoryFields,null);
        var beatFields=MakeFields("WorkshopBeat",StoryWorkshopSchema.BeatFields,StoryWorkshopEntryKind.Beat);
        var clueFields=MakeFields("WorkshopClue",StoryWorkshopSchema.ClueFields,StoryWorkshopEntryKind.Clue);
        Grid EntryPage(StoryWorkshopEntryKind kind,ListBox list,TextBox name,Button saveName,Button add,Button delete,WorkshopFieldPanel fields,ListBox? links=null)
        {
            var page=new Grid{Margin=new Thickness(10)};page.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(205)});page.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(12)});page.ColumnDefinitions.Add(new ColumnDefinition());
            var left=new Grid();left.RowDefinitions.Add(new RowDefinition());left.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});left.Children.Add(list);
            var management=new WrapPanel();management.Children.Add(add);management.Children.Add(delete);Grid.SetRow(management,1);left.Children.Add(management);page.Children.Add(left);
            var right=new Grid();right.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});right.RowDefinitions.Add(new RowDefinition());if(links!=null)right.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var namePanel=new DockPanel();DockPanel.SetDock(saveName,Dock.Right);namePanel.Children.Add(saveName);namePanel.Children.Add(name);right.Children.Add(namePanel);
            Grid.SetRow(fields.Root,1);right.Children.Add(fields.Root);Grid.SetColumn(right,2);page.Children.Add(right);
            if(links!=null)
            {
                var linked=new Expander{Header="Characters involved — select one or more",Content=links,Margin=new Thickness(0,8,0,0)};
                Grid.SetRow(linked,2);right.Children.Add(linked);
            }
            else{management.Children.Add(characterVoice);}
            editableControls.Add(add);editableControls.Add(saveName);
            list.SelectionChanged+=(_,__)=>
            {
                if(assigning)return;assigning=true;
                try{name.Text=Selected(kind)?.Name??"";fields.Refresh();if(links!=null)RefreshLinks(links,kind);}
                finally{assigning=false;}UpdateButtons();
            };
            void SaveName()
            {
                if(assigning || !Editable())return;var next=SnapshotStoryWorkshop();var entry=Resolve(next,kind);if(entry==null)return;
                entry.Name=name.Text;entry.Status=StoryWorkshopValueStatus.Confirmed;CommitLocal(next,"Card name updated.");
            }
            name.TextChanged+=(_,__)=>{if(!assigning)SaveName();};saveName.Click+=(_,__)=>SaveName();
            add.Click+=(_,__)=>
            {
                if(!Editable())return;var next=SnapshotStoryWorkshop();var entries=Entries(next,kind);var noun=kind.ToString().ToLowerInvariant();
                var entry=new StoryWorkshopEntry{Kind=kind,Name="New "+noun,Status=StoryWorkshopValueStatus.Confirmed};entries.Add(entry);
                if(CommitLocal(next,"New "+noun+" added.")){list.SelectedItem=Entries(state,kind).First(item=>item.Id==entry.Id);name.Focus();name.SelectAll();}
            };
            delete.Click+=(_,__)=>
            {
                if(!Editable() || Selected(kind)==null)return;var id=Selected(kind)!.Id;var next=SnapshotStoryWorkshop();Entries(next,kind).RemoveAll(entry=>entry.Id==id);
                if(kind==StoryWorkshopEntryKind.Character)foreach(var entry in next.Characters.Concat(next.Beats).Concat(next.Clues))entry.CharacterIds.Remove(id);
                CommitLocal(next,"Card removed. Undo planning edit can restore it.");
            };
            if(links!=null)links.SelectionChanged+=(_,__)=>
            {
                if(assigning || !Editable())return;var next=SnapshotStoryWorkshop();var entry=Resolve(next,kind);if(entry==null)return;
                entry.CharacterIds=links.SelectedItems.Cast<StoryWorkshopEntry>().Select(character=>character.Id).ToList();CommitLocal(next,"Linked characters updated.",false);
            };
            if(kind!=StoryWorkshopEntryKind.Character)
            {
                var up=Named(kind==StoryWorkshopEntryKind.Beat?"WorkshopBeatUp":"WorkshopClueUp",Button("Move up"));
                var down=Named(kind==StoryWorkshopEntryKind.Beat?"WorkshopBeatDown":"WorkshopClueDown",Button("Move down"));management.Children.Add(up);management.Children.Add(down);
                editableControls.Add(up);editableControls.Add(down);
                void Move(int direction)
                {
                    if(!Editable() || Selected(kind)==null)return;var next=SnapshotStoryWorkshop();var entries=Entries(next,kind);var index=entries.FindIndex(entry=>entry.Id==Selected(kind)!.Id);var destination=index+direction;
                    if(index<0 || destination<0 || destination>=entries.Count)return;(entries[index],entries[destination])=(entries[destination],entries[index]);CommitLocal(next,"Storyboard order updated.");
                }
                up.Click+=(_,__)=>Move(-1);down.Click+=(_,__)=>Move(1);
            }
            return page;
        }
        Tab(tabs,"Characters",EntryPage(StoryWorkshopEntryKind.Character,characters,characterName,characterSaveName,addCharacter,deleteCharacter,characterFields));
        var storyboard=new TabControl{Margin=new Thickness(6)};Tab(tabs,"Storyboard",storyboard);
        Tab(storyboard,"Overview",overview);storyFields.Root.Margin=new Thickness(10);Tab(storyboard,"Story plan",storyFields.Root);
        Tab(storyboard,"Plot beats",EntryPage(StoryWorkshopEntryKind.Beat,beats,beatName,beatSaveName,addBeat,deleteBeat,beatFields,beatLinks));
        Tab(storyboard,"Clue ledger",EntryPage(StoryWorkshopEntryKind.Clue,clues,clueName,clueSaveName,addClue,deleteClue,clueFields,clueLinks));
        var questionPage=new Grid{Margin=new Thickness(10)};questionPage.ColumnDefinitions.Add(new ColumnDefinition());questionPage.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(12)});questionPage.ColumnDefinitions.Add(new ColumnDefinition());
        StackPanel QuestionPane(string title,TextBox box,Button save)
        {
            var pane=new StackPanel();pane.Children.Add(Label(title));pane.Children.Add(new TextBlock{Text="One item per line. Edit or remove questions as you settle them.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,8)});
            box.MinHeight=180;box.MaxHeight=350;pane.Children.Add(box);pane.Children.Add(save);editableControls.Add(save);return pane;
        }
        questionPage.Children.Add(QuestionPane("Open questions",questions,saveQuestions));
        var continuityPane=QuestionPane("Continuity and contradictions to resolve",contradictions,saveContradictions);Grid.SetColumn(continuityPane,2);questionPage.Children.Add(continuityPane);
        Tab(tabs,"Questions",new ScrollViewer{Content=questionPage,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        questions.IsReadOnly=!Editable();contradictions.IsReadOnly=!Editable();
        void SaveQuestions(bool continuity)
        {
            if(assigning || !Editable())return;var next=SnapshotStoryWorkshop();var box=continuity?contradictions:questions;
            var values=box.Text.Replace("\r\n","\n").Replace('\r','\n').Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).ToList();
            if(continuity)next.Contradictions=values;else next.OpenQuestions=values;
            CommitLocal(next,continuity?"Continuity questions updated.":"Open questions updated.",false);
        }
        questions.TextChanged+=(_,__)=>SaveQuestions(false);contradictions.TextChanged+=(_,__)=>SaveQuestions(true);
        saveQuestions.Click+=(_,__)=>SaveQuestions(false);saveContradictions.Click+=(_,__)=>SaveQuestions(true);

        string Configuration()
        {
            var language=$"{languageSettings.AllowAiLanguageServices}|{languageSettings.LanguageTag}|{languageSettings.WritingMode}|{languageSettings.CustomProfileName}";
            if(StoryWorkshopService.GeneratorOverride!=null)return "Test provider|"+language;
            var provider=AiManager.GetDefaultProvider();var profile=AiDirectService.GetProfile(provider);return provider+"|"+profile.Model+"|"+profile.HasKey+"|"+language;
        }
        void RefreshAvailability()
        {
            if(closed)return;
            try
            {
                var next=Configuration();if(configuration.Length>0 && configuration!=next)CancelRequest("AI settings changed; send again when ready.");configuration=next;
                unavailable=!languageSettings.AllowAiLanguageServices?"Enable AI language services in Language settings before sending a message.":null;
                if(StoryWorkshopService.GeneratorOverride==null)
                {
                    var provider=AiManager.GetDefaultProvider();
                    if(provider=="Copilot")unavailable="Choose ChatGPT, Claude or Gemini in AI settings for direct planning requests.";
                    else if(!AiDirectService.GetProfile(provider).HasKey)unavailable=$"Configure a {provider} API key in AI settings before sending a message.";
                }
            }
            catch(Exception ex){unavailable="AI settings could not be read: "+ex.Message;CancelRequest(unavailable);}
            questions.IsReadOnly=!Editable();contradictions.IsReadOnly=!Editable();UpdateButtons();
        }
        async Task SendAsync()
        {
            if(closed || busy || !Editable())return;RefreshAvailability();
            if(unavailable!=null){status.Text=unavailable;return;}var message=input.Text;if(string.IsNullOrWhiteSpace(message))return;
            var requested=SnapshotStoryWorkshop();var requestedRevision=storyWorkshopRevision;var requestedConfiguration=configuration;
            CancelRequest();var requestGeneration=++generation;cancellation=new CancellationTokenSource();var token=cancellation.Token;busy=true;UpdateButtons();status.Text="Building the story with you…";
            try
            {
                var result=await StoryWorkshopService.GenerateAsync(requested,message,token);
                if(closed || token.IsCancellationRequested || requestGeneration!=generation)return;RefreshAvailability();
                if(!Editable() || requestGeneration!=generation || configuration!=requestedConfiguration || storyWorkshopRevision!=requestedRevision || SnapshotStoryWorkshop().Id!=requested.Id)
                {status.Text="The planning record or AI settings changed. Your message is still here; send it again when ready.";return;}
                committing=true;
                try
                {
                    CommitStoryWorkshop(result.NextState,"Story Workshop updated.");undo.Add(requested);while(undo.Count>20)undo.RemoveAt(0);
                    RefreshAll();if(input.Text==message)input.Text="";
                    status.Text=$"Story updated: {result.AppliedChangeCount} notes added or revised; {result.DeferredChangeCount} possible conflicts kept for review. Proposed ideas remain marked in the storyboard.";
                }
                finally{committing=false;}
            }
            catch(OperationCanceledException){if(!closed && requestGeneration==generation)status.Text="Request cancelled. Your message is still here.";}
            catch(Exception ex){if(!closed && requestGeneration==generation)status.Text="Could not continue the workshop: "+ex.Message;}
            finally
            {
                if(!closed && requestGeneration==generation){busy=false;cancellation?.Dispose();cancellation=null;UpdateButtons();}
            }
        }
        send.Click+=async (_,__)=>await SendAsync();cancel.Click+=(_,__)=>{CancelRequest("Request cancelled. Your message is still here.");UpdateButtons();};
        input.TextChanged+=(_,__)=>UpdateButtons();input.PreviewKeyDown+=async (_,args)=>
        {if(args.Key==Key.Enter && (Keyboard.Modifiers&ModifierKeys.Control)!=0){args.Handled=true;await SendAsync();}};
        mode.SelectionChanged+=(_,__)=>
        {
            if(assigning || !Editable() || mode.SelectedItem is not WritingModeProfile selected)return;var next=SnapshotStoryWorkshop();next.WritingMode=selected.Mode;CommitLocal(next,"Workshop writing mode updated.");
        };
        characterVoice.Click+=(_,__)=>{if(Selected(StoryWorkshopEntryKind.Character) is { } character)OpenStoryWorkshopCharacterVoice(character);};
        undoPlanning.Click+=(_,__)=>
        {
            if(!Editable() || undo.Count==0)return;var previous=undo[^1];undo.RemoveAt(undo.Count-1);CommitLocal(previous,"Planning edit undone.",true,false);
        };
        clearConversation.Click+=(_,__)=>
        {if(!Editable())return;var next=SnapshotStoryWorkshop();next.Conversation.Clear();CommitLocal(next,"Conversation cleared; character and story notes retained.");};
        languageSetup.Click+=(_,__)=>{CancelRequest("Language settings opened; send again afterwards.");LanguageSettings_Click(languageSetup,new RoutedEventArgs());RefreshAvailability();};
        aiSetup.Click+=(_,__)=>{CancelRequest("AI settings opened; send again afterwards.");AiManager_Click(aiSetup,new RoutedEventArgs());RefreshAvailability();};
        close.Click+=(_,__)=>w.Close();
        void MetadataChanged()
        {if(closed || committing)return;CancelRequest("Planning changed; any pending reply was cancelled.");undo.Clear();RefreshAll();}
        void DocumentChanged(){if(!closed)w.Close();}
        StoryWorkshopChanged+=MetadataChanged;StoryWorkshopDocumentChanged+=DocumentChanged;
        var readOnlyDescriptor=DependencyPropertyDescriptor.FromProperty(TextBoxBase.IsReadOnlyProperty,typeof(RichTextBox));
        EventHandler readOnlyChanged=(_,__)=>{if(!Editable())CancelRequest("The document is read-only; the planning record is available for reference.");RefreshAvailability();};
        readOnlyDescriptor?.AddValueChanged(Editor,readOnlyChanged);
        DependencyPropertyChangedEventHandler enabledChanged=(_,__)=>
        {if(!Editable())CancelRequest("Editing is unavailable; the planning record is available for reference.");RefreshAvailability();};
        Editor.IsEnabledChanged+=enabledChanged;
        var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromSeconds(1)};timer.Tick+=(_,__)=>RefreshAvailability();timer.Start();w.Activated+=(_,__)=>RefreshAvailability();
        w.Closed+=(_,__)=>
        {
            closed=true;CancelRequest();timer.Stop();StoryWorkshopChanged-=MetadataChanged;StoryWorkshopDocumentChanged-=DocumentChanged;readOnlyDescriptor?.RemoveValueChanged(Editor,readOnlyChanged);Editor.IsEnabledChanged-=enabledChanged;
            if(ReferenceEquals(storyWorkshopWindow,w))storyWorkshopWindow=null;
        };
        RefreshAll();assigning=false;RefreshAvailability();
        status.Text=Editor.IsReadOnly?"Read-only document: use the workshop as a reference.":unavailable??"How can I help you? Send a premise, a character, an ending or the question you are trying to solve.";
        w.Show();
    }

    sealed class WorkshopFieldPanel
    {
        internal Grid Root { get; }
        internal Action Refresh { get; }
        internal Action UpdateButtons { get; }
        internal WorkshopFieldPanel(Grid root,Action refresh,Action buttons){Root=root;Refresh=refresh;UpdateButtons=buttons;}
    }
}
