Option Strict On
Option Explicit On

Imports System.Text.RegularExpressions
Imports WhichLlm.Core.Dto

Namespace Services
    Public Class GpuCatalog
        Implements IGpuCatalog

        Private Shared ReadOnly Entries As List(Of GpuCatalogEntry) = New List(Of GpuCatalogEntry) From {
            Entry("RTX 5090", "NVIDIA", 32, 1792, "12.0", False, "GeForce RTX 5090"),
            Entry("RTX 4090", "NVIDIA", 24, 1008, "8.9", False, "GeForce RTX 4090"),
            Entry("RTX 4080 SUPER", "NVIDIA", 16, 736, "8.9", False, "RTX 4080S"),
            Entry("RTX 4070 Ti SUPER", "NVIDIA", 16, 672, "8.9", False, "4070 Ti Super"),
            Entry("RTX 4070", "NVIDIA", 12, 504, "8.9", False, "GeForce RTX 4070"),
            Entry("RTX 3090", "NVIDIA", 24, 936, "8.6", False, "GeForce RTX 3090"),
            Entry("RTX 3080", "NVIDIA", 10, 760, "8.6", False, "GeForce RTX 3080"),
            Entry("RTX 3060", "NVIDIA", 12, 360, "8.6", False, "GeForce RTX 3060"),
            Entry("GTX 1650", "NVIDIA", 4, 128, "7.5", False, "GeForce GTX 1650"),
            Entry("H100", "NVIDIA", 80, 3350, "9.0", False, "H100 SXM"),
            Entry("A100 80GB", "NVIDIA", 80, 2039, "8.0", False, "A100"),
            Entry("RX 7900 XTX", "AMD", 24, 960, "", False, "Radeon RX 7900 XTX"),
            Entry("RX 7800 XT", "AMD", 16, 624, "", False, "Radeon RX 7800 XT"),
            Entry("RX 7600", "AMD", 8, 288, "", False, "Radeon RX 7600"),
            Entry("Radeon 8060S", "AMD", 96, 256, "", True, "Strix Halo", "Ryzen AI MAX 395"),
            Entry("Radeon 890M", "AMD", 32, 120, "", True, "Ryzen AI 9 HX 370"),
            Entry("Intel Arc A770", "Intel", 16, 560, "", False, "Arc A770"),
            Entry("Intel Arc B580", "Intel", 12, 456, "", False, "Arc B580"),
            Entry("Apple M4 Max", "Apple", 128, 546, "", True, "M4 Max"),
            Entry("Apple M3 Max", "Apple", 128, 400, "", True, "M3 Max"),
            Entry("Apple M2 Max", "Apple", 96, 400, "", True, "M2 Max")
        }

        Public Function Resolve(input As String, Optional overrideVramBytes As Long? = Nothing, Optional overrideBandwidthGbps As Double? = Nothing) As GpuInfo Implements IGpuCatalog.Resolve
            If String.IsNullOrWhiteSpace(input) Then
                Throw New ArgumentException("GPU name is required.")
            End If

            Dim clean = NormalizeGpuName(input)
            Dim match = Entries.
                OrderByDescending(Function(e) ScoreGpuMatch(clean, e)).
                FirstOrDefault(Function(e) ScoreGpuMatch(clean, e) > 0)

            If match Is Nothing Then
                Return New GpuInfo With {
                    .Name = input.Trim(),
                    .Vendor = GuessVendor(input),
                    .VramBytes = If(overrideVramBytes.HasValue, overrideVramBytes.Value, 0),
                    .UsableVramBytes = overrideVramBytes,
                    .MemoryBandwidthGbps = overrideBandwidthGbps,
                    .IsSharedMemory = LooksSharedMemory(input),
                    .Notes = New List(Of String) From {"Unknown GPU; using manual VRAM/bandwidth overrides when present."}
                }
            End If

            Dim vram = If(overrideVramBytes.HasValue, overrideVramBytes.Value, CLng(match.VramGb * 1024 * 1024 * 1024))
            Return New GpuInfo With {
                .Name = match.Name,
                .Vendor = match.Vendor,
                .VramBytes = vram,
                .UsableVramBytes = vram,
                .ComputeCapability = match.ComputeCapability,
                .MemoryBandwidthGbps = If(overrideBandwidthGbps.HasValue, overrideBandwidthGbps.Value, match.BandwidthGbps),
                .IsSharedMemory = match.IsSharedMemory
            }
        End Function

        Public Function ResolveMany(inputs As IEnumerable(Of String)) As List(Of GpuInfo) Implements IGpuCatalog.ResolveMany
            Dim result As New List(Of GpuInfo)
            For Each raw In inputs
                If String.IsNullOrWhiteSpace(raw) Then
                    Continue For
                End If

                For Each part In SplitGpuInput(raw)
                    Dim count = part.Count
                    For index = 1 To count
                        result.Add(Resolve(part.Name))
                    Next
                Next
            Next
            Return result
        End Function

        Public Function Search(term As String) As IReadOnlyList(Of GpuCatalogEntry) Implements IGpuCatalog.Search
            If String.IsNullOrWhiteSpace(term) Then
                Return Entries.Take(20).ToList()
            End If
            Dim clean = NormalizeGpuName(term)
            Return Entries.
                Select(Function(e) New With {.Entry = e, .Score = ScoreGpuMatch(clean, e)}).
                Where(Function(x) x.Score > 0).
                OrderByDescending(Function(x) x.Score).
                Select(Function(x) x.Entry).
                Take(20).
                ToList()
        End Function

        Public Function CommonGpuNames() As IReadOnlyList(Of String) Implements IGpuCatalog.CommonGpuNames
            Return Entries.Select(Function(e) e.Name).ToList()
        End Function

        Private Shared Function Entry(name As String, vendor As String, vramGb As Double, bandwidthGbps As Double, computeCapability As String, isSharedMemory As Boolean, ParamArray aliases() As String) As GpuCatalogEntry
            Return New GpuCatalogEntry With {
                .Name = name,
                .Vendor = vendor,
                .VramGb = vramGb,
                .BandwidthGbps = bandwidthGbps,
                .ComputeCapability = computeCapability,
                .IsSharedMemory = isSharedMemory,
                .Aliases = aliases.ToList()
            }
        End Function

        Private Shared Iterator Function SplitGpuInput(raw As String) As IEnumerable(Of (Name As String, Count As Integer))
            For Each piece In raw.Split(","c)
                Dim text = piece.Trim()
                If text.Length = 0 Then
                    Continue For
                End If
                Dim match = Regex.Match(text, "^\s*(\d+)\s*x\s+(.+)$", RegexOptions.IgnoreCase)
                If match.Success Then
                    Yield (match.Groups(2).Value.Trim(), Integer.Parse(match.Groups(1).Value))
                Else
                    Yield (text, 1)
                End If
            Next
        End Function

        Private Shared Function NormalizeGpuName(value As String) As String
            Return Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "")
        End Function

        Private Shared Function ScoreGpuMatch(cleanInput As String, entry As GpuCatalogEntry) As Integer
            Dim names = New List(Of String) From {entry.Name}
            names.AddRange(entry.Aliases)
            For Each candidate In names
                Dim cleanCandidate = NormalizeGpuName(candidate)
                If cleanInput = cleanCandidate Then
                    Return 100
                End If
                If cleanInput.Contains(cleanCandidate, StringComparison.Ordinal) OrElse cleanCandidate.Contains(cleanInput, StringComparison.Ordinal) Then
                    Return Math.Min(90, cleanCandidate.Length)
                End If
            Next
            Return 0
        End Function

        Private Shared Function GuessVendor(name As String) As String
            Dim text = name.ToLowerInvariant()
            If text.Contains("nvidia", StringComparison.Ordinal) OrElse text.Contains("rtx", StringComparison.Ordinal) OrElse text.Contains("gtx", StringComparison.Ordinal) Then Return "NVIDIA"
            If text.Contains("amd", StringComparison.Ordinal) OrElse text.Contains("radeon", StringComparison.Ordinal) OrElse text.Contains("rx ", StringComparison.Ordinal) Then Return "AMD"
            If text.Contains("intel", StringComparison.Ordinal) OrElse text.Contains("arc", StringComparison.Ordinal) Then Return "Intel"
            If text.Contains("apple", StringComparison.Ordinal) OrElse text.Contains("m", StringComparison.Ordinal) Then Return "Apple"
            Return "Unknown"
        End Function

        Private Shared Function LooksSharedMemory(name As String) As Boolean
            Dim text = name.ToLowerInvariant()
            Return text.Contains("890m", StringComparison.Ordinal) OrElse text.Contains("780m", StringComparison.Ordinal) OrElse text.Contains("strix", StringComparison.Ordinal) OrElse text.Contains("apple", StringComparison.Ordinal)
        End Function
    End Class
End Namespace
