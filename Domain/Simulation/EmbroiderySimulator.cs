namespace AtlasEmbroidery.Domain.Simulation;

using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Stitching;
using AtlasEmbroidery.Domain.Geometry;

/// <summary>
/// Simulador de bordado - Playback frame a frame, análisis de densidad, capas, tiempo/hilo, límites de máquina
/// </summary>
public sealed class EmbroiderySimulator
{
    private readonly EmbroiderySimulatorOptions _options;

    public EmbroiderySimulator(EmbroiderySimulatorOptions? options = null)
    {
        _options = options ?? new EmbroiderySimulatorOptions();
    }

    /// <summary>
    /// Ejecuta simulación completa del proyecto
    /// </summary>
    public SimulationResult Simulate(AtlasProject project, CancellationToken ct = default)
    {
        var result = new SimulationResult
        {
            SimulatedAt = DateTime.UtcNow,
            Layers = new List<SimulationLayer>(),
            Risks = new List<SimulationRisk>(),
            TotalStitches = 0,
            EstimatedTimeSeconds = 0,
            EstimatedThreadMeters = 0
        };

        // Compilar plan de puntadas si no existe
        var engine = new StitchEngine();
        var stitchPlan = engine.Compile(project);

        // Agrupar por color/aguja (cada cambio de color = capa)
        var allStitches = stitchPlan.GetAllStitches();
        var colorGroups = allStitches
            .GroupBy(s => s.ColorIndex)
            .OrderBy(g => g.Key)
            .ToList();

        double currentTime = 0;
        double currentThread = 0;
        var machine = project.TargetMachine ?? MachineProfile.Default();
        var hoop = project.SelectedHoop ?? HoopProfile.Default();

        foreach (var colorGroup in colorGroups)
        {
            ct.ThrowIfCancellationRequested();

            var layer = SimulateLayer(colorGroup, currentTime, machine, hoop, project.WorkProfile);
            result.Layers.Add(layer);

            currentTime = layer.EndTimeSeconds;
            currentThread += layer.ThreadUsedMeters;
            result.TotalStitches += layer.StitchCount;

            // Verificar límites de máquina por capa
            CheckMachineLimits(layer, machine, result.Risks);
        }

        result.EstimatedTimeSeconds = currentTime;
        result.EstimatedThreadMeters = currentThread;

        // Análisis global de densidad
        AnalyzeGlobalDensity(stitchPlan, project, result);

        // Análisis de overlaps entre capas
        AnalyzeLayerOverlaps(result.Layers, result.Risks);

        // Verificar que el diseño cabe en el bastidor
        CheckHoopFit(stitchPlan, hoop, result.Risks);

        result.FitsInHoop = !result.Risks.Any(r => r.Code == "SIM_HOOP_FIT" && r.Severity == ValidationSeverity.Critical);
        result.ExceedsMachineLimits = result.Risks.Any(r => r.Severity == ValidationSeverity.Critical);

        return result;
    }

    private SimulationLayer SimulateLayer(
            IGrouping<byte, StitchPoint> colorGroup,
            double startTime,
            MachineProfile machine,
            HoopProfile hoop,
            WorkProfile? workProfile)
    {
        var stitches = colorGroup.ToList();
        var layer = new SimulationLayer
                {
                    ColorIndex = colorGroup.Key,
                    Color = stitches.FirstOrDefault().ColorIndex >= 0 && stitches.FirstOrDefault().ColorIndex < 255
                        ? null // Will be populated from project palette
                        : null,
                    StitchCount = stitches.Count,
                    ThreadMeters = 0,
                    Bounds = GeometryUtils.ComputeBounds(stitches.Select(s => s.Position))
                };

                if (layer.Bounds.IsEmpty)
                {
                    layer.Bounds = Rectangle.Empty;
                }

        double currentTime = startTime;
        double threadUsed = 0;
        Point? lastPosition = null;

        foreach (var stitch in stitches)
        {
            // Calcular tiempo y hilo para esta puntada
            if (lastPosition.HasValue)
            {
                double distance = lastPosition.Value.DistanceTo(stitch.Position) / 1000.0; // mm
                
                if (stitch.IsJump)
                {
                    // Jump: velocidad rápida, sin hilo
                    double jumpSpeed = machine.MaxJumpSpeedMmMin / 60.0; // mm/s
                    currentTime += distance / jumpSpeed;
                }
                else
                {
                    // Puntada normal: velocidad de costura
                    double stitchSpeed = Math.Min(machine.MaxStitchSpeedRpm / 60.0 * _options.StitchLengthMm, machine.MaxStitchSpeedMmMin / 60.0);
                    currentTime += distance / stitchSpeed;
                    
                    // Longitud de hilo = distancia + overhead
                    threadUsed += distance * 1.1; // 10% overhead
                }

                // Tiempo extra para trim
                if (stitch.IsTrim)
                {
                    currentTime += _options.TrimTimeSeconds;
                    threadUsed += _options.TrimThreadMm;
                }
            }

            lastPosition = stitch.Position;
        }

        layer.EndTimeSeconds = currentTime;
        layer.ThreadUsedMeters = threadUsed / 1000.0;

        return layer;
    }

    private void CheckMachineLimits(SimulationLayer layer, MachineProfile machine, List<SimulationRisk> risks)
    {
        // Velocidad máxima
        // Simplified check - actual speed calculation would need per-stitch timing
        if (layer.ThreadUsedMeters > 0)
        {
            double avgSpeedMmMin = (layer.ThreadUsedMeters * 1000) / Math.Max(0.001, (layer.EndTimeSeconds - layer.StartTimeSeconds)) * 60;
            if (avgSpeedMmMin > machine.MaxStitchSpeedMmMin)
            {
                risks.Add(new SimulationRisk
                {
                    Code = "SIM_SPEED_EXCEED",
                    Severity = ValidationSeverity.Warning,
                    Message = $"Velocidad promedio excede límite de máquina: {avgSpeedMmMin:F0} mm/min > {machine.MaxStitchSpeedMmMin} mm/min",
                    Area = layer.Bounds,
                    Probability = 0.7
                });
            }
        }

        // Campo de bordado (bounds vs hoop)
        if (!layer.Bounds.IsEmpty)
        {
            double widthMm = layer.Bounds.Width / 1000.0;
            double heightMm = layer.Bounds.Height / 1000.0;
            if (widthMm > machine.MaxFieldWidthMm || heightMm > machine.MaxFieldHeightMm)
            {
                risks.Add(new SimulationRisk
                {
                    Code = "SIM_FIELD_EXCEED",
                    Severity = ValidationSeverity.Critical,
                    Message = $"Capa excede campo de bordado de la máquina: {widthMm:F1}x{heightMm:F1}mm > {machine.MaxFieldWidthMm}x{machine.MaxFieldHeightMm}mm",
                    Area = layer.Bounds,
                    Probability = 1.0
                });
            }
        }

        // Número de trims excesivo - not directly available in SimulationLayer
        // Would need to count trims from stitches
    }

    private void AnalyzeGlobalDensity(StitchPlan stitchPlan, AtlasProject project, SimulationResult result)
        {
            var allStitches = stitchPlan.GetAllStitches();
            if (allStitches.Count == 0) return;

            var bounds = GeometryUtils.ComputeBounds(allStitches.Select(s => s.Position));
            if (bounds.IsEmpty) return;

            double areaMm2 = (bounds.Width / 1000.0) * (bounds.Height / 1000.0);
            double density = allStitches.Count / Math.Max(0.01, areaMm2);

            result.GlobalDensityStitchesPerMm2 = density;

            // Umbrales según material
            double maxDensity = project.WorkProfile?.Fabric?.MaxDensityStitchesPerMm2 ?? 10.0;

            if (density > maxDensity)
            {
                result.Risks.Add(new SimulationRisk
                {
                    Code = "SIM_GLOBAL_DENSITY_HIGH",
                    Severity = ValidationSeverity.Warning,
                    Message = $"Densidad global alta: {density:F1} pts/mm² > {maxDensity:F1} pts/mm² (material)",
                    Area = bounds,
                    Probability = 0.8
                });
            }
        }

    private void AnalyzeLayerOverlaps(List<SimulationLayer> layers, List<SimulationRisk> risks)
    {
        for (int i = 0; i < layers.Count; i++)
        {
            for (int j = i + 1; j < layers.Count; j++)
            {
                var a = layers[i];
                var b = layers[j];

                if (!a.Bounds.IsEmpty && !b.Bounds.IsEmpty)
                {
                    var intersection = a.Bounds.Intersect(b.Bounds);
                    if (!intersection.IsEmpty)
                    {
                        double overlapArea = intersection.Width * intersection.Height;
                        double aArea = (double)a.Bounds.Width * a.Bounds.Height;
                        double overlapPercent = overlapArea / Math.Max(1, aArea) * 100;

                        if (overlapPercent > 30) // Más del 30% overlap
                        {
                            risks.Add(new SimulationRisk
                            {
                                Code = "SIM_LAYER_OVERLAP",
                                Severity = ValidationSeverity.Info,
                                Message = $"Overlap significativo entre capas {a.ColorIndex} y {b.ColorIndex}: {overlapPercent:F0}%",
                                Area = intersection,
                                Probability = 0.5
                            });
                        }
                    }
                }
            }
        }
    }

    private void CheckHoopFit(StitchPlan stitchPlan, HoopProfile hoop, List<SimulationRisk> risks)
    {
        var allStitches = stitchPlan.GetAllStitches();
        if (allStitches.Count == 0) return;

        var bounds = GeometryUtils.ComputeBounds(allStitches.Select(s => s.Position));
        if (bounds.IsEmpty) return;

        double designWidthMm = bounds.Width / 1000.0;
        double designHeightMm = bounds.Height / 1000.0;

        if (designWidthMm > hoop.WidthMm || designHeightMm > hoop.HeightMm)
        {
            risks.Add(new SimulationRisk
            {
                Code = "SIM_HOOP_FIT",
                Severity = ValidationSeverity.Critical,
                Message = $"Diseño ({designWidthMm:F1}x{designHeightMm:F1}mm) no cabe en bastidor ({hoop.WidthMm}x{hoop.HeightMm}mm)",
                Area = bounds,
                Probability = 1.0
            });
        }
    }
}

/// <summary>
/// Opciones del simulador
/// </summary>
public sealed class EmbroiderySimulatorOptions
{
    public double StitchLengthMm { get; set; } = 4.0; // Longitud promedio de puntada en mm
    public double TrimTimeSeconds { get; set; } = 1.5; // Tiempo por trim
    public double TrimThreadMm { get; set; } = 5.0; // Hilo consumido por trim (mm)
}