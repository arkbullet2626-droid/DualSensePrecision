using System.Text.Json;
using DualSensePrecision.Configuration;

namespace DualSensePrecision.Profiles;

public sealed class ProfileConflictException(string message, Profile existing) : Exception(message)
{
    public Profile Existing { get; } = existing;
}

/// <summary>
/// One JSON file per profile, named by profile ID. Never overwrites without an explicit <c>overwrite: true</c>,
/// which the UI only passes after the user confirms.
/// </summary>
public sealed class ProfileStore
{
    public string Directory { get; }

    public ProfileStore(string? directory = null)
    {
        Directory = directory ?? AppPaths.ProfilesDir;
        System.IO.Directory.CreateDirectory(Directory);
    }

    private string PathFor(string id) => Path.Combine(Directory, $"{id}.json");

    /// <summary>Loads every readable profile. Unreadable files are reported, not silently skipped.</summary>
    public IReadOnlyList<Profile> LoadAll(out IReadOnlyList<string> errors)
    {
        var list = new List<Profile>();
        var errs = new List<string>();
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            try
            {
                var p = Read(file);
                if (p != null) list.Add(p);
            }
            catch (Exception ex)
            {
                errs.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        errors = errs;
        return list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public Profile? Get(string id) => File.Exists(PathFor(id)) ? Read(PathFor(id)) : null;

    public Profile? FindByName(string name) =>
        LoadAll(out _).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));

    /// <summary>Creates a new profile. Fails if a different profile already uses the name.</summary>
    public Profile Create(string name, Profile? template = null)
    {
        name = ValidateName(name);
        if (FindByName(name) is { } existing)
            throw new ProfileConflictException($"A profile named \"{name}\" already exists.", existing);
        var now = DateTime.UtcNow;
        var p = (template ?? new Profile()) with { Id = Guid.NewGuid().ToString("N"), Name = name, CreatedUtc = now, ModifiedUtc = now };
        Json.WriteAtomic(PathFor(p.Id), p);
        return p;
    }

    /// <summary>Saves edits to an existing profile (same ID). This is an update, not an overwrite of another profile.</summary>
    public Profile Update(Profile p)
    {
        if (FindByName(p.Name) is { } clash && clash.Id != p.Id)
            throw new ProfileConflictException($"A profile named \"{p.Name}\" already exists.", clash);
        var saved = p with { ModifiedUtc = DateTime.UtcNow };
        Json.WriteAtomic(PathFor(saved.Id), saved);
        return saved;
    }

    public Profile Rename(string id, string newName)
    {
        var p = Get(id) ?? throw new FileNotFoundException("Profile not found.", id);
        return Update(p with { Name = ValidateName(newName) });
    }

    public Profile Duplicate(string id)
    {
        var p = Get(id) ?? throw new FileNotFoundException("Profile not found.", id);
        string name = UniqueName($"{p.Name} copy");
        return Create(name, p);
    }

    public void Delete(string id)
    {
        var path = PathFor(id);
        if (File.Exists(path)) File.Delete(path);
    }

    public void Export(string id, string destinationFile)
    {
        var p = Get(id) ?? throw new FileNotFoundException("Profile not found.", id);
        Json.WriteAtomic(destinationFile, p);
    }

    /// <summary>
    /// Imports a profile file. If its ID or name matches an existing profile, throws ProfileConflictException
    /// unless <paramref name="overwrite"/> (replace the match) or <paramref name="asCopy"/> (new ID + unique name).
    /// </summary>
    public Profile Import(string sourceFile, bool overwrite = false, bool asCopy = false)
    {
        var incoming = Read(sourceFile) ?? throw new InvalidDataException("The file is not a DualSense Precision profile.");
        if (incoming.SchemaVersion > Profile.CurrentSchema)
            throw new InvalidDataException($"This profile was made by a newer version (schema {incoming.SchemaVersion}).");

        var byId = Get(incoming.Id);
        var byName = FindByName(incoming.Name);
        var clash = byId ?? byName;

        if (clash != null && !overwrite && !asCopy)
            throw new ProfileConflictException($"\"{incoming.Name}\" conflicts with an existing profile.", clash);

        if (clash != null && asCopy)
            return Create(UniqueName(incoming.Name), incoming);

        if (clash != null && overwrite)
        {
            var replaced = incoming with { Id = clash.Id, ModifiedUtc = DateTime.UtcNow };
            if (byId != null && byName != null && byId.Id != byName.Id) Delete(byName.Id); // avoid two profiles with one name
            Json.WriteAtomic(PathFor(replaced.Id), replaced);
            return replaced;
        }

        Json.WriteAtomic(PathFor(incoming.Id), incoming);
        return incoming;
    }

    /// <summary>Creates the built-in starter profiles if the store is empty.</summary>
    public void EnsureDefaults()
    {
        if (LoadAll(out _).Count > 0) return;
        foreach (var p in DefaultProfiles.All()) Json.WriteAtomic(PathFor(p.Id), p);
    }

    public string UniqueName(string baseName)
    {
        var names = LoadAll(out _).Select(p => p.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!names.Contains(baseName)) return baseName;
        for (int i = 2; ; i++)
            if (!names.Contains($"{baseName} {i}")) return $"{baseName} {i}";
    }

    private static string ValidateName(string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("Profile name cannot be empty.");
        if (name.Length > 64) throw new ArgumentException("Profile name must be 64 characters or fewer.");
        return name;
    }

    private static Profile? Read(string file) => JsonSerializer.Deserialize<Profile>(File.ReadAllText(file), Json.Options);
}
