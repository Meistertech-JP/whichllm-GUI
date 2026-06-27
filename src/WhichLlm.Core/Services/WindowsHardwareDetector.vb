Option Strict On
Option Explicit On

Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Runtime.Intrinsics.X86
Imports System.Text.Json
Imports WhichLlm.Core.Dto
Imports WhichLlm.Core.Utilities

Namespace Services
    Public Class WindowsHardwareDetector
        Implements IHardwareDetector

        Private ReadOnly _gpuCatalog As IGpuCatalog

        Public Sub New(gpuCatalog As IGpuCatalog)
            _gpuCatalog = gpuCatalog
        End Sub

        Public Async Function DetectAsync(options As RankingOptions, Optional cancellationToken As CancellationToken = Nothing) As Task(Of HardwareInfo) Implements IHardwareDetector.DetectAsync
            Dim hardware As New HardwareInfo With {
                .OsName = $"{Environment.OSVersion.Platform} {Environment.OSVersion.Version}",
                .SupportsAvx2 = Avx2.IsSupported,
                .SupportsAvx512 = Avx512F.IsSupported
            }

            Await FillCpuAsync(hardware, cancellationToken)
            Await FillMemoryAsync(hardware, cancellationToken)
            FillDisk(hardware)

            If options.CpuOnly Then
                hardware.DetectionNotes.Add("CPU-only mode is enabled; GPU detection was skipped for ranking.")
            ElseIf options.SimulatedGpuInputs.Count > 0 Then
                hardware.Gpus = _gpuCatalog.ResolveMany(options.SimulatedGpuInputs)
                hardware.DetectionNotes.Add("Using simulated GPU input.")
            Else
                Dim nvidia = Await DetectNvidiaAsync(cancellationToken)
                If nvidia.Count > 0 Then
                    hardware.Gpus.AddRange(nvidia)
                End If

                Dim fallback = Await DetectWmiGpusAsync(cancellationToken)
                For Each gpu In fallback
                    If Not hardware.Gpus.Any(Function(existing) existing.Name.Equals(gpu.Name, StringComparison.OrdinalIgnoreCase)) Then
                        hardware.Gpus.Add(gpu)
                    End If
                Next
            End If

            ApplyOverridesAndBudgets(hardware, options)
            Return hardware
        End Function

        Private Async Function FillCpuAsync(hardware As HardwareInfo, cancellationToken As CancellationToken) As Task
            Try
                Dim json = Await RunProcessAsync("powershell", New String() {"-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", "Get-CimInstance Win32_Processor | Select-Object -First 1 Name,NumberOfCores | ConvertTo-Json -Compress"}, cancellationToken)
                If Not String.IsNullOrWhiteSpace(json) Then
                    Using document = JsonDocument.Parse(json)
                        hardware.CpuName = ReadString(document.RootElement, "Name")
                        hardware.PhysicalCores = CInt(ReadLong(document.RootElement, "NumberOfCores"))
                    End Using
                End If
            Catch ex As Exception
                hardware.DetectionNotes.Add("CPU CIM detection failed: " & ex.Message)
            End Try

            If String.IsNullOrWhiteSpace(hardware.CpuName) Then
                hardware.CpuName = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")
            End If
            If hardware.PhysicalCores <= 0 Then
                hardware.PhysicalCores = Math.Max(1, Environment.ProcessorCount \ 2)
            End If
        End Function

        Private Async Function FillMemoryAsync(hardware As HardwareInfo, cancellationToken As CancellationToken) As Task
            Try
                Dim json = Await RunProcessAsync("powershell", New String() {"-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", "Get-CimInstance Win32_OperatingSystem | Select-Object -First 1 TotalVisibleMemorySize,FreePhysicalMemory | ConvertTo-Json -Compress"}, cancellationToken)
                If Not String.IsNullOrWhiteSpace(json) Then
                    Using document = JsonDocument.Parse(json)
                        hardware.TotalRamBytes = ReadLong(document.RootElement, "TotalVisibleMemorySize") * 1024
                        hardware.AvailableRamBytes = ReadLong(document.RootElement, "FreePhysicalMemory") * 1024
                    End Using
                End If
            Catch ex As Exception
                hardware.DetectionNotes.Add("RAM CIM detection failed: " & ex.Message)
            End Try

            If hardware.TotalRamBytes <= 0 Then
                hardware.TotalRamBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes
            End If
            If hardware.AvailableRamBytes <= 0 Then
                hardware.AvailableRamBytes = CLng(hardware.TotalRamBytes * 0.55R)
            End If
        End Function

        Private Shared Sub FillDisk(hardware As HardwareInfo)
            Try
                Dim root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
                If Not String.IsNullOrWhiteSpace(root) Then
                    hardware.FreeDiskBytes = New DriveInfo(root).AvailableFreeSpace
                End If
            Catch ex As Exception
                hardware.DetectionNotes.Add("Disk detection failed: " & ex.Message)
            End Try
        End Sub

        Private Async Function DetectNvidiaAsync(cancellationToken As CancellationToken) As Task(Of List(Of GpuInfo))
            Dim result As New List(Of GpuInfo)
            Dim output As String = ""
            Try
                output = Await RunProcessAsync("nvidia-smi", New String() {"--query-gpu=name,memory.total,clocks.max.memory", "--format=csv,noheader,nounits"}, cancellationToken)
            Catch
                output = ""
            End Try

            If String.IsNullOrWhiteSpace(output) Then
                Try
                    output = Await RunProcessAsync("nvidia-smi", New String() {"--query-gpu=name,memory.total", "--format=csv,noheader,nounits"}, cancellationToken)
                Catch
                    Return result
                End Try
            End If

            For Each line In output.Split({ControlChars.Cr, ControlChars.Lf}, StringSplitOptions.RemoveEmptyEntries)
                Dim parts = line.Split(","c).Select(Function(p) p.Trim()).ToArray()
                If parts.Length < 2 Then Continue For
                Dim name = parts(0)
                Dim memoryMb As Double
                If Not Double.TryParse(parts(1), NumberStyles.Float, CultureInfo.InvariantCulture, memoryMb) Then Continue For

                Dim catalogGpu = _gpuCatalog.Resolve(name)
                catalogGpu.Name = name
                catalogGpu.Vendor = "NVIDIA"
                catalogGpu.VramBytes = CLng(memoryMb * 1024 * 1024)
                catalogGpu.UsableVramBytes = catalogGpu.VramBytes
                catalogGpu.RuntimeVersion = "nvidia-smi"
                result.Add(catalogGpu)
            Next

            Return result
        End Function

        Private Async Function DetectWmiGpusAsync(cancellationToken As CancellationToken) As Task(Of List(Of GpuInfo))
            Dim result As New List(Of GpuInfo)
            Try
                Dim command = "Get-CimInstance Win32_VideoController | Select-Object Name,AdapterRAM,VideoProcessor | ConvertTo-Json -Compress"
                Dim json = Await RunProcessAsync("powershell", New String() {"-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command}, cancellationToken)
                If String.IsNullOrWhiteSpace(json) Then Return result

                Using document = JsonDocument.Parse(json)
                    If document.RootElement.ValueKind = JsonValueKind.Array Then
                        For Each item In document.RootElement.EnumerateArray()
                            AddWmiGpu(result, item)
                        Next
                    ElseIf document.RootElement.ValueKind = JsonValueKind.Object Then
                        AddWmiGpu(result, document.RootElement)
                    End If
                End Using
            Catch
                Return result
            End Try

            Return result
        End Function

        Private Sub AddWmiGpu(result As List(Of GpuInfo), item As JsonElement)
            Dim name = ReadString(item, "Name")
            If String.IsNullOrWhiteSpace(name) Then Return
            Dim lowered = name.ToLowerInvariant()
            If lowered.Contains("microsoft basic", StringComparison.Ordinal) OrElse lowered.Contains("remote display", StringComparison.Ordinal) Then Return

            Dim adapterRam = ReadLong(item, "AdapterRAM")
            Dim catalogGpu = _gpuCatalog.Resolve(name)
            catalogGpu.Name = name
            catalogGpu.Vendor = If(catalogGpu.Vendor = "Unknown", GuessVendor(name), catalogGpu.Vendor)
            If adapterRam > 0 AndAlso adapterRam < Long.MaxValue Then
                catalogGpu.VramBytes = Math.Max(catalogGpu.VramBytes, adapterRam)
                catalogGpu.UsableVramBytes = catalogGpu.VramBytes
            End If
            If catalogGpu.IsSharedMemory AndAlso catalogGpu.VramBytes < 2L * 1024 * 1024 * 1024 Then
                catalogGpu.Notes.Add("Shared-memory GPU detected; system RAM will be considered for fit checks.")
            End If
            result.Add(catalogGpu)
        End Sub

        Private Shared Function GuessVendor(name As String) As String
            Dim text = name.ToLowerInvariant()
            If text.Contains("nvidia", StringComparison.Ordinal) OrElse text.Contains("geforce", StringComparison.Ordinal) OrElse text.Contains("rtx", StringComparison.Ordinal) Then Return "NVIDIA"
            If text.Contains("amd", StringComparison.Ordinal) OrElse text.Contains("radeon", StringComparison.Ordinal) Then Return "AMD"
            If text.Contains("intel", StringComparison.Ordinal) OrElse text.Contains("arc", StringComparison.Ordinal) Then Return "Intel"
            Return "Unknown"
        End Function

        Private Shared Sub ApplyOverridesAndBudgets(hardware As HardwareInfo, options As RankingOptions)
            If options.OverrideVramBytes.HasValue Then
                If hardware.Gpus.Count = 0 Then
                    hardware.Gpus.Add(New GpuInfo With {.Name = "Manual GPU", .Vendor = "Manual", .VramBytes = options.OverrideVramBytes.Value})
                End If
                Dim index = Math.Clamp(If(options.GpuIndex, 0), 0, Math.Max(0, hardware.Gpus.Count - 1))
                hardware.Gpus(index).VramBytes = options.OverrideVramBytes.Value
                hardware.Gpus(index).UsableVramBytes = options.OverrideVramBytes.Value
                hardware.Gpus(index).Notes.Add("Manual VRAM override applied.")
            End If

            If options.OverrideBandwidthGbps.HasValue AndAlso hardware.Gpus.Count > 0 Then
                Dim index = Math.Clamp(If(options.GpuIndex, 0), 0, hardware.Gpus.Count - 1)
                hardware.Gpus(index).MemoryBandwidthGbps = options.OverrideBandwidthGbps.Value
                hardware.Gpus(index).Notes.Add("Manual bandwidth override applied.")
            End If

            For Each gpu In hardware.Gpus
                gpu.UsableVramBytes = ApplyHeadroom(gpu.VramBytes, options.VramHeadroom)
            Next

            If String.Equals(options.RamBudget, "available", StringComparison.OrdinalIgnoreCase) Then
                hardware.RamBudgetBytes = hardware.AvailableRamBytes
                hardware.BudgetNotes.Add("RAM budget capped to current available memory.")
            ElseIf Not String.IsNullOrWhiteSpace(options.RamBudget) Then
                Try
                    hardware.RamBudgetBytes = InputParsers.ParseBytes(options.RamBudget, hardware.TotalRamBytes)
                    hardware.BudgetNotes.Add("Manual RAM budget applied.")
                Catch
                    hardware.RamBudgetBytes = ConservativeRamBudget(hardware)
                    hardware.BudgetNotes.Add("Invalid RAM budget input; conservative default applied.")
                End Try
            Else
                hardware.RamBudgetBytes = ConservativeRamBudget(hardware)
            End If
        End Sub

        Private Shared Function ApplyHeadroom(vramBytes As Long, headroom As String) As Long
            If vramBytes <= 0 Then Return 0
            If String.IsNullOrWhiteSpace(headroom) OrElse String.Equals(headroom, "auto", StringComparison.OrdinalIgnoreCase) Then
                Dim reserve = Math.Max(512L * 1024 * 1024, CLng(vramBytes * 0.06R))
                Return Math.Max(0, vramBytes - reserve)
            End If
            If String.Equals(headroom, "none", StringComparison.OrdinalIgnoreCase) Then Return vramBytes

            Try
                Dim reserve = InputParsers.ParseBytes(headroom, vramBytes)
                Return Math.Max(0, vramBytes - reserve)
            Catch
                Return vramBytes
            End Try
        End Function

        Private Shared Function ConservativeRamBudget(hardware As HardwareInfo) As Long
            Dim reserve = Math.Max(4L * 1024 * 1024 * 1024, CLng(hardware.TotalRamBytes * 0.25R))
            Return Math.Max(0, hardware.TotalRamBytes - reserve)
        End Function

        Private Shared Function ReadString(element As JsonElement, propertyName As String) As String
            Dim prop As JsonElement
            If element.ValueKind = JsonValueKind.Object AndAlso element.TryGetProperty(propertyName, prop) Then
                If prop.ValueKind = JsonValueKind.String Then Return prop.GetString()
                Return prop.ToString()
            End If
            Return ""
        End Function

        Private Shared Function ReadLong(element As JsonElement, propertyName As String) As Long
            Dim prop As JsonElement
            If element.ValueKind = JsonValueKind.Object AndAlso element.TryGetProperty(propertyName, prop) Then
                If prop.ValueKind = JsonValueKind.Number Then
                    Dim value As Long
                    If prop.TryGetInt64(value) Then Return value
                End If
            End If
            Return 0
        End Function

        Private Shared Async Function RunProcessAsync(fileName As String, args As IEnumerable(Of String), cancellationToken As CancellationToken) As Task(Of String)
            Dim startInfo As New ProcessStartInfo With {
                .FileName = fileName,
                .UseShellExecute = False,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True,
                .CreateNoWindow = True
            }
            For Each arg In args
                startInfo.ArgumentList.Add(arg)
            Next

            Using process As New Process With {.StartInfo = startInfo}
                process.Start()
                Dim outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken)
                Dim errorTask = process.StandardError.ReadToEndAsync(cancellationToken)
                Await process.WaitForExitAsync(cancellationToken)
                Dim output = Await outputTask
                If process.ExitCode <> 0 Then
                    Dim errorText = Await errorTask
                    Throw New InvalidOperationException(errorText)
                End If
                Return output
            End Using
        End Function
    End Class
End Namespace
