using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;

namespace CtrlZed;

public partial class MainWindow
{
    internal static int RunVectorSignatureSelfTest()
    {
        MainWindow? window=null;
        var report=new StringBuilder();
        var temporary=Path.Combine(Path.GetTempPath(),"CtrlZed47Vector_"+Guid.NewGuid().ToString("N"));
        var profileRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed");
        var savedFiles=new Dictionary<string,byte[]?>();
        var svgName="Vector test "+Guid.NewGuid().ToString("N");
        var pngName="Raster test "+Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(temporary);
        Directory.CreateDirectory(profileRoot);
        foreach(var name in new[]{"settings.json","editor-view.json","language-settings.json","personal-en-GB.lex",
                    "Recovery.rtf","Recovery.settings.json","Recovery.rtf.protected"})
        {
            var path=Path.Combine(profileRoot,name);
            savedFiles[path]=File.Exists(path)?File.ReadAllBytes(path):null;
            if(name.StartsWith("Recovery",StringComparison.Ordinal) && File.Exists(path))File.Delete(path);
        }
        try
        {
            var raster=CreateSignatureRaster();
            var png=EncodeSignaturePng(raster);
            VerifySignatureSvgAndTrace(raster,png,report);
            Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            window=new MainWindow{WindowState=WindowState.Maximized};
            window.Show();window.Activate();
            SetForegroundWindow(new WindowInteropHelper(window).Handle);
            PumpInput();
            window.recoveryTimer.Stop();window.uiRefreshTimer.Stop();
            window.languageSettings.GrammarAndStyleSuggestions=false;
            window.languageSettings.PoetryRhymeSuggestions=false;
            window.grammarReviewTimer?.Stop();
            SpellCheck.SetIsEnabled(window.Editor,false);
            window.VerifySignatureImportPreview(png,report);
            window.CheckVectorSignatureBehaviour(temporary,svgName,pngName,png,report);
            report.AppendLine("PASS: vector tracing/import, macro compatibility, native/profile/recovery persistence and vector print sources.");
            return 0;
        }
        catch(Exception error)
        {
            report.AppendLine(error.ToString());
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestFailure.txt"),error.ToString());
            return 125;
        }
        finally
        {
            if(window!=null)
            {
                window.closingApproved=true;
                window.recoveryTimer.Stop();window.uiRefreshTimer.Stop();
                window.grammarReviewTimer?.Stop();window.grammarAiCancellation?.Cancel();
                window.beta35PaginationTimer?.Stop();window.beta35PaginationCancellation?.Cancel();
                window.Close();
            }
            try{ProfileImageStore.Delete(svgName);}catch{}
            try{ProfileImageStore.Delete(pngName);}catch{}
            foreach(var pair in savedFiles)
            {
                if(pair.Value==null){if(File.Exists(pair.Key))File.Delete(pair.Key);}
                else File.WriteAllBytes(pair.Key,pair.Value);
            }
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZed47VectorSignatures.txt"),report.ToString());
            try{Directory.Delete(temporary,true);}catch{}
        }
    }

    void VerifySignatureImportPreview(byte[] png,StringBuilder report)
    {
        var review=new SignatureImportWindow(png,true){Owner=this};
        Exception? failure=null;
        review.Loaded+=async (_,__)=>
        {
            try
            {
                var use=review.FindName("UseSignature") as Button
                    ??throw new InvalidOperationException("The signature preview has no Use Image control.");
                var threshold=review.FindName("InkThreshold") as Slider
                    ??throw new InvalidOperationException("The signature preview has no ink threshold control.");
                var trace=review.FindName("TraceSignature") as CheckBox
                    ??throw new InvalidOperationException("The signature preview has no trace option.");
                RequireVector(trace.IsChecked==true && !use.IsEnabled,
                    "A signature can be accepted before its first vector preview is ready.");
                await WaitForSignaturePreviewAsync(()=>use.IsEnabled);
                threshold.Value=120;
                RequireVector(!use.IsEnabled,"Changing the trace threshold left a stale preview selectable.");
                threshold.Value=190;
                await WaitForSignaturePreviewAsync(()=>use.IsEnabled);
                review.UpdateLayout();
                var visual=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(review.ActualWidth)),
                    Math.Max(1,(int)Math.Ceiling(review.ActualHeight)),96,96,PixelFormats.Pbgra32);
                visual.Render(review);visual.Freeze();
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(),"CtrlZed47SignaturePreview.png"),EncodeSignaturePng(visual));
                use.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch(Exception error){failure=error;review.Close();}
        };
        var accepted=review.ShowDialog();
        if(failure!=null)throw new InvalidOperationException("The vector signature preview failed.",failure);
        RequireVector(accepted==true && review.ResultBytes!=null,"Use Image did not return the reviewed vector asset.");
        var actual=SignatureImages.Load(review.ResultBytes!).Svg;
        var expected=SignatureImages.Load(SignatureTrace.Trace(SignatureImages.Load(png).Bitmap,190,false,"#000000")).Svg;
        RequireVector(actual!=null && actual==expected,"The signature preview accepted a stale threshold result.");
        report.AppendLine("The real signature review window withheld acceptance until its trace was ready; rapid threshold changes returned the latest SVG. Synthetic preview saved to CtrlZed47SignaturePreview.png.");
    }

    static async Task WaitForSignaturePreviewAsync(Func<bool> ready)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();
        while(!ready())
        {
            if(watch.Elapsed>TimeSpan.FromSeconds(15))throw new InvalidOperationException("The vector preview did not finish.");
            await Task.Delay(35);
        }
    }

    static void VerifySignatureSvgAndTrace(BitmapSource raster,byte[] png,StringBuilder report)
    {
        var old=SignatureImages.Load(png);
        RequireVector(old.Bitmap.IsFrozen && !old.Bitmap.IsDownloading && old.Bitmap.PixelWidth==200 &&
            old.Bitmap.PixelHeight==80 && old.Vector==null && old.Svg==null,
            "Loading an existing raster signature changed its image type, size or readiness.");

        var svgBytes=SignatureTrace.Trace(raster,170,false,"#1438A0");
        var svg=Encoding.UTF8.GetString(svgBytes);
        var xml=XDocument.Parse(svg);
        var paths=xml.Descendants().Where(element=>element.Name.LocalName=="path").ToList();
        RequireVector(paths.Count is >0 and <30 && !xml.Descendants().Any(element=>element.Name.LocalName=="image"),
            "The trace did not produce bounded, self-contained vector contours.");
        var traced=SignatureImages.Load(svgBytes);
        RequireVector(traced.Vector is {IsFrozen:true} && traced.Svg!=null && traced.Width>0 && traced.Height>0 &&
            traced.Bitmap.IsFrozen && !traced.Bitmap.IsDownloading,
            "A traced signature did not retain both its vector master and readable bitmap fallback.");
        var pixels=RenderSignatureDrawing(traced.Vector!,420,Math.Max(40,(int)Math.Round(420*traced.Height/traced.Width)));
        RequireVector(CountSignatureInk(pixels)>200 && CountSignatureTransparent(pixels)>200 &&
            HasEnclosedSignatureHole(pixels),
            "Tracing lost the pen strokes, transparent background or enclosed signature loop.");
        RequireVector(CountSignatureColour(pixels,20,56,160)>100,
            "The selected trace ink colour was not retained by the vector drawing.");
        var lightPixels=new byte[raster.PixelWidth*raster.PixelHeight*4];
        raster.CopyPixels(lightPixels,raster.PixelWidth*4,0);
        for(var pixel=0;pixel<lightPixels.Length;pixel+=4)
            for(var channel=0;channel<3;channel++)lightPixels[pixel+channel]=(byte)(255-lightPixels[pixel+channel]);
        var lightSource=BitmapSource.Create(raster.PixelWidth,raster.PixelHeight,96,96,PixelFormats.Bgra32,null,
            lightPixels,raster.PixelWidth*4);lightSource.Freeze();
        var lightTrace=SignatureImages.Load(SignatureTrace.Trace(lightSource,85,true));
        RequireVector(lightTrace.Vector!=null && HasEnclosedSignatureHole(RenderSignatureDrawing(lightTrace.Vector,420,
            Math.Max(40,(int)Math.Round(420*lightTrace.Height/lightTrace.Width)))),
            "Light ink on a dark background did not retain its strokes and enclosed loop.");

        var jpegEncoder=new JpegBitmapEncoder{QualityLevel=95};
        jpegEncoder.Frames.Add(BitmapFrame.Create(raster));
        using(var jpeg=new MemoryStream())
        {
            jpegEncoder.Save(jpeg);
            var jpegAsset=SignatureImages.Load(jpeg.ToArray());
            var jpegTrace=SignatureImages.Load(SignatureTrace.Trace(jpegAsset.Bitmap));
            var jpegPixels=RenderSignatureDrawing(jpegTrace.Vector!,420,
                Math.Max(40,(int)Math.Round(420*jpegTrace.Height/jpegTrace.Width)));
            RequireVector(jpegTrace.Vector!=null && HasEnclosedSignatureHole(jpegPixels),
                "JPEG signature tracing lost its enclosed loop.");
        }

        const string evenOdd="<svg xmlns='http://www.w3.org/2000/svg' width='40' height='40' viewBox='0 0 40 40'><path fill='#000000' fill-rule='evenodd' d='M0 0H40V40H0Z M10 10H30V30H10Z'/></svg>";
        var hole=SignatureImages.FromSvg(evenOdd);
        var solid=SignatureImages.FromSvg(evenOdd.Replace(" fill-rule='evenodd'",""));
        RequireVector(SignaturePixel(RenderSignatureDrawing(hole.Vector!,80,80),40,40).A<10 &&
            SignaturePixel(RenderSignatureDrawing(solid.Vector!,80,80),40,40).A>240,
            "SVG default nonzero and explicit even-odd fill rules were confused.");
        const string transformed="<svg xmlns='http://www.w3.org/2000/svg' width='2in' height='1in' viewBox='0 0 200 100'><g transform='translate(20 10) scale(2)' style='fill:#0000ff;opacity:0.5'><rect x='0' y='0' width='20' height='10'/></g></svg>";
        var transformedAsset=SignatureImages.FromSvg(transformed);
        var transformedPixels=RenderSignatureDrawing(transformedAsset.Vector!,200,100);
        var blue=SignaturePixel(transformedPixels,40,20);
        RequireVector(Math.Abs(transformedAsset.Width/transformedAsset.Height-2)<0.001 && blue.B>=110 &&
            blue.R<10 && blue.A is >110 and <145 && SignaturePixel(transformedPixels,10,20).A<10,
            "SVG physical-unit aspect ratio, viewBox, group transform or inherited opacity changed.");
        var currentColour=SignatureImages.FromSvg("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 20 10'><g color='#ff0000' fill='currentColor'><rect width='10' height='10'/><rect x='10' width='10' height='10' color='#0000ff'/></g></svg>");
        var colourPixels=RenderSignatureDrawing(currentColour.Vector!,200,100);
        RequireVector(SignaturePixel(colourPixels,50,50).R>240 && SignaturePixel(colourPixels,150,50).B>240 &&
            SignaturePixel(colourPixels,150,50).R<10,
            "Inherited SVG currentColor ignored a child's colour override.");
        var curves=SignatureImages.FromSvg("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 40'><path fill='none' stroke='#000000' stroke-width='2' stroke-linecap='round' d='M2 20 C12 2 22 38 32 20 Q42 2 52 20 T72 20 A10 8 0 0 1 92 20'/></svg>");
        RequireVector(curves.Vector!=null && CountSignatureInk(RenderSignatureDrawing(curves.Vector,400,160))>100,
            "SVG curves, arcs or stroked paths were not drawn.");

        foreach(var rejected in new[]{
            "<svg xmlns='http://www.w3.org/2000/svg'><script>bad()</script></svg>",
            "<svg xmlns='http://www.w3.org/2000/svg'><image href='https://example.invalid/signature.png'/></svg>",
            "<svg xmlns='http://www.w3.org/2000/svg'><text>Signature</text></svg>",
            "<!DOCTYPE svg [<!ENTITY stroke 'M0 0L10 10'>]><svg xmlns='http://www.w3.org/2000/svg'><path d='&stroke;'/></svg>",
            "<svg xmlns='http://www.w3.org/2000/svg'><path d='M1 2 BAD'/></svg>",
            "<svg xmlns='http://www.w3.org/2000/svg'><defs><style>.ink{fill:red}</style></defs><path class='ink' d='M0 0H10V10Z'/></svg>",
            "<svg xmlns='http://www.w3.org/2000/svg'><path d='M0 0H10V10Z' fill='url(https://example.invalid/paint)'/></svg>"})
            ExpectSignatureRejection(()=>SignatureImages.FromSvg(rejected),"An unsupported or malformed SVG was accepted.");
        ExpectSignatureRejection(()=>SignatureTrace.Trace(CreateEmptySignatureRaster()),"A blank image produced a signature trace.");
        report.AppendLine("PNG/JPEG tracing retained strokes, selected colour, transparent background and loops. SVG fill rules, units, transforms, opacity, curves/arcs and rejected unsupported input passed.");
    }

    void CheckVectorSignatureBehaviour(string temporary,string svgName,string pngName,byte[] png,StringBuilder report)
    {
        const string sourceSvg="<svg xmlns='http://www.w3.org/2000/svg' width='240' height='60' viewBox='0 0 240 60'><path fill='none' stroke='#142C88' stroke-width='3' stroke-linecap='round' stroke-linejoin='round' d='M6 44 C20 8 42 8 28 42 S62 58 74 24 Q86 4 92 36 T124 30 L150 38 Q170 8 190 32 T234 22'/></svg>";
        var svgBytes=Encoding.UTF8.GetBytes(sourceSvg);
        var canonicalSvg=SignatureImages.FromSvg(sourceSvg).Svg!;
        var canonicalBytes=Encoding.UTF8.GetBytes(canonicalSvg);
        ProfileImageStore.Save(svgName,svgBytes);
        ProfileImageStore.Save(pngName,png);
        RequireVector(ProfileImageStore.LoadBitmap(svgName) is {IsFrozen:true} &&
            ProfileImageStore.LoadBitmap(pngName).PixelWidth==200,
            "The compatible bitmap-loading API stopped working for vector or raster profile images.");
        RequireVector(ProfileImageStore.ReadBytes(svgName).SequenceEqual(canonicalBytes),"The profile lost the canonical SVG master.");
        ExpectSignatureRejection(()=>ProfileImageStore.Save(pngName,Encoding.UTF8.GetBytes("<svg><script/></svg>")),
            "A rejected SVG replaced a stored raster image.");
        RequireVector(ProfileImageStore.ReadBytes(pngName).SequenceEqual(png),"Failed import damaged the existing profile image.");

        ClearFloatingImagePlacements();
        pageWidth=794;pageHeight=1123;pageMargins=new Thickness(72);zoomPercent=100;ApplyZoom();
        Editor.Document=new FlowDocument(new Paragraph(new Run("Vector signatures ")));
        ApplyPage();ApplyBeta35ViewMode(EditorViewMode.Paged,false);
        Editor.CaretPosition=Editor.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        Editor.Focus();PumpInput();
        var document=Editor.Document;
        ProfileImageMacro.Insert(Editor,svgName,40,PicturePlacementMode.Inline);
        RequireVector(Editor.CanUndo,"Inserting a vector signature did not create an Undo operation.");
        Editor.Undo();PumpInput();
        RequireVector(!EnumerateImageContainers(Editor.Document).Any(),"One Undo did not remove the inserted vector signature.");
        Editor.Redo();PumpInput();
        var redone=EnumerateImageContainers(Editor.Document).Single().Image;
        RequireVector(VectorImageSupport.TryGetSvg(redone,out _) && VectorImageSupport.GetDisplaySource(redone) is DrawingImage,
            "Redo restored only the vector signature's bitmap and lost its SVG master.");
        AwaitPagedTask(RunSafeMacroAsync("INSERT_PROFILE_IMAGE {"+svgName+"} 40 IN_FRONT"));
        ProfileImageMacro.Insert(Editor,svgName,40,PicturePlacementMode.BehindText);
        InsertProfileImage(pngName,40,PicturePlacementMode.Inline);
        FreshPagedLayout();
        var images=EnumerateImageContainers(Editor.Document).Select(item=>item.Image).ToList();
        RequireVector(images.Count==4 && ReferenceEquals(document,Editor.Document),"Profile macros replaced the canonical document or missed an image.");
        var expectedWidth=40*96/25.4;
        foreach(var image in images.Take(3))
            RequireVector(image.Source is BitmapSource && VectorImageSupport.TryGetSvg(image,out _) &&
                VectorImageSupport.GetDisplaySource(image) is DrawingImage && Math.Abs(image.Width-expectedWidth)<0.01 &&
                Math.Abs(image.Width/image.Height-4)<0.001,
                "A 40 mm vector macro lost its master, fallback or aspect ratio.");
        RequireVector(!VectorImageSupport.TryGetSvg(images[3],out _) && images[3].Source is BitmapSource &&
            GetPicturePlacement(images[0])==PicturePlacementMode.Inline &&
            GetPicturePlacement(images[1])==PicturePlacementMode.InFrontOfText &&
            GetPicturePlacement(images[2])==PicturePlacementMode.BehindText,
            "Raster compatibility or inline/front/behind signature placement changed.");
        var master=VectorImageSupport.TryGetSvg(images[0],out var storedSvg)?storedSvg:"";
        var placements=CaptureFloatingImagePlacements();
        var path=Path.Combine(temporary,"vectors.czd");
        SaveCzd(path);
        RequireVector(ReferenceEquals(document,Editor.Document) && VectorImageSupport.TryGetSvg(images[0],out _),
            "Native saving replaced the live document or removed a vector master.");

        var profilePath=Path.Combine(temporary,"vectors.czprofile");
        var macroPath=Path.Combine(temporary,"macros");Directory.CreateDirectory(macroPath);
        UserProfileService.Export(profilePath,new Dictionary<string,string>(),CapturePortablePreferences(),styleSets,macroPath);
        using(var profile=ZipFile.OpenRead(profilePath))
        {
            var entry=profile.GetEntry("profile-images/"+Uri.EscapeDataString(svgName)+".img")
                ??throw new InvalidOperationException("The portable profile omitted the vector signature.");
            using var input=entry.Open();using var bytes=new MemoryStream();input.CopyTo(bytes);
            RequireVector(bytes.ToArray().SequenceEqual(canonicalBytes),"Portable profile export changed the SVG master.");
        }
        ProfileImageStore.Delete(svgName);
        RequireVector(!ProfileImageStore.Contains(svgName),"The self-contained native fixture still has its original profile asset.");
        CzdPackage package;
        using(var input=File.OpenRead(path))package=ReadCzdArchive(input);
        ClearFloatingImagePlacements();Editor.Document=package.Document;
        ApplyRecoveredSettings(package.Settings??throw new InvalidOperationException("Native vector settings were omitted."));
        WirePictureHandlers();RefreshFloatingImagePlacements();FreshPagedLayout();
        images=EnumerateImageContainers(Editor.Document).Select(item=>item.Image).ToList();
        RequireVector(images.Count==4 && images.Take(3).All(image=>VectorImageSupport.TryGetSvg(image,out var svg)&&svg==master) &&
            images.All(image=>image.Source is BitmapSource),
            "The native file depended on a profile asset or lost its compatibility bitmap/vector masters.");
        RequireVector(CaptureFloatingImagePlacements().Select(item=>(item.Mode,item.Width,item.Height,item.PageOffset))
            .SequenceEqual(placements.Select(item=>(item.Mode,item.Width,item.Height,item.PageOffset))),
            "Reopening a native vector signature changed its placement or dimensions.");

        var fallbackClone=CloneDocument();
        RequireVector(EnumerateImageContainers(fallbackClone).All(item=>item.Image.Source is BitmapSource),
            "The serialization clone contains non-RTF vector drawing sources.");
        var printClone=CloneDocument(forPrint:true);
        var printed=EnumerateImageContainers(printClone).Select(item=>item.Image).ToList();
        RequireVector(printed.Take(3).All(image=>image.Source is DrawingImage {IsFrozen:true}) &&
            printed[3].Source is BitmapSource,
            "Print preparation rasterised vector masters or converted an ordinary image.");
        var floating=CreateFloatingPageImages(new Size(pageWidth,pageHeight));
        RequireVector(floating.Count==2 && floating.All(item=>item.Source is DrawingImage) &&
            floating.Any(item=>item.Mode==PicturePlacementMode.InFrontOfText) &&
            floating.Any(item=>item.Mode==PicturePlacementMode.BehindText),
            "Floating signature printing lost its true vector source or selected layer.");
        images[0].Width*=2;images[0].Height*=2;
        var resizedClone=CloneDocument(forPrint:true);
        var resized=EnumerateImageContainers(resizedClone).First().Image;
        RequireVector(resized.Source is DrawingImage && Math.Abs(resized.Width-expectedWidth*2)<0.01 &&
            Math.Abs(resized.Width/resized.Height-4)<0.001 && VectorImageSupport.TryGetSvg(images[0],out var resizedSvg) && resizedSvg==master,
            "Resizing changed the SVG master or removed scalable print output.");
        report.AppendLine("Named profile and safe/compiled macro paths inserted 40 mm vectors in all three layers; native save/reopen remained self-contained after deleting the profile original, and print clones retained DrawingImage masters at larger sizes.");

        dirty=true;AwaitPagedTask(WriteRecoverySnapshotAsync());
        RequireVector(File.Exists(recoveryFile)&&File.Exists(recoverySettingsFile),"Vector recovery files were not written.");
        var recovered=new FlowDocument();
        using(var input=File.OpenRead(recoveryFile))new TextRange(recovered.ContentStart,recovered.ContentEnd).Load(input,DataFormats.Rtf);
        var recoveredSettings=JsonSerializer.Deserialize<CzdSettings>(File.ReadAllText(recoverySettingsFile))
            ??throw new InvalidOperationException("The vector recovery metadata was not readable.");
        ClearFloatingImagePlacements();Editor.Document=recovered;ApplyRecoveredSettings(recoveredSettings);
        RequireVector(EnumerateImageContainers(Editor.Document).Count(item=>VectorImageSupport.TryGetSvg(item.Image,out _))==3,
            "Recovery lost the SVG masters after its RTF fallback was restored.");

        var imported=UserProfileService.Import(profilePath,false,new Dictionary<string,string>(),
            new Dictionary<string,StyleSetStore.StyleSet>(),Path.Combine(temporary,"imported-macros"));
        RequireVector(imported.ProfileImagesImported>=1 && ProfileImageStore.ReadBytes(svgName).SequenceEqual(canonicalBytes),
            "Portable profile import did not restore the original vector signature bytes.");
        ClearFloatingImagePlacements();
        var legacyImage=new Image{Source=SignatureImages.Load(png).Bitmap,Width=80,Height=32};
        Editor.Document=new FlowDocument(new Paragraph(new InlineUIContainer(legacyImage)));
        ApplyRecoveredSettings(JsonSerializer.Deserialize<CzdSettings>("{}")!);
        RequireVector(!VectorImageSupport.TryGetSvg(legacyImage,out _) &&
            EnumerateImageContainers(CloneDocument(forPrint:true)).Single().Image.Source is BitmapSource,
            "An older bitmap-only document requires vector metadata or acquired an unrelated master.");
        report.AppendLine("Recovery preserved SVG masters; portable profile import retained exact source bytes; older bitmap-only document settings remained compatible.");
    }

    static BitmapSource CreateSignatureRaster()
    {
        const int width=200,height=80;
        var bytes=Enumerable.Repeat((byte)255,width*height*4).ToArray();
        var points=new[]{new Point(70,52),new Point(91,23),new Point(111,51),new Point(133,27),new Point(153,50),new Point(181,32)};
        for(var y=0;y<height;y++)for(var x=0;x<width;x++)
        {
            var ring=Math.Sqrt((x-36)*(x-36)+(y-40)*(y-40));
            var ink=ring>=10 && ring<=18 || (x-174)*(x-174)+(y-18)*(y-18)<=4;
            for(var segment=0;segment+1<points.Length && !ink;segment++)
            {
                var direction=points[segment+1]-points[segment];
                var relative=new Point(x,y)-points[segment];
                var fraction=Math.Clamp(Vector.Multiply(relative,direction)/direction.LengthSquared,0,1);
                ink=(relative-direction*fraction).Length<=2;
            }
            var greyDetail=(x-188)*(x-188)+(y-60)*(y-60)<=9;
            if(ink || greyDetail)
            {
                var pixel=(y*width+x)*4;
                bytes[pixel]=bytes[pixel+1]=bytes[pixel+2]=ink?(byte)0:(byte)180;
            }
        }
        var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,bytes,width*4);bitmap.Freeze();return bitmap;
    }

    static BitmapSource CreateEmptySignatureRaster()
    {
        var bitmap=BitmapSource.Create(8,8,96,96,PixelFormats.Bgra32,null,Enumerable.Repeat((byte)255,8*8*4).ToArray(),8*4);
        bitmap.Freeze();return bitmap;
    }

    static byte[] EncodeSignaturePng(BitmapSource bitmap)
    {
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var bytes=new MemoryStream();encoder.Save(bytes);return bytes.ToArray();
    }

    static BitmapSource RenderSignatureDrawing(ImageSource drawing,int width,int height)
    {
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawImage(drawing,new Rect(0,0,width,height));
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();return bitmap;
    }

    static (byte B,byte G,byte R,byte A) SignaturePixel(BitmapSource image,int x,int y)
    {
        var bytes=new byte[4];image.CopyPixels(new Int32Rect(x,y,1,1),bytes,4,0);return(bytes[0],bytes[1],bytes[2],bytes[3]);
    }

    static int CountSignatureInk(BitmapSource image)=>CountSignaturePixels(image,(b,g,r,a)=>a>200);
    static int CountSignatureTransparent(BitmapSource image)=>CountSignaturePixels(image,(b,g,r,a)=>a<5);
    static int CountSignatureColour(BitmapSource image,int red,int green,int blue)
        =>CountSignaturePixels(image,(b,g,r,a)=>a>245 && Math.Abs(r-red)<8 && Math.Abs(g-green)<8 && Math.Abs(b-blue)<8);

    static int CountSignaturePixels(BitmapSource image,Func<byte,byte,byte,byte,bool> match)
    {
        var pixels=new byte[image.PixelWidth*image.PixelHeight*4];image.CopyPixels(pixels,image.PixelWidth*4,0);
        var count=0;for(var pixel=0;pixel<pixels.Length;pixel+=4)if(match(pixels[pixel],pixels[pixel+1],pixels[pixel+2],pixels[pixel+3]))count++;
        return count;
    }

    static bool HasEnclosedSignatureHole(BitmapSource image)
    {
        var width=image.PixelWidth;var height=image.PixelHeight;
        var bytes=new byte[width*height*4];image.CopyPixels(bytes,width*4,0);
        var visited=new bool[width*height];
        for(var start=0;start<visited.Length;start++)
        {
            if(visited[start] || bytes[start*4+3]>10)continue;
            var stack=new Stack<int>();stack.Push(start);visited[start]=true;
            var count=0;var border=false;
            while(stack.Count>0)
            {
                var point=stack.Pop();var x=point%width;var y=point/width;count++;
                if(x==0 || y==0 || x==width-1 || y==height-1)border=true;
                foreach(var next in new[]{x>0?point-1:-1,x+1<width?point+1:-1,y>0?point-width:-1,y+1<height?point+width:-1})
                    if(next>=0 && !visited[next] && bytes[next*4+3]<=10){visited[next]=true;stack.Push(next);}
            }
            if(!border && count>=20)return true;
        }
        return false;
    }

    static void ExpectSignatureRejection(Action action,string message)
    {
        try{action();}
        catch(Exception error) when(error is InvalidDataException or FormatException or ArgumentException or XmlException or NotSupportedException){return;}
        throw new InvalidOperationException(message);
    }

    static void RequireVector(bool condition,string message)
    {
        if(!condition)throw new InvalidOperationException(message);
    }
}
