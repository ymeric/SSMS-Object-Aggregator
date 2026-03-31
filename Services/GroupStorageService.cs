using SSMS.ObjectAggregator.Models;
using System.IO;
using System.Text.Json;

namespace SSMS.ObjectAggregator.Services;

public class GroupStorageService
{
    #region Fields

    private readonly string _storageFilePath;

    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    #endregion Fields

    #region Construction

    public GroupStorageService()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string targetFolder = Path.Combine(root, "SSMS.ObjectAggregator");
        Directory.CreateDirectory(targetFolder);
        _storageFilePath = Path.Combine(targetFolder, "groups.json");
    }

    #endregion Construction

    #region Properties

    public string StorageFolder => Path.GetDirectoryName(_storageFilePath)!;

    #endregion Properties

    #region Public API

    public async Task<IList<GroupDefinition>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_storageFilePath))
            {
                return new List<GroupDefinition>();
            }

            using var stream = File.OpenRead(_storageFilePath);
            var payload = await JsonSerializer.DeserializeAsync<List<GroupDefinition>>(stream, _serializerOptions, cancellationToken)
                          ?? new List<GroupDefinition>();
            return payload;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(IEnumerable<GroupDefinition> groups, CancellationToken cancellationToken = default)
    {
        if (groups is null)
        {
            throw new ArgumentNullException(nameof(groups));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var stream = File.Create(_storageFilePath);
            await JsonSerializer.SerializeAsync(stream, groups, _serializerOptions, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    #endregion Public API
}