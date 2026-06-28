Option Strict On
Option Explicit On

Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Runtime.Intrinsics.X86
Imports System.Text.Json
Imports System.Text.RegularExpressions
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
                Dim detectedGpuNames = Await DetectVideoControllerNamesAsync(cancellationToken)
                If ShouldProbeNvidia(detectedGpuNames) Then
                    Dim nvidia = Await DetectNvidiaAsync(cancellationToken)
                    If nvidia.Count > 0 Then
                        hardware.Gpus.AddRange(nvidia)
                    End If
                End If

                Dim hasHipInfo = Not String.IsNullOrWhiteSpace(FindHipInfoPath())
                If ShouldProbeHipInfo(detectedGpuNames, hasHipInfo) Then
                    Dim hip = Await DetectHipInfoAsync(cancellationToken)
                    For Each gpu In hip
                        AddDistinctGpu(hardware.Gpus, gpu)
                    Next
                End If

                Dim xpuSmiPath = FindXpuSmiPath()
                If ShouldProbeXpuSmi(detectedGpuNames, Not String.IsNullOrWhiteSpace(xpuSmiPath)) Then
                    Dim intel = Await DetectIntelXpuSmiAsync(xpuSmiPath, cancellationToken)
                    For Each gpu In intel
                        AddDistinctGpu(hardware.Gpus, gpu)
                    Next
                End If

                Dim fallback = Await DetectWmiGpusAsync(cancellationToken)
                For Each gpu In fallback
                    AddDistinctGpu(hardware.Gpus, gpu)
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

        Private Shared Async Function DetectVideoControllerNamesAsync(cancellationToken As CancellationToken) As Task(Of List(Of String))
            Dim result As New List(Of String)
            Try
                Dim command = "Get-CimInstance Win32_VideoController | Select-Object -ExpandProperty Name | ConvertTo-Json -Compress"
                Dim json = Await RunProcessAsync("powershell", New String() {"-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command}, cancellationToken)
                If String.IsNullOrWhiteSpace(json) Then Return result

                Using document = JsonDocument.Parse(json)
                    If document.RootElement.ValueKind = JsonValueKind.Array Then
                        For Each item In document.RootElement.EnumerateArray()
                            AddVideoControllerName(result, item)
                        Next
                    Else
                        AddVideoControllerName(result, document.RootElement)
                    End If
                End Using
            Catch
                Return result
            End Try

            Return result
        End Function

        Private Shared Sub AddVideoControllerName(result As List(Of String), item As JsonElement)
            If item.ValueKind <> JsonValueKind.String Then Return
            Dim name = item.GetString()
            If String.IsNullOrWhiteSpace(name) Then Return
            result.Add(name)
        End Sub

        Friend Shared Function ShouldProbeNvidia(gpuNames As IEnumerable(Of String)) As Boolean
            Dim names = CleanGpuNames(gpuNames)
            If names.Count = 0 Then Return True
            Return names.Any(Function(name) GuessVendor(name).Equals("NVIDIA", StringComparison.OrdinalIgnoreCase))
        End Function

        Friend Shared Function ShouldProbeHipInfo(gpuNames As IEnumerable(Of String), hipInfoAvailable As Boolean) As Boolean
            Dim names = CleanGpuNames(gpuNames)
            If names.Count = 0 Then Return hipInfoAvailable
            Return names.Any(Function(name) GuessVendor(name).Equals("AMD", StringComparison.OrdinalIgnoreCase)) OrElse hipInfoAvailable
        End Function

        Friend Shared Function ShouldProbeXpuSmi(gpuNames As IEnumerable(Of String), xpuSmiAvailable As Boolean) As Boolean
            Dim names = CleanGpuNames(gpuNames)
            If names.Count = 0 Then Return xpuSmiAvailable
            Return names.Any(Function(name) GuessVendor(name).Equals("Intel", StringComparison.OrdinalIgnoreCase)) OrElse xpuSmiAvailable
        End Function

        Private Shared Function CleanGpuNames(gpuNames As IEnumerable(Of String)) As List(Of String)
            If gpuNames Is Nothing Then Return New List(Of String)()
            Return gpuNames.
                Where(Function(name) Not String.IsNullOrWhiteSpace(name)).
                Where(Function(name)
                          Dim lowered = name.ToLowerInvariant()
                          Return Not lowered.Contains("microsoft basic", StringComparison.Ordinal) AndAlso Not lowered.Contains("remote display", StringComparison.Ordinal)
                      End Function).
                ToList()
        End Function

        Private Async Function DetectHipInfoAsync(cancellationToken As CancellationToken) As Task(Of List(Of GpuInfo))
            Dim hipInfoPath = FindHipInfoPath()
            If String.IsNullOrWhiteSpace(hipInfoPath) Then Return New List(Of GpuInfo)()

            Try
                Dim output = Await RunProcessAsync(hipInfoPath, Array.Empty(Of String)(), cancellationToken)
                Return ParseHipInfoOutput(output, _gpuCatalog)
            Catch
                Return New List(Of GpuInfo)()
            End Try
        End Function

        Private Shared Function FindHipInfoPath() As String
            Dim hipPath = Environment.GetEnvironmentVariable("HIP_PATH")
            If Not String.IsNullOrWhiteSpace(hipPath) Then
                Dim candidate = Path.Combine(hipPath, "bin", "hipInfo.exe")
                If File.Exists(candidate) Then Return candidate
            End If

            Return ""
        End Function

        Private Async Function DetectIntelXpuSmiAsync(xpuSmiPath As String, cancellationToken As CancellationToken) As Task(Of List(Of GpuInfo))
            If String.IsNullOrWhiteSpace(xpuSmiPath) Then Return New List(Of GpuInfo)()

            Try
                Dim output = Await RunProcessAsync(xpuSmiPath, New String() {"discovery", "-j"}, cancellationToken)
                Dim parsed = ParseXpuSmiOutput(output, _gpuCatalog)
                If parsed.Count > 0 Then Return parsed
            Catch
            End Try

            Try
                Dim output = Await RunProcessAsync(xpuSmiPath, New String() {"discovery"}, cancellationToken)
                Return ParseXpuSmiOutput(output, _gpuCatalog)
            Catch
                Return New List(Of GpuInfo)()
            End Try
        End Function

        Private Shared Function FindXpuSmiPath() As String
            Dim pathValue = Environment.GetEnvironmentVariable("PATH")
            If Not String.IsNullOrWhiteSpace(pathValue) Then
                For Each directory In pathValue.Split(Path.PathSeparator)
                    If String.IsNullOrWhiteSpace(directory) Then Continue For
                    Try
                        Dim candidate = Path.Combine(directory.Trim(), "xpu-smi.exe")
                        If File.Exists(candidate) Then Return candidate
                    Catch
                    End Try
                Next
            End If

            Dim programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            Dim candidates = New List(Of String) From {
                Path.Combine(programFiles, "Intel", "oneAPI", "tools", "latest", "xpu-smi", "xpu-smi.exe"),
                Path.Combine(programFiles, "Intel", "xpu-smi", "xpu-smi.exe")
            }
            For Each candidate In candidates
                If File.Exists(candidate) Then Return candidate
            Next

            Return ""
        End Function

        Friend Shared Function ParseXpuSmiOutput(output As String, gpuCatalog As IGpuCatalog) As List(Of GpuInfo)
            Dim result As New List(Of GpuInfo)
            If String.IsNullOrWhiteSpace(output) Then Return result

            Dim trimmed = output.Trim()
            If trimmed.StartsWith("{", StringComparison.Ordinal) OrElse trimmed.StartsWith("[", StringComparison.Ordinal) Then
                Try
                    Using document = JsonDocument.Parse(trimmed)
                        AddXpuSmiJsonDevices(result, document.RootElement, gpuCatalog)
                    End Using
                Catch
                End Try
                If result.Count > 0 Then Return result
            End If

            Return ParseXpuSmiTextOutput(output, gpuCatalog)
        End Function

        Private Shared Sub AddXpuSmiJsonDevices(result As List(Of GpuInfo), element As JsonElement, gpuCatalog As IGpuCatalog)
            Select Case element.ValueKind
                Case JsonValueKind.Array
                    For Each child In element.EnumerateArray()
                        AddXpuSmiJsonDevices(result, child, gpuCatalog)
                    Next
                Case JsonValueKind.Object
                    Dim values = FlattenJsonObject(element)
                    AddXpuSmiDevice(result, values, gpuCatalog)

                    For Each prop In element.EnumerateObject()
                        If prop.Value.ValueKind = JsonValueKind.Array OrElse prop.Value.ValueKind = JsonValueKind.Object Then
                            AddXpuSmiJsonDevices(result, prop.Value, gpuCatalog)
                        End If
                    Next
            End Select
        End Sub

        Private Shared Function FlattenJsonObject(element As JsonElement) As Dictionary(Of String, String)
            Dim result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            FlattenJsonObjectInto(element, "", result)
            Return result
        End Function

        Private Shared Sub FlattenJsonObjectInto(element As JsonElement, prefix As String, result As Dictionary(Of String, String))
            If element.ValueKind <> JsonValueKind.Object Then Return
            For Each prop In element.EnumerateObject()
                Dim key = If(String.IsNullOrWhiteSpace(prefix), prop.Name, prefix & "." & prop.Name)
                Select Case prop.Value.ValueKind
                    Case JsonValueKind.Object
                        FlattenJsonObjectInto(prop.Value, key, result)
                    Case JsonValueKind.String, JsonValueKind.Number, JsonValueKind.True, JsonValueKind.False
                        result(key) = prop.Value.ToString()
                End Select
            Next
        End Sub

        Private Shared Function ParseXpuSmiTextOutput(output As String, gpuCatalog As IGpuCatalog) As List(Of GpuInfo)
            Dim result As New List(Of GpuInfo)
            Dim current As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

            For Each rawLine In output.Split({ControlChars.Cr, ControlChars.Lf}, StringSplitOptions.RemoveEmptyEntries)
                Dim line = rawLine.Trim()
                If line.Length = 0 Then Continue For

                Dim separator = line.IndexOf(":"c)
                If separator > 0 Then
                    Dim key = line.Substring(0, separator).Trim()
                    Dim value = line.Substring(separator + 1).Trim()
                    If IsNewXpuTextDevice(key, value, current) Then
                        AddXpuSmiDevice(result, current, gpuCatalog)
                        current.Clear()
                    End If
                    current(key) = value
                ElseIf line.Contains("Intel", StringComparison.OrdinalIgnoreCase) OrElse line.Contains("Arc", StringComparison.OrdinalIgnoreCase) Then
                    AddXpuSmiDevice(result, current, gpuCatalog)
                    current.Clear()
                    current("Device Name") = line
                End If
            Next

            AddXpuSmiDevice(result, current, gpuCatalog)
            Return result
        End Function

        Private Shared Function IsNewXpuTextDevice(key As String, value As String, current As Dictionary(Of String, String)) As Boolean
            If current.Count = 0 Then Return False
            Dim normalizedKey = NormalizeLabel(key)
            If normalizedKey = "devicename" OrElse normalizedKey = "name" Then
                Return value.Contains("Intel", StringComparison.OrdinalIgnoreCase) OrElse value.Contains("Arc", StringComparison.OrdinalIgnoreCase)
            End If
            Return False
        End Function

        Private Shared Sub AddXpuSmiDevice(result As List(Of GpuInfo), values As Dictionary(Of String, String), gpuCatalog As IGpuCatalog)
            If values.Count = 0 Then Return

            Dim name = FindLabeledValue(values, "devicename", "name", "productname", "gpu")
            If String.IsNullOrWhiteSpace(name) OrElse Not LooksIntelGpuName(name) Then Return

            Dim memoryBytes = FindMemoryBytes(values)
            Dim gpu = gpuCatalog.Resolve(name)
            gpu.Name = name
            gpu.Vendor = "Intel"
            If memoryBytes.HasValue AndAlso memoryBytes.Value > 0 Then
                gpu.VramBytes = Math.Max(gpu.VramBytes, memoryBytes.Value)
                gpu.UsableVramBytes = gpu.VramBytes
            ElseIf gpu.VramBytes > 0 Then
                gpu.UsableVramBytes = gpu.VramBytes
            End If
            gpu.RuntimeVersion = "xpu-smi"
            gpu.Notes.RemoveAll(Function(note) note.StartsWith("Unknown GPU;", StringComparison.OrdinalIgnoreCase))
            gpu.Notes.Add("GPU read from Intel xpu-smi.")
            result.Add(gpu)
        End Sub

        Private Shared Function LooksIntelGpuName(name As String) As Boolean
            Return name.Contains("Intel", StringComparison.OrdinalIgnoreCase) OrElse name.Contains("Arc", StringComparison.OrdinalIgnoreCase)
        End Function

        Private Shared Function FindLabeledValue(values As Dictionary(Of String, String), ParamArray normalizedLabels() As String) As String
            For Each pair In values
                Dim label = NormalizeLabel(pair.Key)
                If normalizedLabels.Any(Function(candidate) label.EndsWith(candidate, StringComparison.OrdinalIgnoreCase)) Then
                    Return pair.Value
                End If
            Next
            Return ""
        End Function

        Private Shared Function FindMemoryBytes(values As Dictionary(Of String, String)) As Long?
            For Each pair In values
                Dim label = NormalizeLabel(pair.Key)
                If Not label.Contains("memory", StringComparison.OrdinalIgnoreCase) AndAlso Not label.Contains("mem", StringComparison.OrdinalIgnoreCase) Then Continue For
                If label.Contains("free", StringComparison.OrdinalIgnoreCase) OrElse label.Contains("used", StringComparison.OrdinalIgnoreCase) OrElse label.Contains("util", StringComparison.OrdinalIgnoreCase) Then Continue For

                Dim parsed = ParseIntelBytes(pair.Value)
                If parsed.HasValue AndAlso parsed.Value > 0 Then Return parsed.Value
            Next
            Return Nothing
        End Function

        Private Shared Function NormalizeLabel(value As String) As String
            Return Regex.Replace(If(value, "").ToLowerInvariant(), "[^a-z0-9]+", "")
        End Function

        Private Shared Function ParseIntelBytes(value As String) As Long?
            If String.IsNullOrWhiteSpace(value) Then Return Nothing
            Dim match = Regex.Match(value, "([0-9]+(?:\.[0-9]+)?)\s*(b|bytes|kb|mb|gb|tb|kib|mib|gib|tib)?", RegexOptions.IgnoreCase)
            If Not match.Success Then Return Nothing

            Dim number = Double.Parse(match.Groups(1).Value, CultureInfo.InvariantCulture)
            Dim unit = match.Groups(2).Value.ToLowerInvariant()
            Dim multiplier As Double
            Select Case unit
                Case "", "b", "bytes"
                    multiplier = 1
                Case "kb", "kib"
                    multiplier = 1024
                Case "mb", "mib"
                    multiplier = 1024.0R * 1024.0R
                Case "gb", "gib"
                    multiplier = 1024.0R * 1024.0R * 1024.0R
                Case "tb", "tib"
                    multiplier = 1024.0R * 1024.0R * 1024.0R * 1024.0R
                Case Else
                    multiplier = 1
            End Select
            Return CLng(Math.Round(number * multiplier))
        End Function

        Friend Shared Function ParseHipInfoOutput(output As String, gpuCatalog As IGpuCatalog) As List(Of GpuInfo)
            Dim result As New List(Of GpuInfo)
            If String.IsNullOrWhiteSpace(output) Then Return result

            Dim current As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            For Each rawLine In output.Split({ControlChars.Cr, ControlChars.Lf}, StringSplitOptions.RemoveEmptyEntries)
                Dim line = rawLine.TrimEnd()
                If Regex.IsMatch(line, "^\s*device#\s+\d+\s*$", RegexOptions.IgnoreCase) Then
                    AddHipDevice(result, current, gpuCatalog)
                    current.Clear()
                    Continue For
                End If

                Dim separator = line.IndexOf(":"c)
                If separator <= 0 Then Continue For
                Dim key = line.Substring(0, separator).Trim()
                Dim value = line.Substring(separator + 1).Trim()
                If key.Length > 0 Then current(key) = value
            Next

            AddHipDevice(result, current, gpuCatalog)
            Return result
        End Function

        Private Shared Sub AddHipDevice(result As List(Of GpuInfo), values As Dictionary(Of String, String), gpuCatalog As IGpuCatalog)
            If values.Count = 0 Then Return

            Dim name = ReadValue(values, "Name")
            If String.IsNullOrWhiteSpace(name) Then Return

            Dim totalBytes = ParseHipBytes(ReadFirstValue(values, "totalGlobalMem", "memInfo.total"))
            If Not totalBytes.HasValue OrElse totalBytes.Value <= 0 Then Return

            Dim gpu = gpuCatalog.Resolve(name)
            gpu.Name = name
            gpu.Vendor = "AMD"
            gpu.VramBytes = Math.Max(gpu.VramBytes, totalBytes.Value)
            gpu.UsableVramBytes = gpu.VramBytes
            gpu.RuntimeVersion = "hipInfo"
            gpu.ComputeCapability = ReadValue(values, "gcnArchName")
            gpu.Notes.RemoveAll(Function(note) note.StartsWith("Unknown GPU;", StringComparison.OrdinalIgnoreCase))
            gpu.Notes.Add("VRAM read from AMD hipInfo.")
            If Not String.IsNullOrWhiteSpace(gpu.ComputeCapability) Then
                gpu.Notes.Add("HIP arch: " & gpu.ComputeCapability)
            End If
            result.Add(gpu)
        End Sub

        Private Shared Function ReadFirstValue(values As Dictionary(Of String, String), ParamArray keys() As String) As String
            For Each key In keys
                Dim value = ReadValue(values, key)
                If Not String.IsNullOrWhiteSpace(value) Then Return value
            Next
            Return ""
        End Function

        Private Shared Function ReadValue(values As Dictionary(Of String, String), key As String) As String
            Dim value As String = ""
            If values.TryGetValue(key, value) Then Return value
            Return ""
        End Function

        Private Shared Function ParseHipBytes(value As String) As Long?
            If String.IsNullOrWhiteSpace(value) Then Return Nothing
            Dim match = Regex.Match(value, "([0-9]+(?:\.[0-9]+)?)\s*(b|kb|mb|gb|tb|kib|mib|gib|tib)?", RegexOptions.IgnoreCase)
            If Not match.Success Then Return Nothing

            Dim number = Double.Parse(match.Groups(1).Value, CultureInfo.InvariantCulture)
            Dim unit = match.Groups(2).Value.ToLowerInvariant()
            Dim multiplier As Double = 1
            Select Case unit
                Case "", "b"
                    multiplier = 1
                Case "kb", "kib"
                    multiplier = 1024
                Case "mb", "mib"
                    multiplier = 1024.0R * 1024.0R
                Case "gb", "gib"
                    multiplier = 1024.0R * 1024.0R * 1024.0R
                Case "tb", "tib"
                    multiplier = 1024.0R * 1024.0R * 1024.0R * 1024.0R
            End Select

            Return CLng(Math.Round(number * multiplier))
        End Function

        Private Async Function DetectWmiGpusAsync(cancellationToken As CancellationToken) As Task(Of List(Of GpuInfo))
            Dim result As New List(Of GpuInfo)
            Try
                Dim registryMemory = Await DetectRegistryGpuMemoryAsync(cancellationToken)
                Dim command = "Get-CimInstance Win32_VideoController | Select-Object Name,AdapterRAM,VideoProcessor | ConvertTo-Json -Compress"
                Dim json = Await RunProcessAsync("powershell", New String() {"-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command}, cancellationToken)
                If String.IsNullOrWhiteSpace(json) Then Return result

                Using document = JsonDocument.Parse(json)
                    If document.RootElement.ValueKind = JsonValueKind.Array Then
                        For Each item In document.RootElement.EnumerateArray()
                            AddWmiGpu(result, item, registryMemory)
                        Next
                    ElseIf document.RootElement.ValueKind = JsonValueKind.Object Then
                        AddWmiGpu(result, document.RootElement, registryMemory)
                    End If
                End Using
            Catch
                Return result
            End Try

            Return result
        End Function

        Private Shared Async Function DetectRegistryGpuMemoryAsync(cancellationToken As CancellationToken) As Task(Of Dictionary(Of String, Long))
            Dim result As New Dictionary(Of String, Long)(StringComparer.OrdinalIgnoreCase)
            Try
                Dim command = "$items = Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Control\Video' -ErrorAction SilentlyContinue | ForEach-Object { Get-ChildItem $_.PSPath -ErrorAction SilentlyContinue }; $items | ForEach-Object { $p = Get-ItemProperty -Path $_.PSPath -ErrorAction SilentlyContinue; $mem = $p.'HardwareInformation.qwMemorySize'; if ($mem) { $name = $p.DriverDesc; if (-not $name) { $name = $p.'HardwareInformation.AdapterString' }; [pscustomobject]@{ Name = [string]$name; MemoryBytes = [int64]$mem } } } | ConvertTo-Json -Compress"
                Dim json = Await RunProcessAsync("powershell", New String() {"-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command}, cancellationToken)
                If String.IsNullOrWhiteSpace(json) Then Return result

                Using document = JsonDocument.Parse(json)
                    If document.RootElement.ValueKind = JsonValueKind.Array Then
                        For Each item In document.RootElement.EnumerateArray()
                            AddRegistryMemory(result, item)
                        Next
                    ElseIf document.RootElement.ValueKind = JsonValueKind.Object Then
                        AddRegistryMemory(result, document.RootElement)
                    End If
                End Using
            Catch
                Return result
            End Try

            Return result
        End Function

        Private Shared Sub AddRegistryMemory(result As Dictionary(Of String, Long), item As JsonElement)
            Dim name = ReadString(item, "Name")
            Dim memoryBytes = ReadLong(item, "MemoryBytes")
            If String.IsNullOrWhiteSpace(name) OrElse memoryBytes <= 0 Then Return
            result(name) = Math.Max(If(result.ContainsKey(name), result(name), 0), memoryBytes)
        End Sub

        Private Sub AddWmiGpu(result As List(Of GpuInfo), item As JsonElement, registryMemory As Dictionary(Of String, Long))
            Dim name = ReadString(item, "Name")
            If String.IsNullOrWhiteSpace(name) Then Return
            Dim lowered = name.ToLowerInvariant()
            If lowered.Contains("microsoft basic", StringComparison.Ordinal) OrElse lowered.Contains("remote display", StringComparison.Ordinal) Then Return

            Dim adapterRam = ReadLong(item, "AdapterRAM")
            Dim registryRam = FindRegistryMemoryBytes(name, registryMemory)
            Dim dedicatedRam = Math.Max(adapterRam, registryRam)
            Dim catalogGpu = _gpuCatalog.Resolve(name)
            catalogGpu.Name = name
            catalogGpu.Vendor = If(catalogGpu.Vendor = "Unknown", GuessVendor(name), catalogGpu.Vendor)
            If dedicatedRam > 0 AndAlso dedicatedRam < Long.MaxValue Then
                catalogGpu.VramBytes = Math.Max(catalogGpu.VramBytes, dedicatedRam)
                catalogGpu.UsableVramBytes = catalogGpu.VramBytes
            End If
            If registryRam > 0 Then
                catalogGpu.Notes.RemoveAll(Function(note) note.StartsWith("Unknown GPU;", StringComparison.OrdinalIgnoreCase))
                If Not catalogGpu.Notes.Any(Function(note) note.Equals("VRAM read from Windows registry.", StringComparison.OrdinalIgnoreCase)) Then
                    catalogGpu.Notes.Add("VRAM read from Windows registry.")
                End If
            End If
            If catalogGpu.IsSharedMemory AndAlso catalogGpu.VramBytes < 2L * 1024 * 1024 * 1024 Then
                catalogGpu.Notes.Add("Shared-memory GPU detected; system RAM will be considered for fit checks.")
            End If
            result.Add(catalogGpu)
        End Sub

        Private Shared Function FindRegistryMemoryBytes(name As String, registryMemory As Dictionary(Of String, Long)) As Long
            If registryMemory Is Nothing OrElse registryMemory.Count = 0 Then Return 0

            Dim cleanName = NormalizeGpuName(name)
            For Each pair In registryMemory
                Dim cleanCandidate = NormalizeGpuName(pair.Key)
                If cleanCandidate.Length = 0 Then Continue For
                If cleanName = cleanCandidate OrElse cleanName.Contains(cleanCandidate, StringComparison.Ordinal) OrElse cleanCandidate.Contains(cleanName, StringComparison.Ordinal) Then
                    Return pair.Value
                End If
            Next

            Return 0
        End Function

        Private Shared Function NormalizeGpuName(value As String) As String
            Return Regex.Replace(If(value, "").ToLowerInvariant(), "[^a-z0-9]+", "")
        End Function

        Private Shared Sub AddDistinctGpu(target As List(Of GpuInfo), gpu As GpuInfo)
            If gpu Is Nothing OrElse String.IsNullOrWhiteSpace(gpu.Name) Then Return
            If target.Any(Function(existing) SameGpuName(existing.Name, gpu.Name)) Then Return
            target.Add(gpu)
        End Sub

        Private Shared Function SameGpuName(left As String, right As String) As Boolean
            Dim cleanLeft = NormalizeGpuName(left)
            Dim cleanRight = NormalizeGpuName(right)
            If cleanLeft.Length = 0 OrElse cleanRight.Length = 0 Then Return False
            Return cleanLeft = cleanRight OrElse cleanLeft.Contains(cleanRight, StringComparison.Ordinal) OrElse cleanRight.Contains(cleanLeft, StringComparison.Ordinal)
        End Function

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
            Const ProcessTimeoutSeconds As Integer = 12
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
                Using timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(ProcessTimeoutSeconds))
                    process.Start()
                    Dim outputTask = process.StandardOutput.ReadToEndAsync()
                    Dim errorTask = process.StandardError.ReadToEndAsync()
                    Try
                        Await process.WaitForExitAsync(timeoutCts.Token)
                    Catch ex As OperationCanceledException When Not cancellationToken.IsCancellationRequested
                        Try
                            If Not process.HasExited Then process.Kill(entireProcessTree:=True)
                        Catch
                        End Try
                        Throw New TimeoutException($"{fileName} did not finish within {ProcessTimeoutSeconds} seconds.")
                    End Try
                    cancellationToken.ThrowIfCancellationRequested()
                    Dim output = Await outputTask
                    If process.ExitCode <> 0 Then
                        Dim errorText = Await errorTask
                        Throw New InvalidOperationException(errorText)
                    End If
                    Return output
                End Using
            End Using
        End Function
    End Class
End Namespace
