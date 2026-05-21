using System;
using System.Drawing;
using System.Windows.Forms;
using CreamInstaller.Components;
using CreamInstaller.Utility;

namespace CreamInstaller.Forms;

internal sealed partial class DebugForm : CustomForm
{
    private static DebugForm current;

    private Form attachedForm;

    private DebugForm()
    {
        InitializeComponent();
        debugTextBox.BackColor = LogTextBox.Background;
    }

    internal static DebugForm Current
    {
        get
        {
            if (current is not null && (current.Disposing || current.IsDisposed))
                current = null;
            return current ??= new();
        }
    }

    protected override void WndProc(ref Message message) // make form immovable by user
    {
        if (message.Msg == 0x0112) // WM_SYSCOMMAND
        {
            int command = message.WParam.ToInt32() & 0xFFF0;
            if (command == 0xF010) // SC_MOVE
                return;
        }

        base.WndProc(ref message);
    }

    internal void Attach(Form form)
    {
        if (attachedForm is not null)
        {
            attachedForm.Activated -= OnChange;
            attachedForm.LocationChanged -= OnChange;
            attachedForm.SizeChanged -= OnChange;
            attachedForm.VisibleChanged -= OnChange;
        }

        attachedForm = form;
        attachedForm.Activated += OnChange;
        attachedForm.LocationChanged += OnChange;
        attachedForm.SizeChanged += OnChange;
        attachedForm.VisibleChanged += OnChange;
        UpdateAttachment();
    }

    private void OnChange(object sender, EventArgs args) => UpdateAttachment();

    private void UpdateAttachment()
    {
        if (attachedForm is null || !attachedForm.Visible)
            return;
        //Size = new(Size.Width, attachedForm.Size.Height);
        Location = new(attachedForm.Right, attachedForm.Top);
        BringToFrontWithoutActivation();
    }

    internal void Log(string text) => Log(text, LogTextBox.Error);

    internal void Log(string text, Color color)
    {
        // IsDisposed -> Invoke is racy: the form can be torn down between the
        // check and the marshal. Swallow the post-shutdown exceptions so
        // background tasks logging during teardown don't crash the process.
        if (debugTextBox.Disposing || debugTextBox.IsDisposed || !IsHandleCreated)
            return;
        try
        {
            Invoke(() =>
            {
                if (debugTextBox.IsDisposed)
                    return;
                if (debugTextBox.Text.Length > 0)
                    debugTextBox.AppendText(Environment.NewLine, color, true);
                debugTextBox.AppendText(text, color, true);
            });
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }
}