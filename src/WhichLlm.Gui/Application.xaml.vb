Option Strict On
Option Explicit On

Imports System.Windows
Imports System.Windows.Threading

Class Application
    Protected Overrides Sub OnStartup(e As StartupEventArgs)
        EnsureWindowsDirectoryEnvironment()
        AddHandler DispatcherUnhandledException, AddressOf Application_DispatcherUnhandledException
        MyBase.OnStartup(e)
    End Sub

    Private Shared Sub EnsureWindowsDirectoryEnvironment()
        Dim windowsDirectory = Environment.GetEnvironmentVariable("SystemRoot")
        If String.IsNullOrWhiteSpace(windowsDirectory) Then
            windowsDirectory = Environment.GetEnvironmentVariable("WINDIR")
        End If
        If String.IsNullOrWhiteSpace(windowsDirectory) Then
            windowsDirectory = Environment.GetEnvironmentVariable("windir")
        End If
        If String.IsNullOrWhiteSpace(windowsDirectory) Then
            windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        End If
        If String.IsNullOrWhiteSpace(windowsDirectory) AndAlso IO.Directory.Exists("C:\Windows") Then
            windowsDirectory = "C:\Windows"
        End If
        If String.IsNullOrWhiteSpace(windowsDirectory) Then Return

        windowsDirectory = IO.Path.GetFullPath(windowsDirectory)
        Environment.SetEnvironmentVariable("SystemRoot", windowsDirectory)
        Environment.SetEnvironmentVariable("WINDIR", windowsDirectory)
        Environment.SetEnvironmentVariable("windir", windowsDirectory)
    End Sub

    Private Sub Application_DispatcherUnhandledException(sender As Object, e As DispatcherUnhandledExceptionEventArgs)
        Try
            Dim logDir = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "whichllm-gui", "logs")
            IO.Directory.CreateDirectory(logDir)
            IO.File.WriteAllText(IO.Path.Combine(logDir, "last-error.txt"), e.Exception.ToString())
        Catch
        End Try

        MessageBox.Show(
            "予期しないエラーが発生しました。" & Environment.NewLine & e.Exception.Message,
            "whichllm GUI",
            MessageBoxButton.OK,
            MessageBoxImage.Error)
        e.Handled = True
    End Sub
End Class
