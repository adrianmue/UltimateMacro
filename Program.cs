namespace UltimateMacro;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args is ["--image-capture-worker"])
        {
            BackgroundImageCapture.RunWorker();
            return;
        }
        if (args.Contains("--self-test"))
        {
            SelfTests.Run(!args.Contains("--skip-native"), args.Contains("--test-capture"), args.Contains("--test-drag"));
            return;
        }
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, error) => MessageBox.Show(error.Exception.Message, "Ultimate Macro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new MainForm());
    }
}
