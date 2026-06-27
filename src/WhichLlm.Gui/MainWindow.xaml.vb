Option Strict On
Option Explicit On

Imports WhichLlm.Gui.ViewModels

Class MainWindow
    Private ReadOnly _viewModel As New MainViewModel()

    Public Sub New()
        InitializeComponent()
        DataContext = _viewModel
        AddHandler Loaded, AddressOf MainWindow_Loaded
    End Sub

    Private Async Sub MainWindow_Loaded(sender As Object, e As RoutedEventArgs)
        RemoveHandler Loaded, AddressOf MainWindow_Loaded
        Await _viewModel.InitializeAsync()
    End Sub
End Class
