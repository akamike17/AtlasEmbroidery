namespace AtlasEmbroidery.Infrastructure.Services;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Serialization;
using AtlasEmbroidery.Domain.Stitching;
using AtlasEmbroidery.Domain.Geometry;

/// <summary>
/// Servicio de persistencia para archivos .atlas (sin SQL)
/// Almacena proyectos como archivos JSON con hash de integridad
/// </summary>
public interface IProjectStore
{
    Task<AtlasProject?> LoadAsync(string projectPath, CancellationToken ct = default);
    Task SaveAsync(AtlasProject project, string projectPath, CancellationToken ct = default);
    Task<bool> ExistsAsync(string projectPath, CancellationToken ct = default);
    Task DeleteAsync(string projectPath, CancellationToken ct = default);
    Task<IEnumerable<string>> ListProjectsAsync(string directory, CancellationToken ct = default);
    Task<ProjectMetadata?> GetMetadataAsync(string projectPath, CancellationToken ct = default);
}

/// <summary>
/// Metadatos ligeros de proyecto (sin cargar completo)
/// </summary>
public sealed class ProjectMetadata
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public long FileSize { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public Guid ProjectId { get; set; }
    public string ProjectName { get; set; } = "";
    public int FormatVersion { get; set; }
    public int ObjectCount { get; set; }
    public long StitchCount { get; set; }
    public int ColorCount { get; set; }
    public string? ContentHash { get; set; }
    public bool IsValid { get; set; }
    public string? ValidationStatus { get; set; }
}

/// <summary>
/// Implementación basada en sistema de archivos
/// </summary>
public sealed class FileProjectStore : IProjectStore
{
    private readonly ILogger<FileProjectStore>? _logger;
    private readonly JsonSerializerOptions _metadataOptions;

    public FileProjectStore(ILogger<FileProjectStore>? logger = null)
    {
        _logger = logger;
        _metadataOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<AtlasProject?> LoadAsync(string projectPath, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(projectPath))
            {
                _logger?.LogWarning("Project file not found: {Path}", projectPath);
                return null;
            }

            var json = await File.ReadAllTextAsync(projectPath, ct);
            var project = AtlasSerializer.DeserializeFromJson(json);

            if (project != null)
            {
                // Verificar integridad
                var computedHash = AtlasSerializer.ComputeContentHash(project);
                if (!string.IsNullOrEmpty(project.ContentHash) &&
                    !string.Equals(computedHash, project.ContentHash, StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogWarning("Project integrity check failed: {Path}", projectPath);
                    // No fallar, solo advertir - el usuario decide
                }
            }

            return project;
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "Failed to deserialize project: {Path}", projectPath);
            throw new InvalidDataException($"Invalid project file format: {projectPath}", ex);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error loading project: {Path}", projectPath);
            throw;
        }
    }

    public async Task SaveAsync(AtlasProject project, string projectPath, CancellationToken ct = default)
    {
        try
        {
            // Actualizar metadatos
            project.LastSavedAt = DateTime.UtcNow;
            project.ModifiedAt = DateTime.UtcNow;
            project.ContentHash = AtlasSerializer.ComputeContentHash(project);

            // Asegurar directorio
            var directory = Path.GetDirectoryName(projectPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Escribir a archivo temporal y luego mover (atomicidad)
            var tempPath = projectPath + ".tmp";
            var json = AtlasSerializer.SerializeToJson(project, pretty: true);
            await File.WriteAllTextAsync(tempPath, json, ct);
            File.Move(tempPath, projectPath, overwrite: true);

            _logger?.LogInformation("Project saved: {Path} ({Size} bytes)", projectPath, json.Length);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error saving project: {Path}", projectPath);
            throw;
        }
    }

    public async Task<bool> ExistsAsync(string projectPath, CancellationToken ct = default)
    {
        return await Task.FromResult(File.Exists(projectPath));
    }

    public async Task DeleteAsync(string projectPath, CancellationToken ct = default)
    {
        if (File.Exists(projectPath))
        {
            File.Delete(projectPath);
            _logger?.LogInformation("Project deleted: {Path}", projectPath);
        }
        await Task.CompletedTask;
    }

    public async Task<IEnumerable<string>> ListProjectsAsync(string directory, CancellationToken ct = default)
    {
        if (!Directory.Exists(directory))
            return Enumerable.Empty<string>();

        var files = Directory.GetFiles(directory, "*.atlas", SearchOption.TopDirectoryOnly);
        return await Task.FromResult(files.OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc));
    }

    public async Task<ProjectMetadata?> GetMetadataAsync(string projectPath, CancellationToken ct = default)
    {
        if (!File.Exists(projectPath))
            return null;

        try
        {
            var info = new FileInfo(projectPath);
            var json = await File.ReadAllTextAsync(projectPath, ct);

            // Parse mínimo para metadatos
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var metadata = new ProjectMetadata
            {
                FilePath = projectPath,
                FileName = info.Name,
                FileSize = info.Length,
                CreatedAt = info.CreationTimeUtc,
                ModifiedAt = info.LastWriteTimeUtc
            };

            if (root.TryGetProperty("id", out var idProp) && Guid.TryParse(idProp.GetString(), out var pid))
                metadata.ProjectId = pid;

            if (root.TryGetProperty("name", out var nameProp))
                metadata.ProjectName = nameProp.GetString() ?? "";
            // Fallback for PascalCase
            else if (root.TryGetProperty("Name", out var nameProp2))
                metadata.ProjectName = nameProp2.GetString() ?? "";

            if (root.TryGetProperty("formatVersion", out var verProp))
            {
                if (verProp.ValueKind == JsonValueKind.Number)
                    metadata.FormatVersion = verProp.GetInt32();
                else if (verProp.ValueKind == JsonValueKind.String && int.TryParse(verProp.GetString(), out var ver))
                    metadata.FormatVersion = ver;
            }

            if (root.TryGetProperty("objects", out var objProp) && objProp.ValueKind == JsonValueKind.Array)
                metadata.ObjectCount = objProp.GetArrayLength();
            // Fallback for PascalCase
            else if (root.TryGetProperty("Objects", out var objProp2) && objProp2.ValueKind == JsonValueKind.Array)
                metadata.ObjectCount = objProp2.GetArrayLength();

            if (root.TryGetProperty("threadPalette", out var paletteProp) && paletteProp.ValueKind == JsonValueKind.Array)
                metadata.ColorCount = paletteProp.GetArrayLength();
            // Fallback for PascalCase
            else if (root.TryGetProperty("ThreadPalette", out var paletteProp2) && paletteProp2.ValueKind == JsonValueKind.Array)
                metadata.ColorCount = paletteProp2.GetArrayLength();

            if (root.TryGetProperty("contentHash", out var hashProp))
                metadata.ContentHash = hashProp.GetString();

            if (root.TryGetProperty("validationResult", out var valProp))
            {
                if (valProp.TryGetProperty("overallStatus", out var statusProp))
                    metadata.ValidationStatus = statusProp.GetString();
            }

            // Verificar integridad rápida
            metadata.IsValid = !string.IsNullOrEmpty(metadata.ContentHash);

            return metadata;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error reading project metadata: {Path}", projectPath);
            return new ProjectMetadata
            {
                FilePath = projectPath,
                FileName = Path.GetFileName(projectPath),
                IsValid = false
            };
        }
    }
}

/// <summary>
/// Store para plantillas de material (MaterialProfile, WorkProfile, MachineProfile, HoopProfile)
/// </summary>
public interface ITemplateStore
{
    Task<T?> LoadAsync<T>(string templateId, CancellationToken ct = default) where T : class;
    Task SaveAsync<T>(T template, string templateId, CancellationToken ct = default) where T : class;
    Task DeleteAsync(string templateId, CancellationToken ct = default);
    Task<IEnumerable<TemplateInfo>> ListAsync<T>(CancellationToken ct = default) where T : class;
    Task<bool> ExistsAsync(string templateId, CancellationToken ct = default);
}

public sealed class TemplateInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public DateTime ModifiedAt { get; set; }
    public bool IsSystemTemplate { get; set; }
    public ConfidenceLevel Confidence { get; set; }
}

/// <summary>
/// Implementación basada en archivos JSON por categoría
/// </summary>
public sealed class FileTemplateStore : ITemplateStore
{
    private readonly string _basePath;
    private readonly ILogger<FileTemplateStore>? _logger;
    private readonly JsonSerializerOptions _options;

    public FileTemplateStore(string basePath, ILogger<FileTemplateStore>? logger = null)
    {
        _basePath = basePath;
        _logger = logger;
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters =
            {
                new PointJsonConverter(),
                new RectangleJsonConverter(),
                new ThreadColorJsonConverter(),
                new StitchPointJsonConverter(),
                new StitchTypeJsonConverter()
            }
        };

        // Asegurar directorios base
        Directory.CreateDirectory(Path.Combine(_basePath, "materials"));
        Directory.CreateDirectory(Path.Combine(_basePath, "machines"));
        Directory.CreateDirectory(Path.Combine(_basePath, "hoops"));
        Directory.CreateDirectory(Path.Combine(_basePath, "workprofiles"));
    }

    private string GetCategoryPath<T>() => typeof(T).Name switch
    {
        nameof(MaterialProfile) => "materials",
        nameof(MachineProfile) => "machines",
        nameof(HoopProfile) => "hoops",
        nameof(WorkProfile) => "workprofiles",
        _ => "custom"
    };

    private string GetFilePath<T>(string templateId) =>
        Path.Combine(_basePath, GetCategoryPath<T>(), $"{templateId}.json");

    public async Task<T?> LoadAsync<T>(string templateId, CancellationToken ct = default) where T : class
    {
        var path = GetFilePath<T>(templateId);
        if (!File.Exists(path)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            return JsonSerializer.Deserialize<T>(json, _options);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error loading template {Type}/{Id}", typeof(T).Name, templateId);
            return null;
        }
    }

    public async Task SaveAsync<T>(T template, string templateId, CancellationToken ct = default) where T : class
    {
        var path = GetFilePath<T>(templateId);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(template, _options);
        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, json, ct);
        File.Move(tempPath, path, overwrite: true);
    }

    public async Task DeleteAsync(string templateId, CancellationToken ct = default)
    {
        // Buscar en todas las categorías
        var categories = new[] { "materials", "machines", "hoops", "workprofiles", "custom" };
        foreach (var cat in categories)
        {
            var path = Path.Combine(_basePath, cat, $"{templateId}.json");
            if (File.Exists(path))
            {
                File.Delete(path);
                break;
            }
        }
        await Task.CompletedTask;
    }

    public async Task<IEnumerable<TemplateInfo>> ListAsync<T>(CancellationToken ct = default) where T : class
    {
        var catPath = Path.Combine(_basePath, GetCategoryPath<T>());
        if (!Directory.Exists(catPath))
            return Enumerable.Empty<TemplateInfo>();

        var files = Directory.GetFiles(catPath, "*.json");
        var result = new List<TemplateInfo>();

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var info = new TemplateInfo
                {
                    Id = Path.GetFileNameWithoutExtension(file),
                    Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Category = GetCategoryPath<T>(),
                    ModifiedAt = File.GetLastWriteTimeUtc(file),
                    IsSystemTemplate = root.TryGetProperty("isSystemTemplate", out var sys) && sys.GetBoolean(),
                    Confidence = root.TryGetProperty("confidence", out var conf) ? (ConfidenceLevel)conf.GetInt32() : ConfidenceLevel.Custom
                };
                result.Add(info);
            }
            catch
            {
                // Ignorar archivos corruptos
            }
        }

        return result.OrderByDescending(t => t.ModifiedAt);
    }

    public async Task<bool> ExistsAsync(string templateId, CancellationToken ct = default)
    {
        var categories = new[] { "materials", "machines", "hoops", "workprofiles", "custom" };
        foreach (var cat in categories)
        {
            var path = Path.Combine(_basePath, cat, $"{templateId}.json");
            if (File.Exists(path)) return true;
        }
        return false;
    }
}