using Microsoft.Win32;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;

namespace CtrlZed;

public partial class MainWindow
{
    const string NativeDocumentFilter = "CtrlZed documents (*.czd)|*.czd";
    const string DocumentSaveFilter = NativeDocumentFilter +
        "|Word documents (*.docx)|*.docx|Rich Text Format (*.rtf)|*.rtf|Text files (*.txt)|*.txt";

    SecureString? documentPassword;
    bool keepLockedRecovery;
    bool protectedRecoveryInProgress;
    int protectedRecoveryGeneration;
    string ProtectedRecoveryFile => recoveryFile + ".protected";

    void SetDocumentPassword(SecureString? password)
    {
        documentPassword?.Dispose();
        documentPassword = password;
    }

    void ProtectDocument_Click(object sender, RoutedEventArgs e)
    {
        var path = currentFile;
        if (path == null || !string.Equals(Path.GetExtension(path), ".czd", StringComparison.OrdinalIgnoreCase))
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save Password-Protected Document",
                Filter = NativeDocumentFilter,
                DefaultExt = ".czd",
                FileName = path==null?SuggestedFirstSaveFileName():Path.GetFileNameWithoutExtension(path),
                AddExtension = true
            };
            if (dialog.ShowDialog(this) != true) return;
            path = dialog.FileName;
        }

        // CtrlZed's ordinary .czd saves can leave a plaintext .bak or .tmp.
        var sidecars = new[] { path + ".bak", path + ".tmp" };
        try
        {
            sidecars = sidecars.Where(File.Exists).Where(file =>
            {
                using var source = File.OpenRead(file);
                return !CzdProtection.IsProtected(source);
            }).ToArray();
        }
        catch (IOException ex)
        {
            MessageBox.Show(this,ex.Message,"Password protection",MessageBoxButton.OK,MessageBoxImage.Error);
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(this,ex.Message,"Password protection",MessageBoxButton.OK,MessageBoxImage.Error);
            return;
        }

        if (sidecars.Length > 0 && MessageBox.Show(this,
            "Protecting this file will remove the older unprotected CtrlZed backup files shown below. Continue?\n\n" +
            string.Join(Environment.NewLine, sidecars),
            "Password protection", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        using var entered = PromptForDocumentPassword("Set Document Password", Path.GetFileName(path), confirm: true);
        if (entered == null) return;

        var previousFile = currentFile;
        var previousPassword = documentPassword;
        currentFile = path;
        documentPassword = entered.Copy();
        if (!SaveCurrent())
        {
            SetDocumentPassword(previousPassword);
            currentFile = previousFile;
            UpdateTitle();
            return;
        }

        previousPassword?.Dispose();
        try
        {
            foreach (var sidecar in sidecars) File.Delete(sidecar);
            DeletePlainRecoveryFiles();
            Status.Text = "Password protection enabled for " + Path.GetFileName(path);
        }
        catch (IOException ex)
        {
            ShowUnprotectedCopyWarning(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowUnprotectedCopyWarning(ex);
        }
    }

    void ShowUnprotectedCopyWarning(Exception error)
        => MessageBox.Show(this,
            "The document is password protected. Windows could not remove an older, unprotected copy.\n\n" +
            error.Message, "Password protection", MessageBoxButton.OK, MessageBoxImage.Warning);

    SecureString? PromptForDocumentPassword(string title, string fileName, bool confirm)
    {
        var window = new Window
        {
            Title = title,
            Width = 450,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = IsLoaded ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Icon = Branding.CreateIconImageSource()
        };
        if (IsLoaded) window.Owner = this;
        var content = new StackPanel { Margin = new Thickness(16) };
        content.Children.Add(new TextBlock
        {
            Text = confirm
                ? $"Choose a password for {fileName}. You will need it to open the document and recover unsaved changes."
                : $"Enter the password for {fileName}.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0,0,0,12)
        });

        PasswordBox AddPasswordBox(string label)
        {
            var box = new PasswordBox { Margin = new Thickness(0,4,0,10) };
            var prompt = new Label { Content = label, Target = box, Padding = new Thickness(0) };
            content.Children.Add(prompt);
            content.Children.Add(box);
            return box;
        }

        var passwordBox = AddPasswordBox("_Password");
        var confirmationBox = confirm ? AddPasswordBox("_Confirm password") : null;
        var validation = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DarkRed
        };
        content.Children.Add(validation);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var ok = new Button { Content = confirm ? "Protect and Save" : "Open", IsDefault = true, MinWidth = 90, Margin = new Thickness(0,10,8,0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinWidth = 72, Margin = new Thickness(0,10,0,0) });
        content.Children.Add(buttons);

        ok.Click += (_,__) =>
        {
            using var password = passwordBox.SecurePassword;
            if (password.Length == 0)
            {
                validation.Text = "Enter a password.";
                passwordBox.Focus();
                return;
            }
            if (confirmationBox != null)
            {
                using var confirmation = confirmationBox.SecurePassword;
                if (!CzdProtection.PasswordsMatch(password, confirmation))
                {
                    validation.Text = "The passwords do not match.";
                    confirmationBox.Focus();
                    return;
                }
            }
            window.DialogResult = true;
        };

        window.Content = content;
        window.Loaded += (_,__) => passwordBox.Focus();
        try { return window.ShowDialog() == true ? passwordBox.SecurePassword : null; }
        finally
        {
            passwordBox.Clear();
            confirmationBox?.Clear();
        }
    }

    static void WriteProtectedCzdFile(string temporaryPath, MemoryStream archive, SecureString password)
    {
        using var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        CzdProtection.Encrypt(destination, archive.GetBuffer().AsSpan(0, checked((int)archive.Length)), password);
        destination.Flush(flushToDisk: true);
    }

    void SaveProtectedCzd(string path, SecureString password)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using var archive = new MemoryStream();
        try
        {
            WriteCzdArchive(archive);
            WriteProtectedCzdFile(temporaryPath, archive, password);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(archive.GetBuffer());
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    bool TryReadCzd(string path, out CzdPackage package, out SecureString? password)
    {
        using var source = File.OpenRead(path);
        password = null;
        package = default!;
        if (!CzdProtection.IsProtected(source))
        {
            package = ReadCzdArchive(source);
            return true;
        }

        while (true)
        {
            var name = path == ProtectedRecoveryFile ? "the recovery snapshot" : Path.GetFileName(path);
            using var entered = PromptForDocumentPassword("Open Protected Document", name, confirm: false);
            if (entered == null) return false;
            source.Position = 0;
            byte[] plaintext;
            try
            {
                plaintext = CzdProtection.Decrypt(source, entered);
            }
            catch (AuthenticationTagMismatchException)
            {
                MessageBox.Show("The password is incorrect or the document has been damaged.",
                    "Password protection", MessageBoxButton.OK, MessageBoxImage.Warning);
                continue;
            }

            try
            {
                using var archive = new MemoryStream(plaintext, writable: false);
                package = ReadCzdArchive(archive);
                password = entered.Copy();
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    async Task WriteProtectedRecoverySnapshotAsync()
    {
        if (documentPassword == null || keepLockedRecovery || protectedRecoveryInProgress) return;
        protectedRecoveryInProgress = true;
        var generation = protectedRecoveryGeneration;
        var temporaryPath = ProtectedRecoveryFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var restoreFields = fieldValuesVisible;
        using var password = documentPassword.Copy();
        using var rtf = new MemoryStream();
        try
        {
            CzdSettings settings;
            if (restoreFields) SetFieldValuesVisible(false);
            try
            {
                WriteEditorRtf(rtf);
                settings = CaptureDocumentSettings();
            }
            finally { if (restoreFields) SetFieldValuesVisible(true); }

            rtf.Position = 0;
            await Task.Run(() =>
            {
                using var archive = new MemoryStream();
                try
                {
                    WriteCzdArchive(archive, rtf.CopyTo, settings);
                    WriteProtectedCzdFile(temporaryPath, archive, password);
                }
                finally { CryptographicOperations.ZeroMemory(archive.GetBuffer()); }
            });
            if (generation == protectedRecoveryGeneration)
                File.Move(temporaryPath, ProtectedRecoveryFile, overwrite: true);
        }
        catch { }
        finally
        {
            CryptographicOperations.ZeroMemory(rtf.GetBuffer());
            try { File.Delete(temporaryPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            protectedRecoveryInProgress = false;
        }
    }

    void DeletePlainRecoveryFiles()
    {
        foreach (var path in new[] { recoveryFile, recoverySettingsFile, recoveryFile + ".tmp", recoverySettingsFile + ".tmp" })
            File.Delete(path);
    }
}
