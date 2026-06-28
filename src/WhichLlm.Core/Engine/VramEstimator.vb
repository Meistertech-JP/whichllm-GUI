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
            decision.Notes.AddRange(CompatibilityWarnings(hardware))
            Dim ramBudget = If(hardware.RamBudgetBytes, Math.Max(0, hardware.AvailableRamBytes))

            If options.CpuOnly OrElse hardware.Gpus.Count = 0 Then
                decision.VramAvailableBytes = 0
                decision.FitType = "cpu_only"
                decision.IsRunnable = requiredBytes <= ramBudget AndAlso requiredBytes <= hardware.FreeDiskBytes
                If Not decision.IsRunnable Then decision.Notes.Add("Required memory exceeds RAM or disk budget.")
                Return decision
            End If

            Dim usableDedicated = hardware.Gpus.
                Where(Function(g) Not g.IsSharedMemory OrElse hardware.Gpus.Count = 1).
                Where(Function(g) Math.Max(0, g.EffectiveVramBytes) > 0).
                ToList()
            Dim effectiveGpuBytes As Long = 0
            If usableDedicated.Count = 1 Then
                effectiveGpuBytes = usableDedicated(0).EffectiveVramBytes
            ElseIf usableDedicated.Count > 1 Then
                Dim compatibleGroup = SelectCompatibleMultiGpuGroup(usableDedicated, options.GpuGroupKey, decision.Notes)
                Dim rawTotal = compatibleGroup.Sum(Function(g) Math.Max(0, g.EffectiveVramBytes))
                Dim homogeneous = compatibleGroup.Select(Function(g) MultiGpuModelKey(g)).Distinct(StringComparer.OrdinalIgnoreCase).Count() = 1 AndAlso compatibleGroup.Select(Function(g) Math.Max(0, g.EffectiveVramBytes)).Distinct().Count() = 1
                Dim factor = If(homogeneous, 0.88R, 0.75R)
                If compatibleGroup.Count > 1 Then
                    effectiveGpuBytes = CLng(rawTotal * factor)
                    decision.UsesMultiGpu = True
                    decision.MultiGpuEffectiveVramBytes = effectiveGpuBytes
                    decision.Notes.Add("Multi-GPU fit uses conservative effective VRAM.")
                    If compatibleGroup.Count < usableDedicated.Count Then
                        decision.Notes.Add("Mixed GPU generations detected; only the largest compatible GPU group is counted for a single-model split.")
                    End If
                Else
                    Dim bestGpu = compatibleGroup.First()
                    effectiveGpuBytes = Math.Max(0, bestGpu.EffectiveVramBytes)
                    decision.Notes.Add("Mixed GPU generations detected; GPU memory is not combined for a single-model split.")
                End If
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

        Private Shared Function SelectCompatibleMultiGpuGroup(gpus As IEnumerable(Of GpuInfo), selectedGroupKey As String, notes As List(Of String)) As List(Of GpuInfo)
            Dim gpuList = gpus.Where(Function(g) g IsNot Nothing).ToList()
            If gpuList.Count <= 1 Then Return gpuList

            Dim groups = gpuList.
                GroupBy(Function(g) MultiGpuCompatibilityKey(g), StringComparer.OrdinalIgnoreCase).
                Select(Function(group) group.ToList()).
                OrderByDescending(Function(group) group.Sum(Function(g) Math.Max(0, g.EffectiveVramBytes))).
                ThenByDescending(Function(group) group.Count).
                ThenByDescending(Function(group) group.Sum(Function(g) If(g.MemoryBandwidthGbps, 0))).
                ToList()

            If Not String.IsNullOrWhiteSpace(selectedGroupKey) AndAlso
                Not selectedGroupKey.Equals("auto", StringComparison.OrdinalIgnoreCase) Then
                Dim selected = groups.FirstOrDefault(Function(group) MultiGpuCompatibilityKey(group(0)).Equals(selectedGroupKey, StringComparison.OrdinalIgnoreCase))
                If selected IsNot Nothing Then
                    notes.Add("Selected GPU group: " & selectedGroupKey)
                    Return selected
                End If
                notes.Add("Selected GPU group was not found; using the largest compatible group.")
            End If

            Return groups.First()
        End Function

        Public Shared Function MultiGpuCompatibilityKey(gpu As GpuInfo) As String
            Dim vendor = If(gpu?.Vendor, "").Trim().ToLowerInvariant()
            If vendor = "nvidia" Then Return "nvidia:cuda"
            Dim architecture = NormalizeGpuArchitecture(gpu)
            If architecture.Length > 0 Then Return vendor & ":" & architecture
            Return vendor & ":" & MultiGpuModelKey(gpu)
        End Function

        Private Shared Function MultiGpuModelKey(gpu As GpuInfo) As String
            Return System.Text.RegularExpressions.Regex.Replace(If(gpu?.Name, "").ToLowerInvariant(), "[^a-z0-9]+", "")
        End Function

        Private Shared Function NormalizeGpuArchitecture(gpu As GpuInfo) As String
            Dim rawArch = If(gpu?.ComputeCapability, "").Trim().ToLowerInvariant()
            If rawArch.Length > 0 Then Return rawArch

            Dim name = If(gpu?.Name, "").ToLowerInvariant()
            Dim vendor = If(gpu?.Vendor, "").Trim().ToLowerInvariant()
            If vendor = "amd" Then
                If name.Contains("radeon vii", StringComparison.Ordinal) OrElse name.Contains("vega 20", StringComparison.Ordinal) OrElse
                    name.Contains("mi50", StringComparison.Ordinal) OrElse name.Contains("mi60", StringComparison.Ordinal) Then Return "gfx906"
                If name.Contains("rx 69", StringComparison.Ordinal) OrElse name.Contains("rx 68", StringComparison.Ordinal) OrElse
                    name.Contains("navi 21", StringComparison.Ordinal) Then Return "gfx1030"
                If name.Contains("rx 67", StringComparison.Ordinal) OrElse name.Contains("navi 22", StringComparison.Ordinal) Then Return "gfx1031"
                If name.Contains("rx 66", StringComparison.Ordinal) OrElse name.Contains("rx 6500", StringComparison.Ordinal) OrElse
                    name.Contains("rx 6400", StringComparison.Ordinal) OrElse name.Contains("navi 23", StringComparison.Ordinal) OrElse
                    name.Contains("navi 24", StringComparison.Ordinal) Then Return "gfx1032"
                If name.Contains("rx 79", StringComparison.Ordinal) OrElse name.Contains("navi 31", StringComparison.Ordinal) Then Return "gfx1100"
                If name.Contains("rx 78", StringComparison.Ordinal) OrElse name.Contains("rx 77", StringComparison.Ordinal) OrElse
                    name.Contains("navi 32", StringComparison.Ordinal) Then Return "gfx1101"
                If name.Contains("rx 76", StringComparison.Ordinal) OrElse name.Contains("navi 33", StringComparison.Ordinal) Then Return "gfx1102"
            End If

            Return ""
        End Function

        Private Shared Function CompatibilityWarnings(hardware As HardwareInfo) As IEnumerable(Of String)
            Dim warnings As New List(Of String)
            If hardware Is Nothing OrElse hardware.Gpus.Count = 0 Then Return warnings

            Dim osKind = NormalizeOs(hardware.OsName)
            Dim bestGpu = hardware.Gpus.
                OrderByDescending(Function(g) Math.Max(0, g.EffectiveVramBytes)).
                FirstOrDefault()
            If bestGpu Is Nothing Then Return warnings

            Dim vendor = If(bestGpu.Vendor, "").Trim().ToLowerInvariant()
            If vendor = "nvidia" Then
                Dim cc = ParseNvidiaComputeCapability(bestGpu.ComputeCapability)
                If cc.HasValue AndAlso cc.Value < 5.0R Then
                    warnings.Add($"Compute capability {bestGpu.ComputeCapability} is below minimum 5.0 for current Ollama/llama.cpp CUDA builds.")
                End If
                If IsVulkanOnlyGpu(bestGpu.Name) Then
                    warnings.Add("Legacy Kepler GPU: modern CUDA builds no longer support this card; use a Vulkan backend where available.")
                End If
            End If

            If vendor = "amd" AndAlso osKind <> "windows" AndAlso osKind <> "linux" Then
                warnings.Add("ROCm GPU inference on AMD requires Linux; Windows users usually need Vulkan or DirectML backends.")
            End If

            If vendor = "apple" AndAlso osKind <> "darwin" Then
                warnings.Add("Metal requires macOS for Apple Silicon inference.")
            End If

            If vendor = "intel" AndAlso osKind <> "windows" AndAlso osKind <> "linux" Then
                warnings.Add("Intel GPU inference usually requires Windows or Linux Level Zero/oneAPI support.")
            End If

            Return warnings
        End Function

        Private Shared Function ParseNvidiaComputeCapability(value As String) As Double?
            If String.IsNullOrWhiteSpace(value) Then Return Nothing
            Dim match = System.Text.RegularExpressions.Regex.Match(value, "(\d+)(?:\.(\d+))?")
            If Not match.Success Then Return Nothing
            Dim major As Double
            If Not Double.TryParse(match.Groups(1).Value, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, major) Then Return Nothing
            Dim minor = 0.0R
            If match.Groups(2).Success Then
                Double.TryParse(match.Groups(2).Value, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, minor)
            End If
            Return major + minor / 10.0R
        End Function

        Private Shared Function NormalizeOs(osName As String) As String
            Dim text = If(osName, "").ToLowerInvariant()
            If text.Contains("win", StringComparison.Ordinal) Then Return "windows"
            If text.Contains("darwin", StringComparison.Ordinal) OrElse text.Contains("mac", StringComparison.Ordinal) Then Return "darwin"
            If text.Contains("linux", StringComparison.Ordinal) Then Return "linux"
            If Environment.OSVersion.Platform = PlatformID.Win32NT Then Return "windows"
            Return text
        End Function

        Private Shared Function IsVulkanOnlyGpu(name As String) As Boolean
            Dim text = If(name, "").ToUpperInvariant()
            Dim markers = New String() {"QUADRO K6000", "QUADRO K5200", "QUADRO K4200", "QUADRO K2200", "QUADRO K620", "QUADRO K420", "GTX 780", "GTX 770", "GTX 760"}
            Return markers.Any(Function(marker) text.Contains(marker, StringComparison.Ordinal))
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
