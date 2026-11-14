using System.Text.Json;

namespace UltimateMacro;

public sealed record MacroLibraryEntry(string Name, string FilePath, bool BesideApplication = false)
{
    public override string ToString() => BesideApplication ? Name + " (beside app)" : Name;
}

public sealed class MacroLibrary
{
    private readonly string applicationDirectory;
    public string DirectoryPath { get; }

    public MacroLibrary(string applicationDirectory)
    {
        this.applicationDirectory = Path.GetFullPath(applicationDirectory);
        DirectoryPath = Path.Combine(this.applicationDirectory, "Recordings");
    }

    public static string NameFromPath(string path)
    {
        var filename = Path.GetFileName(path);
        return filename.EndsWith(".macro.json", StringComparison.OrdinalIgnoreCase) ? filename[..^11] : Path.GetFileNameWithoutExtension(filename);
    }

    public string GetPath(string name)
    {
        name = name.Trim();
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (name.Length == 0 || name.Length > 120 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') ||
            stem is "CON" or "PRN" or "AUX" or "NUL" ||
            stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".Contains(stem[3]))
            throw new InvalidDataException("Enter a name of 1–120 characters without Windows filename symbols (\\ / : * ? \" < > |), a trailing dot, or a reserved name such as CON.");
        return Path.Combine(DirectoryPath, name + ".macro.json");
    }

    public IReadOnlyList<MacroLibraryEntry> List()
    {
        var saved = Directory.Exists(DirectoryPath)
            ? Directory.EnumerateFiles(DirectoryPath).Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).Select(path => new MacroLibraryEntry(NameFromPath(path), path))
            : [];
        var previous = Directory.Exists(applicationDirectory)
            ? Directory.EnumerateFiles(applicationDirectory).Where(path => path.EndsWith(".macro.json", StringComparison.OrdinalIgnoreCase)).Select(path => new MacroLibraryEntry(NameFromPath(path), path, true))
            : [];
        return saved.Concat(previous).OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.BesideApplication).ToArray();
    }

    public string SavePath(string name)
    {
        var canonical = GetPath(name);
        return List().FirstOrDefault(entry => !entry.BesideApplication && string.Equals(entry.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))?.FilePath ?? canonical;
    }

    public string Rename(MacroLibraryEntry entry, string name)
    {
        name = name.Trim();
        var filename = Path.GetFileName(GetPath(name));
        var entries = List();
        var source = entries.FirstOrDefault(candidate => candidate.FilePath == entry.FilePath)
            ?? throw new FileNotFoundException($"„{entry.Name}“ ist nicht mehr in der Bibliothek vorhanden.", entry.FilePath);
        if (source.Name != name && entries.Any(candidate => candidate != source && string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new IOException($"Ein Makro namens „{name}“ ist bereits gespeichert. Wähle einen anderen Namen.");

        var directory = Path.GetDirectoryName(source.FilePath)!;
        if (!source.FilePath.EndsWith(".macro.json", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith(".macro", StringComparison.OrdinalIgnoreCase))
            filename = name + Path.GetExtension(source.FilePath);
        var destination = source.Name == name ? source.FilePath : Path.Combine(directory, filename);
        using var json = ReadForRename(source.FilePath);
        var root = json.RootElement;
        var nameField = nameof(MacroDocument.Name);
        if (destination == source.FilePath && root.TryGetProperty(nameField, out var previousName) &&
            previousName.ValueKind == JsonValueKind.String && previousName.GetString() == name)
            return destination;

        var temporary = TemporaryPath(destination);
        try
        {
            using (var output = File.Open(temporary, FileMode.CreateNew))
            using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = MacroFiles.JsonOptions.WriteIndented }))
            {
                writer.WriteStartObject();
                writer.WriteString(nameField, name);
                foreach (var property in root.EnumerateObject())
                    if (!property.NameEquals(nameField)) property.WriteTo(writer);
                writer.WriteEndObject();
            }

            var moved = destination != source.FilePath;
            if (moved) File.Move(source.FilePath, destination);
            try { File.Move(temporary, destination, overwrite: true); }
            catch (Exception updateError) when (moved && (updateError is IOException or UnauthorizedAccessException))
            {
                try { File.Move(destination, source.FilePath); }
                catch (Exception rollbackError) when (rollbackError is IOException or UnauthorizedAccessException)
                {
                    throw new IOException($"Der Name in der Makrodatei konnte nicht aktualisiert werden. Prüfe den bisherigen Pfad und „{destination}“.",
                        new AggregateException(updateError, rollbackError));
                }
                throw;
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return destination;
    }

    private static JsonDocument ReadForRename(string path)
    {
        try
        {
            var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
            document.Dispose();
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Der Makroname konnte nicht aktualisiert werden. Die Datei enthält ungültiges JSON.", error);
        }
        throw new InvalidDataException("Der Makroname konnte nicht aktualisiert werden. Die Datei enthält kein JSON-Objekt.");
    }

    private static string TemporaryPath(string path) => path + "." + Guid.NewGuid().ToString("N") + ".tmp";

    public bool Delete(MacroLibraryEntry entry)
    {
        var path = Path.GetFullPath(entry.FilePath);
        var listed = List().FirstOrDefault(candidate => string.Equals(candidate.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (listed is null) return false;
        File.Delete(listed.FilePath);
        return true;
    }

    public string Save(string name, MacroDocument document, bool overwrite)
    {
        var path = SavePath(name);
        MacroFiles.ValidateOptions(document);
        MacroFiles.ValidateActions(document.Actions, []);
        document.Name = name.Trim();
        Directory.CreateDirectory(DirectoryPath);
        var temporary = TemporaryPath(path);
        try
        {
            File.WriteAllText(temporary, MacroFiles.Serialize(document));
            File.Move(temporary, path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return path;
    }
}
