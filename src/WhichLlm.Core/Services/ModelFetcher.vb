Option Strict On
Option Explicit On

Imports WhichLlm.Core.Dto

Namespace Services
    Public Class ModelFetcher
        Implements IModelFetcher

        Private ReadOnly _cache As IModelCache
        Private ReadOnly _client As IHuggingFaceClient

        Public Sub New(cache As IModelCache, client As IHuggingFaceClient)
            _cache = cache
            _client = client
        End Sub

        Public Async Function LoadModelsAsync(options As RankingOptions, Optional cancellationToken As CancellationToken = Nothing) As Task(Of List(Of ModelInfo)) Implements IModelFetcher.LoadModelsAsync
            If Not options.Refresh AndAlso _cache.IsFresh() Then
                Dim cached = Await _cache.LoadAsync(cancellationToken)
                If cached.Count > 0 Then Return cached
            End If

            Dim fetchError As Exception = Nothing
            Try
                Dim fetched = Await _client.FetchModelsAsync(options.Profile, options.UseCase, options.Refresh, cancellationToken)
                If fetched.Count > 0 Then
                    Await _cache.SaveAsync(fetched, cancellationToken)
                    Return fetched
                End If
            Catch ex As Exception
                fetchError = ex
            End Try

            Dim stale = Await _cache.LoadAsync(cancellationToken)
            If stale.Count > 0 Then Return stale
            If fetchError IsNot Nothing Then Throw fetchError
            Return stale
        End Function
    End Class
End Namespace
