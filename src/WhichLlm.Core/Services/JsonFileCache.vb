Option Strict On
Option Explicit On

Imports System.Text.Json
Imports System.IO
Imports WhichLlm.Core.Dto

Namespace Services
    Public MustInherit Class JsonFileCacheBase(Of T)
        Protected ReadOnly CacheFile As String
        Private ReadOnly _ttl As TimeSpan
        Private Shared ReadOnly Options As New JsonSerializerOptions With {
            .WriteIndented = True,
            .PropertyNameCaseInsensitive = True
        }

        Protected Sub New(fileName As String, ttl As TimeSpan)
            Dim root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "whichllm-gui", "cache")
            Directory.CreateDirectory(root)
            CacheFile = Path.Combine(root, fileName)
            _ttl = ttl
        End Sub

        Public Function IsFresh() As Boolean
            Dim file = New FileInfo(CacheFile)
            If Not file.Exists Then
                Return False
            End If
            Return DateTimeOffset.Now - file.LastWriteTime < _ttl
        End Function

        Protected Async Function LoadValueAsync(Optional cancellationToken As CancellationToken = Nothing) As Task(Of T)
            If Not File.Exists(CacheFile) Then
                Return Nothing
            End If
            Using stream = File.OpenRead(CacheFile)
                Dim value = Await JsonSerializer.DeserializeAsync(Of T)(stream, Options, cancellationToken)
                Return value
            End Using
        End Function

        Protected Async Function SaveValueAsync(value As T, Optional cancellationToken As CancellationToken = Nothing) As Task
            Directory.CreateDirectory(Path.GetDirectoryName(CacheFile))
            Using stream = File.Create(CacheFile)
                Await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken)
            End Using
        End Function
    End Class

    Public Class ModelCache
        Inherits JsonFileCacheBase(Of List(Of ModelInfo))
        Implements IModelCache

        Public Sub New()
            MyBase.New("models.json", TimeSpan.FromHours(6))
        End Sub

        Public Async Function LoadAsync(Optional cancellationToken As CancellationToken = Nothing) As Task(Of List(Of ModelInfo)) Implements IModelCache.LoadAsync
            Return If(Await LoadValueAsync(cancellationToken), New List(Of ModelInfo)())
        End Function

        Public Function SaveAsync(models As IEnumerable(Of ModelInfo), Optional cancellationToken As CancellationToken = Nothing) As Task Implements IModelCache.SaveAsync
            Return SaveValueAsync(models.ToList(), cancellationToken)
        End Function

        Private Function IModelCache_IsFresh() As Boolean Implements IModelCache.IsFresh
            Return MyBase.IsFresh()
        End Function
    End Class

    Public Class BenchmarkCache
        Inherits JsonFileCacheBase(Of Dictionary(Of String, BenchmarkEvidence))
        Implements IBenchmarkCache

        Public Sub New()
            MyBase.New("benchmark.json", TimeSpan.FromHours(24))
        End Sub

        Public Async Function LoadAsync(Optional cancellationToken As CancellationToken = Nothing) As Task(Of Dictionary(Of String, BenchmarkEvidence)) Implements IBenchmarkCache.LoadAsync
            Return If(Await LoadValueAsync(cancellationToken), New Dictionary(Of String, BenchmarkEvidence)(StringComparer.OrdinalIgnoreCase))
        End Function

        Public Function SaveAsync(evidence As Dictionary(Of String, BenchmarkEvidence), Optional cancellationToken As CancellationToken = Nothing) As Task Implements IBenchmarkCache.SaveAsync
            Return SaveValueAsync(evidence, cancellationToken)
        End Function

        Private Function IBenchmarkCache_IsFresh() As Boolean Implements IBenchmarkCache.IsFresh
            Return MyBase.IsFresh()
        End Function
    End Class
End Namespace
