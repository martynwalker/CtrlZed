using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CtrlZed;

public partial class MainWindow
{
    void ShowStyleEditor()
    {
        var working=StyleSetStore.Clone(styleSets.TryGetValue(activeStyleSet,out var stored)
            ?stored:StyleSetStore.Defaults()["Default"]);
        working=StyleSetStore.NormalizeSet(working);
        var savedSnapshot=JsonSerializer.Serialize(working);
        string? selectedStyle=null;
        var loading=false;
        var formEdited=false;
        var closingAfterDelete=false;
        var w=new Window
        {
            Title="CtrlZed Styles",Owner=this,Width=930,Height=740,MinWidth=760,MinHeight=560,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,Icon=Branding.CreateIconImageSource()
        };
        var root=new Grid{Margin=new Thickness(14)};
        root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(200)});
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});

        var selector=new DockPanel{Margin=new Thickness(0,0,16,0)};
        var label=new TextBlock{Text="Style to edit",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6)};
        DockPanel.SetDock(label,Dock.Top);selector.Children.Add(label);
        var styleActions=new StackPanel{Margin=new Thickness(0,8,0,0)};
        var newStyle=StyleEditorButton("New Style…");
        var renameStyle=StyleEditorButton("Rename Style…");
        var deleteStyle=StyleEditorButton("Delete Style");
        styleActions.Children.Add(newStyle);styleActions.Children.Add(renameStyle);styleActions.Children.Add(deleteStyle);
        DockPanel.SetDock(styleActions,Dock.Bottom);selector.Children.Add(styleActions);
        var stylePicker=new ListBox{SelectionMode=SelectionMode.Single,Padding=new Thickness(3),HorizontalContentAlignment=HorizontalAlignment.Stretch};
        var itemStyle=new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(6,4,6,4)));
        stylePicker.ItemContainerStyle=itemStyle;
        ScrollViewer.SetVerticalScrollBarVisibility(stylePicker,ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(stylePicker,ScrollBarVisibility.Disabled);
        System.Windows.Automation.AutomationProperties.SetName(stylePicker,"Style to edit");
        selector.Children.Add(stylePicker);root.Children.Add(selector);

        var form=new Grid();
        form.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(176)});
        form.ColumnDefinitions.Add(new ColumnDefinition());
        var row=0;
        void Row(string caption,FrameworkElement control)
        {
            form.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            AddRow(form,row++,caption,control);
        }
        void Note(string text,bool section=false)
        {
            form.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var note=new TextBlock
            {
                Text=text,TextWrapping=TextWrapping.Wrap,
                Foreground=section?Brushes.Black:Brushes.DimGray,
                FontWeight=section?FontWeights.SemiBold:FontWeights.Normal,
                Margin=new Thickness(0,section?14:8,0,section?8:4)
            };
            Grid.SetRow(note,row++);Grid.SetColumnSpan(note,2);form.Children.Add(note);
        }
        TextBox NumberBox()=>new(){Margin=new Thickness(0,3,0,8)};
        var setName=new TextBox{Text=working.Name,IsReadOnly=true,Margin=new Thickness(0,3,0,8)};
        var fonts=Fonts.SystemFontFamilies.OrderBy(font=>font.Source).ToList();
        var font=new ComboBox{ItemsSource=fonts,DisplayMemberPath="Source",IsEditable=true,Margin=new Thickness(0,3,0,8)};
        var size=NumberBox();
        var colourPanel=new DockPanel{Margin=new Thickness(0,3,0,8)};
        var pickColour=new Button{Content="Pick Colour…",MinWidth=100,Margin=new Thickness(6,0,0,0)};
        DockPanel.SetDock(pickColour,Dock.Right);colourPanel.Children.Add(pickColour);
        var swatch=new Border{Width=26,BorderBrush=Brushes.Gray,BorderThickness=new Thickness(1),Margin=new Thickness(6,0,0,0)};
        DockPanel.SetDock(swatch,Dock.Right);colourPanel.Children.Add(swatch);
        var colour=new ComboBox{ItemsSource=new[]{"#000000","#333333","#666666","#808080","#FFFFFF","#8B0000","#CC0000","#006400","#00008B","#1E4D7B","#4B0082"},IsEditable=true};
        colourPanel.Children.Add(colour);
        colour.AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler((_,__)=>swatch.Background=BrushFromHex(colour.Text)));
        var emphasis=new WrapPanel{Margin=new Thickness(0,3,0,8)};
        var bold=new CheckBox{Content="Bold",Margin=new Thickness(0,0,16,0)};
        var italic=new CheckBox{Content="Italic",Margin=new Thickness(0,0,16,0)};
        var underline=new CheckBox{Content="Underline"};
        emphasis.Children.Add(bold);emphasis.Children.Add(italic);emphasis.Children.Add(underline);
        var alignment=new ComboBox{ItemsSource=new[]{"Left","Centre","Right","Justify"},Margin=new Thickness(0,3,0,8)};
        var following=new ComboBox{Margin=new Thickness(0,3,0,8),ToolTip="The style used for the next paragraph when you press Return."};
        var outline=new ComboBox
        {
            ItemsSource=Enumerable.Range(0,10).Select(level=>level==0?"Body text":$"Heading level {level}").ToArray(),
            Margin=new Thickness(0,3,0,8),ToolTip="Heading levels appear in document navigation and can supply the suggested filename."
        };
        Row("Style set",setName);Row("Font",font);Row("Size (pt)",size);Row("Colour",colourPanel);
        Row("Emphasis",emphasis);Row("Alignment",alignment);Row("Following style",following);Row("Outline level",outline);
        Note("Paragraph",true);
        var left=NumberBox();var right=NumberBox();var first=NumberBox();var before=NumberBox();var after=NumberBox();
        first.ToolTip="Use a negative value for a hanging indent.";
        Row("Left indent (pt)",left);Row("Right indent (pt)",right);Row("First line indent (pt)",first);
        Row("Space before (pt)",before);Row("Space after (pt)",after);
        var linePanel=new DockPanel{Margin=new Thickness(0,3,0,8)};
        var lineHeight=new TextBox{Width=86,Margin=new Thickness(8,0,0,0),ToolTip="Exact line height in points."};
        DockPanel.SetDock(lineHeight,Dock.Right);linePanel.Children.Add(lineHeight);
        var lineMode=new ComboBox{ItemsSource=new[]{"Automatic","Exact height (pt)"}};
        linePanel.Children.Add(lineMode);
        lineMode.SelectionChanged+=(_,__)=>lineHeight.IsEnabled=lineMode.SelectedIndex==1;
        Row("Line spacing",linePanel);
        var together=new CheckBox{Content="Keep lines together",Margin=new Thickness(0,4,0,8)};
        var withNext=new CheckBox{Content="Keep with next paragraph",Margin=new Thickness(0,4,0,8)};
        var pageBreak=new CheckBox{Content="Start on a new page",Margin=new Thickness(0,4,0,8)};
        Row("Page layout",together);Row("",withNext);Row("",pageBreak);
        Note("New Style copies the selected style. Save Set stores all edits in this set; New Set copies the complete set.");
        Note("The six standard styles are retained for existing documents and macros.");
        var scroll=new ScrollViewer{Content=form,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,8,0)};
        Grid.SetColumn(scroll,1);root.Children.Add(scroll);

        var footer=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        var newSet=StyleEditorButton("New Set…");var renameSet=StyleEditorButton("Rename Set…");var deleteSet=StyleEditorButton("Delete Set");
        var saveSet=StyleEditorButton("Save Set");var close=StyleEditorButton("Close");
        foreach(var button in new[]{newSet,renameSet,deleteSet,saveSet,close})footer.Children.Add(button);
        Grid.SetRow(footer,1);Grid.SetColumnSpan(footer,2);root.Children.Add(footer);

        bool IsBuiltIn(string? name)=>StyleSetStore.ParagraphStyleNames.Contains(name??"",StringComparer.OrdinalIgnoreCase);
        string FormatPt(double value)=>(value*72/96).ToString("0.#####",CultureInfo.CurrentCulture);
        double ReadNumber(TextBox box,string caption,bool nonNegative=true)
        {
            if(!double.TryParse(box.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out var value) || !double.IsFinite(value) || (nonNegative && value<0))
                throw new InvalidOperationException($"Enter a valid {(nonNegative?"non-negative ":"")}number for {caption}.");
            return value;
        }
        void LoadStyle(string name)
        {
            if(!working.Styles.TryGetValue(name,out var spec))return;
            loading=true;
            try
            {
                selectedStyle=name;
                font.SelectedItem=fonts.FirstOrDefault(f=>string.Equals(f.Source,spec.FontFamily,StringComparison.OrdinalIgnoreCase));
                font.Text=spec.FontFamily;size.Text=spec.SizePt.ToString("0.#####",CultureInfo.CurrentCulture);colour.Text=spec.Colour;
                bold.IsChecked=spec.Bold;italic.IsChecked=spec.Italic;underline.IsChecked=spec.Underline;
                alignment.SelectedItem=spec.Alignment is "Center" or "Centre"?"Centre":spec.Alignment;
                following.SelectedItem=StyleSetStore.NormalizeFollowingStyle(spec.FollowingStyle,working);
                outline.SelectedIndex=spec.OutlineLevel;
                left.Text=FormatPt(spec.MarginLeft);right.Text=FormatPt(spec.MarginRight);first.Text=FormatPt(spec.FirstLineIndent);
                before.Text=FormatPt(spec.MarginTop);after.Text=FormatPt(spec.MarginBottom);
                lineMode.SelectedIndex=spec.LineHeight>0?1:0;
                lineHeight.Text=FormatPt(spec.LineHeight>0?spec.LineHeight:spec.SizePt*96/72*1.2);
                together.IsChecked=spec.KeepTogether;withNext.IsChecked=spec.KeepWithNext;pageBreak.IsChecked=spec.BreakPageBefore;
                renameStyle.IsEnabled=deleteStyle.IsEnabled=!IsBuiltIn(name);
                renameSet.IsEnabled=deleteSet.IsEnabled=!string.Equals(setName.Text,"Default",StringComparison.OrdinalIgnoreCase);
            }
            finally { loading=false; }
            formEdited=false;
        }
        void SaveCurrentStyle()
        {
            if(selectedStyle==null || !formEdited)return;
            var spec=StyleSetStore.Clone(working.Styles[selectedStyle]);
            spec.FontFamily=font.Text.Trim();
            spec.SizePt=ReadNumber(size,"font size");
            spec.Colour=string.IsNullOrWhiteSpace(colour.Text)?"#000000":colour.Text.Trim();
            spec.Bold=bold.IsChecked==true;spec.Italic=italic.IsChecked==true;spec.Underline=underline.IsChecked==true;
            spec.Alignment=alignment.SelectedItem as string??"Left";
            spec.FollowingStyle=StyleSetStore.NormalizeFollowingStyle(following.SelectedItem as string,working);
            spec.OutlineLevel=Math.Max(0,outline.SelectedIndex);
            spec.MarginLeft=ReadNumber(left,"left indent")*96/72;spec.MarginRight=ReadNumber(right,"right indent")*96/72;
            spec.FirstLineIndent=ReadNumber(first,"first line indent",false)*96/72;
            spec.MarginTop=ReadNumber(before,"space before")*96/72;spec.MarginBottom=ReadNumber(after,"space after")*96/72;
            spec.LineHeight=lineMode.SelectedIndex==1?ReadNumber(lineHeight,"line height")*96/72:0;
            if(lineMode.SelectedIndex==1 && spec.LineHeight<=0)throw new InvalidOperationException("Exact line height must be greater than zero.");
            spec.KeepTogether=together.IsChecked==true;spec.KeepWithNext=withNext.IsChecked==true;spec.BreakPageBefore=pageBreak.IsChecked==true;
            StyleSetStore.ValidateStyle(spec);working.Styles[selectedStyle]=spec;formEdited=false;
        }
        void RefreshChoices(string? requested)
        {
            loading=true;
            try
            {
                var names=StyleSetStore.StyleNames(working);
                stylePicker.ItemsSource=names;following.ItemsSource=names;
                stylePicker.SelectedItem=StyleSetStore.ResolveStyleName(working,requested)??"Normal";
            }
            finally { loading=false; }
            LoadStyle((string)stylePicker.SelectedItem);
        }
        void ShowError(Exception error,string title="Styles")=>MessageBox.Show(w,error.Message,title,MessageBoxButton.OK,MessageBoxImage.Warning);
        bool TrySaveCurrent()
        {
            try { SaveCurrentStyle();return true; }
            catch(Exception error) { ShowError(error);return false; }
        }
        void PersistSet(string name,string? previous=null)
        {
            SaveCurrentStyle();
            var saved=StyleSetStore.NormalizeSet(StyleSetStore.Clone(working,name));
            var updated=new Dictionary<string,StyleSetStore.StyleSet>(styleSets,StringComparer.OrdinalIgnoreCase);
            if(previous!=null)updated.Remove(previous);
            updated[name]=saved;
            StyleSetStore.Save(updated);
            styleSets.Clear();foreach(var pair in updated)styleSets.Add(pair.Key,pair.Value);
            working=StyleSetStore.Clone(saved);activeStyleSet=name;setName.Text=name;
            savedSnapshot=JsonSerializer.Serialize(working);
            RefreshStyleSetBox();RefreshParagraphStyleBox();SaveAppSettings();RefreshChoices(selectedStyle);
        }
        stylePicker.SelectionChanged+=(_,__)=>
        {
            if(loading)return;
            var requested=stylePicker.SelectedItem as string;
            try { SaveCurrentStyle();if(requested!=null)LoadStyle(requested); }
            catch(Exception error)
            {
                loading=true;stylePicker.SelectedItem=selectedStyle;loading=false;ShowError(error);
            }
        };
        void MarkFormEdited(object? sender,RoutedEventArgs e){if(!loading)formEdited=true;}
        foreach(var box in new[]{size,left,right,first,before,after,lineHeight})box.TextChanged+=MarkFormEdited;
        foreach(var box in new[]{font,colour,alignment,following,outline,lineMode})box.SelectionChanged+=MarkFormEdited;
        font.AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler(MarkFormEdited));
        colour.AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler(MarkFormEdited));
        foreach(var box in new[]{bold,italic,underline,together,withNext,pageBreak})
        {
            box.Checked+=MarkFormEdited;box.Unchecked+=MarkFormEdited;
        }
        pickColour.Click+=(_,__)=>{var picked=PickColour(w,colour.Text);if(picked!=null)colour.Text=picked;};
        newStyle.Click+=(_,__)=>
        {
            if(!TrySaveCurrent())return;
            var requested=ShowMacroInput("New style name","",w,"New Style")?.Trim();
            if(requested==null)return;
            try
            {
                var name=StyleSetStore.ValidateStyleName(requested,working);
                working.Styles.Add(name,StyleSetStore.CreateStyle(name,selectedStyle==null?null:working.Styles[selectedStyle]));
                RefreshChoices(name);
            }
            catch(Exception error){ShowError(error,"New Style");}
        };
        renameStyle.Click+=(_,__)=>
        {
            if(selectedStyle==null || IsBuiltIn(selectedStyle) || !TrySaveCurrent())return;
            var oldName=selectedStyle;
            var requested=ShowMacroInput("New style name",oldName,w,"Rename Style")?.Trim();
            if(requested==null || string.Equals(requested,oldName,StringComparison.Ordinal))return;
            try
            {
                var spec=working.Styles[oldName];
                var names=working.Styles.Keys.Where(name=>!string.Equals(name,oldName,StringComparison.OrdinalIgnoreCase))
                    .Concat(working.Aliases.Where(alias=>!string.Equals(alias.Value,spec.Id,StringComparison.OrdinalIgnoreCase)).Select(alias=>alias.Key))
                    .Concat(working.Styles.Values.Where(item=>!string.Equals(item.Id,spec.Id,StringComparison.OrdinalIgnoreCase)).Select(item=>item.Id));
                var name=StyleSetStore.ValidateStyleName(requested,names);
                working.Styles.Remove(oldName);working.Styles.Add(name,spec);
                working.Aliases.Remove(name);
                if(!string.Equals(name,oldName,StringComparison.OrdinalIgnoreCase))working.Aliases[oldName]=spec.Id;
                foreach(var item in working.Styles.Values)
                    if(string.Equals(item.FollowingStyle,oldName,StringComparison.OrdinalIgnoreCase))item.FollowingStyle=name;
                selectedStyle=null;RefreshChoices(name);
            }
            catch(Exception error){ShowError(error,"Rename Style");}
        };
        deleteStyle.Click+=(_,__)=>
        {
            if(selectedStyle==null || IsBuiltIn(selectedStyle) || !TrySaveCurrent())return;
            var name=selectedStyle;var spec=working.Styles[name];
            if(IsParagraphStyleInUse(spec.Id,name))
            {
                MessageBox.Show(w,"This style is used in the current document. Apply another style to those paragraphs before deleting it.","Delete Style",MessageBoxButton.OK,MessageBoxImage.Information);
                return;
            }
            if(MessageBox.Show(w,$"Delete the style “{name}” from this set? Paragraphs which followed it will use Normal instead.","Delete Style",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            var removedNames=working.Aliases.Where(alias=>string.Equals(alias.Value,spec.Id,StringComparison.OrdinalIgnoreCase)).Select(alias=>alias.Key).Append(name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            working.Styles.Remove(name);
            foreach(var alias in working.Aliases.Where(alias=>string.Equals(alias.Value,spec.Id,StringComparison.OrdinalIgnoreCase)).Select(alias=>alias.Key).ToList())working.Aliases.Remove(alias);
            foreach(var item in working.Styles.Values)if(removedNames.Contains(item.FollowingStyle))item.FollowingStyle="Normal";
            selectedStyle=null;RefreshChoices("Normal");
        };
        void ChangeSetName(bool rename)
        {
            if(!TrySaveCurrent())return;
            var previous=setName.Text;
            var requested=ShowMacroInput("Style set name",rename?previous:"",w,rename?"Rename Set":"New Set")?.Trim();
            if(requested==null)return;
            if(requested.Length==0 || (styleSets.ContainsKey(requested) && !(rename && string.Equals(previous,requested,StringComparison.OrdinalIgnoreCase))))
            {
                MessageBox.Show(w,"Enter a unique style-set name.","Styles",MessageBoxButton.OK,MessageBoxImage.Warning);return;
            }
            try { PersistSet(requested,rename?previous:null); }
            catch(Exception error){ShowError(error,"Save style set");}
        }
        newSet.Click+=(_,__)=>ChangeSetName(false);renameSet.Click+=(_,__)=>ChangeSetName(true);
        saveSet.Click+=(_,__)=>
        {
            try { PersistSet(setName.Text);MessageBox.Show(w,$"Style set “{setName.Text}” has been saved.","Styles",MessageBoxButton.OK,MessageBoxImage.Information); }
            catch(Exception error){ShowError(error,"Save style set");}
        };
        deleteSet.Click+=(_,__)=>
        {
            var name=setName.Text;
            if(string.Equals(name,"Default",StringComparison.OrdinalIgnoreCase))return;
            if(MessageBox.Show(w,$"Delete the style set “{name}”? The Default set will become active.","Delete Set",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            try
            {
                var updated=new Dictionary<string,StyleSetStore.StyleSet>(styleSets,StringComparer.OrdinalIgnoreCase);updated.Remove(name);
                StyleSetStore.Save(updated);styleSets.Clear();foreach(var pair in updated)styleSets.Add(pair.Key,pair.Value);
                activeStyleSet="Default";RefreshStyleSetBox();RefreshParagraphStyleBox();SaveAppSettings();closingAfterDelete=true;w.Close();
            }
            catch(Exception error){ShowError(error,"Delete Set");}
        };
        close.Click+=(_,__)=>w.Close();
        w.Closing+=(_,e)=>
        {
            if(closingAfterDelete)return;
            if(!TrySaveCurrent()){e.Cancel=true;return;}
            if(JsonSerializer.Serialize(working)==savedSnapshot)return;
            var choice=MessageBox.Show(w,"Save changes to this style set?","Styles",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);
            if(choice==MessageBoxResult.Cancel){e.Cancel=true;return;}
            if(choice==MessageBoxResult.Yes)
            {
                try { PersistSet(setName.Text); }
                catch(Exception error){ShowError(error,"Save style set");e.Cancel=true;}
            }
        };
        var initial=StyleBox.SelectedItem is ComboBoxItem item?item.Content as string:StyleBox.SelectedItem as string;
        RefreshChoices(initial??"Normal");w.Content=root;w.ShowDialog();
    }

    static Button StyleEditorButton(string text)=>new(){Content=text,MinWidth=96,Padding=new Thickness(9,4,9,4),Margin=new Thickness(3)};
}
