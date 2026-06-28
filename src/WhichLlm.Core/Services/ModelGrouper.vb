Option Strict On
Option Explicit On

Imports WhichLlm.Core.Dto
Imports WhichLlm.Core.Engine
Imports WhichLlm.Core.Utilities

Namespace Services
    Public Class ModelGrouper
        Implements IModelGrouper

        Public Function FamilyKey(model As ModelInfo) As String Implements IModelGrouper.FamilyKey
            If Not String.IsNullOrWhiteSpace(model.BaseModel) AndAlso CanUseBaseModelFamily(model) Then
                Return Formatters.NormalizeModelName(model.BaseModel)
            End If
            Return Formatters.NormalizeModelName(model.RepoId)
        End Function

        Private Shared Function CanUseBaseModelFamily(model As ModelInfo) As Boolean
            If model Is Nothing OrElse String.IsNullOrWhiteSpace(model.BaseModel) Then Return False
            Dim actualParams As Double? = Nothing
            If model.ParameterCountB > 0 Then actualParams = model.ParameterCountB
            If Not BenchmarkResolver.ParamsCompatible(actualParams, model.BaseModel) Then Return False

            Dim text = (If(model.RepoId, "") & " " & If(model.DisplayName, "")).ToLowerInvariant()
            If text.Contains("draft", StringComparison.Ordinal) OrElse text.Contains("mtp", StringComparison.Ordinal) OrElse text.Contains("speculative", StringComparison.Ordinal) OrElse text.Contains("fork", StringComparison.Ordinal) Then
                Dim actual As Double? = actualParams
                Dim baseSize = BenchmarkResolver.ExtractParamsB(model.BaseModel)
                If actual.HasValue AndAlso baseSize.HasValue AndAlso baseSize.Value > 0 Then
                    Dim ratio = actual.Value / baseSize.Value
                    Return ratio >= 0.85R AndAlso ratio <= 1.15R
                End If
                Return False
            End If

            Return True
        End Function
    End Class
End Namespace
