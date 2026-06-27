Option Strict On
Option Explicit On

Namespace Dto
    Public Class GpuInfo
        Public Property Name As String = ""
        Public Property Vendor As String = ""
        Public Property VramBytes As Long
        Public Property UsableVramBytes As Long?
        Public Property ComputeCapability As String = ""
        Public Property RuntimeVersion As String = ""
        Public Property MemoryBandwidthGbps As Double?
        Public Property IsSharedMemory As Boolean
        Public Property Notes As New List(Of String)

        Public ReadOnly Property EffectiveVramBytes As Long
            Get
                If UsableVramBytes.HasValue AndAlso UsableVramBytes.Value > 0 Then
                    Return UsableVramBytes.Value
                End If
                Return VramBytes
            End Get
        End Property
    End Class

    Public Class HardwareInfo
        Public Property Gpus As New List(Of GpuInfo)
        Public Property CpuName As String = ""
        Public Property PhysicalCores As Integer
        Public Property SupportsAvx2 As Boolean
        Public Property SupportsAvx512 As Boolean
        Public Property TotalRamBytes As Long
        Public Property AvailableRamBytes As Long
        Public Property FreeDiskBytes As Long
        Public Property OsName As String = ""
        Public Property RamBudgetBytes As Long?
        Public Property BudgetNotes As New List(Of String)
        Public Property DetectionNotes As New List(Of String)

        Public Function Clone() As HardwareInfo
            Return New HardwareInfo With {
                .Gpus = Gpus.Select(Function(g) New GpuInfo With {
                    .Name = g.Name,
                    .Vendor = g.Vendor,
                    .VramBytes = g.VramBytes,
                    .UsableVramBytes = g.UsableVramBytes,
                    .ComputeCapability = g.ComputeCapability,
                    .RuntimeVersion = g.RuntimeVersion,
                    .MemoryBandwidthGbps = g.MemoryBandwidthGbps,
                    .IsSharedMemory = g.IsSharedMemory,
                    .Notes = New List(Of String)(g.Notes)
                }).ToList(),
                .CpuName = CpuName,
                .PhysicalCores = PhysicalCores,
                .SupportsAvx2 = SupportsAvx2,
                .SupportsAvx512 = SupportsAvx512,
                .TotalRamBytes = TotalRamBytes,
                .AvailableRamBytes = AvailableRamBytes,
                .FreeDiskBytes = FreeDiskBytes,
                .OsName = OsName,
                .RamBudgetBytes = RamBudgetBytes,
                .BudgetNotes = New List(Of String)(BudgetNotes),
                .DetectionNotes = New List(Of String)(DetectionNotes)
            }
        End Function
    End Class
End Namespace
