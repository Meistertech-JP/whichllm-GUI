Option Strict On
Option Explicit On

Imports WhichLlm.Core.Dto
Imports WhichLlm.Core.Utilities

Namespace Services
    Public Class ModelGrouper
        Implements IModelGrouper

        Public Function FamilyKey(model As ModelInfo) As String Implements IModelGrouper.FamilyKey
            If Not String.IsNullOrWhiteSpace(model.BaseModel) Then
                Return Formatters.NormalizeModelName(model.BaseModel)
            End If
            Return Formatters.NormalizeModelName(model.RepoId)
        End Function
    End Class
End Namespace
