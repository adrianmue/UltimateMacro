using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace UltimateMacro;

internal sealed class UpdateInstall : IDisposable
{
    private const long MaximumPackageSize = 256L * 1024 * 1024;
    private const string ExecutableName = "UltimateMacro.exe";
    private readonly string target;
    private readonly string directory;
    private bool handedOff;

    private UpdateInstall(string target, string directory) => (this.target, this.directory) = (target, directory);

    internal static bool CanInstall(UpdatePackage package) =>
        package.Sha256 is not null && package.Size is > 0 and <= MaximumPackageSize &&
        Environment.ProcessPath is { } path && string.Equals(Path.GetFileName(path), ExecutableName, StringComparison.OrdinalIgnoreCase);

    internal static async Task<UpdateInstall> PrepareAsync(UpdatePackage package, CancellationToken cancellation)
    {
        if (!CanInstall(package)) throw new InvalidOperationException("Dieses Paket kann nur manuell installiert werden.");
        var target = Environment.ProcessPath!;
        var directory = Path.Combine(Path.GetDirectoryName(target)!, ".update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var update = new UpdateInstall(target, directory);
        var zipPath = Path.Combine(directory, "download.zip");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            var token = timeout.Token;
            using var response = await UpdateCheck.Client.GetAsync(UpdateCheck.DownloadUrl(package.Version),
                HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var download = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var file = new FileStream(zipPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long received = 0;
                int count;
                while ((count = await download.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    received += count;
                    if (received > package.Size) throw new InvalidDataException("Die Größe des Downloads stimmt nicht.");
                    await file.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                }
                if (received != package.Size) throw new InvalidDataException("Der Download ist unvollständig.");
                file.Position = 0;
                var digest = await SHA256.HashDataAsync(file, token).ConfigureAwait(false);
                if (!string.Equals(Convert.ToHexString(digest), package.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Die Prüfsumme des Downloads stimmt nicht.");
                file.Position = 0;
                await ExtractExecutableAsync(file, Path.Combine(directory, "new.exe"), package.Version, token).ConfigureAwait(false);
            }
            File.Delete(zipPath);
            return update;
        }
        catch
        {
            update.Dispose();
            throw;
        }
    }

    internal static async Task ExtractExecutableAsync(Stream zip, string path, Version version, CancellationToken cancellation)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(ExecutableName);
        if (entry is null || entry.Length is <= 0 or > MaximumPackageSize)
            throw new InvalidDataException("Das Paket enthält keine gültige UltimateMacro.exe.");
        await using (var input = entry.Open())
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            await input.CopyToAsync(output, cancellation).ConfigureAwait(false);
        var actual = FileVersionInfo.GetVersionInfo(path);
        if (actual.ProductVersion != version.ToString(3) || actual.OriginalFilename != "UltimateMacro.dll")
            throw new InvalidDataException("Die EXE passt nicht zur angekündigten Version.");
    }

    internal void InstallAfterExit()
    {
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(powershell) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(InstallScript)) })
            start.ArgumentList.Add(argument);
        start.Environment["ULTIMATEMACRO_UPDATE_PID"] = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["ULTIMATEMACRO_UPDATE_TARGET"] = target;
        start.Environment["ULTIMATEMACRO_UPDATE_DIRECTORY"] = directory;
        using var helper = Process.Start(start) ?? throw new InvalidOperationException("Der Update-Helfer konnte nicht gestartet werden.");
        handedOff = true;
    }

    public void Dispose()
    {
        if (handedOff) return;
        try
        {
            File.Delete(Path.Combine(directory, "download.zip"));
            File.Delete(Path.Combine(directory, "new.exe"));
            Directory.Delete(directory);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private const string InstallScript = """
        $ErrorActionPreference = 'Stop'
        $target = $env:ULTIMATEMACRO_UPDATE_TARGET
        $folder = $env:ULTIMATEMACRO_UPDATE_DIRECTORY
        $staged = Join-Path $folder 'new.exe'
        $backup = Join-Path $folder 'previous.exe'
        $replaced = $false
        try {
            $runningApp = Get-Process -Id ([int]$env:ULTIMATEMACRO_UPDATE_PID) -ErrorAction SilentlyContinue
            if ($runningApp -and !$runningApp.WaitForExit(30000)) { throw 'Ultimate Macro wurde nicht rechtzeitig beendet.' }
            [IO.File]::Replace($staged, $target, $backup)
            $replaced = $true
            Start-Process -FilePath $target -WorkingDirectory ([IO.Path]::GetDirectoryName($target))
            $replaced = $false
            Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
        } catch {
            $message = $_.Exception.Message
            if ($replaced) {
                try { [IO.File]::Replace($backup, $target, (Join-Path $folder 'failed.exe')); $replaced = $false }
                catch { $message += "`nSicherung: $backup" }
            }
            Add-Type -AssemblyName System.Windows.Forms
            [void][Windows.Forms.MessageBox]::Show("Update fehlgeschlagen.`n$message", 'Ultimate Macro')
        } finally {
            Remove-Item -LiteralPath $staged -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath (Join-Path $folder 'failed.exe') -Force -ErrorAction SilentlyContinue
            try { [IO.Directory]::Delete($folder) } catch {}
        }
        """;
}
