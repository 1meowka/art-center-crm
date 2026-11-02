using System;
using System.Windows;
using VKR.Models;
using VKR.Utilities;

namespace VKR.Views;

public partial class PaymentEditDialog : Window
{
    public bool Saved { get; private set; }

    public PaymentEditDialog() : this(null, null) { }

    public PaymentEditDialog(int? idPayment) : this(idPayment, null) { }

    public PaymentEditDialog(DebtorItem debtor) : this(null, debtor) { }

    private PaymentEditDialog(int? idPayment, DebtorItem? debtor)
    {
        InitializeComponent();
        WindowMinimize.Attach(this);

        TitleText.Text = idPayment.HasValue
            ? "Редактирование оплаты"
            : debtor != null
                ? "Погашение задолженности"
                : "Новая оплата";

        Loaded += (_, _) =>
        {
            if (idPayment.HasValue)
                Editor.BeginEdit(idPayment.Value);
            else if (debtor != null)
                Editor.BeginDebtor(debtor);
            else
                Editor.BeginNew();
        };

        Editor.Saved += (_, _) =>
        {
            Saved = true;
            TrySetDialogResult(true);
            Close();
        };
        Editor.Cancelled += (_, _) =>
        {
            TrySetDialogResult(false);
            Close();
        };
    }

    public static void OpenFloating(Window? owner, Action? onSaved, int? idPayment = null, DebtorItem? debtor = null)
    {
        var dialog = new PaymentEditDialog(idPayment, debtor) { Owner = owner };
        dialog.Closed += (_, _) =>
        {
            if (dialog.Saved)
                onSaved?.Invoke();
        };
        dialog.Show();
        dialog.WindowState = WindowState.Maximized;
        dialog.Activate();
    }

    private void TrySetDialogResult(bool value)
    {
        try
        {
            DialogResult = value;
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowMinimize.Minimize(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            DragMove();
    }
}
