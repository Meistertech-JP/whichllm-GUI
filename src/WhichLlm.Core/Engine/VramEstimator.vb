Option Strict On
Option Explicit On

Imports WhichLlm.Core.Dto
Imports WhichLlm.Core.Services

Namespace Engine
    Public Class VramEstimator
        Implements IVramEstimator

        Public Function EstimateRequiredBytes(model As ModelInfo, modelVariant As ModelVariant, contextLength As Integer) As Long Implements IVramEstimator.EstimateRequiredBytes
            Dim paramsB = Math.Max(0.1R, model.ParameterCountB)
            Dim activeParamsB = Math.Max(0.1R, If(model.ActiveParameterCountB, paramsB))
            Dim weightBytes = paramsB * 1_000_000_000.0R * QuantizationRules.BytesPerParam(modelVariant.Quantization)
            Dim kvBytes = activeParamsB * 1_000_000_000.0R * Math.Max(512, contextLength) / 4096.0R * 0.015R
            Dim runtimeOverhead = Math.Max(350_000_000.0R, weightBytes * 0.05R)
            Dim total = weightBytes + kvBytes + runtimeOverhead
            Return CLng(Math.Ceiling(total))
        End Function

        Public Function ClassifyFit(requiredBytes As Long, hardware As HardwareInfo, options As RankingOptions) As FitDecision Implements IVramEstimator.ClassifyFit
            Dim decision As New FitDecision With {.VramRequiredBytes = requiredBytes}
            Dim ramBudget = If(hardware.RamBudgetBytes, Math.Max(0, hardware.AvailableRamBytes))

            If options.CpuOnly OrElse hardware.Gpus.Count = 0 Then
                decision.VramAvailableBytes = 0
                decision.FitType = "cpu_only"
                decision.IsRunnable = requiredBytes <= ramBudget AndAlso requiredBytes <= hardware.FreeDiskBytes
                If Not decision.IsRunnable Then decision.Notes.Add("Required memory exceeds RAM or disk budget.")
                Return decision
            End If

            Dim usableDedicated = hardware.Gpus.Where(Function(g) Not g.IsSharedMemory OrElse hardware.Gpus.Count = 1).Select(Function(g) Math.Max(0, g.EffectiveVramBytes)).ToList()
            Dim effectiveGpuBytes As Long = 0
            If usableDedicated.Count = 1 Then
                effectiveGpuBytes = usableDedicated(0)
            ElseIf usableDedicated.Count > 1 Then
                Dim rawTotal = usableDedicated.Sum()
                Dim homogeneous = usableDedicated.Distinct().Count() = 1
                Dim factor = If(homogeneous, 0.88R, 0.75R)
                effectiveGpuBytes = CLng(rawTotal * factor)
                decision.UsesMultiGpu = True
                decision.MultiGpuEffectiveVramBytes = effectiveGpuBytes
                decision.Notes.Add("Multi-GPU fit uses conservative effective VRAM.")
            End If
            decision.VramAvailableBytes = effectiveGpuBytes

            If requiredBytes <= effectiveGpuBytes AndAlso requiredBytes <= hardware.FreeDiskBytes Then
                decision.FitType = "full_gpu"
                decision.IsRunnable = True
                Return decision
            End If

            If requiredBytes <= effectiveGpuBytes + ramBudget AndAlso requiredBytes <= hardware.FreeDiskBytes Then
                decision.FitType = "partial_offload"
                decision.IsRunnable = True
                decision.Notes.Add("Requires partial CPU/RAM offload.")
                Return decision
            End If

            If requiredBytes <= ramBudget AndAlso requiredBytes <= hardware.FreeDiskBytes Then
                decision.FitType = "cpu_only"
                decision.IsRunnable = True
                decision.Notes.Add("Runs from system RAM without GPU fit.")
                Return decision
            End If

            decision.FitType = "unfit"
            decision.IsRunnable = False
            decision.Notes.Add("Required memory exceeds available GPU/RAM or disk budget.")
            Return decision
        End Function
    End Class
End Namespace
