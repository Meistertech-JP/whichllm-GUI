Option Strict On
Option Explicit On

Imports System.Windows.Input

Namespace Infrastructure
    Public Class RelayCommand
        Implements ICommand

        Private ReadOnly _execute As Action
        Private ReadOnly _canExecute As Func(Of Boolean)

        Public Sub New(execute As Action, Optional canExecute As Func(Of Boolean) = Nothing)
            _execute = execute
            _canExecute = If(canExecute, Function() True)
        End Sub

        Public Event CanExecuteChanged As EventHandler Implements ICommand.CanExecuteChanged

        Public Function CanExecute(parameter As Object) As Boolean Implements ICommand.CanExecute
            Return _canExecute()
        End Function

        Public Sub Execute(parameter As Object) Implements ICommand.Execute
            _execute()
        End Sub

        Public Sub RaiseCanExecuteChanged()
            RaiseEvent CanExecuteChanged(Me, EventArgs.Empty)
        End Sub
    End Class

    Public Class AsyncRelayCommand
        Implements ICommand

        Private ReadOnly _execute As Func(Of Task)
        Private ReadOnly _canExecute As Func(Of Boolean)
        Private _isRunning As Boolean

        Public Sub New(execute As Func(Of Task), Optional canExecute As Func(Of Boolean) = Nothing)
            _execute = execute
            _canExecute = If(canExecute, Function() True)
        End Sub

        Public Event CanExecuteChanged As EventHandler Implements ICommand.CanExecuteChanged

        Public Function CanExecute(parameter As Object) As Boolean Implements ICommand.CanExecute
            Return Not _isRunning AndAlso _canExecute()
        End Function

        Public Async Sub Execute(parameter As Object) Implements ICommand.Execute
            If Not CanExecute(parameter) Then Return
            _isRunning = True
            RaiseCanExecuteChanged()
            Try
                Await _execute()
            Finally
                _isRunning = False
                RaiseCanExecuteChanged()
            End Try
        End Sub

        Public Sub RaiseCanExecuteChanged()
            RaiseEvent CanExecuteChanged(Me, EventArgs.Empty)
        End Sub
    End Class
End Namespace
