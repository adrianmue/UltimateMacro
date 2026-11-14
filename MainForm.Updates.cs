using System.Diagnostics;

namespace UltimateMacro;

public sealed partial class MainForm
{
    private readonly Button updateButton = Button("Update verfügbar");
    private readonly Button helpUpdateButton = new PaletteButton { Name = "HelpCheckForUpdates" };
    private readonly CancellationTokenSource updateLifetime = new();
    private UpdateResult? updateResult;
    private bool checkingUpdates;
    private bool showUpdateCheckResult;
    private bool installingUpdate;
    private UpdateInstall? pendingUpdate;

    private void RefreshUpdateButtons() => helpUpdateButton.Enabled = updateButton.Enabled = !Busy && !installingUpdate;

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (IsDisposed || installingUpdate) return;
        showUpdateCheckResult |= manual;
        if (manual) SetStatus("Suche auf GitHub nach Updates …");
        if (checkingUpdates) return;
        checkingUpdates = true;
        try
        {
            var result = await UpdateCheck.CheckAsync(updateLifetime.Token);
            if (IsDisposed || Disposing) return;
            updateResult = result;
            var available = result.Package is not null;
            updateButton.Visible = available;
            if (result.Package is { } package) updateButton.Text = $"Update {package.Version} verfügbar";
            if (showUpdateCheckResult) await ShowUpdateResultAsync();
        }
        catch (OperationCanceledException) { }
        finally { checkingUpdates = showUpdateCheckResult = false; }
    }

    private async Task ShowUpdateResultAsync()
    {
        if (Busy || installingUpdate || updateResult is not { } result) return;
        if (result.Package is { } package)
        {
            SetStatus($"Ultimate Macro {package.Version} ist verfügbar.");
            var install = UpdateInstall.CanInstall(package);
            var action = install ? "Jetzt aktualisieren? Ultimate Macro startet danach neu." : "Windows-ZIP im Browser herunterladen?";
            if (MessageBox.Show(this, $"Installiert ist {UpdateCheck.InstalledVersion.ToString(3)}. Version {package.Version} ist verfügbar.\n\n{action}",
                "Update verfügbar", MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            if (install) await InstallUpdateAsync(package);
            else Guard(() => OpenUpdatePage(UpdateCheck.DownloadUrl(package.Version)));
            return;
        }

        var message = result.Status switch
        {
            UpdateStatus.Current => $"Ultimate Macro {UpdateCheck.InstalledVersion.ToString(3)} ist aktuell.",
            UpdateStatus.NoRelease => "Für das neueste Release ist noch kein Windows-Paket verfügbar.",
            _ => "Die Updateprüfung war gerade nicht möglich. Versuche es später erneut."
        };
        SetStatus(message);
        if (result.Status == UpdateStatus.Current)
            MessageBox.Show(this, message, "Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
        else if (MessageBox.Show(this, message + "\n\nRelease-Seite im Browser öffnen?", "Updates", MessageBoxButtons.YesNo,
            MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            Guard(() => OpenUpdatePage(UpdateCheck.ReleasesUrl));
    }

    private async Task InstallUpdateAsync(UpdatePackage package)
    {
        installingUpdate = true;
        RefreshUpdateButtons();
        SetStatus($"Lade Ultimate Macro {package.Version} herunter …");
        try
        {
            using var update = await UpdateInstall.PrepareAsync(package, updateLifetime.Token);
            if (IsDisposed || Disposing) return;
            if (Busy)
            {
                SetStatus("Beende zuerst die laufende Aufnahme oder Wiedergabe und versuche das Update erneut.");
                return;
            }
            pendingUpdate = update;
            Close();
            if (!IsDisposed) SetStatus("Update aufgeschoben.");
        }
        catch (OperationCanceledException) when (IsDisposed || Disposing) { }
        catch (Exception error) when (error is IOException or InvalidDataException or HttpRequestException or UnauthorizedAccessException or OperationCanceledException)
        {
            if (!IsDisposed && !Disposing)
            {
                SetStatus("Update konnte nicht installiert werden.");
                if (MessageBox.Show(this, $"Update fehlgeschlagen.\n{error.Message}\n\nWindows-ZIP im Browser herunterladen?",
                    "Updates", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                    Guard(() => OpenUpdatePage(UpdateCheck.DownloadUrl(package.Version)));
            }
        }
        finally
        {
            installingUpdate = false;
            pendingUpdate = null;
            if (!IsDisposed) RefreshUpdateButtons();
        }
    }

    private static void OpenUpdatePage(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
