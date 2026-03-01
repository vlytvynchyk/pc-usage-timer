using System.Windows;
using System.Windows.Input;

namespace PcUsageTimer;

public partial class PinPromptDialog : Window
{
    private readonly string _pin;

    public PinPromptDialog(string pin)
    {
        _pin = pin;
        InitializeComponent();
        Loaded += (_, _) => PinEntry.Focus();
    }

    private void PinEntry_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TryConfirm();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        TryConfirm();
    }

    private void TryConfirm()
    {
        if (PinEntry.Password == _pin)
        {
            DialogResult = true;
            Close();
        }
        else
        {
            ErrorText.Text = "Wrong PIN.";
            PinEntry.Password = "";
            PinEntry.Focus();
        }
    }
}
