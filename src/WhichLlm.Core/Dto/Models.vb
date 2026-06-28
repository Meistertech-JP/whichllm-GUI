Option Strict On
Option Explicit On

Namespace Dto
    Public Class ModelVariant
        Public Property Quantization As String = ""
        Public Property FileName As String = ""
        Public Property FileSizeBytes As Long?
        Public Property IsSynthetic As Boolean
        Public Property RuntimeKind As String = "gguf"
    End Class

    Public Class ModelInfo
        Public Property RepoId As String = ""
        Public Property DisplayName As String = ""
        Public Property ParameterCountB As Double
        Public Property ActiveParameterCountB As Double?
        Public Property Architecture As String = ""
        Public Property ContextLength As Integer?
        Public Property License As String = ""
        Public Property Downloads As Long
        Public Property Likes As Long
        Public Property PublishedDate As DateTimeOffset?
        Public Property UpdatedDate As DateTimeOffset?
        Public Property BaseModel As String = ""
        Public Property PipelineTag As String = ""
        Public Property UseCase As String = "general"
        Public Property Tags As New List(Of String)
        Public Property Variants As New List(Of ModelVariant)
        Public Property EvalScore As Double?

        Public ReadOnly Property FamilyKey As String
            Get
                If Not String.IsNullOrWhiteSpace(BaseModel) Then
                    Return BaseModel
                End If
                Return RepoId
            End Get
        End Property
    End Class

    Public Class BenchmarkEvidence
        Public Property Source As String = "none"
        Public Property Score As Double
        Public Property Confidence As Double
        Public Property Status As String = "?"
        Public Property Notes As String = ""
        Public Property BenchmarkTier As String = ""
    End Class
End Namespace
