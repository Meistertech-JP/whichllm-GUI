Option Strict On
Option Explicit On

Imports System.Windows
Imports System.Windows.Threading

Class Application
    Protected Overrides Sub OnStartup(e As StartupEventArgs)
        AddHandler DispatcherUnhandledException, AddressOf Application_DispatcherUnhandledException
        MyBase.OnStartup(e)
    End Sub

    Private Sub Application_DispatcherUnhandledException(sender As Object, e As DispatcherUnhandledExceptionEventArgs)
        MessageBox.Show(
            "予期しないエラーが発生しました。" & Environment.NewLine & e.Exception.Message,
            "whichllm GUI",
            MessageBoxButton.OK,
            MessageBoxImage.Error)
        e.Handled = True
    End Sub
End Class
