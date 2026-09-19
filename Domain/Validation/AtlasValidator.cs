namespace AtlasEmbroidery.Domain.Validation;

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using AtlasEmbroidery.Domain.Models;

/// <summary>
/// Atlas Validator V1 - Detecta incompatibilidades, riesgos y defectos previsibles
/// Estados: 🟢 Apto - 🟡 Advertencia - 🔴 Crítico - ⚪ Desconocido
/// Familias: Geometría, Puntadas, Registro/deformación, Material, Máquina, Producción
/// </summary>
public sealed class AtlasValidator
{
    private readonly ValidatorOptions _options;
    private readonly ILogger<AtlasValidator>? _logger;

    public AtlasValidator(ValidatorOptions? options = null, ILogger<AtlasValidator>? logger = null)
    {
        _options = options ?? new ValidatorOptions();
        _logger = logger;
    }

    /// <summary>
    /// Valida un proyecto completo
    /// </summary>
    public ValidationResult Validate(AtlasProject project)
    {
        var result = new ValidationResult
        {
            ValidatorVersion = "1.0.0"
        };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // 1. Geometría
            ValidateGeometry(project, result);

            // 2. Puntadas
            ValidateStitches(project, result);

            // 3. Registro / deformación
            ValidateRegistration(project, result);

            // 4. Material
            ValidateMaterial(project, result);

            // 5. Máquina
            ValidateMachine(project, result);

            // 6. Producción
            ValidateProduction(project, result);

            // Determinar estado general
            result.OverallStatus = DetermineOverallStatus(result);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Validation error");
            result.AddIssue(ValidationSeverity.Critical, "VALIDATOR_ERROR", ex.Message);
            result.OverallStatus = ValidationStatus.Fail;
        }
        finally
        {
            stopwatch.Stop();
            result.ValidationTime = stopwatch.Elapsed;
        }

        return result;
    }

    /// <summary>
    /// Valida geometría: dimensiones, área, hoop, orientación, límites, splits, colisiones
    /// </summary>
    private void ValidateGeometry(AtlasProject project, ValidationResult result)
    {
        var bounds = project.GetDesignBounds();
        if (bounds.IsEmpty)
        {
            result.AddIssue(ValidationSeverity.Warning, "GEOM_EMPTY", "Design has no visible objects");
            return;
        }

        // Dimensiones
        if (bounds.Width > _options.MaxDesignWidth)
        {
            result.AddIssue(ValidationSeverity.Critical, "GEOM_WIDTH_EXCEEDS",
                $"Design width {bounds.Width / 1000.0:F1}mm exceeds maximum {_options.MaxDesignWidth / 1000.0:F1}mm",
                data: new() { { "width", bounds.Width }, { "max", _options.MaxDesignWidth } });
        }

        if (bounds.Height > _options.MaxDesignHeight)
        {
            result.AddIssue(ValidationSeverity.Critical, "GEOM_HEIGHT_EXCEEDS",
                $"Design height {bounds.Height / 1000.0:F1}mm exceeds maximum {_options.MaxDesignHeight / 1000.0:F1}mm",
                data: new() { { "height", bounds.Height }, { "max", _options.MaxDesignHeight } });
        }

        // Área
        double areaCm2 = (bounds.Width / 10000.0) * (bounds.Height / 10000.0);
        if (areaCm2 > _options.MaxDesignAreaCm2)
        {
            result.AddIssue(ValidationSeverity.Warning, "GEOM_AREA_LARGE",
                $"Design area {areaCm2:F1}cm² is large, may cause registration issues",
                data: new() { { "area_cm2", areaCm2 }, { "max", _options.MaxDesignAreaCm2 } });
        }

        // Hoop check
        if (project.SelectedHoop != null)
        {
            var usableArea = project.SelectedHoop.GetUsableArea();
            if (!project.SelectedHoop.FitsDesign(bounds))
            {
                result.AddIssue(ValidationSeverity.Critical, "GEOM_HOOP_FIT",
                    $"Design {bounds.Width / 1000.0:F1}x{bounds.Height / 1000.0:F1}mm does not fit in hoop {project.SelectedHoop.Name} ({usableArea.Width / 1000.0:F1}x{usableArea.Height / 1000.0:F1}mm usable)",
                    affectedArea: bounds,
                    data: new() { { "design_width", bounds.Width }, { "design_height", bounds.Height },
                                  { "hoop_usable_width", usableArea.Width }, { "hoop_usable_height", usableArea.Height } });
            }

            // Verificar si diseño está centrado en hoop
            var hoopCenter = project.SelectedHoop.CenterOffset;
            var designCenter = bounds.Center;
            int offsetX = Math.Abs(designCenter.X - hoopCenter.X);
            int offsetY = Math.Abs(designCenter.Y - hoopCenter.Y);
            int maxOffset = Math.Min(usableArea.Width, usableArea.Height) / 4;

            if (offsetX > maxOffset || offsetY > maxOffset)
            {
                result.AddIssue(ValidationSeverity.Warning, "GEOM_HOOP_OFFCENTER",
                    $"Design is offset from hoop center by {offsetX / 1000.0:F1}mm X, {offsetY / 1000.0:F1}mm Y",
                    data: new() { { "offset_x", offsetX }, { "offset_y", offsetY }, { "max_recommended", maxOffset } });
            }
        }
        else if (project.TargetMachine != null)
        {
            // Verificar contra área máxima de máquina
            var machineArea = project.TargetMachine.GetMaxEmbroideryArea();
            if (!machineArea.Contains(bounds))
            {
                result.AddIssue(ValidationSeverity.Critical, "GEOM_MACHINE_AREA",
                    $"Design exceeds machine {project.TargetMachine.Name} maximum area",
                    affectedArea: bounds);
            }
        }

        // Splits / multi-hoop (detectar si diseño requiere split)
        if (project.SelectedHoop != null)
        {
            var usable = project.SelectedHoop.GetUsableArea();
            if (bounds.Width > usable.Width || bounds.Height > usable.Height)
            {
                result.AddIssue(ValidationSeverity.Info, "GEOM_SPLIT_REQUIRED",
                    "Design requires multi-hoop splitting",
                    data: new() { { "requires_split", true } });
            }
        }

        // Colisiones previsibles (objetos muy cercanos)
        ValidateObjectCollisions(project, result);
    }

    private void ValidateObjectCollisions(AtlasProject project, ValidationResult result)
    {
        var visibleObjects = project.Objects.Where(o => o.Visible).ToList();
        for (int i = 0; i < visibleObjects.Count; i++)
        {
            for (int j = i + 1; j < visibleObjects.Count; j++)
            {
                var obj1 = visibleObjects[i];
                var obj2 = visibleObjects[j];

                if (obj1.Bounds.IntersectsWith(obj2.Bounds))
                {
                    var intersection = obj1.Bounds.Intersect(obj2.Bounds);
                    double overlapArea = intersection.Area;
                    double minArea = Math.Min(obj1.Bounds.Area, obj2.Bounds.Area);
                    double overlapPercent = minArea > 0 ? (overlapArea / minArea) * 100 : 0;

                    if (overlapPercent > _options.CollisionOverlapThresholdPercent)
                    {
                        result.AddIssue(ValidationSeverity.Warning, "GEOM_COLLISION",
                            $"Objects '{obj1.Name}' and '{obj2.Name}' overlap {overlapPercent:F1}%",
                            objectId: obj1.Id.ToString(),
                            data: new() { { "object1", obj1.Name }, { "object2", obj2.Name },
                                          { "overlap_percent", overlapPercent } });
                    }
                }
            }
        }
    }

    /// <summary>
    /// Valida puntadas: longitud, micro-puntadas, penetraciones cercanas/repetidas,
    /// densidad local, capas, jumps, trims, tie-ins, stops, comandos incompatibles
    /// </summary>
    private void ValidateStitches(AtlasProject project, ValidationResult result)
    {
        // Se valida después de compilar (StitchPlan)
        // Aquí validamos parámetros de configuración

        foreach (var obj in project.Objects.Where(o => o.Visible))
        {
            var param = obj.StitchParams;

            // Longitud de puntada
            if (param.MinStitchLength < _options.AbsoluteMinStitchLength)
            {
                result.AddIssue(ValidationSeverity.Critical, "STITCH_LEN_MIN",
                    $"Object '{obj.Name}': Min stitch length {param.MinStitchLength / 1000.0:F1}mm below absolute minimum {_options.AbsoluteMinStitchLength / 1000.0:F1}mm",
                    objectId: obj.Id.ToString(),
                    data: new() { { "min_length", param.MinStitchLength }, { "absolute_min", _options.AbsoluteMinStitchLength } });
            }

            if (param.MaxStitchLength > _options.AbsoluteMaxStitchLength)
            {
                result.AddIssue(ValidationSeverity.Warning, "STITCH_LEN_MAX",
                    $"Object '{obj.Name}': Max stitch length {param.MaxStitchLength / 1000.0:F1}mm exceeds recommended {_options.AbsoluteMaxStitchLength / 1000.0:F1}mm",
                    objectId: obj.Id.ToString(),
                    data: new() { { "max_length", param.MaxStitchLength }, { "recommended_max", _options.AbsoluteMaxStitchLength } });
            }

            // Densidad
            if (param.Density < _options.MinDensity)
            {
                result.AddIssue(ValidationSeverity.Warning, "STITCH_DENSITY_LOW",
                    $"Object '{obj.Name}': Density {param.Density / 1000.0:F1}mm may cause gaps",
                    objectId: obj.Id.ToString(),
                    data: new() { { "density", param.Density }, { "min_recommended", _options.MinDensity } });
            }

            if (param.Density > _options.MaxDensity)
            {
                result.AddIssue(ValidationSeverity.Warning, "STITCH_DENSITY_HIGH",
                    $"Object '{obj.Name}': Density {param.Density / 1000.0:F1}mm may cause thread breaks",
                    objectId: obj.Id.ToString(),
                    data: new() { { "density", param.Density }, { "max_recommended", _options.MaxDensity } });
            }

            // Satin column width
            if (param.PrimaryStitchType == StitchType.Satin && param.Satin != null)
            {
                if (param.Satin.ColumnWidth < _options.MinSatinWidth)
                {
                    result.AddIssue(ValidationSeverity.Critical, "SATIN_WIDTH_MIN",
                        $"Object '{obj.Name}': Satin column width {param.Satin.ColumnWidth / 1000.0:F1}mm too narrow",
                        objectId: obj.Id.ToString());
                }
                if (param.Satin.ColumnWidth > _options.MaxSatinWidth)
                {
                    result.AddIssue(ValidationSeverity.Warning, "SATIN_WIDTH_MAX",
                        $"Object '{obj.Name}': Satin column width {param.Satin.ColumnWidth / 1000.0:F1}mm may cause loose stitches",
                        objectId: obj.Id.ToString());
                }
            }

            // Jumps / trims
            if (param.MaxJumpDistance > _options.MaxJumpDistance)
            {
                result.AddIssue(ValidationSeverity.Warning, "STITCH_JUMP_LONG",
                    $"Object '{obj.Name}': Max jump {param.MaxJumpDistance / 1000.0:F1}mm may cause thread issues",
                    objectId: obj.Id.ToString());
            }

            if (param.TrimPolicy == TrimPolicy.Never && param.MaxJumpDistance > _options.MaxJumpNoTrim)
            {
                result.AddIssue(ValidationSeverity.Warning, "STITCH_NO_TRIM_LONG_JUMP",
                    $"Object '{obj.Name}': Long jumps without trim may leave loose threads",
                    objectId: obj.Id.ToString());
            }

            // Underlay
            if (param.Underlay != null && param.Underlay.Enabled)
            {
                if (param.Underlay.Density > param.Density * 2)
                {
                    result.AddIssue(ValidationSeverity.Info, "STITCH_UNDERLAY_DENSE",
                        $"Object '{obj.Name}': Underlay denser than main stitching",
                        objectId: obj.Id.ToString());
                }
            }
        }

        // Validar comandos de máquina vs capacidades
        if (project.TargetMachine != null)
        {
            ValidateMachineCommands(project, result);
        }
    }

    private void ValidateMachineCommands(AtlasProject project, ValidationResult result)
    {
        var machine = project.TargetMachine;
        if (machine == null) return;

        // Verificar si el diseño usa comandos no soportados
        bool hasTrims = project.Objects.Any(o => o.StitchParams.TrimPolicy != TrimPolicy.Never);
        bool hasColorChanges = project.ThreadPalette.Count > 1;
        bool hasStops = project.Objects.Any(o => o.StitchParams.PrimaryStitchType == StitchType.Stop);

        if (hasTrims && !machine.SupportsTrim)
        {
            result.AddIssue(ValidationSeverity.Critical, "MACH_TRIM_UNSUPPORTED",
                $"Machine {machine.Name} does not support trim commands",
                data: new() { { "machine", machine.Name } });
        }

        if (hasColorChanges && machine.NeedleCount == 1 && project.ThreadPalette.Count > 1)
        {
            result.AddIssue(ValidationSeverity.Warning, "MACH_SINGLE_NEEDLE_COLORS",
                $"Single-needle machine {machine.Name} requires manual thread changes for {project.ThreadPalette.Count} colors",
                data: new() { { "colors", project.ThreadPalette.Count }, { "needles", machine.NeedleCount } });
        }

        if (hasColorChanges && project.ThreadPalette.Count > machine.MaxColorChanges)
        {
            result.AddIssue(ValidationSeverity.Critical, "MACH_COLOR_CHANGES_EXCEED",
                $"Design has {project.ThreadPalette.Count} color changes, machine {machine.Name} supports max {machine.MaxColorChanges}",
                data: new() { { "colors", project.ThreadPalette.Count }, { "max", machine.MaxColorChanges } });
        }
    }

    /// <summary>
    /// Valida registro/deformación: pull/push, overlap, ángulos, dependencias, outlines, tatami grande
    /// </summary>
    private void ValidateRegistration(AtlasProject project, ValidationResult result)
    {
        // Pull compensation
        foreach (var obj in project.Objects.Where(o => o.Visible))
        {
            var param = obj.StitchParams;

            if (param.PullCompensation < _options.MinPullCompensation)
            {
                result.AddIssue(ValidationSeverity.Warning, "REG_PULL_COMP_LOW",
                    $"Object '{obj.Name}': Pull compensation {param.PullCompensation / 1000.0:F1}mm may be insufficient for fabric",
                    objectId: obj.Id.ToString(),
                    data: new() { { "pull_comp", param.PullCompensation }, { "min_recommended", _options.MinPullCompensation } });
            }

            if (param.PullCompensation > _options.MaxPullCompensation)
            {
                result.AddIssue(ValidationSeverity.Warning, "REG_PULL_COMP_HIGH",
                    $"Object '{obj.Name}': Pull compensation {param.PullCompensation / 1000.0:F1}mm may cause gaps",
                    objectId: obj.Id.ToString(),
                    data: new() { { "pull_comp", param.PullCompensation }, { "max_recommended", _options.MaxPullCompensation } });
            }

            // Overlap entre objetos adyacentes de diferente color
            if (param.Overlap < _options.MinOverlap)
            {
                result.AddIssue(ValidationSeverity.Info, "REG_OVERLAP_LOW",
                    $"Object '{obj.Name}': Overlap {param.Overlap / 1000.0:F1}mm may show gaps between colors",
                    objectId: obj.Id.ToString());
            }
        }

        // Tatami grande - riesgo de deformación
        var tatamiObjects = project.Objects.Where(o => o.Visible && o.StitchParams.PrimaryStitchType == StitchType.Tatami).ToList();
        foreach (var obj in tatamiObjects)
        {
            var bounds = obj.Bounds;
            double areaCm2 = (bounds.Width / 10000.0) * (bounds.Height / 10000.0);
            if (areaCm2 > _options.LargeTatamiAreaCm2)
            {
                result.AddIssue(ValidationSeverity.Warning, "REG_LARGE_TATAMI",
                    $"Object '{obj.Name}': Large tatami area {areaCm2:F1}cm² - consider splitting or using contour strategy",
                    objectId: obj.Id.ToString(),
                    data: new() { { "area_cm2", areaCm2 }, { "threshold", _options.LargeTatamiAreaCm2 } });
            }
        }

        // Ángulos de puntada - verificar consistencia
        ValidateStitchAngles(project, result);
    }

    private void ValidateStitchAngles(AtlasProject project, ValidationResult result)
    {
        // Agrupar objetos por color y verificar ángulos
        var byColor = project.Objects
            .Where(o => o.Visible)
            .GroupBy(o => o.StitchParams.ColorIndex)
            .Where(g => g.Count() > 1);

        foreach (var group in byColor)
        {
            var angles = group.Select(o => o.StitchParams.Angle).Distinct().ToList();
            if (angles.Count > 1)
            {
                // Verificar si ángulos son paralelos o perpendiculares (bueno) vs intermedios (riesgo)
                foreach (var a1 in angles)
                {
                    foreach (var a2 in angles)
                    {
                        if (a1 >= a2) continue;
                        int diff = Math.Abs(a1 - a2);
                        diff = Math.Min(diff, 1800 - diff); // Normalizar 0-180°

                        // Ángulos intermedios (15-75°) causan moiré
                        if (diff > 150 && diff < 750) // 15° a 75°
                        {
                            result.AddIssue(ValidationSeverity.Warning, "REG_ANGLE_MOIRE",
                                $"Color {group.Key}: Objects with angles {a1 / 10.0:F1}° and {a2 / 10.0:F1}° may cause moiré",
                                data: new() { { "color", group.Key }, { "angle1", a1 }, { "angle2", a2 }, { "diff", diff } });
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Valida material: estabilidad, elasticidad, textura/nap, grosor, backing, topping, aguja, hilo, velocidad, hooping
    /// </summary>
    private void ValidateMaterial(AtlasProject project, ValidationResult result)
    {
        if (project.WorkProfile == null) return;

        var profile = project.WorkProfile;

        // Tela con nap + satin -> requiere knockdown
        if (profile.Fabric?.HasNap == true)
        {
            var satinObjects = project.Objects.Where(o => o.Visible && o.StitchParams.PrimaryStitchType == StitchType.Satin).ToList();
            if (satinObjects.Any())
            {
                result.AddIssue(ValidationSeverity.Warning, "MAT_NAP_SATIN",
                    "Fabric has nap - satin stitches may sink. Consider knockdown underlay",
                    data: new() { { "fabric", profile.Fabric.Name }, { "satin_count", satinObjects.Count } });
            }
        }

        // Tela elástica -> más compensación pull
        if (profile.Fabric?.Elasticity > 0.3)
        {
            foreach (var obj in project.Objects.Where(o => o.Visible))
            {
                if (obj.StitchParams.PullCompensation < _options.MinPullCompensation * 2)
                {
                    result.AddIssue(ValidationSeverity.Warning, "MAT_ELASTIC_PULL",
                        $"Object '{obj.Name}': Elastic fabric requires higher pull compensation",
                        objectId: obj.Id.ToString(),
                        data: new() { { "elasticity", profile.Fabric.Elasticity }, { "current_comp", obj.StitchParams.PullCompensation } });
                }
            }
        }

        // Hilo metálico -> velocidad reducida, aguja especial
        if (profile.Thread?.ThreadMaterial == ThreadMaterial.Metallic)
        {
            result.AddIssue(ValidationSeverity.Info, "MAT_METALLIC_THREAD",
                "Metallic thread: reduce speed, use metallic needle, smooth path",
                data: new() { { "thread", profile.Thread.Name } });

            // Verificar aguja
            if (profile.Needle?.NeedlePoint != NeedlePoint.Metallic)
            {
                result.AddIssue(ValidationSeverity.Warning, "MAT_METALLIC_NEEDLE",
                    "Metallic thread requires metallic needle point",
                    data: new() { { "current_needle", profile.Needle?.NeedlePoint.ToString() ?? "Unknown" } });
            }
        }

        // Tela gruesa -> aguja más grande
        if (profile.Fabric?.ThicknessMicrons > 1000 && (profile.Needle?.NeedleSize ?? 0) < 90)
        {
            result.AddIssue(ValidationSeverity.Info, "MAT_THICK_FABRIC_NEEDLE",
                $"Thick fabric ({profile.Fabric.ThicknessMicrons / 1000.0:F1}mm) - consider larger needle (90/14 or 100/16)",
                data: new() { { "thickness_mm", profile.Fabric.ThicknessMicrons / 1000.0 }, { "current_needle", profile.Needle?.NeedleSize ?? 0 } });
        }

        // Estabilizador inadecuado para tela
        ValidateStabilizerSuitability(profile, result);
    }

    private void ValidateStabilizerSuitability(WorkProfile profile, ValidationResult result)
    {
        if (profile.Fabric == null || profile.Stabilizer == null) return;

        var fabric = profile.Fabric;
        var stabilizer = profile.Stabilizer;

        // Tejido de punto (jersey, piqué) -> cut-away
        if ((fabric.FabricType == FabricType.Jersey || fabric.FabricType == FabricType.Pique) &&
            stabilizer.StabilizerType != StabilizerType.CutAway)
        {
            result.AddIssue(ValidationSeverity.Warning, "MAT_STABILIZER_KNIT",
                $"Knit fabric ({fabric.FabricType}) usually requires cut-away stabilizer",
                data: new Dictionary<string, object> { { "fabric", fabric.FabricType.ToString()! }, { "stabilizer", stabilizer.StabilizerType.ToString()! } });
        }

        // Tela transparente/delicada -> no-show o wash-away
        if ((fabric.FabricType == FabricType.Silk || fabric.FabricType == FabricType.Synthetic) &&
            stabilizer.StabilizerType == StabilizerType.TearAway)
        {
            result.AddIssue(ValidationSeverity.Info, "MAT_STABILIZER_DELICATE",
                "Delicate fabric - consider no-show or wash-away stabilizer",
                data: new Dictionary<string, object> { { "fabric", fabric.FabricType.ToString()! } });
        }

        // Toalla -> topping requerido
        if (fabric.FabricType == FabricType.Towel && profile.Topping == null)
        {
            result.AddIssue(ValidationSeverity.Warning, "MAT_TOWEL_TOPPING",
                "Towel fabric requires water-soluble topping to prevent nap showing through",
                data: new Dictionary<string, object> { { "fabric", fabric.FabricType.ToString()! } });
        }
    }

    /// <summary>
    /// Valida máquina: área, hoops, agujas, comandos, límites stitch/jump, trims, accesorios, transporte, modelo/firmware
    /// </summary>
    private void ValidateMachine(AtlasProject project, ValidationResult result)
    {
        if (project.TargetMachine == null) return;

        var machine = project.TargetMachine;

        // Verificar firmware version
        if (!string.IsNullOrEmpty(machine.ControllerFirmware))
        {
            // TODO: Comparar con versiones mínimas conocidas
        }

        // Total stitches vs límite máquina
        // (Se validará en StitchPlan)

        // Hoop seleccionado compatible con máquina
        if (project.SelectedHoop != null)
        {
            if (!string.IsNullOrEmpty(project.SelectedHoop.MachineBrand) &&
                !project.SelectedHoop.MachineBrand.Equals(machine.Brand, StringComparison.OrdinalIgnoreCase))
            {
                result.AddIssue(ValidationSeverity.Warning, "MACH_HOOP_BRAND_MISMATCH",
                    $"Hoop {project.SelectedHoop.Name} is for {project.SelectedHoop.MachineBrand}, machine is {machine.Brand}",
                    data: new() { { "hoop_brand", project.SelectedHoop.MachineBrand }, { "machine_brand", machine.Brand } });
            }
        }

        // Formato nativo
        if (project.SourceFilePath != null)
        {
            var ext = Path.GetExtension(project.SourceFilePath).ToUpperInvariant();
            if (!machine.NativeFormats.Any(f => f.Equals(ext.TrimStart('.'), StringComparison.OrdinalIgnoreCase)))
            {
                result.AddIssue(ValidationSeverity.Info, "MACH_FORMAT_CONVERSION",
                    $"Source format {ext} not native to {machine.Name}, conversion required",
                    data: new() { { "source_format", ext }, { "native_formats", string.Join(", ", machine.NativeFormats) } });
            }
        }
    }

    /// <summary>
    /// Valida producción: tiempo, cambios, bobbin planning, consumibles, first article, checkpoints
    /// </summary>
    private void ValidateProduction(AtlasProject project, ValidationResult result)
    {
        // Solo se puede validar completamente con StitchPlan
        // Aquí validaciones básicas de configuración

        if (project.WorkProfile != null)
        {
            // Verificar planning de bobbin
            if (project.WorkProfile.Machine != null)
            {
                // Estimación burda: 1 bobbin cada ~50k puntadas
                // Se validará mejor en simulación
            }
        }

        // First article reminder
        if (project.ValidationResult?.OverallStatus == ValidationStatus.Pass)
        {
            result.AddIssue(ValidationSeverity.Info, "PROD_FIRST_ARTICLE",
                "Remember to run first article stitch-out before production batch",
                data: new() { { "reminder", true } });
        }
    }

    private ValidationStatus DetermineOverallStatus(ValidationResult result)
    {
        if (result.ErrorCount > 0) return ValidationStatus.Fail;
        if (result.WarningCount > 0) return ValidationStatus.Warning;
        // If no issues at all, it's a pass
        if (result.InfoCount == 0) return ValidationStatus.Pass;
        return ValidationStatus.Pass;
    }
}

/// <summary>
/// Opciones configurables del Validator
/// </summary>
public sealed class ValidatorOptions
{
    // Límites geométricos
    public int MaxDesignWidth { get; set; } = 1000000;      // 1000mm
    public int MaxDesignHeight { get; set; } = 1000000;     // 1000mm
    public double MaxDesignAreaCm2 { get; set; } = 5000;    // 5000 cm²
    public double CollisionOverlapThresholdPercent { get; set; } = 20;

    // Límites puntada
    public int AbsoluteMinStitchLength { get; set; } = 50;     // 0.05mm
    public int AbsoluteMaxStitchLength { get; set; } = 12700;  // 12.7mm (DST limit)
    public int MinDensity { get; set; } = 100;                // 0.1mm
    public int MaxDensity { get; set; } = 1000;               // 1mm
    public int MinSatinWidth { get; set; } = 1000;            // 1mm
    public int MaxSatinWidth { get; set; } = 12000;           // 12mm
    public int MaxJumpDistance { get; set; } = 12700;         // 12.7mm
    public int MaxJumpNoTrim { get; set; } = 3000;            // 3mm sin trim

    // Pull compensation
    public int MinPullCompensation { get; set; } = 100;       // 0.1mm
    public int MaxPullCompensation { get; set; } = 500;       // 0.5mm
    public int MinOverlap { get; set; } = 100;                // 0.1mm

    // Tatami grande
    public double LargeTatamiAreaCm2 { get; set; } = 100;     // 100 cm²

    // Configuración
    public bool StrictMode { get; set; } = false;             // Convert warnings to errors
}