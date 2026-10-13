using PackWise.Archiving;
using PackWise.Core.Archives;
using PackWise.Core.Security;
using PackWise.Services;

namespace PackWise.Browser;

/// <summary>An archive opened for browsing, with its folder tree and origin information.</summary>
public sealed class ArchiveSession : IDisposable
{
    private ArchiveSession(ArchiveReader reader, ArchiveLocation location, string? zoneIdentifier)
    {
        Reader = reader;
        Location = location;
        ZoneIdentifier = zoneIdentifier;
        Root = ArchiveTree.Build(reader.Entries);
    }

    public ArchiveReader Reader { get; private set; }

    public ArchiveLocation Location { get; }

    public ArchiveNode Root { get; private set; }

    public string FilePath => Location.ArchivePath;

    public ArchiveInfo Info => Reader.Info;

    /// <summary>Internet-origin mark of the archive (or of the outer archive it came from).</summary>
    public string? ZoneIdentifier { get; }

    public bool CameFromInternet => ZoneIdentifier is not null;

    public bool CanModify => Info.Format.CanUpdate && Info.FormatChain.Count == 1 && !Info.IsMultiVolume && !Location.IsNested;

    public static Task<ArchiveSession> OpenAsync(ArchiveLocation location, IPasswordProvider passwordProvider, CancellationToken cancellationToken = default)
    {
        var services = AppServices.Current;
        return Task.Run(() =>
        {
            var reader = ArchiveReader.Open(location.ArchivePath, new ArchiveOpenOptions
            {
                AllowUntrustedFormats = services.Settings.AllowUntrustedFormats,
                Password = services.Passwords.Get(location.ArchivePath),
                PasswordProvider = passwordProvider,
            }, cancellationToken);

            services.Passwords.Set(location.ArchivePath, reader.Password);
            return new ArchiveSession(reader, location, MarkOfTheWeb.Read(location.ArchivePath));
        }, cancellationToken);
    }

    /// <summary>Re-reads the archive after it was modified on disk.</summary>
    public async Task ReloadAsync(IPasswordProvider passwordProvider)
    {
        var services = AppServices.Current;
        var old = Reader;
        await Task.Run(old.Dispose).ConfigureAwait(true);
        Reader = await Task.Run(() => ArchiveReader.Open(FilePath, new ArchiveOpenOptions
        {
            AllowUntrustedFormats = services.Settings.AllowUntrustedFormats,
            Password = services.Passwords.Get(FilePath),
            PasswordProvider = passwordProvider,
        })).ConfigureAwait(true);
        Root = ArchiveTree.Build(Reader.Entries);
    }

    /// <summary>Remembers a password that worked during extraction for the rest of the session.</summary>
    public void RememberPassword() => AppServices.Current.Passwords.Set(FilePath, Reader.Password);

    public void Dispose() => Reader.Dispose();
}
