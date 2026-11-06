$ErrorActionPreference = 'Stop'

function Read-Text([string]$Path) {
    return [System.IO.File]::ReadAllText((Resolve-Path $Path))
}

function Write-Text([string]$Path,[string]$Content) {
    [System.IO.File]::WriteAllText((Resolve-Path $Path),$Content,[System.Text.UTF8Encoding]::new($false))
}

function Replace-Exact([string]$Path,[string]$Old,[string]$New,[string]$Label) {
    $content=Read-Text $Path
    if(-not $content.Contains($Old)) { throw "Patch point not found: $Label in $Path" }
    $content=$content.Replace($Old,$New)
    Write-Text $Path $content
}

# AI Macro Assistant: visible message and owned indeterminate progress dialogue.
$old=@'
            generate.IsEnabled=false;
            heading.Text=(macroMode?"AI Macro Assistant":"AI Writing & Research Assistant")+" — working…";
            try
            {
                result.Text=await AiDirectService.GenerateAsync(name,BuildPrompt(name),liveWeb.IsChecked==true);
                heading.Text=macroMode?"AI Macro Assistant — result ready":"AI Writing & Research Assistant — result ready";
                SetMessage("Result generated successfully.");
            }
            catch(Exception ex)
            {
                heading.Text=macroMode?"AI Macro Assistant":"AI Writing & Research Assistant";
                SetMessage("Direct AI failed: "+ex.Message,true);
                MessageBox.Show(ex.Message,"Direct AI",MessageBoxButton.OK,MessageBoxImage.Error);
            }
            finally{generate.IsEnabled=true;}
'@
$new=@'
            generate.IsEnabled=false;
            free.IsEnabled=false;
            signed.IsEnabled=false;
            paste.IsEnabled=false;
            saveDefault.IsEnabled=false;
            insert.IsEnabled=false;
            replace.IsEnabled=false;
            copy.IsEnabled=false;
            heading.Text=(macroMode?"AI Macro Assistant":"AI Writing & Research Assistant")+" — working…";
            SetMessage(macroMode?"Generating your macro…":"Generating your document result…");

            var progressText=macroMode?"Generating your macro…":"Generating your document result…";
            var progressWindow=new Window
            {
                Title="CtrlZed AI — working",
                Owner=w,
                Width=430,
                Height=155,
                ResizeMode=ResizeMode.NoResize,
                WindowStartupLocation=WindowStartupLocation.CenterOwner,
                ShowInTaskbar=false,
                Icon=Branding.CreateIconImageSource()
            };
            var progressPanel=new StackPanel{Margin=new Thickness(20)};
            progressPanel.Children.Add(new TextBlock
            {
                Text=progressText,
                FontSize=16,
                FontWeight=FontWeights.SemiBold,
                Margin=new Thickness(0,0,0,14)
            });
            progressPanel.Children.Add(new ProgressBar
            {
                IsIndeterminate=true,
                Height=18,
                Foreground=new SolidColorBrush(Color.FromRgb(55,150,85)),
                Background=new SolidColorBrush(Color.FromRgb(220,232,222))
            });
            progressPanel.Children.Add(new TextBlock
            {
                Text="The request is running. CtrlZed will return the result here when it is ready.",
                TextWrapping=TextWrapping.Wrap,
                Foreground=Brushes.DimGray,
                Margin=new Thickness(0,10,0,0)
            });
            progressWindow.Content=progressPanel;
            progressWindow.Show();

            try
            {
                result.Text=await AiDirectService.GenerateAsync(name,BuildPrompt(name),liveWeb.IsChecked==true);
                heading.Text=macroMode?"AI Macro Assistant — result ready":"AI Writing & Research Assistant — result ready";
                SetMessage("Result generated successfully.");
            }
            catch(Exception ex)
            {
                heading.Text=macroMode?"AI Macro Assistant":"AI Writing & Research Assistant";
                SetMessage("Direct AI failed: "+ex.Message,true);
                MessageBox.Show(ex.Message,"Direct AI",MessageBoxButton.OK,MessageBoxImage.Error);
            }
            finally
            {
                if(progressWindow.IsVisible)progressWindow.Close();
                generate.IsEnabled=true;
                free.IsEnabled=true;
                signed.IsEnabled=true;
                paste.IsEnabled=true;
                saveDefault.IsEnabled=true;
                insert.IsEnabled=true;
                replace.IsEnabled=!macroMode && !string.IsNullOrWhiteSpace(selectedText);
                copy.IsEnabled=true;
                w.Activate();
            }
'@
Replace-Exact 'AiManager.cs' $old $new 'AI direct progress'

# Generated compiled macros must expose a visible provider-secret constant.
$old=@'
- Never embed API keys, passwords, tokens or other secrets in source. If the user has not explicitly named a secret, use CtrlZedSecrets.Get(CtrlZedSecrets.DefaultName(""ProviderName"")) at runtime. CtrlZed's defaults are ChatGPTKey, GeminiKey and ClaudeKey. Tell the user which provider default is required, but never invent a different secret name.
'@
$new=@'
- Never embed API keys, passwords, tokens or other secrets in source.
- When this macro uses an AI service, declare one clearly visible constant near the top of the macro class, for example: public const string AI_API_KEY = ""ChatGPTKey""; Choose ChatGPTKey, GeminiKey or ClaudeKey to match the provider, then retrieve it only with CtrlZedSecrets.Get(AI_API_KEY). Do not bury the secret name inside an HTTP helper or method.
'@
Replace-Exact 'AiResultRenderer.cs' $old $new 'C# AI_API_KEY prompt'

$old=@'
- Never embed API keys, passwords, tokens or other secrets in source. Use CtrlZedSecrets.Get(""SecretName"") at runtime and tell the user which named secret must be configured in Macro Manager → Secrets.
'@
$new=@'
- Never embed API keys, passwords, tokens or other secrets in source.
- When this macro uses an AI service, declare one clearly visible constant near the top of the Module or Class, for example: Public Const AI_API_KEY As String = ""ChatGPTKey"". Choose ChatGPTKey, GeminiKey or ClaudeKey to match the provider, then retrieve it only with CtrlZedSecrets.Get(AI_API_KEY). Do not bury the secret name inside an HTTP helper or method.
'@
Replace-Exact 'AiResultRenderer.cs' $old $new 'VB AI_API_KEY prompt'

$old=@'
        source=(source??"").Trim();
        instruction=(instruction??"").Trim();
        if(string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(instruction))return source;
'@
$new=@'
        source=(source??"").Trim();
        instruction=(instruction??"").Trim();
        source=EnsureVisibleAiApiKeyConstant(source,macroExtension,provider);
        if(string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(instruction))return source;
'@
Replace-Exact 'AiResultRenderer.cs' $old $new 'AI constant normalisation call'

$marker=@'
    static string TrimContext(string text,int max)
'@
$helper=@'
    static string EnsureVisibleAiApiKeyConstant(string source,string macroExtension,string provider)
    {
        macroExtension=(macroExtension??"").ToLowerInvariant();
        if(macroExtension is not ".cs" and not ".vb")return source;
        if(source.Contains("AI_API_KEY",StringComparison.OrdinalIgnoreCase))return source;

        var usesAiSecret=source.Contains("CtrlZedSecrets",StringComparison.OrdinalIgnoreCase) ||
                         source.Contains("CtrlZedAi.GetModel",StringComparison.OrdinalIgnoreCase);
        if(!usesAiSecret)return source;

        var key=source.Contains("Gemini",StringComparison.OrdinalIgnoreCase)?"GeminiKey":
                source.Contains("Claude",StringComparison.OrdinalIgnoreCase) || source.Contains("Anthropic",StringComparison.OrdinalIgnoreCase)?"ClaudeKey":
                source.Contains("ChatGPT",StringComparison.OrdinalIgnoreCase) || source.Contains("OpenAI",StringComparison.OrdinalIgnoreCase)?"ChatGPTKey":
                provider.Equals("Gemini",StringComparison.OrdinalIgnoreCase)?"GeminiKey":
                provider.Equals("Claude",StringComparison.OrdinalIgnoreCase)?"ClaudeKey":"ChatGPTKey";

        var knownSecretPattern=@"CtrlZedSecrets\.Get\(\s*(?:CtrlZedSecrets\.DefaultName\(\s*""(?:ChatGPT|Gemini|Claude)""\s*\)|""(?:ChatGPTKey|GeminiKey|ClaudeKey)"")\s*\)";
        source=Regex.Replace(source,knownSecretPattern,"CtrlZedSecrets.Get(AI_API_KEY)",RegexOptions.IgnoreCase);

        if(macroExtension==".cs")
        {
            var match=Regex.Match(source,@"\b(?:public\s+)?(?:static\s+)?class\s+\w+[^\{]*\{",RegexOptions.IgnoreCase);
            if(!match.Success)return source;
            return source.Insert(match.Index+match.Length,$"{Environment.NewLine}    public const string AI_API_KEY = \"{key}\";{Environment.NewLine}");
        }

        var vbMatch=Regex.Match(source,@"(?im)^\s*(?:Public\s+)?(?:Module|Class)\s+\w+[^\r\n]*");
        if(!vbMatch.Success)return source;
        return source.Insert(vbMatch.Index+vbMatch.Length,$"{Environment.NewLine}    Public Const AI_API_KEY As String = \"{key}\"{Environment.NewLine}");
    }

    static string TrimContext(string text,int max)
'@
Replace-Exact 'AiResultRenderer.cs' $marker $helper 'AI constant helper'

# Grammar AI requests show activity and automatically open/activate the review pane when results arrive.
$old=@'
        Status.Text="AI is reviewing the current paragraph for suggestions…";

        try
'@
$new=@'
        BeginActivity("AI is reviewing the current paragraph…");

        try
'@
Replace-Exact 'MainWindow.GrammarReview.Core.cs' $old $new 'grammar activity start'

$old=@'
            Status.Text=findings.Count==0
                ?"AI found no sufficiently useful contextual grammar suggestions for this paragraph."
                :$"AI added {findings.Count} contextual suggestion{(findings.Count==1?"":"s")}.";
'@
$new=@'
            Status.Text=findings.Count==0
                ?"AI found no sufficiently useful contextual grammar suggestions for this paragraph."
                :$"AI added {findings.Count} contextual suggestion{(findings.Count==1?"":"s")}.";
            if(findings.Count>0)
            {
                ShowGrammarReviewWindow();
                grammarReviewWindow?.Activate();
            }
'@
Replace-Exact 'MainWindow.GrammarReview.Core.cs' $old $new 'open grammar review after AI'

$old=@'
        catch(Exception ex)
        {
            MessageBox.Show(ex.Message,"AI grammar review",MessageBoxButton.OK,MessageBoxImage.Error);
            Status.Text="AI grammar review failed.";
        }
    }
}
'@
$new=@'
        catch(Exception ex)
        {
            MessageBox.Show(ex.Message,"AI grammar review",MessageBoxButton.OK,MessageBoxImage.Error);
            Status.Text="AI grammar review failed.";
        }
        finally{EndActivity();}
    }
}
'@
Replace-Exact 'MainWindow.GrammarReview.Core.cs' $old $new 'grammar activity finish'

# DOCX can be read from a shared snapshot while Word has it open.
$old=@'
        using var word=WordprocessingDocument.Open(path,false);
        var result=new FlowDocument();
'@
$new=@'
        using var sourceStream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        using var snapshot=new MemoryStream();
        sourceStream.CopyTo(snapshot);
        snapshot.Position=0;
        using var word=WordprocessingDocument.Open(snapshot,false);
        var result=new FlowDocument();
'@
Replace-Exact 'DocxInterop.cs' $old $new 'shared DOCX snapshot'

# Paragraph background shading must be represented in paragraph properties.
$old=@'
        var spacing=new W.SpacingBetweenLines();
'@
$new=@'
        if(paragraph.Background is SolidColorBrush paragraphBackground && paragraphBackground.Color.A>0)
            properties.Append(new W.Shading
            {
                Val=W.ShadingPatternValues.Clear,
                Color="auto",
                Fill=Rgb(paragraphBackground.Color)
            });

        var spacing=new W.SpacingBetweenLines();
'@
Replace-Exact 'DocxInterop.cs' $old $new 'paragraph shading export'

# Use effective TextRange formatting, including formatting inherited through Span elements, and emit Word highlight where possible.
$old=@'
    static W.Run ToWordRun(Run run)
    {
        var properties=new W.RunProperties();
        if(!string.IsNullOrWhiteSpace(run.FontFamily?.Source))
            properties.Append(new W.RunFonts{Ascii=run.FontFamily.Source,HighAnsi=run.FontFamily.Source});
        if(run.FontWeight.ToOpenTypeWeight()>=600)properties.Append(new W.Bold());
        if(run.FontStyle==FontStyles.Italic)properties.Append(new W.Italic());
        if(HasDecoration(run.TextDecorations,TextDecorationLocation.Strikethrough))properties.Append(new W.Strike());
        if(run.Foreground is SolidColorBrush foreground && foreground.Color.A>0)
            properties.Append(new W.Color{Val=Rgb(foreground.Color)});
        if(!double.IsNaN(run.FontSize) && run.FontSize>0)
            properties.Append(new W.FontSize{Val=((int)Math.Round(run.FontSize*72/96*2)).ToString()});
        if(HasDecoration(run.TextDecorations,TextDecorationLocation.Underline))
            properties.Append(new W.Underline{Val=W.UnderlineValues.Single});
        if(run.Background is SolidColorBrush background && background.Color.A>0)
            properties.Append(new W.Shading
            {
                Val=W.ShadingPatternValues.Clear,
                Color="auto",
                Fill=Rgb(background.Color)
            });
        if(run.BaselineAlignment==BaselineAlignment.Superscript)
            properties.Append(new W.VerticalTextAlignment{Val=W.VerticalPositionValues.Superscript});
        else if(run.BaselineAlignment==BaselineAlignment.Subscript)
            properties.Append(new W.VerticalTextAlignment{Val=W.VerticalPositionValues.Subscript});
        if(!string.IsNullOrWhiteSpace(run.Language?.IetfLanguageTag))
            properties.Append(new W.Languages{Val=run.Language.IetfLanguageTag});
        return new W.Run(properties,new W.Text(run.Text??""){Space=SpaceProcessingModeValues.Preserve});
    }
'@
$new=@'
    static W.Run ToWordRun(Run run)
    {
        var effective=new TextRange(run.ContentStart,run.ContentEnd);
        var properties=new W.RunProperties();

        var fontValue=effective.GetPropertyValue(TextElement.FontFamilyProperty);
        var font=fontValue is FontFamily family?family:run.FontFamily;
        if(!string.IsNullOrWhiteSpace(font?.Source))
            properties.Append(new W.RunFonts{Ascii=font.Source,HighAnsi=font.Source});

        var weightValue=effective.GetPropertyValue(TextElement.FontWeightProperty);
        var weight=weightValue is FontWeight fontWeight?fontWeight:run.FontWeight;
        if(weight.ToOpenTypeWeight()>=600)properties.Append(new W.Bold());

        var styleValue=effective.GetPropertyValue(TextElement.FontStyleProperty);
        var style=styleValue is FontStyle fontStyle?fontStyle:run.FontStyle;
        if(style==FontStyles.Italic)properties.Append(new W.Italic());

        var decorationValue=effective.GetPropertyValue(Inline.TextDecorationsProperty);
        var decorations=decorationValue as TextDecorationCollection??run.TextDecorations;
        if(HasDecoration(decorations,TextDecorationLocation.Strikethrough))properties.Append(new W.Strike());

        var foregroundValue=effective.GetPropertyValue(TextElement.ForegroundProperty);
        var foreground=foregroundValue as SolidColorBrush??run.Foreground as SolidColorBrush;
        if(foreground!=null && foreground.Color.A>0)
            properties.Append(new W.Color{Val=Rgb(foreground.Color)});

        var sizeValue=effective.GetPropertyValue(TextElement.FontSizeProperty);
        var size=sizeValue is double fontSize?fontSize:run.FontSize;
        if(!double.IsNaN(size) && size>0)
            properties.Append(new W.FontSize{Val=((int)Math.Round(size*72/96*2)).ToString()});

        if(HasDecoration(decorations,TextDecorationLocation.Underline))
            properties.Append(new W.Underline{Val=W.UnderlineValues.Single});

        var backgroundValue=effective.GetPropertyValue(TextElement.BackgroundProperty);
        var background=backgroundValue as SolidColorBrush??run.Background as SolidColorBrush;
        if(background!=null && background.Color.A>0)
        {
            var highlight=ToWordHighlight(background.Color);
            if(highlight!=null)properties.Append(new W.Highlight{Val=highlight});
            else properties.Append(new W.Shading
            {
                Val=W.ShadingPatternValues.Clear,
                Color="auto",
                Fill=Rgb(background.Color)
            });
        }

        var baselineValue=effective.GetPropertyValue(Inline.BaselineAlignmentProperty);
        var baseline=baselineValue is BaselineAlignment alignment?alignment:run.BaselineAlignment;
        if(baseline==BaselineAlignment.Superscript)
            properties.Append(new W.VerticalTextAlignment{Val=W.VerticalPositionValues.Superscript});
        else if(baseline==BaselineAlignment.Subscript)
            properties.Append(new W.VerticalTextAlignment{Val=W.VerticalPositionValues.Subscript});

        var language=run.Language?.IetfLanguageTag;
        if(!string.IsNullOrWhiteSpace(language))properties.Append(new W.Languages{Val=language});
        return new W.Run(properties,new W.Text(run.Text??""){Space=SpaceProcessingModeValues.Preserve});
    }
'@
Replace-Exact 'DocxInterop.cs' $old $new 'effective run formatting export'

$old=@'
    static string Rgb(System.Windows.Media.Color colour)
        =>$"{colour.R:X2}{colour.G:X2}{colour.B:X2}";
'@
$new=@'
    static string Rgb(System.Windows.Media.Color colour)
        =>$"{colour.R:X2}{colour.G:X2}{colour.B:X2}";

    static W.HighlightColorValues? ToWordHighlight(System.Windows.Media.Color colour)
    {
        if(colour.R>220 && colour.G>210 && colour.B<120)return W.HighlightColorValues.Yellow;
        if(colour.R<120 && colour.G>190 && colour.B<120)return W.HighlightColorValues.Green;
        if(colour.R<120 && colour.G>190 && colour.B>190)return W.HighlightColorValues.Cyan;
        if(colour.R>220 && colour.G<150 && colour.B>180)return W.HighlightColorValues.Magenta;
        if(colour.R>220 && colour.G<140 && colour.B<140)return W.HighlightColorValues.Red;
        if(colour.R<130 && colour.G<160 && colour.B>200)return W.HighlightColorValues.Blue;
        return null;
    }

    static SolidColorBrush? BrushFromHighlight(W.HighlightColorValues value)
    {
        var colour=value switch
        {
            W.HighlightColorValues.Yellow=>Colors.Yellow,
            W.HighlightColorValues.Green=>Colors.Lime,
            W.HighlightColorValues.Cyan=>Colors.Cyan,
            W.HighlightColorValues.Magenta=>Colors.Magenta,
            W.HighlightColorValues.Red=>Colors.Red,
            W.HighlightColorValues.Blue=>Colors.Blue,
            W.HighlightColorValues.DarkYellow=>Color.FromRgb(128,128,0),
            W.HighlightColorValues.DarkGreen=>Color.FromRgb(0,128,0),
            W.HighlightColorValues.DarkCyan=>Color.FromRgb(0,128,128),
            W.HighlightColorValues.DarkMagenta=>Color.FromRgb(128,0,128),
            W.HighlightColorValues.DarkRed=>Color.FromRgb(128,0,0),
            W.HighlightColorValues.DarkBlue=>Color.FromRgb(0,0,128),
            W.HighlightColorValues.LightGray=>Colors.LightGray,
            W.HighlightColorValues.DarkGray=>Colors.DarkGray,
            W.HighlightColorValues.Black=>Colors.Black,
            _=>Colors.Transparent
        };
        if(colour.A==0)return null;
        var brush=new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }
'@
Replace-Exact 'DocxInterop.cs' $old $new 'highlight colour helpers'

# Import Word highlight before falling back to arbitrary run shading.
$old=@'
        var colour=properties.GetFirstChild<W.Color>()?.Val?.Value;
        if(TryBrush(colour,out var foreground))target.Foreground=foreground;
        var fill=properties.GetFirstChild<W.Shading>()?.Fill?.Value;
        if(TryBrush(fill,out var background))target.Background=background;
'@
$new=@'
        var colour=properties.GetFirstChild<W.Color>()?.Val?.Value;
        if(TryBrush(colour,out var foreground))target.Foreground=foreground;
        var highlight=properties.GetFirstChild<W.Highlight>()?.Val?.Value;
        var highlightBrush=highlight==null?null:BrushFromHighlight(highlight.Value);
        if(highlightBrush!=null)target.Background=highlightBrush;
        else
        {
            var fill=properties.GetFirstChild<W.Shading>()?.Fill?.Value;
            if(TryBrush(fill,out var background))target.Background=background;
        }
'@
Replace-Exact 'DocxInterop.cs' $old $new 'highlight import'

# Import paragraph shading.
$old=@'
        var margin=paragraph.Margin;
'@
$new=@'
        var paragraphFill=properties.GetFirstChild<W.Shading>()?.Fill?.Value;
        if(TryBrush(paragraphFill,out var paragraphBackground))paragraph.Background=paragraphBackground;

        var margin=paragraph.Margin;
'@
Replace-Exact 'DocxInterop.cs' $old $new 'paragraph shading import'

Write-Host 'Beta 34.1 source refinements applied.'
