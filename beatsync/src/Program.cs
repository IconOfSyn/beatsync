using beatsync;

static class Program
{
    // STAThread is required if you deploy using NativeAOT on Windows - See https://github.com/raylib-cs/raylib-cs/issues/301
    [STAThread]
    public static void Main(string[] args)
    {
        bool isGui = Array.Exists(args, a => string.Equals(a, "--gui", StringComparison.OrdinalIgnoreCase));
        if (isGui)
        {
            GuiApp.Run();
        }
        else
        {
            CliApp.Run();
        }
    }
}