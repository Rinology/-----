using System;
using System.Diagnostics;

namespace ChatHelper.Tests;

public class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("==================================================");
        Console.WriteLine(" ChatHelper Core Services Unit Test Suite");
        Console.WriteLine("==================================================");
        Console.WriteLine();

        int passed = 0;
        int failed = 0;
        var sw = Stopwatch.StartNew();

        void RunSuite(string name, Action testAction)
        {
            Console.Write($"[RUNNING] {name}... ");
            try
            {
                testAction();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("PASSED");
                Console.ResetColor();
                passed++;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAILED");
                Console.ResetColor();
                Console.WriteLine($"   => Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"   => Inner: {ex.InnerException.Message}");
                }
                failed++;
            }
        }

        RunSuite("ConfigManager (Thread-Safety & Persistence)", ConfigManagerTests.RunAll);
        RunSuite("AuthService (User Validation & Async Flow)", AuthServiceTests.RunAll);
        RunSuite("TemplateManager (CSV Parsing & Search/Matching)", TemplateManagerTests.RunAll);

        sw.Stop();
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine($"Result: {passed} Passed, {failed} Failed (Time: {sw.ElapsedMilliseconds} ms)");
        Console.WriteLine("--------------------------------------------------");

        return failed == 0 ? 0 : 1;
    }
}
