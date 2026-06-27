Option Strict On
Option Explicit On

Imports WhichLlm.Core.Dto
Imports WhichLlm.Core.Services

Namespace Engine
    Public Class PerformanceEstimator
        Implements IPerformanceEstimator

        Public Function Estimate(model As ModelInfo, modelVariant As ModelVariant, fit As FitDecision, hardware As HardwareInfo) As SpeedEstimate Implements IPerformanceEstimator.Estimate
            Dim activeParamsB = Math.Max(0.25R, If(model.ActiveParameterCountB, model.ParameterCountB))
            Dim bytesPerParam = QuantizationRules.BytesPerParam(modelVariant.Quantization)
            Dim speedEstimate As New SpeedEstimate()

            Select Case fit.FitType
                Case "full_gpu"
                    Dim gpu = hardware.Gpus.OrderByDescending(Function(g) If(g.MemoryBandwidthGbps, 0)).FirstOrDefault()
                    Dim bandwidth = If(gpu?.MemoryBandwidthGbps, 220.0R)
                    Dim factor = BackendFactor(If(gpu?.Vendor, "Unknown"))
                    speedEstimate.TokPerSec = Math.Max(0.2R, bandwidth * factor / Math.Max(1.0R, activeParamsB * bytesPerParam) * 0.85R)
                    speedEstimate.Confidence = If(fit.UsesMultiGpu, "low", "medium")
                    If fit.UsesMultiGpu Then speedEstimate.Notes.Add("Multi-GPU speed depends on backend split mode.")
                Case "partial_offload"
                    Dim gpu = hardware.Gpus.OrderByDescending(Function(g) If(g.MemoryBandwidthGbps, 0)).FirstOrDefault()
                    Dim bandwidth = Math.Min(If(gpu?.MemoryBandwidthGbps, 160.0R), 180.0R)
                    Dim spillRatio = Math.Clamp((fit.VramRequiredBytes - fit.VramAvailableBytes) / Math.Max(1.0R, CDbl(fit.VramRequiredBytes)), 0.05R, 0.95R)
                    speedEstimate.TokPerSec = Math.Max(0.15R, bandwidth / Math.Max(1.0R, activeParamsB * bytesPerParam) * (1.0R - spillRatio) * 0.45R)
                    speedEstimate.Confidence = "low"
                    speedEstimate.Notes.Add("Partial offload estimates vary strongly by runtime and PCIe bandwidth.")
                Case Else
                    Dim cores = Math.Max(1, hardware.PhysicalCores)
                    Dim avxFactor = If(hardware.SupportsAvx512, 1.45R, If(hardware.SupportsAvx2, 1.15R, 0.8R))
                    speedEstimate.TokPerSec = Math.Max(0.05R, cores * avxFactor / Math.Max(1.0R, activeParamsB * bytesPerParam) * 1.4R)
                    speedEstimate.Confidence = "low"
                    speedEstimate.Notes.Add("CPU-only speed is a planning estimate.")
            End Select

            Dim rangeFactorLow = If(speedEstimate.Confidence = "medium", 0.6R, 0.35R)
            Dim rangeFactorHigh = If(speedEstimate.Confidence = "medium", 1.6R, 2.0R)
            speedEstimate.RangeLow = speedEstimate.TokPerSec * rangeFactorLow
            speedEstimate.RangeHigh = speedEstimate.TokPerSec * rangeFactorHigh
            Return speedEstimate
        End Function

        Private Shared Function BackendFactor(vendor As String) As Double
            Select Case vendor.ToLowerInvariant()
                Case "nvidia"
                    Return 1.0R
                Case "amd"
                    Return 0.75R
                Case "apple"
                    Return 0.82R
                Case "intel"
                    Return 0.55R
                Case Else
                    Return 0.65R
            End Select
        End Function
    End Class
End Namespace
