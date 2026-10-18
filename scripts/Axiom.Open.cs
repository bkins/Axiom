using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
[assembly: System.Reflection.AssemblyTitle("Axiom")]
[assembly: System.Reflection.AssemblyProduct("Axiom")]
[assembly: System.Reflection.AssemblyDescription("Open files in the current Axiom review build")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

// This stable executable is the Windows association target. The verified review
// wrapper beside it is retargeted when publishing; Windows never caches a build path.
internal static class AxiomOpen
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1 || !File.Exists(args[0]))
                throw new InvalidOperationException("Choose an existing Markdown file.");
            string document = Path.GetFullPath(args[0]);
            string extension = Path.GetExtension(document);
            if (!String.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(extension, ".markdown", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose a Markdown file.");
            string wrapper = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Open-AxiomFile.ps1");
            if (!File.Exists(wrapper)) throw new FileNotFoundException("Repair Axiom file opening; its launcher is missing.");
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    @"System32\WindowsPowerShell\v1.0\powershell.exe"),
                Arguments = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"" + wrapper +
                    "\" -Document \"" + document + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var process = Process.Start(start))
            {
                process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidOperationException("Axiom could not open this file. Repair the current review registration.");
            }
            return 0;
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Axiom", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
