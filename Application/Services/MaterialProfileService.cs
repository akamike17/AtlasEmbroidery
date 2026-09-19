namespace AtlasEmbroidery.Application.Services;

using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Infrastructure.Services;

/// <summary>
/// Servicio para gestión de perfiles de material (CRUD, versionado, confianza)
/// </summary>
public sealed class MaterialProfileService
{
    private readonly ITemplateStore _templateStore;

    public MaterialProfileService(ITemplateStore templateStore)
    {
        _templateStore = templateStore;
    }

    /// <summary>
    /// Crea un nuevo perfil de material
    /// </summary>
    public async Task<MaterialProfile> CreateAsync(MaterialProfile profile, CancellationToken ct = default)
    {
        profile.Id = Guid.NewGuid();
        profile.CreatedAt = DateTime.UtcNow;
        profile.ModifiedAt = DateTime.UtcNow;
        
        var id = profile.Id.ToString();
        await _templateStore.SaveAsync(profile, id, ct);
        
        return profile;
    }

    /// <summary>
    /// Obtiene un perfil por ID
    /// </summary>
    public async Task<MaterialProfile?> GetAsync(string id, CancellationToken ct = default)
    {
        return await _templateStore.LoadAsync<MaterialProfile>(id, ct);
    }

    /// <summary>
    /// Actualiza un perfil existente (crea nueva versión)
    /// </summary>
    public async Task<MaterialProfile> UpdateAsync(MaterialProfile profile, CancellationToken ct = default)
    {
        profile.ModifiedAt = DateTime.UtcNow;
        
        var id = profile.Id.ToString();
        await _templateStore.SaveAsync(profile, id, ct);
        
        return profile;
    }

    /// <summary>
    /// Elimina un perfil (soft delete - lo marca como inactivo)
    /// </summary>
    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await _templateStore.DeleteAsync(id, ct);
    }

    /// <summary>
    /// Lista todos los perfiles de una categoría
    /// </summary>
    public async Task<IEnumerable<TemplateInfo>> ListAsync(CancellationToken ct = default)
    {
        return await _templateStore.ListAsync<MaterialProfile>(ct);
    }

    /// <summary>
    /// Duplica un perfil del sistema (para personalización)
    /// </summary>
    public async Task<MaterialProfile> DuplicateSystemTemplateAsync(string systemTemplateId, string newName, CancellationToken ct = default)
    {
        var template = await GetAsync(systemTemplateId, ct);
        if (template == null || !template.IsSystemTemplate)
            throw new InvalidOperationException("Template not found or not a system template");

        var duplicate = template.DeepClone();
        duplicate.Id = Guid.NewGuid();
        duplicate.Name = newName;
        duplicate.IsSystemTemplate = false;
        duplicate.CreatedAt = DateTime.UtcNow;
        duplicate.ModifiedAt = DateTime.UtcNow;
        duplicate.Confidence = ConfidenceLevel.Custom;

        await CreateAsync(duplicate, ct);
        
        return duplicate;
    }

    /// <summary>
    /// Compara dos perfiles
    /// </summary>
    public ProfileComparison Compare(string id1, string id2)
    {
        var p1 = GetAsync(id1).Result;
        var p2 = GetAsync(id2).Result;

        if (p1 == null || p2 == null)
            throw new ArgumentException("One or both profiles not found");

        return new ProfileComparison
        {
            Profile1Id = p1.Id,
            Profile1Name = p1.Name,
            Profile2Id = p2.Id,
            Profile2Name = p2.Name,
            Differences = ComputeDifferences(p1, p2)
        };
    }

    private List<ProfileDifference> ComputeDifferences(MaterialProfile p1, MaterialProfile p2)
    {
        var differences = new List<ProfileDifference>();

        // Compare properties using reflection or manual comparison
        if (p1.FabricType != p2.FabricType)
            differences.Add(new ProfileDifference { Property = "FabricType", Value1 = p1.FabricType?.ToString() ?? "", Value2 = p2.FabricType?.ToString() ?? "" });
        
        if (p1.WeightGsm != p2.WeightGsm)
            differences.Add(new ProfileDifference { Property = "WeightGsm", Value1 = p1.WeightGsm.ToString(), Value2 = p2.WeightGsm.ToString() });

        if (p1.ThicknessMicrons != p2.ThicknessMicrons)
            differences.Add(new ProfileDifference { Property = "ThicknessMicrons", Value1 = p1.ThicknessMicrons.ToString(), Value2 = p2.ThicknessMicrons.ToString() });

        if (p1.Elasticity != p2.Elasticity)
            differences.Add(new ProfileDifference { Property = "Elasticity", Value1 = p1.Elasticity.ToString(), Value2 = p2.Elasticity.ToString() });

        if (p1.HasNap != p2.HasNap)
            differences.Add(new ProfileDifference { Property = "HasNap", Value1 = p1.HasNap.ToString(), Value2 = p2.HasNap.ToString() });

        if (p1.ThreadMaterial != p2.ThreadMaterial)
            differences.Add(new ProfileDifference { Property = "ThreadMaterial", Value1 = p1.ThreadMaterial?.ToString() ?? "", Value2 = p2.ThreadMaterial?.ToString() ?? "" });

        if (p1.ThreadWeight != p2.ThreadWeight)
            differences.Add(new ProfileDifference { Property = "ThreadWeight", Value1 = p1.ThreadWeight.ToString(), Value2 = p2.ThreadWeight.ToString() });

        if (p1.NeedleSystem != p2.NeedleSystem)
            differences.Add(new ProfileDifference { Property = "NeedleSystem", Value1 = p1.NeedleSystem?.ToString() ?? "", Value2 = p2.NeedleSystem?.ToString() ?? "" });

        if (p1.NeedlePoint != p2.NeedlePoint)
            differences.Add(new ProfileDifference { Property = "NeedlePoint", Value1 = p1.NeedlePoint?.ToString() ?? "", Value2 = p2.NeedlePoint?.ToString() ?? "" });

        if (p1.NeedleSize != p2.NeedleSize)
            differences.Add(new ProfileDifference { Property = "NeedleSize", Value1 = p1.NeedleSize.ToString(), Value2 = p2.NeedleSize.ToString() });

        if (p1.StabilizerType != p2.StabilizerType)
            differences.Add(new ProfileDifference { Property = "StabilizerType", Value1 = p1.StabilizerType?.ToString() ?? "", Value2 = p2.StabilizerType?.ToString() ?? "" });

        if (p1.StabilizerLayers != p2.StabilizerLayers)
            differences.Add(new ProfileDifference { Property = "StabilizerLayers", Value1 = p1.StabilizerLayers.ToString(), Value2 = p2.StabilizerLayers.ToString() });

        if (p1.Confidence != p2.Confidence)
            differences.Add(new ProfileDifference { Property = "Confidence", Value1 = p1.Confidence.ToString(), Value2 = p2.Confidence.ToString() });

        return differences;
    }
}

/// <summary>
/// Servicio para gestión de WorkProfiles (combinación de perfiles)
/// </summary>
public sealed class WorkProfileService
{
    private readonly ITemplateStore _templateStore;
    private readonly MaterialProfileService _materialService;

    public WorkProfileService(ITemplateStore templateStore, MaterialProfileService materialService)
    {
        _templateStore = templateStore;
        _materialService = materialService;
    }

    /// <summary>
    /// Crea un WorkProfile combinando perfiles existentes
    /// </summary>
    public async Task<WorkProfile> CreateAsync(WorkProfile workProfile, CancellationToken ct = default)
    {
        workProfile.Id = Guid.NewGuid();
        workProfile.CreatedAt = DateTime.UtcNow;
        workProfile.ModifiedAt = DateTime.UtcNow;

        // Calcular confianza global (mínima de componentes)
        workProfile.Confidence = ComputeOverallConfidence(workProfile);

        // Calcular parámetros derivados
        ComputeDerivedParameters(workProfile);

        var id = workProfile.Id.ToString();
        await _templateStore.SaveAsync(workProfile, id, ct);

        return workProfile;
    }

    /// <summary>
    /// Obtiene un WorkProfile por ID
    /// </summary>
    public async Task<WorkProfile?> GetAsync(string id, CancellationToken ct = default)
    {
        return await _templateStore.LoadAsync<WorkProfile>(id, ct);
    }

    /// <summary>
    /// Actualiza un WorkProfile
    /// </summary>
    public async Task<WorkProfile> UpdateAsync(WorkProfile workProfile, CancellationToken ct = default)
    {
        workProfile.ModifiedAt = DateTime.UtcNow;
        workProfile.Confidence = ComputeOverallConfidence(workProfile);
        ComputeDerivedParameters(workProfile);

        var id = workProfile.Id.ToString();
        await _templateStore.SaveAsync(workProfile, id, ct);

        return workProfile;
    }

    /// <summary>
    /// Lista WorkProfiles
    /// </summary>
    public async Task<IEnumerable<TemplateInfo>> ListAsync(CancellationToken ct = default)
    {
        return await _templateStore.ListAsync<WorkProfile>(ct);
    }

    /// <summary>
    /// Elimina un WorkProfile
    /// </summary>
    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await _templateStore.DeleteAsync(id, ct);
    }

    /// <summary>
    /// Combina perfiles en un nuevo WorkProfile (AutoSetup)
    /// </summary>
    public async Task<WorkProfile> CombineProfilesAsync(
        MaterialProfile fabric,
        MaterialProfile thread,
        MaterialProfile needle,
        MaterialProfile stabilizer,
        MachineProfile machine,
        HoopProfile hoop,
        MaterialProfile? backing = null,
        MaterialProfile? topping = null,
        string name = "",
        CancellationToken ct = default)
    {
        var workProfile = new WorkProfile
        {
            Name = string.IsNullOrEmpty(name) ? $"{fabric.Name} + {thread.Name}" : name,
            Fabric = fabric,
            Thread = thread,
            Needle = needle,
            Stabilizer = stabilizer,
            Backing = backing,
            Topping = topping,
            Machine = machine,
            Hoop = hoop
        };

        return await CreateAsync(workProfile, ct);
    }

    private ConfidenceLevel ComputeOverallConfidence(WorkProfile wp)
    {
        var confidences = new List<ConfidenceLevel>();
        
        if (wp.Fabric != null) confidences.Add(wp.Fabric.Confidence);
        if (wp.Thread != null) confidences.Add(wp.Thread.Confidence);
        if (wp.Needle != null) confidences.Add(wp.Needle.Confidence);
        if (wp.Stabilizer != null) confidences.Add(wp.Stabilizer.Confidence);
        if (wp.Backing != null) confidences.Add(wp.Backing.Confidence);
        if (wp.Topping != null) confidences.Add(wp.Topping.Confidence);
        if (wp.Machine != null) confidences.Add(ConfidenceLevel.Validated); // Machine profiles are validated
        if (wp.Hoop != null) confidences.Add(ConfidenceLevel.Validated);

        if (confidences.Count == 0) return ConfidenceLevel.Custom;

        // Minimum confidence
        return confidences.Min();
    }

    private void ComputeDerivedParameters(WorkProfile wp)
    {
        // Base values
        wp.RecommendedDensity = 400;
        wp.RecommendedUnderlayDensity = 800;
        wp.RecommendedPullComp = 200;
        wp.RecommendedMaxSpeed = 800;

        // Fabric adjustments
        if (wp.Fabric != null)
        {
            switch (wp.Fabric.FabricType)
            {
                case FabricType.Towel:
                    wp.RecommendedDensity = 300;
                    wp.RecommendedPullComp = 300;
                    wp.RequiresTopping = true;
                    break;
                case FabricType.Jersey:
                case FabricType.Pique:
                    wp.RecommendedPullComp = 300;
                    break;
                case FabricType.Denim:
                case FabricType.Canvas:
                    wp.RecommendedPullComp = 150;
                    wp.RecommendedDensity = 350;
                    break;
                case FabricType.Silk:
                case FabricType.Synthetic:
                    wp.RecommendedDensity = 500;
                    wp.RecommendedPullComp = 150;
                    break;
                case FabricType.Leather:
                    wp.RecommendedPullComp = 100;
                    wp.RecommendedMaxSpeed = 600;
                    break;
            }

            if (wp.Fabric.HasNap)
            {
                wp.RequiresKnockdown = true;
            }

            if (wp.Fabric.Elasticity > 0.3)
            {
                wp.RecommendedPullComp = Math.Max(wp.RecommendedPullComp, 300);
            }
        }

        // Thread adjustments
        if (wp.Thread != null)
        {
            if (wp.Thread.ThreadWeight > 40)
            {
                wp.RecommendedDensity = (int)(wp.RecommendedDensity * 1.2);
            }
            else if (wp.Thread.ThreadWeight < 40)
            {
                wp.RecommendedDensity = (int)(wp.RecommendedDensity * 0.9);
            }

            if (wp.Thread.ThreadMaterial == ThreadMaterial.Metallic)
            {
                wp.RecommendedMaxSpeed = Math.Min(wp.RecommendedMaxSpeed, 600);
            }
        }

        // Needle adjustments
        if (wp.Needle != null)
        {
            if (wp.Needle.NeedlePoint == NeedlePoint.BallPoint)
            {
                // Good for knits
            }
        }

        // Machine limits
        if (wp.Machine != null)
        {
            wp.RecommendedMaxSpeed = Math.Min(wp.RecommendedMaxSpeed, wp.Machine.MaxSpeed);
        }

        // Hoop adjustments
        if (wp.Hoop != null)
        {
            if (wp.Hoop.Type == HoopType.Cap)
            {
                wp.RequiresCapProfile = true;
                wp.RecommendedMaxSpeed = Math.Min(wp.RecommendedMaxSpeed, 700);
            }
        }
    }
}

/// <summary>
/// Servicio para MachineProfiles
/// </summary>
public sealed class MachineProfileService
{
    private readonly ITemplateStore _templateStore;

    public MachineProfileService(ITemplateStore templateStore)
    {
        _templateStore = templateStore;
    }

    public async Task<MachineProfile> CreateAsync(MachineProfile profile, CancellationToken ct = default)
    {
        profile.Id = Guid.NewGuid();
        profile.CreatedAt = DateTime.UtcNow;
        profile.ModifiedAt = DateTime.UtcNow;

        var id = profile.Id.ToString();
        await _templateStore.SaveAsync(profile, id, ct);
        
        return profile;
    }

    public async Task<MachineProfile?> GetAsync(string id, CancellationToken ct = default)
    {
        return await _templateStore.LoadAsync<MachineProfile>(id, ct);
    }

    public async Task<MachineProfile> UpdateAsync(MachineProfile profile, CancellationToken ct = default)
    {
        profile.ModifiedAt = DateTime.UtcNow;
        
        var id = profile.Id.ToString();
        await _templateStore.SaveAsync(profile, id, ct);
        
        return profile;
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await _templateStore.DeleteAsync(id, ct);
    }

    public async Task<IEnumerable<TemplateInfo>> ListAsync(CancellationToken ct = default)
    {
        return await _templateStore.ListAsync<MachineProfile>(ct);
    }

    public async Task<MachineProfile> DuplicateAsync(string sourceId, string newName, CancellationToken ct = default)
    {
        var source = await GetAsync(sourceId, ct);
        if (source == null) throw new ArgumentException("Source machine profile not found");

        var duplicate = source.DeepClone();
        duplicate.Id = Guid.NewGuid();
        duplicate.Name = newName;
        duplicate.IsBuiltIn = false;
        duplicate.CreatedAt = DateTime.UtcNow;
        duplicate.ModifiedAt = DateTime.UtcNow;

        return await CreateAsync(duplicate, ct);
    }
}

/// <summary>
/// Servicio para HoopProfiles
/// </summary>
public sealed class HoopProfileService
{
    private readonly ITemplateStore _templateStore;

    public HoopProfileService(ITemplateStore templateStore)
    {
        _templateStore = templateStore;
    }

    public async Task<HoopProfile> CreateAsync(HoopProfile profile, CancellationToken ct = default)
    {
        profile.Id = Guid.NewGuid();
        profile.CreatedAt = DateTime.UtcNow;
        profile.ModifiedAt = DateTime.UtcNow;

        var id = profile.Id.ToString();
        await _templateStore.SaveAsync(profile, id, ct);
        
        return profile;
    }

    public async Task<HoopProfile?> GetAsync(string id, CancellationToken ct = default)
    {
        return await _templateStore.LoadAsync<HoopProfile>(id, ct);
    }

    public async Task<HoopProfile> UpdateAsync(HoopProfile profile, CancellationToken ct = default)
    {
        profile.ModifiedAt = DateTime.UtcNow;
        
        var id = profile.Id.ToString();
        await _templateStore.SaveAsync(profile, id, ct);
        
        return profile;
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await _templateStore.DeleteAsync(id, ct);
    }

    public async Task<IEnumerable<TemplateInfo>> ListAsync(CancellationToken ct = default)
    {
        return await _templateStore.ListAsync<HoopProfile>(ct);
    }

    public async Task<IEnumerable<HoopProfile>> GetByMachineAsync(string machineBrand, string? machineModel = null, CancellationToken ct = default)
    {
        var all = await ListAsync(ct);
        var hoops = new List<HoopProfile>();

        foreach (var info in all)
        {
            var hoop = await GetAsync(info.Id, ct);
            if (hoop != null && 
                hoop.MachineBrand?.Equals(machineBrand, StringComparison.OrdinalIgnoreCase) == true &&
                (machineModel == null || hoop.MachineModel?.Equals(machineModel, StringComparison.OrdinalIgnoreCase) == true))
            {
                hoops.Add(hoop);
            }
        }

        return hoops;
    }
}

/// <summary>
/// Resultado de comparación de perfiles
/// </summary>
public sealed class ProfileComparison
{
    public Guid Profile1Id { get; set; }
    public string Profile1Name { get; set; } = "";
    public Guid Profile2Id { get; set; }
    public string Profile2Name { get; set; } = "";
    public List<ProfileDifference> Differences { get; set; } = new();
    public bool AreEqual => Differences.Count == 0;
}

public sealed class ProfileDifference
{
    public string Property { get; set; } = "";
    public string Value1 { get; set; } = "";
    public string Value2 { get; set; } = "";
}