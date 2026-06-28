Option Strict On
Option Explicit On

Imports WhichLlm.Core.Dto
Imports WhichLlm.Core.Services

Namespace Engine
    Public Class VramEstimator
        Implements IVramEstimator

        Private Const MinimumContextLength As Integer = 512
        Private Const BytesPerKvElement As Double = 2.0R

        Public Function EstimateRequiredBytes(model As ModelInfo, modelVariant As ModelVariant, contextLength As Integer) As Long Implements IVramEstimator.EstimateRequiredBytes
            Dim paramsB = Math.Max(0.1R, model.ParameterCountB)
            Dim weightBytes = EstimateWeightBytes(paramsB, modelVariant)
            Dim kvBytes = EstimateKvCacheBytes(model, contextLength)
            Dim runtimeOverhead = Math.Max(512.0R * 1024.0R * 1024.0R, weightBytes * 0.06R)
            Dim total = weightBytes + kvBytes + runtimeOverhead
            Return CLng(Math.Ceiling(total))
        End Function

        Friend Shared Function EstimateKvCacheBytes(model As ModelInfo, contextLength As Integer) As Long
            Dim shape = InferKvShape(model)
            Dim context = Math.Max(MinimumContextLength, contextLength)
            Dim bytes = 2.0R * shape.LayerCount * CDbl(context) * shape.KvWidth * BytesPerKvElement
            Return CLng(Math.Ceiling(bytes))
        End Function

        Private Shared Function EstimateWeightBytes(paramsB As Double, modelVariant As ModelVariant) As Double
            If modelVariant IsNot Nothing AndAlso modelVariant.FileSizeBytes.HasValue AndAlso modelVariant.FileSizeBytes.Value > 0 Then
                Return modelVariant.FileSizeBytes.Value
            End If
            Dim quant = If(modelVariant?.Quantization, "")
            Return paramsB * 1_000_000_000.0R * QuantizationRules.BytesPerParam(quant)
        End Function

        Private Shared Function InferKvShape(model As ModelInfo) As KvShape
            Dim paramsB = Math.Max(0.1R, model.ParameterCountB)
            Dim text = NormalizeModelText(model)

            If text.Contains("llama", StringComparison.Ordinal) Then
                Return LlamaShape(paramsB)
            End If
            If text.Contains("qwen", StringComparison.Ordinal) Then
                Return QwenShape(paramsB)
            End If
            If text.Contains("gemma", StringComparison.Ordinal) Then
                Return GemmaShape(paramsB)
            End If
            If text.Contains("mistral", StringComparison.Ordinal) OrElse text.Contains("mixtral", StringComparison.Ordinal) Then
                Return MistralShape(paramsB)
            End If
            If text.Contains("phi", StringComparison.Ordinal) Then
                Return PhiShape(paramsB)
            End If
            If text.Contains("deepseek", StringComparison.Ordinal) Then
                Return DeepSeekShape(paramsB)
            End If

            Return GenericShape(paramsB)
        End Function

        Private Shared Function NormalizeModelText(model As ModelInfo) As String
            If model Is Nothing Then Return ""
            Return String.Join(" ", New String() {
                If(model.RepoId, ""),
                If(model.DisplayName, ""),
                If(model.BaseModel, ""),
                If(model.Architecture, ""),
                If(model.PipelineTag, ""),
                String.Join(" ", model.Tags)
            }).ToLowerInvariant()
        End Function

        Private Shared Function LlamaShape(paramsB As Double) As KvShape
            If paramsB >= 300 Then Return New KvShape(126, 2048)
            If paramsB >= 60 Then Return New KvShape(80, 1024)
            If paramsB >= 25 Then Return New KvShape(60, 1024)
            Return New KvShape(32, 1024)
        End Function

        Private Shared Function QwenShape(paramsB As Double) As KvShape
            If paramsB >= 60 Then Return New KvShape(80, 1024)
            If paramsB >= 25 Then Return New KvShape(64, 1024)
            If paramsB >= 12 Then Return New KvShape(48, 1024)
            If paramsB >= 3 Then Return New KvShape(36, 512)
            Return New KvShape(24, 512)
        End Function

        Private Shared Function GemmaShape(paramsB As Double) As KvShape
            If paramsB >= 20 Then Return New KvShape(46, 1024)
            If paramsB >= 8 Then Return New KvShape(42, 1024)
            If paramsB >= 3 Then Return New KvShape(34, 1024)
            Return New KvShape(26, 512)
        End Function

        Private Shared Function MistralShape(paramsB As Double) As KvShape
            If paramsB >= 100 Then Return New KvShape(88, 2048)
            If paramsB >= 20 Then Return New KvShape(56, 1024)
            Return New KvShape(32, 1024)
        End Function

        Private Shared Function PhiShape(paramsB As Double) As KvShape
            If paramsB >= 10 Then Return New KvShape(40, 2048)
            Return New KvShape(32, 3072)
        End Function

        Private Shared Function DeepSeekShape(paramsB As Double) As KvShape
            If paramsB >= 100 Then Return New KvShape(61, 4096)
            If paramsB >= 20 Then Return New KvShape(48, 1536)
            Return QwenShape(paramsB)
        End Function

        Private Shared Function GenericShape(paramsB As Double) As KvShape
            Dim layerCount As Integer
            If paramsB >= 60 Then
                layerCount = 80
            ElseIf paramsB >= 25 Then
                layerCount = 60
            ElseIf paramsB >= 12 Then
                layerCount = 48
            ElseIf paramsB >= 3 Then
                layerCount = 32
            Else
                layerCount = 24
            End If

            Dim hidden = Math.Sqrt(paramsB * 1_000_000_000.0R / Math.Max(1.0R, 12.0R * layerCount))
            Dim kvWidth = CInt(Math.Ceiling(Math.Max(512.0R, hidden / 2.0R) / 128.0R) * 128)
            Return New KvShape(layerCount, kvWidth)
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

        Private Structure KvShape
            Public ReadOnly Property LayerCount As Integer
            Public ReadOnly Property KvWidth As Integer

            Public Sub New(layerCount As Integer, kvWidth As Integer)
                Me.LayerCount = Math.Max(1, layerCount)
                Me.KvWidth = Math.Max(1, kvWidth)
            End Sub
        End Structure
    End Class
End Namespace
