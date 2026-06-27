Option Strict On
Option Explicit On

Imports WhichLlm.Core.Dto
Imports WhichLlm.Core.Utilities

Namespace Services
    Public Class BenchmarkProvider
        Implements IBenchmarkProvider

        Private ReadOnly _cache As IBenchmarkCache

        Public Sub New(cache As IBenchmarkCache)
            _cache = cache
        End Sub

        Public Async Function LoadBenchmarksAsync(refresh As Boolean, Optional cancellationToken As CancellationToken = Nothing) As Task(Of Dictionary(Of String, BenchmarkEvidence)) Implements IBenchmarkProvider.LoadBenchmarksAsync
            If Not refresh AndAlso _cache.IsFresh() Then
                Dim cached = Await _cache.LoadAsync(cancellationToken)
                If cached.Count > 0 Then Return cached
            End If

            Dim evidence = BuildCuratedEvidence()
            Await _cache.SaveAsync(evidence, cancellationToken)
            Return evidence
        End Function

        Private Shared Function BuildCuratedEvidence() As Dictionary(Of String, BenchmarkEvidence)
            Dim data As New Dictionary(Of String, BenchmarkEvidence)(StringComparer.OrdinalIgnoreCase)
            Add(data, "qwen3", 87, "direct")
            Add(data, "qwen2-5", 80, "direct")
            Add(data, "llama-3-3", 84, "direct")
            Add(data, "llama-3-1", 78, "direct")
            Add(data, "deepseek-r1", 88, "direct")
            Add(data, "deepseek-v3", 86, "direct")
            Add(data, "gemma-3", 79, "direct")
            Add(data, "mistral-small", 76, "direct")
            Add(data, "mixtral", 72, "variant")
            Add(data, "phi-4", 73, "direct")
            Add(data, "phi-3", 66, "direct")
            Add(data, "glm-4", 78, "direct")
            Add(data, "kimi", 82, "direct")
            Add(data, "granite", 68, "direct")
            Add(data, "olmo", 65, "direct")
            Return data
        End Function

        Private Shared Sub Add(data As Dictionary(Of String, BenchmarkEvidence), key As String, score As Double, source As String)
            data(Formatters.NormalizeModelName(key)) = New BenchmarkEvidence With {
                .Source = source,
                .Score = score,
                .Confidence = If(source = "direct", 1.0R, 0.75R),
                .Status = If(source = "direct", "", "~"),
                .Notes = "Curated GUI seed benchmark used when live benchmark feeds are unavailable."
            }
        End Sub
    End Class
End Namespace
