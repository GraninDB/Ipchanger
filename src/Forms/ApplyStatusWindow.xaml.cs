using System.Windows;

namespace IPChanger.Forms;

public partial class ApplyStatusWindow : Window
{
    public ApplyStatusWindow()
    {
        InitializeComponent();
        Title = L.T("ApplyStatusWindowTitle");
        OkButton.Content = L.T("BtnOk");
    }

    public void AddStep(string step)
    {
        if (Dispatcher.CheckAccess())
        {
            StepsList.Items.Add(step);
            StepsList.ScrollIntoView(StepsList.Items[^1]);
        }
        else
        {
            Dispatcher.Invoke(() => AddStep(step));
        }
    }

    public void ClearSteps()
    {
        if (Dispatcher.CheckAccess())
        {
            StepsList.Items.Clear();
        }
        else
        {
            Dispatcher.Invoke(ClearSteps);
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
