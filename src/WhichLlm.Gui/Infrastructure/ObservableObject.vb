Option Strict On
Option Explicit On

Imports System.ComponentModel
Imports System.Runtime.CompilerServices

Namespace Infrastructure
    Public MustInherit Class ObservableObject
        Implements INotifyPropertyChanged

        Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

        Protected Function SetProperty(Of T)(ByRef storage As T, value As T, <CallerMemberName> Optional propertyName As String = "") As Boolean
            If EqualityComparer(Of T).Default.Equals(storage, value) Then
                Return False
            End If
            storage = value
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(propertyName))
            Return True
        End Function

        Protected Sub OnPropertyChanged(<CallerMemberName> Optional propertyName As String = "")
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(propertyName))
        End Sub
    End Class
End Namespace
